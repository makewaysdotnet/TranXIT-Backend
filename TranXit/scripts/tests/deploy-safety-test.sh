#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
DEPLOY_SOURCE="${TRANXIT_TEST_DEPLOY_SOURCE:-$PROJECT_DIR/scripts/deploy.sh}"
MODE="${1:---all}"
case "$MODE" in
  --all|--preflight|--preflight-credibility) ;;
  *) echo "Usage: $0 [--all|--preflight|--preflight-credibility]" >&2; exit 2 ;;
esac
TEMP_PARENT="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
TMP_ROOT="$(mktemp -d "$TEMP_PARENT/tranxit-deploy-safety.XXXXXXXX")"

cleanup() {
  case "$TMP_ROOT" in
    "$TEMP_PARENT"/tranxit-deploy-safety.*) rm -rf -- "$TMP_ROOT" ;;
    *) echo "Refusing cleanup outside the test temp directory" >&2; exit 1 ;;
  esac
}
trap cleanup EXIT

fail() {
  echo "FAIL: $*" >&2
  if [ -n "${PREFLIGHT_OUTPUT:-}" ] && [ -f "$PREFLIGHT_OUTPUT" ]; then
    echo "Captured preflight output:" >&2
    cat "$PREFLIGHT_OUTPUT" >&2
  fi
  if [ -n "${DOCKER_LOG:-}" ] && [ -f "$DOCKER_LOG" ]; then
    echo "Captured Docker events:" >&2
    cat "$DOCKER_LOG" >&2
  fi
  for output in "$TMP_ROOT"/*.out; do
    [ -f "$output" ] || continue
    echo "Captured $(basename "$output"):" >&2
    cat "$output" >&2
  done
  exit 1
}

assert_contains() {
  local file="$1"
  local expected="$2"
  grep -F -- "$expected" "$file" >/dev/null ||
    fail "Expected '$expected' in $file"
}

assert_status() {
  local expected="$1"
  local actual="$2"
  local label="$3"
  [ "$actual" -eq "$expected" ] ||
    fail "$label returned $actual; expected $expected"
}

create_backend_release_repo() {
  local origin="$TMP_ROOT/backend-origin.git"
  local repo="$TMP_ROOT/backend"

  git init --bare "$origin" >/dev/null
  git -c core.autocrlf=false clone "$origin" "$repo" >/dev/null 2>&1
  git -C "$repo" config user.email "deploy-safety@example.invalid"
  git -C "$repo" config user.name "Deploy Safety Test"
  git -C "$repo" config core.autocrlf false

  mkdir -p "$repo/TranXit/scripts/tests"
  cp "$DEPLOY_SOURCE" "$repo/TranXit/scripts/deploy.sh"
  cp "$PROJECT_DIR/scripts/backup.sh" "$repo/TranXit/scripts/backup.sh"
  cp "$PROJECT_DIR/scripts/restore.sh" "$repo/TranXit/scripts/restore.sh"
  cp "$PROJECT_DIR/scripts/admission-contract.sh" "$repo/TranXit/scripts/admission-contract.sh"
  mkdir -p "$repo/TranXit/ops/caddy"
  cp "$PROJECT_DIR/"docker-compose{,.prod,.staging}.yml "$repo/TranXit/"
  cp "$PROJECT_DIR/ops/"Caddyfile{,.staging} "$repo/TranXit/ops/"
  cp "$PROJECT_DIR/ops/caddy/Dockerfile" "$repo/TranXit/ops/caddy/Dockerfile"
  cat > "$repo/TranXit/scripts/verify-production-topology.sh" <<'SH'
#!/usr/bin/env bash
echo topology >> "$TRANXIT_TEST_DOCKER_LOG"
exit "${TRANXIT_TEST_TOPOLOGY_STATUS:-0}"
SH
  cat > "$repo/TranXit/scripts/smoke.sh" <<'SH'
#!/usr/bin/env bash
echo http-smoke >> "$TRANXIT_TEST_DOCKER_LOG"
[ "${TRANXIT_SMOKE_PRIVATE_HTTP:-}" = true ] || exit 71
[ "$*" = "--base-url http://caddy:8082" ] || exit 72
[ ! -e "$TRANXIT_ADMISSION_DIR/open" ] || exit 73
exit "${TRANXIT_TEST_HTTP_STATUS:-0}"
SH
  chmod +x "$repo/TranXit/scripts/"*.sh

  printf 'green\n' > "$repo/release.txt"
  git -C "$repo" add .
  git -C "$repo" commit -m "green release" >/dev/null
  git -C "$repo" branch -M main
  GREEN_BACKEND_SHA="$(git -C "$repo" rev-parse HEAD)"

  printf 'candidate\n' > "$repo/release.txt"
  git -C "$repo" add release.txt
  git -C "$repo" commit -m "candidate release" >/dev/null
  CANDIDATE_BACKEND_SHA="$(git -C "$repo" rev-parse HEAD)"
  git -C "$repo" push -u origin main >/dev/null 2>&1

  BACKEND_REPO="$repo"
}

create_controller_fixture() {
  local helper
  CONTROLLER_SCRIPTS="$TMP_ROOT/operator-controller/scripts"
  mkdir -p "$CONTROLLER_SCRIPTS"
  # The app fixture contains the selected reviewed DEPLOY_SOURCE and the model helpers.
  # Install once outside both repos; later checkouts must never refresh this controller.
  for helper in deploy admission-contract backup restore smoke verify-production-topology; do
    cp -- "$BACKEND_REPO/TranXit/scripts/$helper.sh" "$CONTROLLER_SCRIPTS/$helper.sh"
    chmod 500 "$CONTROLLER_SCRIPTS/$helper.sh"
  done
  ln -s "$TMP_ROOT/operator-controller" "$TMP_ROOT/controller-current"
  CONTROLLER_ENTRYPOINT="$TMP_ROOT/controller-current/scripts/deploy.sh"
  [ "$(cd "$(dirname "$CONTROLLER_ENTRYPOINT")" && pwd -P)" = "$CONTROLLER_SCRIPTS" ] ||
    fail 'Installed controller symlink did not resolve to its physical directory'
  export TRANXIT_DEPLOY_PROJECT_DIR="$BACKEND_REPO/TranXit"
}

create_frontend_release_repo() {
  local origin="$TMP_ROOT/frontend-origin.git"
  local repo="$TMP_ROOT/frontend"

  git init --bare "$origin" >/dev/null
  git -c core.autocrlf=false clone "$origin" "$repo" >/dev/null 2>&1
  git -C "$repo" config user.email "deploy-safety@example.invalid"
  git -C "$repo" config user.name "Deploy Safety Test"
  git -C "$repo" config core.autocrlf false

  printf 'green\n' > "$repo/release.txt"
  git -C "$repo" add release.txt
  git -C "$repo" commit -m "green release" >/dev/null
  git -C "$repo" branch -M main
  GREEN_FRONTEND_SHA="$(git -C "$repo" rev-parse HEAD)"

  printf 'candidate\n' > "$repo/release.txt"
  git -C "$repo" add release.txt
  git -C "$repo" commit -m "candidate release" >/dev/null
  CANDIDATE_FRONTEND_SHA="$(git -C "$repo" rev-parse HEAD)"
  git -C "$repo" push -u origin main >/dev/null 2>&1

  FRONTEND_REPO="$repo"
}

create_mock_commands() {
  export TRANXIT_TEST_REAL_GIT="$(command -v git)"
  MOCK_BIN="$TMP_ROOT/bin"
  DOCKER_LOG="$TMP_ROOT/docker-events.log"
  mkdir -p "$MOCK_BIN"
  export TRANXIT_TEST_STATE_DIR="$TMP_ROOT/service-state"
  mkdir -p "$TRANXIT_TEST_STATE_DIR"
  for service in caddy frontend ocelotapigw accountservice courierjobservice; do
    echo exited > "$TRANXIT_TEST_STATE_DIR/$service"
  done
  : > "$DOCKER_LOG"

  cat > "$MOCK_BIN/docker" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail

args="$*"
printf 'command %s\n' "$args" >> "$TRANXIT_TEST_DOCKER_LOG"
if [[ " $args " == *' --connect-to ::caddy:443 '* ]]; then
  printf '%s' "${TRANXIT_TEST_PUBLIC_EDGE_REPLY:-Temporarily unavailable|503|60}"
  exit "${TRANXIT_TEST_PUBLIC_EDGE_STATUS:-0}"
fi
if [ "$1" = ps ]; then
  for argument in "$@"; do
    case "$argument" in
      label=com.docker.compose.service=*)
        service="${argument##*=}"
        [ ! -f "$TRANXIT_TEST_STATE_DIR/$service" ] || echo "fake-$service"
        ;;
    esac
  done
  exit 0
fi
if [ "$1" = inspect ]; then
  service="${*: -1}"
  cat "$TRANXIT_TEST_STATE_DIR/${service#fake-}"
  exit 0
fi
if [[ " $args " == *" up "* ]] || [[ " $args " == *" start "* ]] || [[ " $args " == *" stop "* ]]; then
  for service in caddy frontend ocelotapigw accountservice courierjobservice; do
    if [[ " $args " == *" $service "* ]]; then
      if [[ " $args " == *" stop "* ]]; then
        echo exited > "$TRANXIT_TEST_STATE_DIR/$service"
      else
        echo running > "$TRANXIT_TEST_STATE_DIR/$service"
      fi
    fi
  done
  if [ "${TRANXIT_TEST_FAIL_CANDIDATE_START:-false}" = true ] &&
     [[ " $args " == *" up "*" accountservice courierjobservice "* ]] &&
     [ ! -f "$TRANXIT_TEST_STATE_DIR/start-failure-injected" ]; then
    touch "$TRANXIT_TEST_STATE_DIR/start-failure-injected"
    echo startup-failure >> "$TRANXIT_TEST_DOCKER_LOG"
    exit 47
  fi
fi
if [[ "$args" == *"install -d -o mssql -g mssql -m 700 /var/opt/mssql/backup"* ]] &&
   [ "${TRANXIT_TEST_FAIL_STORAGE_PREP:-false}" = true ]; then
  echo storage-preparation-failure >> "$TRANXIT_TEST_DOCKER_LOG"
  exit 49
fi
if [[ "$args" == *"BACKUP DATABASE"* ]]; then
  echo "backup" >> "$TRANXIT_TEST_DOCKER_LOG"
  if [ "${TRANXIT_TEST_FAIL_BACKUP:-false}" = "true" ]; then
    exit 42
  fi
fi
if [[ "$args" == *"--apply-migrations"* ]]; then
  echo "migration" >> "$TRANXIT_TEST_DOCKER_LOG"
fi
if [[ " $args " == *" build "* ]]; then
  echo "build" >> "$TRANXIT_TEST_DOCKER_LOG"
  if [ "${TRANXIT_TEST_FAIL_ROLLBACK_BUILD:-false}" = "true" ] &&
     [ "$(grep -c '^build$' "$TRANXIT_TEST_DOCKER_LOG")" -ge 2 ]; then
    exit 43
  fi
fi
if [[ "$args" == *"RESTORE_SQL="* ]]; then
  printf 'restore-sql %s\n' "$args" >> "$TRANXIT_TEST_DOCKER_LOG"
  if [ "${TRANXIT_TEST_FAIL_SECOND_RESTORE:-false}" = "true" ] &&
     [ "$(grep -c '^restore-sql ' "$TRANXIT_TEST_DOCKER_LOG")" -ge 2 ]; then
    exit 44
  fi
fi

if [[ "$args" == *"RESTORE FILELISTONLY"* ]]; then
  printf 'LogicalData|/var/opt/mssql/data/source.mdf|D|PRIMARY\n'
  printf 'LogicalLog|/var/opt/mssql/data/source_log.ldf|L|NULL\n'
  exit 0
fi
if [[ "$args" == *"MigrationId"* ]]; then
  printf '202607270001_TestMigration\n'
  exit 0
fi
if [[ "$args" == *"tranxit-database-presence"* ]]; then
  printf '%s\n' "${TRANXIT_TEST_DATABASE_PRESENCE:-1|1}"
  exit 0
fi
if [[ "$args" == *"ps --services --filter status=running"* ]] &&
   [ -n "${TRANXIT_TEST_RUNNING_SERVICES:-}" ]; then
  printf '%s\n' "$TRANXIT_TEST_RUNNING_SERVICES"
  exit 0
fi

if [[ " $args " == *" cp "* ]]; then
  destination="${@: -1}"
  if [[ "$destination" != sqlserver:* ]]; then
    mkdir -p "$(dirname "$destination")"
    printf 'mock sql backup\n' > "$destination"
  fi
fi
MOCK

  cat > "$MOCK_BIN/curl" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK

  cat > "$MOCK_BIN/sleep" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK

  cat > "$MOCK_BIN/flock" <<'MOCK'
#!/usr/bin/env bash
exit 0
MOCK

  cat > "$MOCK_BIN/git" <<'MOCK'
#!/usr/bin/env bash
if [ -n "${TRANXIT_TEST_GIT_LOG:-}" ]; then
  case " $* " in
    *" fetch "*|*" checkout "*|*" switch "*|*" reset "*|*" clean "*)
      printf '%s\n' "$*" >> "$TRANXIT_TEST_GIT_LOG"
      ;;
  esac
fi
exec "$TRANXIT_TEST_REAL_GIT" "$@"
MOCK

  chmod +x "$MOCK_BIN/docker" "$MOCK_BIN/curl" "$MOCK_BIN/sleep" "$MOCK_BIN/flock" "$MOCK_BIN/git"
}

preflight_case() {
  # UC-NFR-9, A-1 - T-NFR-9.RequiredEnvPreflight (actual entry point, command doubles).
  local target="$1" release="$2" setting="$3" problem="$4"
  local root="$TMP_ROOT/preflight-$target-$release-$setting-$problem"
  local name value status=0 expected_status=2 flags=() service
  PREFLIGHT_OUTPUT="$root/result.out"
  mkdir -p "$root/services"
  git -C "$BACKEND_REPO" checkout --detach "$GREEN_BACKEND_SHA" >/dev/null 2>&1
  git -C "$FRONTEND_REPO" checkout --detach "$GREEN_FRONTEND_SHA" >/dev/null 2>&1
  if [ "$release" = known-green ]; then
    mkdir -p "$root/markers/admission-$target"
    printf 'environment=%s\nbackend_sha=%s\nfrontend_sha=%s\nadmission_policy=private-smoke-v1\n' \
      "$target" "$GREEN_BACKEND_SHA" "$GREEN_FRONTEND_SHA" > "$root/markers/last-$target-green"
    printf 'previous-marker\n' > "$root/markers/last-$target-green.prev"
    printf 'lock-sentinel\n' > "$root/markers/deploy-$target.lock"
    printf 'admitted\n' > "$root/markers/admission-$target/open"
    cp -a "$root/markers" "$root/markers.before"
    find "$root/markers" -exec stat -c '%n|%F|%a|%i|%s|%y|%z' {} + | sort > "$root/marker-metadata.before"
  else
    flags+=(--allow-first-deploy)
  fi
  for service in caddy frontend ocelotapigw accountservice courierjobservice; do
    if [ "$release" = known-green ]; then value=running; else value=exited; fi
    printf '%s\n' "$value" > "$root/services/$service"
  done
  cp -a "$root/services" "$root/services.before"
  cat > "$root/deploy.env" <<ENV
TRANXIT_FRONTEND_DIR=$FRONTEND_REPO
TRANXIT_MARKER_DIR=$root/markers
TRANXIT_ADMISSION_DIR=$root/markers/admission-$target
TRANXIT_BACKUP_DIR=$root/backups
TRANXIT_BACKUP_RETENTION_DAYS=14
TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE=false
TRANXIT_DEPLOY_TEST_FORCE_POST_ADMISSION_FAILURE=false
ENV
  for name in TRANXIT_EGRESS_PROBE_URL PUBLIC_APP_URL STAGING_APP_URL; do
    value="https://$name.example.invalid"
    if [ "$name" = "$setting" ]; then
      case "$problem" in
        unset) continue ;;
        empty) value='' ;;
        http) value='http://must-not-log.example.invalid/sensitive-probe-path' ;;
        missing-host) value='https:///sensitive-probe-path' ;;
        whitespace) value='https://must-not-log.example.invalid/secret path' ;;
        userinfo) value='https://not-a-credential@must-not-log.example.invalid' ;;
      esac
    fi
    printf '%s=%q\n' "$name" "$value" >> "$root/deploy.env"
  done
  case "$problem" in
    relative-path) printf '%s=relative-path\n' "$setting" >> "$root/deploy.env" ;;
    missing-repo)
      printf '%s=%q\n' "$setting" "$root/missing-repo" >> "$root/deploy.env"
      expected_status=1
      ;;
    production-mailpit)
      printf '%s=mail.example.invalid\n' "$setting" >> "$root/deploy.env"
      expected_status=1
      ;;
  esac
  : > "$DOCKER_LOG"
  : > "$root/git-events"
  (
    unset PUBLIC_APP_URL STAGING_APP_URL TRANXIT_EGRESS_PROBE_URL
    unset MAILPIT_DOMAIN MAILPIT_BASIC_AUTH_USER MAILPIT_BASIC_AUTH_HASH TRANXIT_E2E_MAIL_INBOX
    PATH="$MOCK_BIN:$PATH" TRANXIT_TEST_GIT_LOG="$root/git-events" \
      TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" TRANXIT_TEST_STATE_DIR="$root/services" \
      TRANXIT_ENV_FILE="$root/deploy.env" \
      "$CONTROLLER_ENTRYPOINT" --env "$target" --sha "$CANDIDATE_BACKEND_SHA" \
        --frontend-ref "$CANDIDATE_FRONTEND_SHA" --migration-policy expand-contract "${flags[@]}"
  ) > "$root/result.out" 2>&1 || status=$?

  if [ "$problem" = valid ]; then
    assert_status 0 "$status" "valid $target $release preflight"
    assert_contains "$DOCKER_LOG" topology
    assert_contains "$DOCKER_LOG" http-smoke
    assert_contains "$root/markers/last-$target-green" "backend_sha=$CANDIDATE_BACKEND_SHA"
    [ -f "$root/markers/admission-$target/open" ] || fail "Valid preflight did not complete admission"
  else
    [ ! -s "$DOCKER_LOG" ] || fail "Invalid preflight reached Docker ($target/$release/$setting/$problem)"
    [ ! -s "$root/git-events" ] || fail "Invalid preflight mutated Git ($target/$release/$setting/$problem)"
    [ "$(git -C "$BACKEND_REPO" rev-parse HEAD)" = "$GREEN_BACKEND_SHA" ] || fail 'Preflight changed backend HEAD'
    [ "$(git -C "$FRONTEND_REPO" rev-parse HEAD)" = "$GREEN_FRONTEND_SHA" ] || fail 'Preflight changed frontend HEAD'
    if [ "$release" = known-green ]; then
      diff -r "$root/markers.before" "$root/markers" >/dev/null || fail 'Preflight changed marker/lock/admission files'
      find "$root/markers" -exec stat -c '%n|%F|%a|%i|%s|%y|%z' {} + | sort > "$root/marker-metadata.after"
      cmp "$root/marker-metadata.before" "$root/marker-metadata.after" >/dev/null || fail 'Preflight changed marker/lock/admission metadata'
    else
      [ ! -e "$root/markers" ] || fail 'Preflight created first-deploy state'
    fi
    diff -r "$root/services.before" "$root/services" >/dev/null || fail 'Preflight changed service states'
    [ ! -e "$root/backups" ] || fail 'Preflight created backups'
    assert_status "$expected_status" "$status" "invalid $target $release $setting $problem"
    assert_contains "$root/result.out" "$setting"
    if grep -E 'must-not-log|sensitive-probe-path|secret path|not-a-credential' "$root/result.out" >/dev/null; then
      fail 'Preflight disclosed an invalid setting value'
    fi
  fi
}

preflight_contract() {
  # UC-NFR-9, A-1 - T-NFR-9.RequiredEnvPreflight.
  local target release setting problem rejected=0 accepted=0
  for target in staging production; do
    for release in known-green first; do
      for setting in TRANXIT_EGRESS_PROBE_URL PUBLIC_APP_URL STAGING_APP_URL; do
        for problem in empty unset http missing-host whitespace userinfo; do
          preflight_case "$target" "$release" "$setting" "$problem"
          rejected=$((rejected + 1))
        done
      done
      for setting in TRANXIT_MARKER_DIR TRANXIT_ADMISSION_DIR; do
        preflight_case "$target" "$release" "$setting" relative-path
        rejected=$((rejected + 1))
      done
      preflight_case "$target" "$release" TRANXIT_FRONTEND_DIR missing-repo
      rejected=$((rejected + 1))
      if [ "$target" = production ]; then
        preflight_case "$target" "$release" MAILPIT_DOMAIN production-mailpit
        rejected=$((rejected + 1))
      fi
      preflight_case "$target" "$release" all valid
      accepted=$((accepted + 1))
    done
  done
  unset PREFLIGHT_OUTPUT
  : > "$DOCKER_LOG"
  echo "PASS T-NFR-9.RequiredEnvPreflight ($rejected invalid configurations rejected without mutation; $accepted valid env-file deploys)"
}

preflight_credibility() {
  # UC-NFR-9, A-1 - T-NFR-9.RequiredEnvPreflightCredibility (private script copy only).
  local mutated="$TMP_ROOT/mutated-deploy.sh"
  sed '/^validate_deploy_inputs || exit \$?$/d' "$DEPLOY_SOURCE" > "$mutated"
  if cmp -s "$DEPLOY_SOURCE" "$mutated"; then fail 'Preflight credibility mutation did not change the script'; fi
  if TRANXIT_TEST_DEPLOY_SOURCE="$mutated" bash "$SCRIPT_DIR/deploy-safety-test.sh" --preflight \
    > "$TMP_ROOT/preflight-red.out" 2>&1; then
    fail 'Preflight-removal mutation passed'
  fi
  assert_contains "$TMP_ROOT/preflight-red.out" 'FAIL: Invalid preflight reached Docker (staging/known-green/TRANXIT_EGRESS_PROBE_URL/empty)'
  echo 'RED T-NFR-9.RequiredEnvPreflight: missing preflight reached Docker before rejection'
  diff -u --label original/deploy.sh --label mutated/deploy.sh "$DEPLOY_SOURCE" "$mutated" || [ "$?" -eq 1 ]
  TRANXIT_TEST_DEPLOY_SOURCE="$DEPLOY_SOURCE" bash "$SCRIPT_DIR/deploy-safety-test.sh" --preflight
  echo 'GREEN T-NFR-9.RequiredEnvPreflightCredibility: original source restored, all preflight cases pass'
}

artifact_entrypoint_contract() {
  # UC-NFR-9 - T-NFR-9.AdmissionArtifactEntrypoint (both deploy profiles; no Docker on refusal).
  local target role root selected green bad status service
  git -C "$BACKEND_REPO" checkout -q -b unsupported-admission "$CANDIDATE_BACKEND_SHA"
  sed -i 's/import public_tranxit_site/import tranxit_site/' "$BACKEND_REPO/TranXit/ops/Caddyfile"{,.staging}
  git -C "$BACKEND_REPO" add TranXit/ops/Caddyfile TranXit/ops/Caddyfile.staging
  git -C "$BACKEND_REPO" commit -qm 'fixture: ungated descendant preserving gate snippet'
  bad="$(git -C "$BACKEND_REPO" rev-parse HEAD)"
  git -C "$BACKEND_REPO" push origin unsupported-admission >/dev/null 2>&1
  for target in staging production; do
    for role in candidate rollback; do
      root="$TMP_ROOT/artifact-$target-$role"
      mkdir -p "$root/markers/admission-$target" "$root/services"
      selected="$CANDIDATE_BACKEND_SHA"; green="$GREEN_BACKEND_SHA"
      if [ "$role" = candidate ]; then selected="$bad"; else green="$bad"; fi
      printf 'environment=%s\nbackend_sha=%s\nfrontend_sha=%s\nadmission_policy=private-smoke-v1\n' \
        "$target" "$green" "$GREEN_FRONTEND_SHA" > "$root/markers/last-$target-green"
      printf 'admitted\n' > "$root/markers/admission-$target/open"
      : > "$root/markers/deploy-$target.lock"
      chmod 600 "$root/markers/deploy-$target.lock"
      cp -a "$root/markers" "$root/markers.before"
      for service in caddy frontend ocelotapigw accountservice courierjobservice; do echo running > "$root/services/$service"; done
      cp -a "$root/services" "$root/services.before"
      cat > "$root/deploy.env" <<ENV
TRANXIT_FRONTEND_DIR=$FRONTEND_REPO
TRANXIT_MARKER_DIR=$root/markers
TRANXIT_ADMISSION_DIR=$root/markers/admission-$target
TRANXIT_BACKUP_DIR=$root/backups
PUBLIC_APP_URL=https://production.example.invalid
STAGING_APP_URL=https://staging.example.invalid
TRANXIT_EGRESS_PROBE_URL=https://probe.example.invalid
ENV
      git -C "$BACKEND_REPO" checkout -q --detach "$GREEN_BACKEND_SHA"
      git -C "$FRONTEND_REPO" checkout -q --detach "$GREEN_FRONTEND_SHA"
      : > "$DOCKER_LOG"; : > "$root/git-events"
      status=0
      (
        unset MAILPIT_DOMAIN MAILPIT_BASIC_AUTH_USER MAILPIT_BASIC_AUTH_HASH TRANXIT_E2E_MAIL_INBOX
        unset TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE TRANXIT_DEPLOY_TEST_FORCE_POST_ADMISSION_FAILURE
        PATH="$MOCK_BIN:$PATH" TRANXIT_TEST_GIT_LOG="$root/git-events" \
          TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" TRANXIT_TEST_STATE_DIR="$root/services" \
          TRANXIT_ENV_FILE="$root/deploy.env" "$CONTROLLER_ENTRYPOINT" \
          --env "$target" --sha "$selected" --frontend-ref "$CANDIDATE_FRONTEND_SHA" --migration-policy restore-required
      ) > "$root/result.out" 2>&1 || status=$?
      assert_status 2 "$status" "$target $role artifact refusal"
      assert_contains "$root/result.out" "$role admission contract rejected:"
      [ ! -s "$DOCKER_LOG" ] || fail 'Artifact refusal reached Docker'
      if grep -F ' checkout ' "$root/git-events" >/dev/null; then fail 'Artifact refusal checked out an unsupported release'; fi
      [ "$(git -C "$BACKEND_REPO" rev-parse HEAD)" = "$GREEN_BACKEND_SHA" ] || fail 'Artifact refusal changed backend HEAD'
      [ "$(git -C "$FRONTEND_REPO" rev-parse HEAD)" = "$GREEN_FRONTEND_SHA" ] || fail 'Artifact refusal changed frontend HEAD'
      diff -r "$root/markers.before" "$root/markers" >/dev/null || fail 'Artifact refusal changed marker or admission contents'
      diff -r "$root/services.before" "$root/services" >/dev/null || fail 'Artifact refusal changed running services'
      [ ! -e "$root/backups" ] || fail 'Artifact refusal reached backup'
    done
  done
  echo 'PASS T-NFR-9.AdmissionArtifactEntrypoint (4 profile/target cases; no Docker or checkout)'
}

controller_next_invocation_case() {
  # UC-NFR-9 - T-NFR-9.InstalledControllerNextInvocation.
  local target="$1" role="$2" root="$TMP_ROOT/controller-next-$1-$2"
  local repo="$root/backend" frontend="$root/frontend" helper service status
  local replaced_green replaced_candidate bad green expected_backend expected_frontend
  local edge_path failed_start=false first_status=0
  mkdir -p "$root/markers/admission-$target" "$root/services"
  git clone -q --bare --no-hardlinks "$TMP_ROOT/backend-origin.git" "$root/backend-origin.git"
  git -c core.autocrlf=false clone -q "$root/backend-origin.git" "$repo"
  git -c core.autocrlf=false clone -q "$TMP_ROOT/frontend-origin.git" "$frontend"
  git -C "$repo" config user.name 'Deploy Safety Test'
  git -C "$repo" config user.email 'deploy-safety@example.invalid'
  git -C "$repo" config core.autocrlf false
  git -C "$frontend" config core.autocrlf false

  git -C "$repo" checkout -q -b replaced-controller "$GREEN_BACKEND_SHA"
  for helper in deploy admission-contract backup restore smoke verify-production-topology; do
    printf '#!/usr/bin/env bash\nprintf "untrusted-app-controller %s\\n" >> "$TRANXIT_TEST_DOCKER_LOG"\nexit 77\n' \
      "$helper" > "$repo/TranXit/scripts/$helper.sh"
  done
  # A downgraded app entrypoint could silently accept an unchecked next release.
  sed -i 's/^exit 77$/exit 0/' "$repo/TranXit/scripts/deploy.sh"
  git -C "$repo" add TranXit/scripts
  git -C "$repo" commit -qm 'fixture: compatible rollback replaces all app controller scripts'
  replaced_green="$(git -C "$repo" rev-parse HEAD)"
  printf 'compatible candidate with replaced controller scripts\n' > "$repo/release.txt"
  git -C "$repo" add release.txt
  git -C "$repo" commit -qm 'fixture: compatible candidate retains app controller replacements'
  replaced_candidate="$(git -C "$repo" rev-parse HEAD)"
  edge_path=TranXit/ops/Caddyfile
  if [ "$target" = staging ]; then edge_path=TranXit/ops/Caddyfile.staging; fi
  sed -i 's/import public_tranxit_site/import tranxit_site/g' "$repo/$edge_path"
  git -C "$repo" add "$edge_path"
  git -C "$repo" commit -qm 'fixture: next release bypasses public admission'
  bad="$(git -C "$repo" rev-parse HEAD)"
  git -C "$repo" push -q origin replaced-controller
  git -C "$repo" checkout -q --detach "$GREEN_BACKEND_SHA"
  git -C "$frontend" checkout -q --detach "$GREEN_FRONTEND_SHA"

  green="$GREEN_BACKEND_SHA"
  expected_backend="$replaced_candidate"
  expected_frontend="$CANDIDATE_FRONTEND_SHA"
  if [ "$role" = rollback ]; then
    green="$replaced_green"
    expected_backend="$replaced_green"
    expected_frontend="$GREEN_FRONTEND_SHA"
    failed_start=true
    first_status=47
  fi
  printf 'environment=%s\nbackend_sha=%s\nfrontend_sha=%s\nadmission_policy=private-smoke-v1\n' \
    "$target" "$green" "$GREEN_FRONTEND_SHA" > "$root/markers/last-$target-green"
  cp "$root/markers/last-$target-green" "$root/initial-marker"
  printf 'admitted\n' > "$root/markers/admission-$target/open"
  for service in caddy frontend ocelotapigw accountservice courierjobservice; do
    printf 'running\n' > "$root/services/$service"
  done
  cat > "$root/deploy.env" <<ENV
TRANXIT_FRONTEND_DIR=$frontend
TRANXIT_MARKER_DIR=$root/markers
TRANXIT_ADMISSION_DIR=$root/markers/admission-$target
TRANXIT_BACKUP_DIR=$root/backups
TRANXIT_BACKUP_RETENTION_DAYS=14
PUBLIC_APP_URL=https://production.example.invalid
STAGING_APP_URL=https://staging.example.invalid
TRANXIT_EGRESS_PROBE_URL=https://probe.example.invalid
TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE=false
TRANXIT_DEPLOY_TEST_FORCE_POST_ADMISSION_FAILURE=false
ENV
  sha256sum "$CONTROLLER_SCRIPTS/"*.sh > "$root/controller.before"
  (
    unset MAILPIT_DOMAIN MAILPIT_BASIC_AUTH_USER MAILPIT_BASIC_AUTH_HASH TRANXIT_E2E_MAIL_INBOX
    unset TRANXIT_TEST_FAIL_BACKUP TRANXIT_TEST_FAIL_ROLLBACK_BUILD TRANXIT_TEST_FAIL_SECOND_RESTORE
    unset TRANXIT_TEST_FAIL_STORAGE_PREP TRANXIT_TEST_PUBLIC_EDGE_REPLY TRANXIT_TEST_PUBLIC_EDGE_STATUS
    export PATH="$MOCK_BIN:$PATH"
    export TRANXIT_DEPLOY_PROJECT_DIR="$repo/TranXit" TRANXIT_ENV_FILE="$root/deploy.env"
    export TRANXIT_TEST_DOCKER_LOG="$root/docker-events" TRANXIT_TEST_GIT_LOG="$root/git-events"
    export TRANXIT_TEST_STATE_DIR="$root/services"
    : > "$root/docker-events"
    : > "$root/git-events"
    status=0
    TRANXIT_TEST_FAIL_CANDIDATE_START="$failed_start" "$CONTROLLER_ENTRYPOINT" \
      --env "$target" --sha "$replaced_candidate" --frontend-ref "$CANDIDATE_FRONTEND_SHA" \
      --migration-policy restore-required > "$root/attempt-1.out" 2>&1 || status=$?
    assert_status "$first_status" "$status" "$target $role first controller invocation"
    if [ "$role" = rollback ]; then
      assert_contains "$root/attempt-1.out" 'Known-green release restored successfully'
      assert_contains "$root/docker-events" startup-failure
      assert_contains "$root/docker-events" restore-sql
      cmp "$root/initial-marker" "$root/markers/last-$target-green" >/dev/null ||
        fail 'Controller rollback changed the known-green marker'
    else
      assert_contains "$root/attempt-1.out" 'Deploy completed.'
      if grep -F restore-sql "$root/docker-events" >/dev/null; then fail 'Accepted controller fixture unexpectedly restored data'; fi
    fi
    assert_contains "$root/docker-events" backup
    assert_contains "$root/docker-events" topology
    assert_contains "$root/docker-events" http-smoke
    if grep -F untrusted-app-controller "$root/docker-events" >/dev/null; then fail 'First invocation executed an app controller replacement'; fi
    [ "$(git -C "$repo" rev-parse HEAD)" = "$expected_backend" ] || fail 'First invocation retained the wrong backend checkout'
    [ "$(git -C "$frontend" rev-parse HEAD)" = "$expected_frontend" ] || fail 'First invocation retained the wrong frontend checkout'
    assert_contains "$root/markers/last-$target-green" "backend_sha=$expected_backend"
    [ -f "$root/markers/admission-$target/open" ] || fail 'First invocation did not admit the verified release'
    [ ! -e "$root/markers/deploy-$target-admitted" ] || fail 'First invocation left unresolved admission'
    for helper in deploy admission-contract backup restore smoke verify-production-topology; do
      assert_contains "$repo/TranXit/scripts/$helper.sh" "untrusted-app-controller $helper"
    done
    for service in caddy frontend ocelotapigw accountservice courierjobservice; do
      [ "$(cat "$root/services/$service")" = running ] || fail 'First invocation left a model service stopped'
    done

    # Preserve the exact accepted/recovered app checkout. Only observation logs are
    # cleared between attempts; no trusted app script is copied or checked out again.
    cp -a "$repo/TranXit" "$root/checkout.before"
    cp "$repo/release.txt" "$root/release.before"
    cp "$frontend/release.txt" "$root/frontend.before"
    git -C "$repo" status --porcelain=v1 --untracked-files=all > "$root/git-status.before"
    cp -a "$root/markers" "$root/markers.before"
    cp -a "$root/services" "$root/services.before"
    cp -a "$root/backups" "$root/backups.before"
    # Lock acquisition can update lock metadata, but not release/admission state.
    find "$root/markers" ! -path "$root/markers/deploy-$target.lock" \
      -exec stat -c '%n|%F|%a|%i|%s|%y|%z' {} + | sort > "$root/state-metadata.before"
    : > "$root/docker-events"
    : > "$root/git-events"
    status=0
    TRANXIT_TEST_FAIL_CANDIDATE_START=false "$CONTROLLER_ENTRYPOINT" \
      --env "$target" --sha "$bad" --frontend-ref "$CANDIDATE_FRONTEND_SHA" \
      --migration-policy restore-required > "$root/attempt-2.out" 2>&1 || status=$?
    assert_status 2 "$status" "$target $role second controller invocation"
    assert_contains "$root/attempt-2.out" "candidate admission contract rejected: $edge_path (checksum mismatch)"
    [ ! -s "$root/docker-events" ] || fail 'Second invocation reached Docker or an app controller replacement'
    if grep -E ' (checkout|switch|reset|clean) ' "$root/git-events" >/dev/null; then fail 'Second invocation changed a deployment checkout'; fi
    [ "$(git -C "$repo" rev-parse HEAD)" = "$expected_backend" ] || fail 'Second invocation changed backend HEAD'
    [ "$(git -C "$frontend" rev-parse HEAD)" = "$expected_frontend" ] || fail 'Second invocation changed frontend HEAD'
    diff -r "$root/checkout.before" "$repo/TranXit" >/dev/null || fail 'Second invocation changed app checkout bytes'
    cmp "$root/release.before" "$repo/release.txt" >/dev/null || fail 'Second invocation changed the backend release file'
    cmp "$root/frontend.before" "$frontend/release.txt" >/dev/null || fail 'Second invocation changed frontend bytes'
    git -C "$repo" status --porcelain=v1 --untracked-files=all > "$root/git-status.after"
    cmp "$root/git-status.before" "$root/git-status.after" >/dev/null || fail 'Second invocation changed checkout status'
    diff -r "$root/markers.before" "$root/markers" >/dev/null || fail 'Second invocation changed marker/admission contents'
    diff -r "$root/services.before" "$root/services" >/dev/null || fail 'Second invocation changed running services'
    diff -r "$root/backups.before" "$root/backups" >/dev/null || fail 'Second invocation changed paired backups'
    find "$root/markers" ! -path "$root/markers/deploy-$target.lock" \
      -exec stat -c '%n|%F|%a|%i|%s|%y|%z' {} + | sort > "$root/state-metadata.after"
    cmp "$root/state-metadata.before" "$root/state-metadata.after" >/dev/null || fail 'Second invocation changed release/admission metadata'
    sha256sum "$CONTROLLER_SCRIPTS/"*.sh > "$root/controller.after"
    cmp "$root/controller.before" "$root/controller.after" >/dev/null || fail 'App checkout changed the installed controller'
  ) || {
    PREFLIGHT_OUTPUT="$root/attempt-2.out"
    [ -f "$PREFLIGHT_OUTPUT" ] || PREFLIGHT_OUTPUT="$root/attempt-1.out"
    cat "$root/attempt-1.out" >&2
    fail "$target $role next-invocation controller regression"
  }
  echo "PASS T-NFR-9.InstalledControllerNextInvocation ($target/$role; compatible scripts retained, next artifact rejected)"
}

controller_next_invocation_contract() {
  local target role
  for target in staging production; do
    for role in candidate rollback; do
      controller_next_invocation_case "$target" "$role"
    done
  done
  echo 'PASS T-NFR-9.InstalledControllerNextInvocationMatrix (4 cases / 8 deploy attempts)'
}

write_env_file() {
  local backup_dir="$1"
  ENV_FILE="$TMP_ROOT/deploy.env"
  cat > "$ENV_FILE" <<ENV
TRANXIT_FRONTEND_DIR=$FRONTEND_REPO
TRANXIT_BACKUP_DIR=$backup_dir
TRANXIT_BACKUP_RETENTION_DAYS=14
TRANXIT_MARKER_DIR=$MARKER_DIR
PUBLIC_APP_URL=https://production.example.invalid
STAGING_APP_URL=https://staging.example.invalid
TRANXIT_EGRESS_PROBE_URL=https://probe.example.invalid
TRANXIT_ALLOW_FAILURE_INJECTION=true
TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE=${TRANXIT_TEST_FORCE_SMOKE_FAILURE:-true}
TRANXIT_DEPLOY_TEST_FORCE_POST_ADMISSION_FAILURE=${TRANXIT_TEST_POST_ADMISSION_FAILURE:-false}
ENV
}

run_failed_deploy() {
  local policy="$1"
  local output="$2"
  set +e
  PATH="$MOCK_BIN:$PATH" \
    TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" \
    TRANXIT_TEST_FAIL_BACKUP="${TRANXIT_TEST_FAIL_BACKUP:-false}" \
    TRANXIT_TEST_FAIL_ROLLBACK_BUILD="${TRANXIT_TEST_FAIL_ROLLBACK_BUILD:-false}" \
    TRANXIT_TEST_FAIL_SECOND_RESTORE="${TRANXIT_TEST_FAIL_SECOND_RESTORE:-false}" \
    TRANXIT_TEST_FAIL_CANDIDATE_START="${TRANXIT_TEST_FAIL_CANDIDATE_START:-false}" \
    TRANXIT_ENV_FILE="$ENV_FILE" \
    "$CONTROLLER_ENTRYPOINT" \
      --env staging \
      --sha "$CANDIDATE_BACKEND_SHA" \
      --frontend-ref "$CANDIDATE_FRONTEND_SHA" \
      --migration-policy "$policy" >"$output" 2>&1
  DEPLOY_STATUS=$?
  set -e
}

assert_rollback_result() {
  local output="$1"
  local expected_status="${2:-1}"
  assert_status "$expected_status" "$DEPLOY_STATUS" "failed deploy"
  cmp "$MARKER_SNAPSHOT" "$MARKER_FILE" >/dev/null ||
    fail "Known-green marker changed after a failed deploy"
  [ ! -e "$MARKER_FILE.prev" ] ||
    fail "Previous marker was written for a failed candidate"
  [ "$(git -C "$BACKEND_REPO" rev-parse HEAD)" = "$GREEN_BACKEND_SHA" ] ||
    fail "Backend did not return to the known-green SHA"
  [ "$(git -C "$FRONTEND_REPO" rev-parse HEAD)" = "$GREEN_FRONTEND_SHA" ] ||
    fail "Frontend did not return to the known-green SHA"
  assert_contains "$output" "Known-green release restored successfully"
}

assert_backup_precedes_migration() {
  local backup_line
  local migration_line
  backup_line="$(grep -n -m1 '^backup$' "$DOCKER_LOG" | cut -d: -f1 || true)"
  migration_line="$(grep -n -m1 '^migration$' "$DOCKER_LOG" | cut -d: -f1 || true)"
  [ -n "$backup_line" ] && [ -n "$migration_line" ] ||
    fail "Backup/migration events were not captured"
  [ "$backup_line" -lt "$migration_line" ] ||
    fail "Migration began before the paired backup completed"
}

if [ "$MODE" = --preflight-credibility ]; then preflight_credibility; exit; fi

create_backend_release_repo
create_frontend_release_repo
create_controller_fixture
create_mock_commands
preflight_contract
if [ "$MODE" = --preflight ]; then exit; fi
artifact_entrypoint_contract
controller_next_invocation_contract

MARKER_DIR="$TMP_ROOT/markers"
MARKER_FILE="$MARKER_DIR/last-staging-green"
MARKER_SNAPSHOT="$TMP_ROOT/marker.before"
mkdir -p "$MARKER_DIR"
cat > "$MARKER_FILE" <<MARKER
format_version=3
environment=staging
backend_sha=$GREEN_BACKEND_SHA
frontend_ref=$GREEN_FRONTEND_SHA
frontend_sha=$GREEN_FRONTEND_SHA
image_tag=${GREEN_BACKEND_SHA:0:12}
migration_policy=expand-contract
admission_policy=private-smoke-v1
pre_migration_backup_manifest=
account_migration_before=none
account_migration_after=none
courier_migration_before=none
courier_migration_after=none
deployed_at_utc=2026-07-27T00:00:00Z
MARKER
cp "$MARKER_FILE" "$MARKER_SNAPSHOT"

write_env_file "$TMP_ROOT/backups-expand"
set +e
PATH="$MOCK_BIN:$PATH" \
  TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" \
  TRANXIT_ENV_FILE="$ENV_FILE" \
  "$CONTROLLER_ENTRYPOINT" \
    --env production \
    --sha "$CANDIDATE_BACKEND_SHA" \
    --frontend-ref "$CANDIDATE_FRONTEND_SHA" \
    --migration-policy expand-contract >"$TMP_ROOT/production-injection.out" 2>&1
PRODUCTION_INJECTION_STATUS=$?
set -e
assert_status 1 "$PRODUCTION_INJECTION_STATUS" "production failure-injection guard"
assert_contains "$TMP_ROOT/production-injection.out" "Failure injection is allowed only on staging"
[ ! -s "$DOCKER_LOG" ] ||
  fail "Production failure injection reached Docker instead of failing fast"

TRANXIT_TEST_FORCE_SMOKE_FAILURE=false TRANXIT_TEST_POST_ADMISSION_FAILURE=true \
  write_env_file "$TMP_ROOT/backups-production-guard"
set +e
PATH="$MOCK_BIN:$PATH" TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" TRANXIT_ENV_FILE="$ENV_FILE" \
  "$CONTROLLER_ENTRYPOINT" --env production --sha "$CANDIDATE_BACKEND_SHA" \
    --frontend-ref "$CANDIDATE_FRONTEND_SHA" --migration-policy expand-contract \
    >"$TMP_ROOT/production-post-admission-injection.out" 2>&1
PRODUCTION_INJECTION_STATUS=$?
set -e
assert_status 1 "$PRODUCTION_INJECTION_STATUS" "production post-admission injection guard"
assert_contains "$TMP_ROOT/production-post-admission-injection.out" "Failure injection is allowed only on staging"
[ ! -s "$DOCKER_LOG" ] || fail "Production post-admission injection reached Docker"
write_env_file "$TMP_ROOT/backups-expand"

: > "$DOCKER_LOG"
FIRST_MARKER_DIR="$TMP_ROOT/first-deploy-markers"
FIRST_ENV_FILE="$TMP_ROOT/first-deploy.env"
cat > "$FIRST_ENV_FILE" <<ENV
TRANXIT_FRONTEND_DIR=$FRONTEND_REPO
TRANXIT_BACKUP_DIR=$TMP_ROOT/backups-first-deploy
TRANXIT_BACKUP_RETENTION_DAYS=14
TRANXIT_MARKER_DIR=$FIRST_MARKER_DIR
PUBLIC_APP_URL=https://production.example.invalid
STAGING_APP_URL=https://staging.example.invalid
TRANXIT_EGRESS_PROBE_URL=https://probe.example.invalid
TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE=false
ENV
PATH="$MOCK_BIN:$PATH" \
  TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" \
  TRANXIT_TEST_DATABASE_PRESENCE='0|0' \
  TRANXIT_ENV_FILE="$FIRST_ENV_FILE" \
  "$CONTROLLER_ENTRYPOINT" \
    --env staging \
    --sha "$CANDIDATE_BACKEND_SHA" \
    --frontend-ref "$CANDIDATE_FRONTEND_SHA" \
    --migration-policy expand-contract \
    --allow-first-deploy >"$TMP_ROOT/first-deploy.out" 2>&1
assert_contains "$DOCKER_LOG" "CREATE DATABASE [Tranxit_Account]"
assert_contains "$DOCKER_LOG" "CREATE DATABASE [Tranxit_CourierJob]"
assert_contains "$DOCKER_LOG" "up -d --no-deps --wait --wait-timeout 300 mailpit"
assert_contains "$FIRST_MARKER_DIR/last-staging-green" "backend_sha=$CANDIDATE_BACKEND_SHA"
assert_contains "$FIRST_MARKER_DIR/last-staging-green" "admission_policy=private-smoke-v1"
[ -f "$FIRST_MARKER_DIR/admission-staging/open" ] || fail "Successful first deploy did not admit traffic"
[ ! -e "$FIRST_MARKER_DIR/deploy-staging-admitted" ] || fail "Successful first deploy left an unresolved admission record"

: > "$DOCKER_LOG"
mv "$MARKER_FILE" "$MARKER_FILE.hidden"
set +e
PATH="$MOCK_BIN:$PATH" \
  TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" \
  TRANXIT_ENV_FILE="$ENV_FILE" \
  "$CONTROLLER_ENTRYPOINT" \
    --env staging \
    --sha "$CANDIDATE_BACKEND_SHA" \
    --frontend-ref "$CANDIDATE_FRONTEND_SHA" \
    --migration-policy expand-contract >"$TMP_ROOT/first-deploy-guard.out" 2>&1
FIRST_DEPLOY_STATUS=$?
set -e
mv "$MARKER_FILE.hidden" "$MARKER_FILE"
assert_status 1 "$FIRST_DEPLOY_STATUS" "unacknowledged first deploy"
[ ! -s "$DOCKER_LOG" ] ||
  fail "Unacknowledged first deploy reached Docker"

EXPAND_OUTPUT="$TMP_ROOT/expand-contract.out"
# UC-NFR-9 - T-NFR-9.DeployRollbackContract
run_failed_deploy expand-contract "$EXPAND_OUTPUT"
assert_rollback_result "$EXPAND_OUTPUT"
assert_backup_precedes_migration
if grep -F 'restore-sql ' "$DOCKER_LOG" >/dev/null; then
  fail "Expand-contract rollback unexpectedly restored a database"
fi
echo "PASS T-NFR-9.DeployRollbackContract"

: > "$DOCKER_LOG"
write_env_file "$TMP_ROOT/backups-start-failure"
# UC-NFR-9, F-02 - T-NFR-9.HelperFailurePropagation (actual deploy entry point)
TRANXIT_TEST_FAIL_CANDIDATE_START=true \
  run_failed_deploy expand-contract "$TMP_ROOT/start-failure.out"
assert_rollback_result "$TMP_ROOT/start-failure.out" 47
assert_contains "$TMP_ROOT/start-failure.out" "Candidate deploy failed at line"
assert_contains "$DOCKER_LOG" startup-failure
[ "$(grep -c '^http-smoke$' "$DOCKER_LOG")" -eq 1 ] ||
  fail "Candidate helper failure was masked and reached HTTP smoke"
echo "PASS T-NFR-9.HelperFailurePropagation (actual deploy entry point)"

: > "$DOCKER_LOG"
write_env_file "$TMP_ROOT/backups-restore"
RESTORE_OUTPUT="$TMP_ROOT/restore-required.out"
run_failed_deploy restore-required "$RESTORE_OUTPUT"
assert_rollback_result "$RESTORE_OUTPUT"
assert_backup_precedes_migration
assert_contains "$DOCKER_LOG" "REPLACE"
assert_contains "$RESTORE_OUTPUT" "restoring the paired pre-migration backup"

# UC-NFR-9 - T-NFR-9.TrustedControllerSnapshot (candidate helper replacements never execute).
ORIGINAL_CANDIDATE="$CANDIDATE_BACKEND_SHA"
git -C "$BACKEND_REPO" checkout -q -b candidate-helper-drift "$ORIGINAL_CANDIDATE"
for helper in backup restore smoke verify-production-topology; do
  printf '#!/usr/bin/env bash\necho untrusted-candidate-helper >> "$TRANXIT_TEST_DOCKER_LOG"\nexit 77\n' > "$BACKEND_REPO/TranXit/scripts/$helper.sh"
done
git -C "$BACKEND_REPO" add TranXit/scripts
git -C "$BACKEND_REPO" commit -qm 'fixture: candidate helper replacements'
CANDIDATE_BACKEND_SHA="$(git -C "$BACKEND_REPO" rev-parse HEAD)"
git -C "$BACKEND_REPO" push origin candidate-helper-drift >/dev/null 2>&1
git -C "$BACKEND_REPO" checkout -q --detach "$GREEN_BACKEND_SHA"
: > "$DOCKER_LOG"
write_env_file "$TMP_ROOT/backups-trusted-controller"
run_failed_deploy restore-required "$TMP_ROOT/trusted-controller.out"
assert_rollback_result "$TMP_ROOT/trusted-controller.out"
assert_contains "$DOCKER_LOG" backup
assert_contains "$DOCKER_LOG" restore-sql
assert_contains "$DOCKER_LOG" http-smoke
if grep -F untrusted-candidate-helper "$DOCKER_LOG" >/dev/null; then fail 'Candidate substituted the trusted controller'; fi
CANDIDATE_BACKEND_SHA="$ORIGINAL_CANDIDATE"
echo 'PASS T-NFR-9.TrustedControllerSnapshot (backup, restore, topology and smoke frozen before checkout)'

# UC-NFR-9 - T-NFR-9.UnverifiedPublicEdgeRefusesRestore (actual deploy recovery dispatcher).
for edge_failure in open tls; do
  : > "$DOCKER_LOG"
  write_env_file "$TMP_ROOT/backups-unverified-$edge_failure"
  if [ "$edge_failure" = open ]; then
    TRANXIT_TEST_PUBLIC_EDGE_REPLY='roles|200|' run_failed_deploy restore-required "$TMP_ROOT/unverified-$edge_failure.out"
  else
    TRANXIT_TEST_PUBLIC_EDGE_REPLY='|000|' TRANXIT_TEST_PUBLIC_EDGE_STATUS=60 \
      run_failed_deploy restore-required "$TMP_ROOT/unverified-$edge_failure.out"
  fi
  assert_status 1 "$DEPLOY_STATUS" "unverified public edge $edge_failure"
  assert_contains "$TMP_ROOT/unverified-$edge_failure.out" 'Public origin 1 did not prove closed admission'
  assert_contains "$TMP_ROOT/unverified-$edge_failure.out" 'AUTOMATIC RESTORE REFUSED'
  assert_contains "$TMP_ROOT/unverified-$edge_failure.out" 'RECOVERY FENCED'
  cmp "$MARKER_SNAPSHOT" "$MARKER_FILE" >/dev/null || fail 'Unverified edge advanced the green marker'
  [ -f "$MARKER_DIR/deploy-staging-admitted" ] || fail 'Unverified edge omitted persistent uncertainty'
  [ ! -e "$MARKER_DIR/admission-staging/open" ] || fail 'Unverified edge opened the gate'
  if grep -F 'restore-sql ' "$DOCKER_LOG" >/dev/null; then fail 'Unverified edge restored a database'; fi
  for service in caddy frontend ocelotapigw accountservice courierjobservice; do
    [ "$(cat "$TRANXIT_TEST_STATE_DIR/$service")" = exited ] || fail 'Unverified edge left an app running'
  done
  # Reset only this command-model fixture; production retains the blocking journal for an operator.
  rm -- "$MARKER_DIR/deploy-staging-admitted"
done
echo 'PASS T-NFR-9.UnverifiedPublicEdgeRefusesRestore (open response and TLS error fence without restore)'

: > "$DOCKER_LOG"
write_env_file "$TMP_ROOT/backups-failed"
BACKUP_FAILURE_OUTPUT="$TMP_ROOT/backup-failure.out"
TRANXIT_TEST_FAIL_BACKUP=true run_failed_deploy expand-contract "$BACKUP_FAILURE_OUTPUT"
assert_rollback_result "$BACKUP_FAILURE_OUTPUT" 42
assert_contains "$BACKUP_FAILURE_OUTPUT" "Migrations did not begin"
assert_contains "$DOCKER_LOG" "rm -f --"
if grep -Fx 'migration' "$DOCKER_LOG" >/dev/null; then
  fail "Migration ran after the paired backup failed"
fi

# UC-NFR-9 - T-NFR-9.BackupStorageFailure
: > "$DOCKER_LOG"
write_env_file "$TMP_ROOT/backups-storage-failure"
STORAGE_FAILURE_OUTPUT="$TMP_ROOT/storage-failure.out"
TRANXIT_TEST_FAIL_STORAGE_PREP=true run_failed_deploy expand-contract "$STORAGE_FAILURE_OUTPUT"
assert_rollback_result "$STORAGE_FAILURE_OUTPUT" 49
assert_contains "$DOCKER_LOG" storage-preparation-failure
if grep -E '^(backup|migration)$' "$DOCKER_LOG" >/dev/null; then
  fail "Backup or migration ran after storage preparation failed"
fi
echo "PASS T-NFR-9.BackupStorageFailure"

: > "$DOCKER_LOG"
write_env_file "$TMP_ROOT/backups-rollback-failure"
ROLLBACK_FAILURE_OUTPUT="$TMP_ROOT/rollback-failure.out"
TRANXIT_TEST_FAIL_ROLLBACK_BUILD=true \
  run_failed_deploy expand-contract "$ROLLBACK_FAILURE_OUTPUT"
assert_status 1 "$DEPLOY_STATUS" "deploy with failed automatic rollback"
cmp "$MARKER_SNAPSHOT" "$MARKER_FILE" >/dev/null ||
  fail "Known-green marker changed after automatic rollback failed"
assert_contains "$ROLLBACK_FAILURE_OUTPUT" "AUTOMATIC ROLLBACK FAILED"
assert_contains "$ROLLBACK_FAILURE_OUTPUT" "RECOVERY FENCED"
for service in caddy frontend ocelotapigw accountservice courierjobservice; do
  [ "$(cat "$TRANXIT_TEST_STATE_DIR/$service")" = exited ] ||
    fail "Failed rollback left $service running"
done
if grep -F 'Known-green release restored successfully' "$ROLLBACK_FAILURE_OUTPUT" >/dev/null; then
  fail "A failed rollback was reported as successful"
fi

# UC-NFR-9, F-01 - T-NFR-9.PostAdmissionRecoveryContract (actual deploy entry point)
for policy in restore-required expand-contract; do
  : > "$DOCKER_LOG"
  TRANXIT_TEST_FORCE_SMOKE_FAILURE=false TRANXIT_TEST_POST_ADMISSION_FAILURE=true \
    write_env_file "$TMP_ROOT/backups-post-admission-$policy"
  run_failed_deploy "$policy" "$TMP_ROOT/post-admission-$policy.out"
  assert_status 1 "$DEPLOY_STATUS" "post-admission $policy failure"
  assert_contains "$TMP_ROOT/post-admission-$policy.out" "Public admission is open"
  if grep -F 'restore-sql ' "$DOCKER_LOG" >/dev/null; then
    fail "Post-admission failure restored an earlier database backup"
  fi
  cmp "$MARKER_SNAPSHOT" "$MARKER_FILE" >/dev/null || fail "Post-admission failure advanced the marker"
  if [ "$policy" = restore-required ]; then
    assert_contains "$TMP_ROOT/post-admission-$policy.out" "AUTOMATIC RESTORE REFUSED"
    [ -f "$MARKER_DIR/deploy-staging-admitted" ] || fail "Post-admission restore boundary was lost"
    [ ! -e "$MARKER_DIR/admission-staging/open" ] || fail "Unsafe recovery left admission open"
    for service in caddy frontend ocelotapigw accountservice courierjobservice; do
      [ "$(cat "$TRANXIT_TEST_STATE_DIR/$service")" = exited ] || fail "Unsafe recovery left $service running"
    done
    : > "$DOCKER_LOG"
    run_failed_deploy "$policy" "$TMP_ROOT/post-admission-restart-guard.out"
    assert_status 1 "$DEPLOY_STATUS" "unresolved admission process restart"
    assert_contains "$TMP_ROOT/post-admission-restart-guard.out" "unresolved public admission"
    [ ! -s "$DOCKER_LOG" ] || fail "Unresolved admission reached Docker on process restart"
    rm -- "$MARKER_DIR/deploy-staging-admitted"
  else
    assert_rollback_result "$TMP_ROOT/post-admission-$policy.out"
    [ -f "$MARKER_DIR/admission-staging/open" ] || fail "Code-only rollback did not reopen verified release"
    [ ! -e "$MARKER_DIR/deploy-staging-admitted" ] || fail "Code-only rollback left unresolved admission"
  fi
done
echo "PASS T-NFR-9.PostAdmissionRecoveryContract (restore refusal, data-preserving code recovery, process restart guard)"

MANIFEST="$(find "$TMP_ROOT/backups-expand" -name manifest.env -print -quit)"
[ -n "$MANIFEST" ] || fail "Deploy did not produce a paired backup manifest"

: > "$DOCKER_LOG"
write_env_file "$TMP_ROOT/backups-standalone"
STANDALONE_BACKUP_OUTPUT="$TMP_ROOT/standalone-backup.out"
PATH="$MOCK_BIN:$PATH" \
  TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" \
  TRANXIT_TEST_RUNNING_SERVICES=$'caddy\nfrontend\nocelotapigw\naccountservice\ncourierjobservice' \
  TRANXIT_ENV_FILE="$ENV_FILE" \
  "$CONTROLLER_SCRIPTS/backup.sh" \
    --env staging \
    --release-id standalone-contract >"$STANDALONE_BACKUP_OUTPUT" 2>&1
assert_contains "$STANDALONE_BACKUP_OUTPUT" "Verified paired backup complete"
stop_line="$(grep -n -m1 ' stop caddy frontend ocelotapigw accountservice courierjobservice$' "$DOCKER_LOG" | cut -d: -f1)"
first_backup_line="$(grep -n -m1 '^backup$' "$DOCKER_LOG" | cut -d: -f1)"
last_backup_line="$(grep -n '^backup$' "$DOCKER_LOG" | tail -n1 | cut -d: -f1)"
start_line="$(grep -n -m1 ' start caddy frontend ocelotapigw accountservice courierjobservice$' "$DOCKER_LOG" | cut -d: -f1)"
[ "$stop_line" -lt "$first_backup_line" ] && [ "$last_backup_line" -lt "$start_line" ] ||
  fail "Standalone paired backup did not quiesce and restart writers around both backups"

set +e
PATH="$MOCK_BIN:$PATH" TRANXIT_ENV_FILE="$ENV_FILE" \
  "$CONTROLLER_SCRIPTS/restore.sh" \
  --manifest "$MANIFEST" \
  --account-database ScratchAccount \
  --courier-database ScratchCourier \
  --confirm ignored >/dev/null 2>&1
NO_ENV_STATUS=$?
set -e
assert_status 2 "$NO_ENV_STATUS" "restore without explicit environment"

set +e
PATH="$MOCK_BIN:$PATH" TRANXIT_ENV_FILE="$ENV_FILE" \
  "$CONTROLLER_SCRIPTS/restore.sh" \
  --env staging \
  --manifest "$MANIFEST" \
  --account-database ScratchAccount \
  --courier-database ScratchCourier \
  --confirm wrong >/dev/null 2>&1
WRONG_CONFIRM_STATUS=$?
set -e
assert_status 2 "$WRONG_CONFIRM_STATUS" "restore with wrong confirmation"

: > "$DOCKER_LOG"
LIVE_FAILURE_OUTPUT="$TMP_ROOT/live-restore-failure.out"
set +e
PATH="$MOCK_BIN:$PATH" \
  TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" \
  TRANXIT_TEST_FAIL_SECOND_RESTORE=true \
  TRANXIT_ENV_FILE="$ENV_FILE" \
  "$CONTROLLER_SCRIPTS/restore.sh" \
    --env staging \
    --manifest "$MANIFEST" \
    --account-database Tranxit_Account \
    --courier-database Tranxit_CourierJob \
    --replace-live \
    --confirm RESTORE-LIVE-staging >"$LIVE_FAILURE_OUTPUT" 2>&1
LIVE_FAILURE_STATUS=$?
set -e
assert_status 44 "$LIVE_FAILURE_STATUS" "second database restore failure"
RESTORE_STATE_FILE="$MARKER_DIR/restore-staging-in-progress"
assert_contains "$RESTORE_STATE_FILE" "phase=account-restored"
assert_contains "$LIVE_FAILURE_OUTPUT" "LIVE RESTORE INCOMPLETE"

: > "$DOCKER_LOG"
set +e
PATH="$MOCK_BIN:$PATH" \
  TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" \
  TRANXIT_ENV_FILE="$ENV_FILE" \
  "$CONTROLLER_SCRIPTS/backup.sh" \
    --env staging \
    --release-id blocked-during-restore >"$TMP_ROOT/incomplete-restore-backup.out" 2>&1
INCOMPLETE_STATE_BACKUP_STATUS=$?
set -e
assert_status 1 "$INCOMPLETE_STATE_BACKUP_STATUS" "backup during incomplete live restore"
[ ! -s "$DOCKER_LOG" ] ||
  fail "Backup reached Docker while an incomplete live restore state existed"

cp "$RESTORE_STATE_FILE" "$RESTORE_STATE_FILE.saved"
sed -i 's|^manifest=.*|manifest=/tmp/different-manifest.env|' "$RESTORE_STATE_FILE"
set +e
PATH="$MOCK_BIN:$PATH" \
  TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" \
  TRANXIT_ENV_FILE="$ENV_FILE" \
  "$CONTROLLER_SCRIPTS/restore.sh" \
    --env staging \
    --manifest "$MANIFEST" \
    --account-database Tranxit_Account \
    --courier-database Tranxit_CourierJob \
    --replace-live \
    --confirm RESTORE-LIVE-staging >"$TMP_ROOT/wrong-manifest-recovery.out" 2>&1
WRONG_RECOVERY_MANIFEST_STATUS=$?
set -e
assert_status 1 "$WRONG_RECOVERY_MANIFEST_STATUS" "live restore with a different recovery manifest"
mv "$RESTORE_STATE_FILE.saved" "$RESTORE_STATE_FILE"

: > "$DOCKER_LOG"
set +e
run_failed_deploy expand-contract "$TMP_ROOT/incomplete-restore-deploy.out"
INCOMPLETE_STATE_DEPLOY_STATUS=$DEPLOY_STATUS
set -e
assert_status 1 "$INCOMPLETE_STATE_DEPLOY_STATUS" "deploy during incomplete live restore"
[ ! -s "$DOCKER_LOG" ] ||
  fail "Deploy reached Docker while an incomplete live restore state existed"

: > "$DOCKER_LOG"
LIVE_RECOVERY_OUTPUT="$TMP_ROOT/live-restore-recovery.out"
PATH="$MOCK_BIN:$PATH" \
  TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" \
  TRANXIT_ENV_FILE="$ENV_FILE" \
  "$CONTROLLER_SCRIPTS/restore.sh" \
    --env staging \
    --manifest "$MANIFEST" \
    --account-database Tranxit_Account \
    --courier-database Tranxit_CourierJob \
    --replace-live \
    --confirm RESTORE-LIVE-staging >"$LIVE_RECOVERY_OUTPUT" 2>&1
assert_contains "$LIVE_RECOVERY_OUTPUT" "Resuming recovery"
[ ! -e "$RESTORE_STATE_FILE" ] ||
  fail "Successful live restore recovery left an incomplete-state marker"

: > "$DOCKER_LOG"
SCRATCH_OUTPUT="$TMP_ROOT/scratch-restore.out"
# UC-NFR-9 - T-NFR-9.PairedRestoreContract
PATH="$MOCK_BIN:$PATH" \
  TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" \
  TRANXIT_ENV_FILE="$ENV_FILE" \
  "$CONTROLLER_SCRIPTS/restore.sh" \
    --env staging \
    --manifest "$MANIFEST" \
    --account-database Tranxit_Account_RestoreDrill \
    --courier-database Tranxit_CourierJob_RestoreDrill \
    --confirm RESTORE-staging-Tranxit_Account_RestoreDrill-Tranxit_CourierJob_RestoreDrill \
    >"$SCRATCH_OUTPUT" 2>&1

assert_contains "$SCRATCH_OUTPUT" "Paired restore complete"
assert_contains "$DOCKER_LOG" "MOVE N'LogicalData' TO N'/var/opt/mssql/data/Tranxit_Account_RestoreDrill.mdf'"
assert_contains "$DOCKER_LOG" "MOVE N'LogicalData' TO N'/var/opt/mssql/data/Tranxit_CourierJob_RestoreDrill.mdf'"
if grep -F 'REPLACE' "$DOCKER_LOG" >/dev/null; then
  fail "Scratch restore used WITH REPLACE"
fi

echo "PASS T-NFR-9.PairedRestoreContract"

bash "$SCRIPT_DIR/deploy-fence-test.sh" --contract
bash "$SCRIPT_DIR/deploy-fence-test.sh" --admission
bash "$SCRIPT_DIR/admission-contract-test.sh"
bash "$SCRIPT_DIR/deploy-public-edge-test.sh"
bash "$SCRIPT_DIR/controller-entrypoint-test.sh"
