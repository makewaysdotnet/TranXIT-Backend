#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
TEMP_PARENT="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
TMP_ROOT="$(mktemp -d "$TEMP_PARENT/tranxit-deploy-refs.XXXXXXXX")"
cleanup() {
  case "$TMP_ROOT" in
    "$TEMP_PARENT"/tranxit-deploy-refs.*) rm -rf -- "$TMP_ROOT" ;;
    *) echo 'Refusing cleanup outside deploy-ref fixture' >&2; exit 1 ;;
  esac
}
trap cleanup EXIT

# Real deploy entrypoint; rejected inputs must not even consult controller/project
# configuration, execute Git/Docker, or create marker state.
mkdir -p "$TMP_ROOT/bin"
for command in git docker; do
  printf '#!/usr/bin/env bash\nprintf "unexpected command\\n" >> "$TRANXIT_REF_TEST_COMMAND_LOG"\nexit 99\n' > "$TMP_ROOT/bin/$command"
  chmod +x "$TMP_ROOT/bin/$command"
done
valid=1111111111111111111111111111111111111111
checks=0
for target in staging production; do
  for field in backend frontend; do
    for invalid in '' main feature/test v1 1111111 AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA 11111111111111111111111111111111111111111; do
      backend="$valid" frontend="$valid"
      if [ "$field" = backend ]; then backend="$invalid"; else frontend="$invalid"; fi
      status=0
      env -u TRANXIT_DEPLOY_PROJECT_DIR \
        PATH="$TMP_ROOT/bin:$PATH" TRANXIT_REF_TEST_COMMAND_LOG="$TMP_ROOT/commands" \
        TRANXIT_MARKER_DIR="$TMP_ROOT/markers" \
        bash "$SCRIPT_DIR/../deploy.sh" --env "$target" --sha "$backend" \
          --frontend-ref "$frontend" --migration-policy expand-contract > "$TMP_ROOT/result" 2>&1 || status=$?
      [ "$status" -eq 2 ] || { echo "Invalid $target/$field identifier returned $status" >&2; exit 1; }
      grep -F 'full 40-character lowercase Git commit SHA' "$TMP_ROOT/result" >/dev/null || {
        echo 'Identifier validation did not reject before controller configuration' >&2; exit 1;
      }
      [ ! -e "$TMP_ROOT/commands" ] && [ ! -e "$TMP_ROOT/markers" ] || {
        echo 'Invalid identifier reached mutating commands/state' >&2; exit 1;
      }
      checks=$((checks + 1))
    done
  done
done
printf 'PASS T-NFR-9.ImmutableDeployRefs (%s actual-entrypoint rejection cases)\n' "$checks"
