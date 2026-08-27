#!/usr/bin/env bash
set -euo pipefail

TEST_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_SOURCE="${TRANXIT_TEST_DEPLOY_SOURCE:-$TEST_DIR/../deploy.sh}"
TEMP_PARENT="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
TMP_ROOT="$(mktemp -d "$TEMP_PARENT/tranxit-f02.XXXXXXXX")"
MODE="${1:---contract}"
runtime_started=false
scenario_count=0

cleanup() {
  local result=$?
  if [ "$runtime_started" = true ]; then
    case "$project_name" in
      tranxit-f02-test-*)
        compose down --timeout 5 --remove-orphans || result=1
        ;;
      *) echo "Refusing cleanup of unexpected project: $project_name" >&2; result=1 ;;
    esac
  fi
  case "$TMP_ROOT" in
    "$TEMP_PARENT"/tranxit-f02.*) rm -rf -- "$TMP_ROOT" ;;
    *) echo "Refusing cleanup outside the test temp directory" >&2; result=1 ;;
  esac
  exit "$result"
}
trap cleanup EXIT

fail() {
  echo "FAIL: $*" >&2
  for file in "$TMP_ROOT"/*.log; do
    [ ! -f "$file" ] || cat "$file" >&2
  done
  exit 1
}

assert_contains() {
  grep -F -- "$2" "$1" >/dev/null || fail "Expected '$2' in $1"
}

assert_absent() {
  if grep -F -- "$2" "$1" >/dev/null; then
    fail "Unexpected '$2' in $1"
  fi
}

expect_status() {
  local expected="$1" actual
  shift
  if "$@"; then actual=0; else actual=$?; fi
  [ "$actual" -eq "$expected" ] || fail "$* returned $actual; expected $expected"
}

# Load the real helper bodies without executing deploy.sh's checkout/deployment entry point.
for name in close_public_admission record_public_admission open_public_admission complete_public_admission \
  fence_application_stack start_application_stack stop_application_stack run_release_smoke rollback_known_green on_error; do
  definition="$(awk -v name="$name" '
    $0 == name "() {" { selected=1 }
    selected { print }
    selected && $0 == "}" { found=1; exit }
    END { if (!found) exit 1 }
  ' "$DEPLOY_SOURCE")"
  eval "$definition"
done

application_services=(caddy frontend ocelotapigw accountservice courierjobservice)
stateful_services=(sqlserver rabbitmq)
TARGET_ENV=staging
project_name=tranxit-f02-contract
base_url=http://unused.example.invalid
SCRIPT_DIR="$TMP_ROOT/scripts"
mkdir -p "$SCRIPT_DIR" "$TMP_ROOT/states"
export TRANXIT_FENCE_EVENTS="$TMP_ROOT/events.log"
export TRANXIT_EGRESS_PROBE_URL=https://unused.example.invalid

cat > "$SCRIPT_DIR/verify-production-topology.sh" <<'SH'
#!/usr/bin/env bash
echo topology >> "$TRANXIT_FENCE_EVENTS"
exit "${TRANXIT_FENCE_TOPOLOGY_STATUS:-0}"
SH
cat > "$SCRIPT_DIR/smoke.sh" <<'SH'
#!/usr/bin/env bash
echo http-smoke >> "$TRANXIT_FENCE_EVENTS"
exit "${TRANXIT_FENCE_HTTP_STATUS:-0}"
SH
cat > "$SCRIPT_DIR/restore.sh" <<'SH'
#!/usr/bin/env bash
echo restore >> "$TRANXIT_FENCE_EVENTS"
cp "$TRANXIT_FENCE_BASELINE" "$TRANXIT_FENCE_WRITES"
SH
chmod +x "$SCRIPT_DIR/"*.sh

checkout_known_green() { echo checkout-green >> "$TRANXIT_FENCE_EVENTS"; }
wait_for_gateway() { echo gateway-ready >> "$TRANXIT_FENCE_EVENTS"; }

reset_case() {
  : > "$TRANXIT_FENCE_EVENTS"
  : > "$TMP_ROOT/recovery.log"
  for service in "${application_services[@]}" mailpit; do
    echo running > "$TMP_ROOT/states/$service"
  done
  startup_failure=""
  stop_failure=""
  inspect_failure=""
  enumerate_failure=""
  retained_service=""
  retained_state=running
  export TRANXIT_FENCE_TOPOLOGY_STATUS=0 TRANXIT_FENCE_HTTP_STATUS=0
  rollback_available=true
  writers_stopped=true
  migration_started=false
  admission_may_have_opened=false
  marker_commit_started=false
  MIGRATION_POLICY=expand-contract
  backup_manifest=""
  green_backend_sha=1111111111111111111111111111111111111111
  green_frontend_sha=2222222222222222222222222222222222222222
  green_admission_policy=private-smoke-v1
  MARKER_DIR="$TMP_ROOT"
  TRANXIT_ADMISSION_DIR="$TMP_ROOT/admission"
  ADMISSION_OPEN="$TRANXIT_ADMISSION_DIR/open"
  ADMISSION_STATE="$TMP_ROOT/deploy-staging-admitted"
  mkdir -p "$TRANXIT_ADMISSION_DIR"
  rm -f -- "$ADMISSION_OPEN" "$ADMISSION_STATE"
  export TRANXIT_FENCE_WRITES="$TMP_ROOT/writes" TRANXIT_FENCE_BASELINE="$TMP_ROOT/baseline"
  printf 'before-deploy\n' > "$TRANXIT_FENCE_WRITES"
  cp "$TRANXIT_FENCE_WRITES" "$TRANXIT_FENCE_BASELINE"
  MARKER_FILE="$TMP_ROOT/last-staging-green"
  printf 'backend_sha=%s\nfrontend_sha=%s\n' "$green_backend_sha" "$green_frontend_sha" > "$MARKER_FILE"
  cp "$MARKER_FILE" "$TMP_ROOT/marker.before"
}

compose() {
  local action="$1" argument failed=0
  shift
  printf 'compose %s %s\n' "$action" "$*" >> "$TRANXIT_FENCE_EVENTS"
  case "$action" in
    up|stop)
      for argument in "$@"; do
        [ -f "$TMP_ROOT/states/$argument" ] || continue
        if [ "$action" = up ]; then
          echo running > "$TMP_ROOT/states/$argument"
          [ "$startup_failure" != "$argument" ] || failed=47
        elif [ "$stop_failure" = "$argument" ]; then
          failed=51
        elif [ "$retained_service" = "$argument" ]; then
          echo "$retained_state" > "$TMP_ROOT/states/$argument"
        else
          echo exited > "$TMP_ROOT/states/$argument"
        fi
      done
      ;;
    config|build|ps|logs) ;;
    *) fail "Unexpected fake compose action: $action" ;;
  esac
  return "$failed"
}

docker() {
  local argument service=""
  printf 'docker %s\n' "$*" >> "$TRANXIT_FENCE_EVENTS"
  case "$1" in
    ps)
      [[ "$*" == *"label=com.docker.compose.project=$project_name"* ]] ||
        fail "Container enumeration was not project-scoped"
      for argument in "$@"; do
        case "$argument" in label=com.docker.compose.service=*) service="${argument##*=}" ;; esac
      done
      [ -n "$service" ] || fail "Container enumeration was not service-scoped"
      [ "$enumerate_failure" != "$service" ] || return 52
      echo "fake-$service"
      ;;
    inspect)
      service="${*: -1}"
      service="${service#fake-}"
      [ "$inspect_failure" != "$service" ] || return 53
      cat "$TMP_ROOT/states/$service"
      ;;
    *) fail "Unexpected fake docker command: $*" ;;
  esac
}

assert_stopped_model() {
  for service in "${application_services[@]}"; do
    [ "$(cat "$TMP_ROOT/states/$service")" = exited ] || fail "$service is still running"
  done
}

recover() {
  ( on_error 49 999 ) > "$TMP_ROOT/recovery.log" 2>&1
}

helper_failure_propagation() {
  # UC-NFR-9, F-02 - T-NFR-9.HelperFailurePropagation
  local stages=(mailpit accountservice ocelotapigw frontend caddy) index next
  for index in "${!stages[@]}"; do
    reset_case
    startup_failure="${stages[$index]}"
    expect_status 47 start_application_stack
    for ((next=index+1; next<${#stages[@]}; next++)); do
      assert_absent "$TRANXIT_FENCE_EVENTS" "300 ${stages[$next]}"
    done
    scenario_count=$((scenario_count + 1))
  done
  reset_case
  export TRANXIT_FENCE_TOPOLOGY_STATUS=48
  expect_status 48 run_release_smoke
  assert_absent "$TRANXIT_FENCE_EVENTS" http-smoke
  scenario_count=$((scenario_count + 1))
  reset_case
  export TRANXIT_FENCE_HTTP_STATUS=49
  expect_status 49 run_release_smoke
  assert_contains "$TRANXIT_FENCE_EVENTS" topology
  scenario_count=$((scenario_count + 1))
  echo "PASS T-NFR-9.HelperFailurePropagation (5 startup stages, 2 smoke stages; conditional context)"
}

first_deploy_failure_fence() {
  # UC-NFR-9, F-02 - T-NFR-9.FirstDeployFailureFence
  reset_case
  rollback_available=false
  rm -- "$MARKER_FILE"
  expect_status 49 recover
  assert_stopped_model
  [ ! -e "$MARKER_FILE" ] || fail "Failed first deploy wrote a green marker"
  assert_absent "$TRANXIT_FENCE_EVENTS" "compose up"
  assert_contains "$TMP_ROOT/recovery.log" "RECOVERY FENCED"
  scenario_count=$((scenario_count + 1))
  echo "PASS T-NFR-9.FirstDeployFailureFence"
}

rollback_smoke_failure_fence() {
  # UC-NFR-9, F-02 - T-NFR-9.RollbackSmokeFailureFence
  reset_case
  export TRANXIT_FENCE_HTTP_STATUS=49
  expect_status 49 recover
  assert_stopped_model
  cmp "$TMP_ROOT/marker.before" "$MARKER_FILE" || fail "Failed recovery advanced the marker"
  assert_contains "$TRANXIT_FENCE_EVENTS" "300 caddy"
  assert_contains "$TRANXIT_FENCE_EVENTS" http-smoke
  assert_contains "$TMP_ROOT/recovery.log" "RECOVERY FENCED"
  assert_absent "$TMP_ROOT/recovery.log" "Known-green release restored successfully"
  scenario_count=$((scenario_count + 1))
  echo "PASS T-NFR-9.RollbackSmokeFailureFence"
}

stop_failure_reported_unsafe() {
  # UC-NFR-9, F-02 - T-NFR-9.StopFailureReportedUnsafe
  local failure
  for failure in stop inspect enumerate running paused restarting; do
    reset_case
    case "$failure" in
      stop) stop_failure=caddy ;;
      inspect) inspect_failure=frontend ;;
      enumerate) enumerate_failure=ocelotapigw ;;
      *) retained_service=frontend; retained_state="$failure" ;;
    esac
    expect_status 49 recover
    assert_contains "$TMP_ROOT/recovery.log" "UNSAFE/UNVERIFIED RECOVERY"
    assert_absent "$TMP_ROOT/recovery.log" "RECOVERY FENCED"
    assert_absent "$TMP_ROOT/recovery.log" "Application services are confirmed stopped"
    assert_absent "$TMP_ROOT/recovery.log" "Known-green release restored successfully"
    assert_absent "$TRANXIT_FENCE_EVENTS" "compose up"
    assert_contains "$TRANXIT_FENCE_EVENTS" "stop --timeout 30 frontend ocelotapigw accountservice courierjobservice"
    [ "$(cat "$TMP_ROOT/states/accountservice")" = exited ] || fail "Stop did not attempt remaining writers"
    cmp "$TMP_ROOT/marker.before" "$MARKER_FILE" || fail "Unsafe recovery advanced the marker"
    scenario_count=$((scenario_count + 1))
  done
  echo "PASS T-NFR-9.StopFailureReportedUnsafe (6 failure/state scenarios)"
}

model_public_write() {
  local phase="$1"
  if [ -f "$ADMISSION_OPEN" ] && [ "$(cat "$TMP_ROOT/states/caddy")" = running ]; then
    printf '%s\n' "$phase" >> "$TRANXIT_FENCE_WRITES"
    echo "admitted $phase" >> "$TRANXIT_FENCE_EVENTS"
    return 0
  fi
  echo "refused $phase" >> "$TRANXIT_FENCE_EVENTS"
  return 60
}

write_safe_admission() {
  # UC-NFR-9, F-01 - T-NFR-9.PrivateAdmissionAndWritePreservation
  local policy
  for policy in restore-required expand-contract; do
    reset_case
    MIGRATION_POLICY="$policy"
    migration_started=true
    backup_manifest="$TMP_ROOT/paired-manifest"
    fence_application_stack
    expect_status 60 model_public_write backup
    expect_status 60 model_public_write migration
    start_application_stack
    expect_status 60 model_public_write candidate-started
    run_release_smoke
    expect_status 60 model_public_write smoke-complete
    cmp "$TRANXIT_FENCE_BASELINE" "$TRANXIT_FENCE_WRITES" || fail "Closed candidate accepted a write"
    record_public_admission candidate-backend candidate-frontend
    [ -f "$ADMISSION_STATE" ] || fail "Admission was not persisted before opening"
    open_public_admission
    model_public_write acknowledged-after-open
    expect_status 49 recover
    assert_contains "$TRANXIT_FENCE_WRITES" acknowledged-after-open
    assert_absent "$TRANXIT_FENCE_EVENTS" restore
    cmp "$TMP_ROOT/marker.before" "$MARKER_FILE" || fail "Post-admission failure advanced marker"
    if [ "$policy" = restore-required ]; then
      assert_stopped_model
      [ -f "$ADMISSION_STATE" ] || fail "Unsafe restore boundary was forgotten"
      assert_contains "$TMP_ROOT/recovery.log" "AUTOMATIC RESTORE REFUSED"
      expect_status 60 model_public_write after-failure
    else
      [ -f "$ADMISSION_OPEN" ] || fail "Known-green code recovery did not reopen after private smoke"
      [ ! -e "$ADMISSION_STATE" ] || fail "Successful code-only recovery left a pending boundary"
      model_public_write after-code-recovery
    fi
    scenario_count=$((scenario_count + 1))
  done

  reset_case
  MIGRATION_POLICY=restore-required
  migration_started=true
  backup_manifest="$TMP_ROOT/paired-manifest"
  expect_status 49 recover
  assert_contains "$TRANXIT_FENCE_EVENTS" restore
  cmp "$TRANXIT_FENCE_BASELINE" "$TRANXIT_FENCE_WRITES" || fail "Pre-admission recovery changed baseline data"
  [ -f "$ADMISSION_OPEN" ] || fail "Pre-admission restore did not reopen known-green after smoke"
  scenario_count=$((scenario_count + 1))
  echo "PASS T-NFR-9.PrivateAdmissionAndWritePreservation (3 pre/post-admission recovery scenarios)"
}

admission_failure_guards() {
  # UC-NFR-9, F-01 - T-NFR-9.AdmissionFailureGuards
  reset_case
  expect_status 1 open_public_admission
  [ ! -e "$ADMISSION_OPEN" ] || fail "Gate opened without a persistent boundary"
  scenario_count=$((scenario_count + 1))

  reset_case
  MIGRATION_POLICY=restore-required
  migration_started=true
  record_public_admission candidate-backend candidate-frontend
  mv() {
    if [ "${*: -1}" = "$ADMISSION_OPEN" ]; then return 55; fi
    command mv "$@"
  }
  expect_status 55 open_public_admission
  unset -f mv
  expect_status 49 recover
  assert_stopped_model
  assert_absent "$TRANXIT_FENCE_EVENTS" restore
  assert_contains "$TMP_ROOT/recovery.log" "AUTOMATIC RESTORE REFUSED"
  scenario_count=$((scenario_count + 1))

  reset_case
  mkdir "$ADMISSION_OPEN"
  expect_status 49 recover
  assert_stopped_model
  assert_contains "$TMP_ROOT/recovery.log" "UNSAFE/UNVERIFIED RECOVERY"
  assert_absent "$TRANXIT_FENCE_EVENTS" "compose up"
  rmdir "$ADMISSION_OPEN"
  scenario_count=$((scenario_count + 1))

  reset_case
  green_admission_policy=""
  expect_status 49 recover
  assert_stopped_model
  assert_absent "$TRANXIT_FENCE_EVENTS" "compose up"
  assert_contains "$TMP_ROOT/recovery.log" "predates the private admission gate"
  scenario_count=$((scenario_count + 1))

  reset_case
  marker_commit_started=true
  expect_status 49 recover
  assert_stopped_model
  assert_absent "$TRANXIT_FENCE_EVENTS" checkout-green
  assert_contains "$TMP_ROOT/recovery.log" "Release-marker finalization is incomplete"
  scenario_count=$((scenario_count + 1))
  echo "PASS T-NFR-9.AdmissionFailureGuards (5 fail-closed scenarios)"
}

runtime_recovery_fence() {
  # UC-NFR-9, F-02 - T-NFR-9.RuntimeRecoveryFence
  local endpoint address scenario service container state status
  unset -f docker
  endpoint="$(docker context inspect --format '{{.Endpoints.docker.Host}}')" ||
    fail "Cannot determine the Docker context endpoint"
  case "$endpoint" in
    unix://*|npipe://*) ;;
    *) fail "Runtime fixture requires a local Docker context, found: $endpoint" ;;
  esac
  case "${DOCKER_HOST:-}" in
    ""|unix://*|npipe://*) ;;
    *) fail "Runtime fixture refuses nonlocal DOCKER_HOST: $DOCKER_HOST" ;;
  esac
  docker info >/dev/null
  project_name="tranxit-f02-test-$(date +%s)-$$"
  RUNTIME_COMPOSE="$TMP_ROOT/compose.yml"
  : > "$TMP_ROOT/empty.env"
  cat > "$RUNTIME_COMPOSE" <<'YAML'
x-sleeper: &sleeper
  image: busybox:1.37
  command: ["sh", "-c", "trap 'exit 0' TERM INT; sleep 3600 & wait"]
  stop_grace_period: 1s
services:
  caddy:
    image: busybox:1.37
    ports: ["127.0.0.1::8080"]
    stop_grace_period: 1s
    command:
      - sh
      - -ec
      - |
        mkdir -p /tmp/www/cgi-bin
        printf 'ready\n' > /tmp/www/index.html
        cat > /tmp/www/cgi-bin/write <<'CGI'
        #!/bin/sh
        echo accepted >> /tmp/accepted-writes
        printf 'Status: 201 Created\r\nContent-Type: text/plain\r\n\r\naccepted\n'
        CGI
        chmod +x /tmp/www/cgi-bin/write
        httpd -f -p 8080 -h /tmp/www &
        trap 'exit 0' TERM INT
        wait
  frontend: *sleeper
  ocelotapigw: *sleeper
  accountservice: *sleeper
  courierjobservice: *sleeper
  sqlserver: *sleeper
  rabbitmq: *sleeper
YAML
  compose() {
    command docker compose --env-file "$TMP_ROOT/empty.env" -p "$project_name" -f "$RUNTIME_COMPOSE" "$@"
  }
  runtime_started=true
  TARGET_ENV=production
  for scenario in first-deploy rollback-smoke; do
    reset_case
    compose up -d --wait
    address="$(compose port caddy 8080 | tr -d '\r')"
    [[ "$address" == 127.0.0.1:* ]] || fail "Writer port is not loopback-only: $address"
    base_url="http://$address"
    for _ in $(seq 1 30); do
      if curl --noproxy '*' -fsS "$base_url/" >/dev/null; then break; fi
      sleep 1
    done
    status="$(curl --noproxy '*' -sS -o /dev/null -w '%{http_code}' -X POST "$base_url/cgi-bin/write")"
    [ "$status" = 201 ] || fail "Test write was not accepted before $scenario: $status"
    MSYS2_ARG_CONV_EXCL=/tmp/accepted-writes compose exec -T caddy cat /tmp/accepted-writes |
      grep -Fx accepted >/dev/null || fail "Test write did not persist"
    if [ "$scenario" = first-deploy ]; then
      rollback_available=false
      rm -- "$MARKER_FILE"
    else
      export TRANXIT_FENCE_HTTP_STATUS=49
    fi
    expect_status 49 recover
    assert_contains "$TMP_ROOT/recovery.log" "RECOVERY FENCED"
    if [ "$scenario" = first-deploy ]; then
      [ ! -e "$MARKER_FILE" ] || fail "Runtime first-deploy failure advanced marker"
    else
      assert_contains "$TRANXIT_FENCE_EVENTS" http-smoke
      cmp "$TMP_ROOT/marker.before" "$MARKER_FILE" || fail "Runtime recovery advanced marker"
    fi
    for service in "${application_services[@]}"; do
      container="$(compose ps --all --quiet "$service" | tr -d '\r')"
      [ -n "$container" ] || fail "Runtime fixture lost $service"
      state="$(docker inspect --format '{{.State.Status}}' "$container" | tr -d '\r')"
      [ "$state" = exited ] || fail "Runtime $service is not stopped: $state"
      echo "$scenario: $service=$state"
    done
    for service in "${stateful_services[@]}"; do
      container="$(compose ps --quiet "$service" | tr -d '\r')"
      [ "$(docker inspect --format '{{.State.Status}}' "$container" | tr -d '\r')" = running ] ||
        fail "Recovery unexpectedly stopped the $service stand-in"
    done
    if curl --noproxy '*' -sS --max-time 3 -o /dev/null -X POST "$base_url/cgi-bin/write" 2>/dev/null; then
      fail "Writer endpoint still reachable after $scenario"
    fi
    echo "$scenario: write endpoint unreachable; data-service stand-ins still running"
    scenario_count=$((scenario_count + 1))
  done
  echo "PASS T-NFR-9.RuntimeRecoveryFence ($scenario_count real-container scenarios)"
}

credibility_check() {
  # UC-NFR-9, F-02 - mutation is confined to a disposable copy, never the worktree.
  local mutated="$TMP_ROOT/deploy-mutated.sh" result
  awk '
    /--egress-url.*TRANXIT_EGRESS_PROBE_URL/ {
      if (sub(/ \|\| return \$\?$/, "")) count++
    }
    { print }
    END { if (count != 1) exit 1 }
  ' "$DEPLOY_SOURCE" > "$mutated"
  echo "Credibility mutation (scratch copy only):"
  diff -u --label deploy.sh --label deploy-mutated.sh "$DEPLOY_SOURCE" "$mutated" || [ "$?" -eq 1 ]
  if TRANXIT_TEST_DEPLOY_SOURCE="$mutated" bash "$TEST_DIR/deploy-fence-test.sh" --contract > "$TMP_ROOT/mutation.log" 2>&1; then
    fail "Credibility mutation did not turn the helper propagation test red"
  else
    result=$?
  fi
  [ "$result" -eq 1 ] || fail "Mutation failed for an unexpected reason: $result"
  assert_contains "$TMP_ROOT/mutation.log" "run_release_smoke returned 0; expected 48"
  echo "RED: T-NFR-9.HelperFailurePropagation detects topology failure masked by HTTP success"
  TRANXIT_TEST_DEPLOY_SOURCE="$DEPLOY_SOURCE" bash "$TEST_DIR/deploy-fence-test.sh" --contract
  echo "GREEN: original source restored (working source was never modified)"
}

case "$MODE" in
  --contract)
    helper_failure_propagation
    first_deploy_failure_fence
    rollback_smoke_failure_fence
    stop_failure_reported_unsafe
    echo "Contract summary: 4 groups, $scenario_count scenarios passed"
    ;;
  --runtime) runtime_recovery_fence ;;
  --admission)
    write_safe_admission
    admission_failure_guards
    echo "Admission summary: 2 groups, $scenario_count scenarios passed"
    ;;
  --credibility) credibility_check ;;
  *) echo "Usage: $0 [--contract|--runtime|--admission|--credibility]" >&2; exit 2 ;;
esac
