#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
TMP_ROOT="$(mktemp -d)"

cleanup() {
  rm -rf -- "$TMP_ROOT"
}
trap cleanup EXIT

fail() {
  echo "FAIL: $*" >&2
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
  cp "$PROJECT_DIR/scripts/deploy.sh" "$repo/TranXit/scripts/deploy.sh"
  cp "$PROJECT_DIR/scripts/backup.sh" "$repo/TranXit/scripts/backup.sh"
  cp "$PROJECT_DIR/scripts/restore.sh" "$repo/TranXit/scripts/restore.sh"
  printf '# test compose\n' > "$repo/TranXit/docker-compose.yml"
  printf '# test production override\n' > "$repo/TranXit/docker-compose.prod.yml"
  printf '# test staging override\n' > "$repo/TranXit/docker-compose.staging.yml"
  printf '#!/usr/bin/env bash\nset -euo pipefail\n' \
    > "$repo/TranXit/scripts/verify-production-topology.sh"
  printf '#!/usr/bin/env bash\nset -euo pipefail\n' \
    > "$repo/TranXit/scripts/smoke.sh"
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
  MOCK_BIN="$TMP_ROOT/bin"
  DOCKER_LOG="$TMP_ROOT/docker-events.log"
  mkdir -p "$MOCK_BIN"
  : > "$DOCKER_LOG"

  cat > "$MOCK_BIN/docker" <<'MOCK'
#!/usr/bin/env bash
set -euo pipefail

args="$*"
printf 'command %s\n' "$args" >> "$TRANXIT_TEST_DOCKER_LOG"
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

  chmod +x "$MOCK_BIN/docker" "$MOCK_BIN/curl" "$MOCK_BIN/sleep" "$MOCK_BIN/flock"
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
TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE=true
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
    TRANXIT_ENV_FILE="$ENV_FILE" \
    "$BACKEND_REPO/TranXit/scripts/deploy.sh" \
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

create_backend_release_repo
create_frontend_release_repo
create_mock_commands

MARKER_DIR="$TMP_ROOT/markers"
MARKER_FILE="$MARKER_DIR/last-staging-green"
MARKER_SNAPSHOT="$TMP_ROOT/marker.before"
mkdir -p "$MARKER_DIR"
cat > "$MARKER_FILE" <<MARKER
format_version=2
environment=staging
backend_sha=$GREEN_BACKEND_SHA
frontend_ref=$GREEN_FRONTEND_SHA
frontend_sha=$GREEN_FRONTEND_SHA
image_tag=${GREEN_BACKEND_SHA:0:12}
migration_policy=expand-contract
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
  "$BACKEND_REPO/TranXit/scripts/deploy.sh" \
    --env production \
    --sha "$CANDIDATE_BACKEND_SHA" \
    --frontend-ref "$CANDIDATE_FRONTEND_SHA" \
    --migration-policy expand-contract >/dev/null 2>&1
PRODUCTION_INJECTION_STATUS=$?
set -e
assert_status 1 "$PRODUCTION_INJECTION_STATUS" "production failure-injection guard"
[ ! -s "$DOCKER_LOG" ] ||
  fail "Production failure injection reached Docker instead of failing fast"

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
  "$BACKEND_REPO/TranXit/scripts/deploy.sh" \
    --env staging \
    --sha "$CANDIDATE_BACKEND_SHA" \
    --frontend-ref "$CANDIDATE_FRONTEND_SHA" \
    --migration-policy expand-contract \
    --allow-first-deploy >"$TMP_ROOT/first-deploy.out" 2>&1
assert_contains "$DOCKER_LOG" "CREATE DATABASE [Tranxit_Account]"
assert_contains "$DOCKER_LOG" "CREATE DATABASE [Tranxit_CourierJob]"
assert_contains "$DOCKER_LOG" "up -d --no-deps --wait --wait-timeout 300 mailpit"
assert_contains "$FIRST_MARKER_DIR/last-staging-green" "backend_sha=$CANDIDATE_BACKEND_SHA"

: > "$DOCKER_LOG"
mv "$MARKER_FILE" "$MARKER_FILE.hidden"
set +e
PATH="$MOCK_BIN:$PATH" \
  TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" \
  TRANXIT_ENV_FILE="$ENV_FILE" \
  "$BACKEND_REPO/TranXit/scripts/deploy.sh" \
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
write_env_file "$TMP_ROOT/backups-restore"
RESTORE_OUTPUT="$TMP_ROOT/restore-required.out"
run_failed_deploy restore-required "$RESTORE_OUTPUT"
assert_rollback_result "$RESTORE_OUTPUT"
assert_backup_precedes_migration
assert_contains "$DOCKER_LOG" "REPLACE"
assert_contains "$RESTORE_OUTPUT" "restoring the paired pre-migration backup"

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

: > "$DOCKER_LOG"
write_env_file "$TMP_ROOT/backups-rollback-failure"
ROLLBACK_FAILURE_OUTPUT="$TMP_ROOT/rollback-failure.out"
TRANXIT_TEST_FAIL_ROLLBACK_BUILD=true \
  run_failed_deploy expand-contract "$ROLLBACK_FAILURE_OUTPUT"
assert_status 1 "$DEPLOY_STATUS" "deploy with failed automatic rollback"
cmp "$MARKER_SNAPSHOT" "$MARKER_FILE" >/dev/null ||
  fail "Known-green marker changed after automatic rollback failed"
assert_contains "$ROLLBACK_FAILURE_OUTPUT" "AUTOMATIC ROLLBACK FAILED"
if grep -F 'Known-green release restored successfully' "$ROLLBACK_FAILURE_OUTPUT" >/dev/null; then
  fail "A failed rollback was reported as successful"
fi

MANIFEST="$(find "$TMP_ROOT/backups-expand" -name manifest.env -print -quit)"
[ -n "$MANIFEST" ] || fail "Deploy did not produce a paired backup manifest"

: > "$DOCKER_LOG"
write_env_file "$TMP_ROOT/backups-standalone"
STANDALONE_BACKUP_OUTPUT="$TMP_ROOT/standalone-backup.out"
PATH="$MOCK_BIN:$PATH" \
  TRANXIT_TEST_DOCKER_LOG="$DOCKER_LOG" \
  TRANXIT_TEST_RUNNING_SERVICES=$'caddy\nfrontend\nocelotapigw\naccountservice\ncourierjobservice' \
  TRANXIT_ENV_FILE="$ENV_FILE" \
  "$BACKEND_REPO/TranXit/scripts/backup.sh" \
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
  "$BACKEND_REPO/TranXit/scripts/restore.sh" \
  --manifest "$MANIFEST" \
  --account-database ScratchAccount \
  --courier-database ScratchCourier \
  --confirm ignored >/dev/null 2>&1
NO_ENV_STATUS=$?
set -e
assert_status 2 "$NO_ENV_STATUS" "restore without explicit environment"

set +e
PATH="$MOCK_BIN:$PATH" TRANXIT_ENV_FILE="$ENV_FILE" \
  "$BACKEND_REPO/TranXit/scripts/restore.sh" \
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
  "$BACKEND_REPO/TranXit/scripts/restore.sh" \
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
  "$BACKEND_REPO/TranXit/scripts/backup.sh" \
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
  "$BACKEND_REPO/TranXit/scripts/restore.sh" \
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
  "$BACKEND_REPO/TranXit/scripts/restore.sh" \
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
  "$BACKEND_REPO/TranXit/scripts/restore.sh" \
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
