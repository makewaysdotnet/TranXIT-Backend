#!/usr/bin/env bash
set -euo pipefail

TEST_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
PROJECT_DIR="$(cd "$TEST_DIR/../.." && pwd -P)"
BACKEND_REPO_DIR="$(git -C "$PROJECT_DIR" rev-parse --show-toplevel)"
TEMP_PARENT="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
TMP_ROOT="$(mktemp -d "$TEMP_PARENT/tranxit-controller-entrypoint.XXXXXXXX")"

cleanup() {
  case "$TMP_ROOT" in
    "$TEMP_PARENT"/tranxit-controller-entrypoint.*) rm -rf -- "$TMP_ROOT" ;;
    *) echo "Refusing cleanup outside the controller-entrypoint fixture" >&2; exit 1 ;;
  esac
}
trap cleanup EXIT

fail() {
  echo "FAIL: $*" >&2
  for output in "$TMP_ROOT"/*.out; do
    [ -f "$output" ] || continue
    echo "Captured $(basename "$output"):" >&2
    cat "$output" >&2
  done
  exit 1
}

assert_contains() {
  local file="$1" expected="$2"
  grep -F -- "$expected" "$file" >/dev/null || fail "Expected '$expected' in $file"
}

script_args() {
  case "$1" in
    deploy)
      printf '%s\0' --env staging --sha 1111111111111111111111111111111111111111 --migration-policy expand-contract
      ;;
    backup)
      printf '%s\0' --env staging --release-id controller-contract
      ;;
    restore)
      printf '%s\0' --env staging --manifest "$TMP_ROOT/missing.manifest" \
        --account-database Tranxit_Account_Test --courier-database Tranxit_CourierJob_Test \
        --confirm RESTORE-staging-Tranxit_Account_Test-Tranxit_CourierJob_Test
      ;;
    *) return 2 ;;
  esac
}

run_case() {
  local script="$1" mode="$2" source="$PROJECT_DIR/scripts/$script.sh"
  local output="$TMP_ROOT/$script-$mode.out" status=0
  local -a args=()
  mapfile -d '' -t args < <(script_args "$script")

  case "$mode" in
    unset)
      env -u TRANXIT_DEPLOY_PROJECT_DIR \
        TRANXIT_ENV_FILE="$TMP_ROOT/missing.env" \
        TRANXIT_MARKER_DIR="$TMP_ROOT/markers" \
        PATH="$TMP_ROOT/bin:$PATH" \
        bash "$source" "${args[@]}" >"$output" 2>&1 || status=$?
      [ "$status" -eq 2 ] || fail "$script without TRANXIT_DEPLOY_PROJECT_DIR returned $status"
      assert_contains "$output" "TRANXIT_DEPLOY_PROJECT_DIR is required"
      ;;
    in-checkout)
      TRANXIT_DEPLOY_PROJECT_DIR="$PROJECT_DIR" \
        TRANXIT_ENV_FILE="$TMP_ROOT/missing.env" \
        TRANXIT_MARKER_DIR="$TMP_ROOT/markers" \
        PATH="$TMP_ROOT/bin:$PATH" \
        bash "$source" "${args[@]}" >"$output" 2>&1 || status=$?
      [ "$status" -eq 2 ] || fail "$script from inside BACKEND_REPO_DIR returned $status"
      assert_contains "$output" "Refusing controller execution from inside BACKEND_REPO_DIR"
      ;;
    installed)
      source="$TMP_ROOT/controller/scripts/$script.sh"
      TRANXIT_DEPLOY_PROJECT_DIR="$PROJECT_DIR" \
        TRANXIT_ENV_FILE="$TMP_ROOT/missing.env" \
        TRANXIT_MARKER_DIR="$TMP_ROOT/markers" \
        PATH="$TMP_ROOT/bin:$PATH" \
        bash "$source" "${args[@]}" >"$output" 2>&1 || status=$?
      [ "$status" -eq 1 ] || fail "$script from the installed controller returned $status instead of reaching its next read-only validation"
      if grep -F "Refusing controller execution" "$output" >/dev/null ||
         grep -F "TRANXIT_DEPLOY_PROJECT_DIR is required" "$output" >/dev/null; then
        fail "$script rejected the supported installed controller"
      fi
      if [ "$script" = restore ]; then
        assert_contains "$output" "Backup manifest not found"
      else
        assert_contains "$output" "Missing env file"
      fi
      ;;
    *) fail "Unknown mode: $mode" ;;
  esac
}

mkdir -p "$TMP_ROOT/controller/scripts" "$TMP_ROOT/bin"
for script in deploy backup restore; do
  cp -- "$PROJECT_DIR/scripts/$script.sh" "$TMP_ROOT/controller/scripts/$script.sh"
done
cat > "$TMP_ROOT/bin/docker" <<'SH'
#!/usr/bin/env bash
echo "Docker must not run in the controller entrypoint contract" >&2
exit 99
SH
chmod +x "$TMP_ROOT/bin/docker"

head_before="$(git -C "$BACKEND_REPO_DIR" rev-parse HEAD)"
status_before="$(git -C "$BACKEND_REPO_DIR" status --porcelain=v1)"
checks=0
for script in deploy backup restore; do
  for mode in unset in-checkout installed; do
    run_case "$script" "$mode"
    checks=$((checks + 1))
  done
done

[ ! -e "$TMP_ROOT/markers" ] || fail "A controller refusal created marker state"
[ "$(git -C "$BACKEND_REPO_DIR" rev-parse HEAD)" = "$head_before" ] || fail "Controller contract changed backend HEAD"
[ "$(git -C "$BACKEND_REPO_DIR" status --porcelain=v1)" = "$status_before" ] || fail "Controller contract changed backend files"

printf 'PASS T-NFR-9.InstalledControllerOnly (%s actual-entrypoint cases)\n' "$checks"
