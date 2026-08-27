#!/usr/bin/env bash
set -euo pipefail
umask 077
. /harness/probe.sh
[[ "${F01_PROJECT:-}" =~ ^tranxit-f01-test-[a-f0-9]{16}$ ]] || exit 78
export LC_ALL=C
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_SYSTEM=/dev/null

all_stopped() {
  local id state service
  for service in caddy frontend ocelotapigw accountservice courierjobservice; do
    for id in $(/usr/local/bin/docker-real ps -aq --filter "label=com.docker.compose.project=$F01_PROJECT" --filter "label=com.docker.compose.service=$service"); do
      state="$(/usr/local/bin/docker-real inspect --format '{{.State.Status}}' "$id")"
      case "$state" in created|exited|dead) ;; *) fail "$service remains $state"; return 1 ;; esac
    done
  done
}

sentinels() {
  printf '%s|%s\n' \
    "$(sql Tranxit_Account "SET NOCOUNT ON; SELECT IIF(OBJECT_ID(N'__F01CandidateMarker') IS NULL,0,1);")" \
    "$(sql Tranxit_CourierJob "SET NOCOUNT ON; SELECT IIF(OBJECT_ID(N'__F01CandidateMarker') IS NULL,0,1);")"
}

run_deploy() {
  local flags=()
  [ "$F01_FIRST" != true ] || flags+=(--allow-first-deploy)
  bash /work/backend/TranXit/scripts/deploy.sh --env staging --sha "$F01_TARGET_SHA" \
    --frontend-ref "$F01_TARGET_FRONTEND" --migration-policy "$F01_POLICY" "${flags[@]}"
}

run_invalid_config_deploy() (
  local name="$1" variant="$2" saved_env="/work/state/$F01_CASE.env.original" inherited_value
  case "$name" in TRANXIT_EGRESS_PROBE_URL|PUBLIC_APP_URL|STAGING_APP_URL) ;; *) exit 78 ;; esac
  case "$variant" in unset|empty|non-https|empty-overrides-inherited) ;; *) exit 78 ;; esac
  inherited_value="${!name}"
  cp /work/env.staging "$saved_env" || exit 1
  # Keep the adapter's existing private env path; restore it even when deploy exits fatally.
  trap 'status=$?; if cp "$saved_env" /work/env.staging; then /bin/rm -f "$saved_env" || status=1; else status=1; fi; exit "$status"' EXIT
  [ "$(grep -c "^$name=" "$saved_env")" = 1 ] || { fail 'Expected one fixture setting'; exit 1; }
  awk -v name="$name" 'index($0,name "=") != 1' "$saved_env" > /work/env.staging || exit 1
  case "$variant" in
    empty|empty-overrides-inherited) printf "%s=''\n" "$name" >> /work/env.staging || exit 1 ;;
    non-https) printf "%s='http://invalid-configuration.example.test/a1'\n" "$name" >> /work/env.staging || exit 1 ;;
  esac
  unset TRANXIT_EGRESS_PROBE_URL PUBLIC_APP_URL STAGING_APP_URL
  unset TRANXIT_ALLOW_FAILURE_INJECTION TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE TRANXIT_DEPLOY_TEST_FORCE_POST_ADMISSION_FAILURE
  if [ "$variant" = empty-overrides-inherited ]; then export "$name=$inherited_value"; fi
  export TRANXIT_ENV_FILE=/work/env.staging
  run_deploy
)

invalid_config_test() {
  # UC-NFR-9 - T-NFR-9.RuntimeInvalidConfiguration (real entry point, no injected failure).
  local name="$1" variant="$2" status=0 log="/work/state/$F01_CASE.raw.log"
  . /work/refs.env
  set -a; . /work/env.staging; set +a
  export TRANXIT_IMAGE_TAG="${GREEN_BACKEND:0:12}"
  export F01_FAULT_POINTS='' F01_PERSISTENT=false
  /bin/rm -f /work/state/recovering
  printf '0' > /work/state/sequence
  : > "/work/state/$F01_CASE_KEY.accounts"; : > "/work/state/$F01_CASE_KEY.jobs"
  if [ ! -f /work/state/actor-ready ] || [ ! -f /work/markers/admission-staging/open ]; then
    fail 'Preflight requires an admitted green release and verified actor'; return 1
  fi
  if [ -e /work/markers/deploy-staging-admitted ] || [ -L /work/markers/deploy-staging-admitted ] ||
     [ -e /work/markers/restore-staging-in-progress ] || [ -L /work/markers/restore-staging-in-progress ]; then
    fail 'Preflight baseline has an unresolved admission or restore'; return 1
  fi
  cmp /work/state/green.marker /work/markers/last-staging-green >/dev/null
  [ "$(sentinels)" = '0|0' ]

  touch /work/state/probes-enabled
  probe invalid-config-before-primary open
  probe invalid-config-before-secondary open
  /bin/rm -f /work/state/probes-enabled
  : > /work/state/docker-events
  snapshot_preflight_state > "/work/state/$F01_CASE.before"
  sanitize < "/work/state/$F01_CASE.before" > "/work/public-results/$F01_CASE.before-state.txt"
  run_invalid_config_deploy "$name" "$variant" > "$log" 2>&1 || status=$?
  sanitize < "$log" > "/work/public-results/$F01_CASE.log"
  snapshot_preflight_state > "/work/state/$F01_CASE.after"
  sanitize < "/work/state/$F01_CASE.after" > "/work/public-results/$F01_CASE.after-state.txt"

  [ "$status" = 2 ] || { fail "$F01_CASE expected preflight exit 2, got $status"; return 1; }
  [ ! -s /work/state/docker-events ] || { fail "$F01_CASE reached deploy-side Docker"; return 1; }
  cmp "/work/state/$F01_CASE.before" "/work/state/$F01_CASE.after" >/dev/null || {
    fail "$F01_CASE changed containers, release files/refs, backup/restore history or schema"; return 1;
  }
  grep -F "$name" "$log" >/dev/null || { fail "$F01_CASE omitted the setting name"; return 1; }
  if grep -F -e "$PUBLIC_APP_URL" -e "$STAGING_APP_URL" -e "$TRANXIT_EGRESS_PROBE_URL" \
    -e 'invalid-configuration.example.test' "$log" >/dev/null; then
    fail "$F01_CASE disclosed a configuration value"; return 1
  fi
  assert_writes "$F01_CASE_KEY"
  assert_writes "$(cat /work/state/baseline.key)"
  [ "$(sql Tranxit_Account "SET NOCOUNT ON; SELECT COUNT(*) FROM Users WHERE Id=$(cat /work/state/actor.id) AND IsEmailVerified=1;")" = 1 ]
  touch /work/state/probes-enabled
  probe invalid-config-after-primary open
  probe invalid-config-after-secondary open
  /bin/rm -f /work/state/probes-enabled
  assert_writes "$F01_CASE_KEY"
  [ "$(wc -l < "/work/state/$F01_CASE_KEY.accounts")" = 4 ]
  [ "$(wc -l < "/work/state/$F01_CASE_KEY.jobs")" = 4 ]

  jq -nc --arg test "$F01_CASE" --arg setting "$name" --arg variant "$variant" \
    '{test:$test,outcome:"passed",setting:$setting,variant:$variant,exitStatus:2,deployDockerCalls:0,
      stateUnchanged:true,publicOrigins:2,accountWrites:4,jobWrites:4,restores:"0|0"}' >> /work/public-results/cases.jsonl
  printf 'PASS: %s (exit=2 Docker=0 unchanged-state accounts=4 jobs=4 origins=2)\n' "$F01_CASE"
}

case_test() {
  # UC-NFR-9 - T-NFR-9.RuntimeDeploymentMatrix (case-specific assertions below).
  . /work/refs.env
  set -a; . /work/env.staging; set +a
  export TRANXIT_IMAGE_TAG="${GREEN_BACKEND:0:12}"
  mkdir -p /work/state /work/public-results
  find /work/state -maxdepth 1 -name 'hit-*' -delete
  /bin/rm -f /work/state/recovering
  printf '0' > /work/state/sequence
  : > "/work/state/$F01_CASE_KEY.accounts"; : > "/work/state/$F01_CASE_KEY.jobs"
  : > /work/state/docker-events
  touch /work/state/probes-enabled
  local status=0 before_count after_count account_restores courier_restores
  local restore_cursor
  restore_cursor="$(sql master 'SET NOCOUNT ON; SELECT COALESCE(MAX(restore_history_id),0) FROM msdb.dbo.restorehistory;' 2>/dev/null || printf 0)"
  run_deploy > "/work/state/$F01_CASE.raw.log" 2>&1 || status=$?
  /bin/rm -f /work/state/probes-enabled
  sanitize < "/work/state/$F01_CASE.raw.log" > "/work/public-results/$F01_CASE.log"
  [ "$status" -eq "$F01_EXPECT_STATUS" ] || { fail "$F01_CASE exit expected $F01_EXPECT_STATUS, got $status (see sanitized log)"; return 1; }
  for point in ${F01_FAULT_POINTS//,/ }; do
    [ -f "/work/state/hit-$point" ] || { fail "$F01_CASE did not reach fault $point"; return 1; }
  done

  # Check data before recovery-log assertions: a forbidden restore must fail on lost writes.
  assert_writes "$F01_CASE_KEY"
  if [ -f /work/state/baseline.key ]; then assert_writes "$(cat /work/state/baseline.key)"; fi
  if [ -f /work/state/actor-ready ]; then
    [ "$(sql Tranxit_Account "SET NOCOUNT ON; SELECT COUNT(*) FROM Users WHERE Id=$(cat /work/state/actor.id) AND IsEmailVerified=1;")" = 1 ] || { fail 'Baseline verified actor was lost'; return 1; }
  fi
  account_restores="$(sql master "SET NOCOUNT ON; SELECT COUNT(*) FROM msdb.dbo.restorehistory WHERE restore_history_id>$restore_cursor AND destination_database_name='Tranxit_Account';")"
  courier_restores="$(sql master "SET NOCOUNT ON; SELECT COUNT(*) FROM msdb.dbo.restorehistory WHERE restore_history_id>$restore_cursor AND destination_database_name='Tranxit_CourierJob';")"
  [ "$account_restores|$courier_restores" = "$F01_EXPECT_RESTORES" ] || { fail "$F01_CASE SQL restore history expected $F01_EXPECT_RESTORES, got $account_restores|$courier_restores"; return 1; }
  [ "$(sentinels)" = "$F01_EXPECT_SCHEMA" ] || { fail "$F01_CASE schema sentinel mismatch"; return 1; }

  case "$F01_END" in
    success)
      grep -F "backend_sha=$F01_TARGET_SHA" /work/markers/last-staging-green >/dev/null
      [ ! -e /work/markers/deploy-staging-admitted ]
      request GET /api/roles; assert_result 200
      ;;
    recovered|unchanged)
      cmp /work/state/green.marker /work/markers/last-staging-green >/dev/null
      [ "$(git -C /work/backend rev-parse HEAD)" = "$GREEN_BACKEND" ]
      [ "$(git -C /work/frontend rev-parse HEAD)" = "$GREEN_FRONTEND" ]
      [ ! -e /work/markers/deploy-staging-admitted ]
      request GET /api/roles; assert_result 200
      local service image
      for service in accountservice courierjobservice frontend; do
        image="$(/usr/local/bin/docker-real inspect --format '{{.Config.Image}}' "$(raw_compose ps -q "$service")")"
        [[ "$image" = *":${GREEN_BACKEND:0:12}" ]] || { fail 'Recovery did not run the known-green image tag'; return 1; }
      done
      ;;
    fenced|first-fenced|marker-fenced|close-fenced)
      all_stopped
      request GET /api/roles
      [ "$HTTP_CODE" = 000 ] || { fail 'Stopped edge was still reachable'; return 1; }
      if [ "$F01_END" = first-fenced ]; then [ ! -e /work/markers/last-staging-green ]
      elif [ "$F01_END" = marker-fenced ]; then
        cmp /work/state/green.marker /work/markers/last-staging-green.prev >/dev/null
        grep -F "backend_sha=$F01_TARGET_SHA" /work/markers/last-staging-green >/dev/null
      else cmp /work/state/green.marker /work/markers/last-staging-green >/dev/null; fi
      if [ "$F01_END" != close-fenced ]; then [ ! -e /work/markers/admission-staging/open ]; fi
      ;;
    unsafe)
      grep -F 'UNSAFE/UNVERIFIED RECOVERY' "/work/state/$F01_CASE.raw.log" >/dev/null
      caddy_running
      request GET /api/roles
      [ "$HTTP_CODE" = 503 ]
      [ -n "$(raw_compose ps -q accountservice)" ]
      cmp /work/state/green.marker /work/markers/last-staging-green >/dev/null
      ;;
    *) fail 'Unknown expected final state'; return 1 ;;
  esac
  if [ -n "${F01_EXPECT_TEXT:-}" ]; then grep -F "$F01_EXPECT_TEXT" "/work/state/$F01_CASE.raw.log" >/dev/null; fi

  if [ "${F01_RESTART_GUARD:-false}" = true ]; then
    before_count="$(wc -l < /work/state/docker-events)"
    status=0
    run_deploy > "/work/state/$F01_CASE.restart.log" 2>&1 || status=$?
    [ "$status" = 1 ]
    grep -E 'unresolved public admission|incomplete live restore' "/work/state/$F01_CASE.restart.log" >/dev/null
    after_count="$(wc -l < /work/state/docker-events)"
    [ "$before_count" = "$after_count" ] || { fail 'Restart guard reached Docker'; return 1; }
    sanitize < "/work/state/$F01_CASE.restart.log" > "/work/public-results/$F01_CASE.restart.log"
  fi
  jq -nc --arg test "$F01_CASE" --arg restores "$account_restores|$courier_restores" \
    --argjson accountWrites "$(wc -l < "/work/state/$F01_CASE_KEY.accounts")" \
    --argjson jobWrites "$(wc -l < "/work/state/$F01_CASE_KEY.jobs")" \
    '{test:$test,outcome:"passed",accountWrites:$accountWrites,jobWrites:$jobWrites,restores:$restores}' >> /work/public-results/cases.jsonl
  printf 'PASS: %s (accounts=%s jobs=%s restores=%s|%s)\n' "$F01_CASE" \
    "$(wc -l < "/work/state/$F01_CASE_KEY.accounts")" "$(wc -l < "/work/state/$F01_CASE_KEY.jobs")" "$account_restores" "$courier_restores"
}

if [ "${1:-}" = --case ]; then case_test; exit; fi
if [ "${1:-}" = --invalid-config ]; then invalid_config_test "${2:-}" "${3:-}"; exit; fi

mkdir -p /work/state /work/public-results /work/markers /work/backups /work/origins
finish() {
  local status=$?
  trap - EXIT
  /bin/rm -f /work/state/probes-enabled
  if [ -f /work/state/driver.log ]; then sanitize < /work/state/driver.log > /work/public-results/driver.log; fi
  if [ "$status" -ne 0 ]; then printf 'FAIL: recovery controller stopped (exit %s); sanitized logs are retained for export.\n' "$status"; fi
  exit "$status"
}
trap finish EXIT

prepare_repo() {
  local name="$1" clone_status=0
  # Match a normal source checkout; the private credentials/backups keep the outer 0077 umask.
  (umask 022; git -c safe.directory="/source/$name" -c safe.directory="/source/$name/.git" -c core.autocrlf=false clone --no-hardlinks "/source/$name" "/work/$name") > "/work/state/clone-$name.log" 2>&1 || clone_status=$?
  if [ "$clone_status" -ne 0 ]; then
    sanitize < "/work/state/clone-$name.log" | tee "/work/public-results/clone-$name.log" >&2
    return "$clone_status"
  fi
  printf '%s=%s\n' "$name" "$(git -C "/work/$name" rev-parse HEAD)" >> /work/public-results/source-refs.txt
  git -C "/work/$name" config user.name 'Recovery Fixture'
  git -C "/work/$name" config user.email 'recovery@example.test'
  git -C "/work/$name" config core.autocrlf false
  git -C "/work/$name" config core.filemode false
  # A PR checkout can point at a detached commit; only the private clone gets a branch.
  git -C "/work/$name" checkout -q -B main
  if [ "$name" = backend ]; then
    for script in deploy backup restore smoke verify-production-topology; do
      cp "/source/backend/TranXit/scripts/$script.sh" "/work/backend/TranXit/scripts/$script.sh"
      chmod +x "/work/backend/TranXit/scripts/$script.sh"
    done
    git -C /work/backend add TranXit/scripts
  fi
  printf 'green\n' > "/work/$name/recovery-release.txt"
  git -C "/work/$name" add recovery-release.txt
  git -C "/work/$name" commit -m 'fixture: known-green release' >/dev/null
  printf 'GREEN_%s=%s\n' "${name^^}" "$(git -C "/work/$name" rev-parse HEAD)" >> /work/refs.env
  printf 'candidate\n' > "/work/$name/recovery-release.txt"
  git -C "/work/$name" add recovery-release.txt
  git -C "/work/$name" commit -m 'fixture: candidate release' >/dev/null
  printf 'CANDIDATE_%s=%s\n' "${name^^}" "$(git -C "/work/$name" rev-parse HEAD)" >> /work/refs.env
  git init --bare "/work/origins/$name.git" >/dev/null 2>&1
  git -C "/work/$name" remote set-url origin "/work/origins/$name.git"
  git -C "/work/$name" push origin main >/dev/null 2>&1
}

echo '[recovery] Preparing private repository copies and local Git origins.'
prepare_repo backend
prepare_repo frontend
if [ "${1:-}" = --prepare-repos ]; then exit; fi
. /work/refs.env
cp /source/backend/TranXit/ops/Caddyfile.staging /work/settings/Caddyfile
cp /work/backend/TranXit/scripts/deploy.sh /work/state/original-deploy.sh
sha256sum /work/backend/TranXit/scripts/{deploy,backup,restore,smoke,verify-production-topology}.sh > /work/public-results/source-hashes.txt
sha256sum /harness/*.sh /harness/compose.yml /harness/runtime.mjs /harness/Dockerfile > /work/public-results/harness-hashes.txt
/usr/local/bin/docker-real version --format '{{json .}}' > /work/public-results/docker-version.json
/usr/local/bin/docker-real compose version > /work/public-results/compose-version.txt

export TRANXIT_ENV_FILE=/work/env.staging TRANXIT_MARKER_DIR=/work/markers
export TRANXIT_BACKUP_DIR=/work/backups TRANXIT_BACKUP_RETENTION_DAYS=14
export TRANXIT_FRONTEND_DIR=/work/frontend TRANXIT_FRONTEND_BUILD_CONTEXT=/work/frontend
export TRANXIT_ADMISSION_DIR=/work/markers/admission-staging
export DOMAIN=tranxit-f01.localhost STAGING_DOMAIN=tranxit-f01-secondary.localhost MAILPIT_DOMAIN=tranxit-f01-mail.localhost
export PUBLIC_APP_URL="https://$DOMAIN" STAGING_APP_URL="https://$STAGING_DOMAIN"
export NEXT_PUBLIC_TRANXIT_API_URL="$PUBLIC_APP_URL" TRANXIT_INTERNAL_API_URL=http://ocelotapigw:8080
export TRANXIT_DEPLOY_ENV=production TRANXIT_IMAGE_TAG="${GREEN_BACKEND:0:12}"
export SQL_SA_PASSWORD="T9!$(openssl rand -hex 24)" JWT_SECRET="$(openssl rand -hex 48)"
export JWT_ISSUER="$F01_PROJECT.Account" JWT_AUDIENCE="$F01_PROJECT.Client" JWT_EXPIRY_MINUTES=120 JWT_REFRESH_EXPIRY_DAYS=1
export RABBITMQ_USER="f01_$(openssl rand -hex 8)" RABBITMQ_PASSWORD="T9!$(openssl rand -hex 24)"
export ADMIN_EMAIL="admin-$F01_PROJECT@example.test" F01_ACTOR_PASSWORD="T9!$(openssl rand -hex 24)"
export SMTP_HOST=mailpit SMTP_PORT=1025 SMTP_USER=recovery SMTP_PASSWORD="$(openssl rand -hex 24)" MAIL_SENDER_NAME=Recovery MAIL_FROM=recovery@example.test
export MAILPIT_BASIC_AUTH_USER=recovery MAILPIT_BASIC_AUTH_PASSWORD="T9!$(openssl rand -hex 24)" MAILPIT_BASIC_AUTH_HASH=pending
export CADDY_ACME_EMAIL=recovery@example.test AUTH_RATE_LIMIT_EVENTS=10000 AUTH_RATE_LIMIT_WINDOW=1m
export TRANXIT_EGRESS_PROBE_URL=https://example.com

write_env() {
  : > /work/env.staging
  local name value
  for name in TRANXIT_MARKER_DIR TRANXIT_BACKUP_DIR TRANXIT_BACKUP_RETENTION_DAYS TRANXIT_FRONTEND_DIR TRANXIT_FRONTEND_BUILD_CONTEXT \
    TRANXIT_ADMISSION_DIR DOMAIN STAGING_DOMAIN MAILPIT_DOMAIN PUBLIC_APP_URL STAGING_APP_URL NEXT_PUBLIC_TRANXIT_API_URL \
    TRANXIT_INTERNAL_API_URL SQL_SA_PASSWORD JWT_SECRET JWT_ISSUER JWT_AUDIENCE JWT_EXPIRY_MINUTES JWT_REFRESH_EXPIRY_DAYS \
    RABBITMQ_USER RABBITMQ_PASSWORD ADMIN_EMAIL F01_ACTOR_PASSWORD SMTP_HOST SMTP_PORT SMTP_USER SMTP_PASSWORD MAIL_SENDER_NAME \
    MAIL_FROM MAILPIT_BASIC_AUTH_USER MAILPIT_BASIC_AUTH_HASH CADDY_ACME_EMAIL AUTH_RATE_LIMIT_EVENTS AUTH_RATE_LIMIT_WINDOW TRANXIT_EGRESS_PROBE_URL; do
    value="${!name}"
    [[ "$value" != *"'"* ]] || return 1
    printf "%s='%s'\n" "$name" "$value" >> /work/env.staging
  done
}
write_env
echo '[recovery] Building Caddy and hashing the disposable mailbox credential.'
raw_compose build caddy > /work/state/driver.log 2>&1
MAILPIT_BASIC_AUTH_HASH="$(printf '%s\n' "$MAILPIT_BASIC_AUTH_PASSWORD" | raw_compose run --rm -T --no-deps --entrypoint caddy caddy hash-password --algorithm bcrypt 2>>/work/state/driver.log | grep -E '^\$2[aby]\$')"
write_env
# Validate the resolved model without printing its credential-bearing JSON.
raw_compose config --format json | jq -e --arg project "$F01_PROJECT" '
  .name == $project and .networks.backend.internal == true and
  ([.services.accountservice,.services.courierjobservice,.services.ocelotapigw] |
    all(.environment.ASPNETCORE_ENVIRONMENT == "Production" and .environment.Jwt__RequireHttpsMetadata == "true")) and
  (.services.frontend.environment | .NODE_ENV == "production" and .TRANXIT_DEPLOY_ENV == "production" and
    .TRANXIT_ENABLE_DEMO_AUTH == "false" and .TRANXIT_E2E_EXPOSE_DEV_CODE == "false") and
  ([.services | to_entries[] | select(.key != "caddy") | .value.ports // [] | length] | all(. == 0)) and
  (.services.caddy.ports | length == 1) and .services.caddy.ports[0].host_ip == "127.0.0.1" and
  ([.services[].restart] | all(. == "no")) and
  ([.services[].labels["io.tranxit.recovery-test"]] | all(. == $project)) and
  ([.services[].volumes[]? | select(.type == "bind")] | length == 0)
' >/dev/null
echo '[recovery] Production service modes, closed SQL network, labels, mounts and loopback port validated.'

case_number=0
run_case() {
  local name="$1" policy="$2" points="$3" status="$4" end="$5" schema="$6" restores="$7" text="${8:-}" restart="${9:-false}" persistent="${10:-false}"
  case_number=$((case_number + 1))
  export F01_CASE="$name" F01_CASE_KEY="c$case_number-$(openssl rand -hex 3)" F01_POLICY="$policy" F01_FAULT_POINTS="$points"
  export F01_EXPECT_STATUS="$status" F01_END="$end" F01_EXPECT_SCHEMA="$schema" F01_EXPECT_RESTORES="$restores"
  export F01_EXPECT_TEXT="$text" F01_RESTART_GUARD="$restart" F01_PERSISTENT="$persistent"
  echo "[recovery] Running $name."
  bash /harness/controller.sh --case
}

run_invalid_config_cases() {
  local name variant key keys=()
  for name in TRANXIT_EGRESS_PROBE_URL PUBLIC_APP_URL STAGING_APP_URL; do
    for variant in unset empty non-https empty-overrides-inherited; do
      case_number=$((case_number + 1))
      export F01_CASE="InvalidConfig-$name-$variant" F01_CASE_KEY="c$case_number-$(openssl rand -hex 3)"
      export F01_POLICY=restore-required
      echo "[recovery] Running $F01_CASE."
      bash /harness/controller.sh --invalid-config "$name" "$variant"
      keys+=("$F01_CASE_KEY")
    done
  done
  for key in "${keys[@]}"; do assert_writes "$key"; done
}

reset_to_baseline() {
  /bin/rm -f /work/state/probes-enabled
  export TRANXIT_IMAGE_TAG="${GREEN_BACKEND:0:12}"
  raw_compose stop --timeout 30 caddy frontend ocelotapigw accountservice courierjobservice >>/work/state/driver.log 2>&1
  all_stopped
  if [ -f /work/markers/restore-staging-in-progress ]; then
    local unfinished
    unfinished="$(awk -F= '/^manifest=/{print substr($0,10)}' /work/markers/restore-staging-in-progress)"
    bash /work/backend/TranXit/scripts/restore.sh --env staging --manifest "$unfinished" \
      --account-database Tranxit_Account --courier-database Tranxit_CourierJob --replace-live --confirm RESTORE-LIVE-staging >>/work/state/driver.log 2>&1
  fi
  bash /work/backend/TranXit/scripts/restore.sh --env staging --manifest "$BASELINE_MANIFEST" \
    --account-database Tranxit_Account --courier-database Tranxit_CourierJob --replace-live --confirm RESTORE-LIVE-staging >>/work/state/driver.log 2>&1
  /bin/rm -f /work/markers/deploy-staging-admitted /work/markers/last-staging-green.prev
  cp /work/state/green.marker /work/markers/last-staging-green
  git -C /work/backend checkout --detach "$GREEN_BACKEND" >>/work/state/driver.log 2>&1
  git -C /work/frontend checkout --detach "$GREEN_FRONTEND" >>/work/state/driver.log 2>&1
  raw_compose up -d --no-deps --wait --wait-timeout 180 accountservice courierjobservice >>/work/state/driver.log 2>&1
  raw_compose up -d --no-deps --wait --wait-timeout 180 ocelotapigw frontend caddy >>/work/state/driver.log 2>&1
  printf 'fixture baseline admitted\n' > /work/markers/admission-staging/open
  request GET /api/roles; assert_result 200
  assert_writes "$(cat /work/state/baseline.key)"
}

export F01_FIRST=true F01_TARGET_SHA="$CANDIDATE_BACKEND" F01_TARGET_FRONTEND="$CANDIDATE_FRONTEND"
export TRANXIT_ALLOW_FAILURE_INJECTION=true TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE=false TRANXIT_DEPLOY_TEST_FORCE_POST_ADMISSION_FAILURE=false
run_case FirstDeployMigrationFailure restore-required forward:migration-account 91 first-fenced '1|0' '0|0' 'Automatic rollback unavailable'
/bin/rm -f /work/state/probes-enabled
sql master 'ALTER DATABASE [Tranxit_Account] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [Tranxit_Account]; ALTER DATABASE [Tranxit_CourierJob] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [Tranxit_CourierJob];' >/dev/null

export TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE=true
run_case FirstDeploySmokeFailure restore-required '' 1 first-fenced '1|1' '0|0' 'Automatic rollback unavailable'
/bin/rm -f /work/state/probes-enabled
sql master 'ALTER DATABASE [Tranxit_Account] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [Tranxit_Account]; ALTER DATABASE [Tranxit_CourierJob] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [Tranxit_CourierJob];' >/dev/null
export TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE=false
export F01_TARGET_SHA="$GREEN_BACKEND" F01_TARGET_FRONTEND="$GREEN_FRONTEND"
run_case FirstDeploySuccess expand-contract '' 0 success '0|0' '0|0'
printf '%s' "$F01_CASE_KEY" > /work/state/baseline.key
cp /work/markers/last-staging-green /work/state/green.marker
/bin/rm -f /work/state/probes-enabled
export F01_FIRST=false F01_TARGET_SHA="$CANDIDATE_BACKEND" F01_TARGET_FRONTEND="$CANDIDATE_FRONTEND"
run_invalid_config_cases
bash /work/backend/TranXit/scripts/backup.sh --env staging --release-id baseline > /work/state/baseline-backup.log 2>&1
BASELINE_MANIFEST="$(awk -F= '/^MANIFEST=/{print substr($0,10)}' /work/state/baseline-backup.log)"
[ -f "$BASELINE_MANIFEST" ]
export F01_FIRST=false F01_TARGET_SHA="$CANDIDATE_BACKEND" F01_TARGET_FRONTEND="$CANDIDATE_FRONTEND"

reset_to_baseline
run_case CandidateSuccess expand-contract '' 0 success '1|1' '0|0'
reset_to_baseline
run_case ConfigFailure expand-contract forward:config 91 unchanged '0|0' '0|0'
reset_to_baseline
run_case BuildFailure expand-contract forward:build 91 unchanged '0|0' '0|0'
reset_to_baseline
run_case BackupPairFailure restore-required forward:backup-courier-before 91 recovered '0|0' '0|0'
reset_to_baseline
run_case PartialMigrationRestore restore-required forward:migration-account 91 recovered '0|0' '1|1'
reset_to_baseline
export TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE=true
run_case PreAdmissionExpand expand-contract '' 1 recovered '1|1' '0|0'
reset_to_baseline
run_case PreAdmissionRestore restore-required '' 1 recovered '0|0' '1|1'
reset_to_baseline
export TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE=false TRANXIT_DEPLOY_TEST_FORCE_POST_ADMISSION_FAILURE=true
run_case PostAdmissionExpand expand-contract '' 1 recovered '1|1' '0|0'
reset_to_baseline
run_case PostAdmissionRestoreRefused restore-required '' 1 fenced '1|1' '0|0' 'AUTOMATIC RESTORE REFUSED' true
reset_to_baseline
export TRANXIT_DEPLOY_TEST_FORCE_POST_ADMISSION_FAILURE=false TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE=true
run_case RecoverySmokeFailure restore-required recovery:smoke 1 fenced '0|0' '1|1' 'AUTOMATIC ROLLBACK FAILED'
reset_to_baseline
run_case SecondDatabaseRestoreFailure restore-required recovery:restore-courier-before 1 fenced '0|1' '1|0' 'LIVE RESTORE INCOMPLETE' true
reset_to_baseline
export TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE=false
run_case GateCloseFailure restore-required forward:close-before,recovery:close-before 1 close-fenced '0|0' '0|0' 'UNSAFE/UNVERIFIED RECOVERY' false true
reset_to_baseline
run_case GateOpenSideEffectFailure restore-required forward:opened 91 fenced '1|1' '0|0' 'AUTOMATIC RESTORE REFUSED' true
reset_to_baseline
run_case JournalSyncFailure restore-required forward:journal-synced 91 fenced '1|1' '0|0' 'AUTOMATIC RESTORE REFUSED' true
reset_to_baseline
run_case MarkerFinalizationFailure restore-required forward:marker-synced 91 marker-fenced '1|1' '0|0' 'Release-marker finalization is incomplete' true
reset_to_baseline
run_case StopVerificationFailure restore-required forward:stop-edge-before,forward:stop-writers-before,recovery:stop-edge-before,recovery:stop-writers-before 1 unsafe '0|0' '0|0' 'UNSAFE/UNVERIFIED RECOVERY' false true

reset_to_baseline
echo '[recovery] Credibility mutation: remove only the post-admission restore refusal in the private script copy.'
sed -i '/if \[ "\$admission_may_have_opened" = "true" \] ||/,+1c\      if false; then' /work/backend/TranXit/scripts/deploy.sh
git -C /work/backend diff -- TranXit/scripts/deploy.sh > /work/public-results/credibility.diff
grep -F '+      if false; then' /work/public-results/credibility.diff >/dev/null
export TRANXIT_DEPLOY_TEST_FORCE_POST_ADMISSION_FAILURE=true
if run_case CredibilityRed restore-required '' 1 fenced '1|1' '0|0' 'AUTOMATIC RESTORE REFUSED' true > /work/state/credibility.out 2>&1; then
  fail 'Credibility check failed: unsafe restore mutation passed'; exit 1
fi
grep -F 'T-NFR-9.AcknowledgedWritesPreserved' /work/state/credibility.out >/dev/null
sanitize < /work/state/credibility.out > /work/public-results/credibility-red.log
echo 'PASS: CredibilityRed caught acknowledged-write loss.'
cp /work/state/original-deploy.sh /work/backend/TranXit/scripts/deploy.sh
cmp /source/backend/TranXit/scripts/deploy.sh /work/backend/TranXit/scripts/deploy.sh >/dev/null
reset_to_baseline
run_case CredibilityRestored restore-required '' 1 fenced '1|1' '0|0' 'AUTOMATIC RESTORE REFUSED' true

jq -s '{passed:length,cases:.,credibility:"unsafe restore red; restored source green"}' /work/public-results/cases.jsonl > /work/public-results/summary.json
echo '[recovery] Full runtime matrix and credibility check passed. Real deployment remains unapproved.'
