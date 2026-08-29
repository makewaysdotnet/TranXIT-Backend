#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage: scripts/deploy.sh --env staging|production --sha <backend-commit> \
  --migration-policy expand-contract|restore-required [--frontend-ref <ref>] \
  [--allow-first-deploy]

Migration policy:
  expand-contract   Candidate migrations are additive/backward-compatible. Smoke failure rolls
                    back code only and keeps the expanded schema.
  restore-required  Candidate migrations are not backward-compatible. Only a pre-admission failure
                    may restore the paired backup. After admission, fence for manual recovery.

Environment:
  TRANXIT_ENV_FILE      Defaults to /opt/tranxit/.env.
  TRANXIT_FRONTEND_DIR  Defaults to ../../frontend from the backend repo root.
  TRANXIT_DEPLOY_PROJECT_DIR  Application project; set explicitly for the installed controller.
USAGE
}

TARGET_ENV=""
BACKEND_SHA=""
FRONTEND_REF="main"
MIGRATION_POLICY=""
ALLOW_FIRST_DEPLOY="false"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --env)
      TARGET_ENV="${2:-}"
      shift 2
      ;;
    --sha)
      BACKEND_SHA="${2:-}"
      shift 2
      ;;
    --frontend-ref)
      FRONTEND_REF="${2:-}"
      shift 2
      ;;
    --migration-policy)
      MIGRATION_POLICY="${2:-}"
      shift 2
      ;;
    --allow-first-deploy)
      ALLOW_FIRST_DEPLOY="true"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage
      exit 2
      ;;
  esac
done

if [ "$TARGET_ENV" != "staging" ] && [ "$TARGET_ENV" != "production" ]; then
  echo "--env must be staging or production" >&2
  exit 2
fi
if ! [[ "$BACKEND_SHA" =~ ^[0-9A-Fa-f]{7,40}$ ]]; then
  echo "--sha must be a 7-40 character Git commit SHA" >&2
  exit 2
fi
if ! [[ "$FRONTEND_REF" =~ ^[A-Za-z0-9._/-]+$ ]]; then
  echo "--frontend-ref contains unsupported characters" >&2
  exit 2
fi
if [[ "$FRONTEND_REF" == -* ]]; then
  echo "--frontend-ref may not begin with a dash" >&2
  exit 2
fi
if [ "$MIGRATION_POLICY" != "expand-contract" ] &&
   [ "$MIGRATION_POLICY" != "restore-required" ]; then
  echo "--migration-policy must be expand-contract or restore-required" >&2
  exit 2
fi

# The supported entrypoint is installed outside the checkout; retain its immutable physical path.
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
if [ -z "${TRANXIT_DEPLOY_PROJECT_DIR:-}" ]; then
  echo "TRANXIT_DEPLOY_PROJECT_DIR is required; invoke the reviewed controller installed outside the backend repository." >&2
  exit 2
fi
if [ ! -d "$TRANXIT_DEPLOY_PROJECT_DIR" ]; then
  echo "TRANXIT_DEPLOY_PROJECT_DIR must name the application project directory." >&2
  exit 2
fi
PROJECT_DIR="$(cd "$TRANXIT_DEPLOY_PROJECT_DIR" && pwd -P)"
BACKEND_REPO_DIR="$(git -C "$PROJECT_DIR" rev-parse --show-toplevel 2>/dev/null)" || {
  echo "TRANXIT_DEPLOY_PROJECT_DIR must be inside the backend Git repository." >&2
  exit 2
}
BACKEND_REPO_DIR="$(cd "$BACKEND_REPO_DIR" && pwd -P)"
case "$SCRIPT_DIR/" in
  "$BACKEND_REPO_DIR/"*)
    echo "Refusing controller execution from inside BACKEND_REPO_DIR; invoke the reviewed external controller." >&2
    exit 2
    ;;
esac
readonly TRANXIT_EDGE_PROBE_IMAGE='curlimages/curl@sha256:d43bdb28bae0be0998f3be83199bfb2b81e0a30b034b6d7586ce7e05de34c3fd'
export TRANXIT_HTTP_PROBE_IMAGE="$TRANXIT_EDGE_PROBE_IMAGE"
ENV_FILE="${TRANXIT_ENV_FILE:-/opt/tranxit/.env}"

if [ ! -f "$ENV_FILE" ]; then
  echo "Missing env file: $ENV_FILE" >&2
  exit 1
fi

set -a
# shellcheck source=/dev/null
. "$ENV_FILE"
set +a

validate_deploy_inputs() {
  local name value authority
  for name in PUBLIC_APP_URL STAGING_APP_URL TRANXIT_EGRESS_PROBE_URL; do
    value="${!name:-}"
    if [ -z "$value" ]; then
      printf 'Missing required deployment setting: %s\n' "$name" >&2
      return 2
    fi
    authority="${value#https://}"
    authority="${authority%%[/?#]*}"
    if [[ "$value" != https://* ]] || [[ "$value" == *[[:space:]]* ]] ||
       [ -z "$authority" ] || [[ "$authority" == *@* ]]; then
      printf '%s must be an absolute HTTPS URL without credentials or whitespace.\n' "$name" >&2
      return 2
    fi
  done
}

# Parameter-expansion errors bypass ERR recovery; reject required inputs before any state change.
validate_deploy_inputs || exit $?
readonly PUBLIC_APP_URL STAGING_APP_URL TRANXIT_EGRESS_PROBE_URL

# Load the trusted contract once; a candidate cannot supply its own admission policy.
# shellcheck source=admission-contract.sh
. "$SCRIPT_DIR/admission-contract.sh" || exit 2

MARKER_DIR="${TRANXIT_MARKER_DIR:-/opt/tranxit}"
MARKER_FILE="$MARKER_DIR/last-$TARGET_ENV-green"
MARKER_PREV_FILE="$MARKER_FILE.prev"
if [[ "$MARKER_DIR" != /* ]]; then
  echo "TRANXIT_MARKER_DIR must be an absolute path" >&2
  exit 2
fi
export TRANXIT_ADMISSION_DIR="${TRANXIT_ADMISSION_DIR:-$MARKER_DIR/admission-$TARGET_ENV}"
if [[ "$TRANXIT_ADMISSION_DIR" != /* ]] || [ -L "$TRANXIT_ADMISSION_DIR" ]; then
  echo "TRANXIT_ADMISSION_DIR must be an absolute, non-symlink directory." >&2
  exit 2
fi

if [ "$TARGET_ENV" = "production" ]; then
  for mailpit_var in MAILPIT_DOMAIN MAILPIT_BASIC_AUTH_USER MAILPIT_BASIC_AUTH_HASH TRANXIT_E2E_MAIL_INBOX; do
    if [ -n "${!mailpit_var:-}" ]; then
      echo "Refusing production deploy: $mailpit_var is set, but Mailpit is staging-only." >&2
      exit 1
    fi
  done
fi

if { [ "${TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE:-false}" = "true" ] ||
     [ "${TRANXIT_DEPLOY_TEST_FORCE_POST_ADMISSION_FAILURE:-false}" = "true" ]; } &&
   { [ "$TARGET_ENV" != "staging" ] ||
     [ "${TRANXIT_ALLOW_FAILURE_INJECTION:-false}" != "true" ]; }; then
  echo "Failure injection is allowed only on staging with TRANXIT_ALLOW_FAILURE_INJECTION=true." >&2
  exit 1
fi

FRONTEND_DIR="${TRANXIT_FRONTEND_DIR:-$(cd "$BACKEND_REPO_DIR/../frontend" 2>/dev/null && pwd || true)}"
if [ -z "$FRONTEND_DIR" ] || [ ! -d "$FRONTEND_DIR/.git" ]; then
  echo "Frontend repo not found. Set TRANXIT_FRONTEND_DIR in $ENV_FILE." >&2
  exit 1
fi

if ! command -v flock >/dev/null 2>&1; then
  echo "The deploy host must provide flock (util-linux)." >&2
  exit 1
fi
mkdir -p "$MARKER_DIR"
lock_file="$MARKER_DIR/deploy-$TARGET_ENV.lock"
exec 9>"$lock_file"
chmod 600 "$lock_file"
if ! flock -n 9; then
  echo "Another $TARGET_ENV deployment is already running." >&2
  exit 1
fi
incomplete_restore_state="$MARKER_DIR/restore-$TARGET_ENV-in-progress"
if [ -f "$incomplete_restore_state" ]; then
  echo "Refusing deploy: an incomplete live restore must be recovered first: $incomplete_restore_state" >&2
  exit 1
fi

ADMISSION_STATE="$MARKER_DIR/deploy-$TARGET_ENV-admitted"
if [ -e "$ADMISSION_STATE" ] || [ -L "$ADMISSION_STATE" ]; then
  echo "Refusing deploy: unresolved public admission may include acknowledged writes: $ADMISSION_STATE" >&2
  echo "Fence and reconcile that release using the runbook; do not restore its pre-migration backup automatically." >&2
  exit 1
fi
mkdir -p "$TRANXIT_ADMISSION_DIR"
ADMISSION_OPEN="$TRANXIT_ADMISSION_DIR/open"

project_name="tranxit-$TARGET_ENV"
compose_files=(
  -f "$PROJECT_DIR/docker-compose.yml"
  -f "$PROJECT_DIR/docker-compose.prod.yml"
)
if [ "$TARGET_ENV" = "staging" ]; then
  compose_files+=(-f "$PROJECT_DIR/docker-compose.staging.yml")
fi

compose() {
  docker compose --env-file "$ENV_FILE" \
    -p "$project_name" \
    "${compose_files[@]}" "$@"
}

application_services=(caddy frontend ocelotapigw accountservice courierjobservice)
stateful_services=(sqlserver rabbitmq)

close_public_admission() {
  closed_edge_verified=false
  rm -f -- "$ADMISSION_OPEN" || return $?
  if [ -e "$ADMISSION_OPEN" ] || [ -L "$ADMISSION_OPEN" ]; then
    echo "Public admission could not be closed; external fencing is required." >&2
    return 1
  fi
  echo "Public admission is closed."
}

record_public_admission() {
  local backend_sha="$1" frontend_sha="$2" state_tmp
  state_tmp="$(mktemp "$MARKER_DIR/.admitted-$TARGET_ENV.XXXXXX")" || return $?
  printf 'environment=%s\nbackend_sha=%s\nfrontend_sha=%s\nbackup_manifest=%s\n' \
    "$TARGET_ENV" "$backend_sha" "$frontend_sha" "$backup_manifest" > "$state_tmp" || return $?
  chmod 600 "$state_tmp" || return $?
  mv -fT -- "$state_tmp" "$ADMISSION_STATE" || return $?
  # Persist before opening the edge so a process restart cannot forget possible public writes.
  sync -f "$ADMISSION_STATE" || return $?
  admission_may_have_opened=true
}

open_public_admission() {
  local open_tmp
  if [ "${closed_edge_verified:-false}" != true ] || [ ! -f "$ADMISSION_STATE" ] ||
     [ -e "$ADMISSION_OPEN" ] || [ -L "$ADMISSION_OPEN" ]; then
    echo "Refusing admission without a persisted boundary and a verified closed edge." >&2
    return 1
  fi
  open_tmp="$(mktemp "$TRANXIT_ADMISSION_DIR/.open.XXXXXX")" || return $?
  printf 'admitted\n' > "$open_tmp" || return $?
  chmod 644 "$open_tmp" || return $?
  mv -fT -- "$open_tmp" "$ADMISSION_OPEN" || return $?
  echo "Public admission is open."
}

complete_public_admission() {
  rm -f -- "$ADMISSION_STATE" || return $?
  sync -f "$MARKER_DIR" || return $?
}

fence_application_stack() {
  local failed=0
  close_public_admission || failed=1
  stop_application_stack || failed=1
  [ "$failed" -eq 0 ]
}

start_application_stack() {
  local status=0
  closed_edge_verified=false
  if [ "$TARGET_ENV" = "staging" ]; then
    compose up -d --no-deps --wait --wait-timeout 300 mailpit || return $?
  fi
  compose up -d --no-deps --wait --wait-timeout 300 accountservice courierjobservice || return $?
  compose up -d --no-deps --wait --wait-timeout 300 ocelotapigw || return $?
  compose up -d --no-deps --wait --wait-timeout 300 frontend || return $?
  compose up -d --no-deps --wait --wait-timeout 300 caddy || status=$?
  if [ "$status" -ne 0 ]; then
    preserve_unverified_admission
    return "$status"
  fi
  verify_closed_public_edge || return $?
}

stop_application_stack() {
  local failed=0
  local service container_ids container_id state

  # Attempt every stop even if the edge stop fails; never infer runtime state from exit 0.
  compose stop --timeout 30 caddy || failed=1
  compose stop --timeout 30 frontend ocelotapigw accountservice courierjobservice || failed=1

  for service in "${application_services[@]}"; do
    if ! container_ids="$(docker ps --all --quiet \
      --filter "label=com.docker.compose.project=$project_name" \
      --filter "label=com.docker.compose.service=$service")"; then
      echo "Cannot enumerate $service containers; stopped state is unverified." >&2
      failed=1
      continue
    fi
    for container_id in $container_ids; do
      if ! state="$(docker inspect --format '{{.State.Status}}' "$container_id")"; then
        echo "Cannot inspect $service container $container_id; stopped state is unverified." >&2
        failed=1
        continue
      fi
      case "$state" in
        created|exited|dead) ;;
        *)
          echo "$service container $container_id is not confirmed stopped (state: $state)." >&2
          failed=1
          ;;
      esac
    done
  done

  if [ "$failed" -ne 0 ]; then
    return 1
  fi
  echo "Application services are confirmed stopped."
}

marker_value() {
  local key="$1"
  local marker="$2"
  awk -v key="$key" '
    index($0, key "=") == 1 {
      value=substr($0, length(key) + 2)
      count++
    }
    END {
      if (count != 1) exit 1
      print value
    }
  ' "$marker"
}

rollback_available="false"
green_backend_sha=""
green_frontend_sha=""
green_image_tag=""
green_admission_policy=""
if [ -f "$MARKER_FILE" ]; then
  if [ "$ALLOW_FIRST_DEPLOY" = "true" ]; then
    echo "--allow-first-deploy is invalid because a known-green marker already exists: $MARKER_FILE" >&2
    exit 2
  fi
  green_environment="$(marker_value environment "$MARKER_FILE")"
  green_backend_sha="$(marker_value backend_sha "$MARKER_FILE")"
  green_frontend_sha="$(marker_value frontend_sha "$MARKER_FILE")"
  green_image_tag="$(marker_value image_tag "$MARKER_FILE" 2>/dev/null || printf '%s' "${green_backend_sha:0:12}")"
  green_admission_policy="$(marker_value admission_policy "$MARKER_FILE" 2>/dev/null || true)"

  if [ "$green_environment" != "$TARGET_ENV" ] ||
     ! [[ "$green_backend_sha" =~ ^[0-9A-Fa-f]{40}$ ]] ||
     ! [[ "$green_frontend_sha" =~ ^[0-9A-Fa-f]{40}$ ]] ||
     ! [[ "$green_image_tag" =~ ^[A-Za-z0-9._-]+$ ]]; then
    echo "Known-green marker is invalid: $MARKER_FILE" >&2
    exit 1
  fi
  rollback_available="true"
else
  if [ "$ALLOW_FIRST_DEPLOY" != "true" ]; then
    echo "No known-green marker exists. Re-run with --allow-first-deploy only after reviewing the first-deploy recovery path." >&2
    exit 1
  fi
  echo "WARNING: first deploy explicitly acknowledged; automatic rollback is unavailable until this candidate becomes green."
fi

base_url="$PUBLIC_APP_URL"
if [ "$TARGET_ENV" = "staging" ]; then
  base_url="$STAGING_APP_URL"
fi

candidate_backend_sha=""
candidate_frontend_sha=""
candidate_image_tag=""
backup_manifest=""
account_migration_before=""
courier_migration_before=""
account_migration_after=""
courier_migration_after=""
writers_stopped="false"
migration_started="false"
admission_may_have_opened="false"
marker_commit_started="false"
closed_edge_verified=false
active_backend_sha=""
active_frontend_sha=""
CONTROLLER_DIR=""

snapshot_controller() {
  local helper
  CONTROLLER_DIR="$(mktemp -d "$MARKER_DIR/.deploy-controller.XXXXXXXX")" || return 1
  trap cleanup_controller EXIT
  for helper in backup restore smoke verify-production-topology; do
    if [ ! -f "$SCRIPT_DIR/$helper.sh" ] || [ -L "$SCRIPT_DIR/$helper.sh" ]; then
      echo "Trusted deployment helper is missing or a symlink: $helper.sh" >&2
      return 1
    fi
    cp -- "$SCRIPT_DIR/$helper.sh" "$CONTROLLER_DIR/$helper.sh" || return 1
    chmod 500 "$CONTROLLER_DIR/$helper.sh" || return 1
  done
}

cleanup_controller() {
  local status=$?
  trap - EXIT
  case "$CONTROLLER_DIR" in
    "$MARKER_DIR"/.deploy-controller.*)
      if [ -d "$CONTROLLER_DIR" ] && [ ! -L "$CONTROLLER_DIR" ]; then
        rm -f -- "$CONTROLLER_DIR/backup.sh" "$CONTROLLER_DIR/restore.sh" \
          "$CONTROLLER_DIR/smoke.sh" "$CONTROLLER_DIR/verify-production-topology.sh"
        rmdir -- "$CONTROLLER_DIR" || echo 'Warning: trusted helper snapshot cleanup needs attention.' >&2
      fi
      ;;
  esac
  exit "$status"
}

preserve_unverified_admission() {
  # An unverified edge may already have acknowledged writes, even with no open sentinel.
  admission_may_have_opened=true
  echo 'Public admission is unverified; automatic database restore is prohibited.' >&2
  record_public_admission "$active_backend_sha" "$active_frontend_sha" || {
    echo 'Could not persist the unverified-admission journal; external fencing and manual reconciliation are required.' >&2
    return 1
  }
}

verify_closed_public_edge() {
  local origin response status attempt ready index=0
  closed_edge_verified=false
  if [ -e "$ADMISSION_OPEN" ] || [ -L "$ADMISSION_OPEN" ]; then
    preserve_unverified_admission || true
    return 1
  fi
  for origin in "$PUBLIC_APP_URL" "$STAGING_APP_URL"; do
    index=$((index + 1))
    ready=false
    for attempt in $(seq 1 12); do
      status=0
      # Connect to the public TLS listener from the private network, preserving the URL's SNI.
      response="$(docker run --pull=never --rm --network "${project_name}_backend" "$TRANXIT_EDGE_PROBE_IMAGE" \
        --connect-timeout 5 --max-time 10 --silent --show-error \
        --connect-to '::caddy:443' --write-out '|%{http_code}|%header{retry-after}' \
        "${origin%/}/api/roles" 2>/dev/null)" || status=$?
      if [ "$status" -eq 0 ]; then
        if [ "$response" = 'Temporarily unavailable|503|60' ]; then ready=true; fi
        break
      fi
      # Only read-only readiness requests are retried. TLS/network failures never prove closure.
      [ "$attempt" -eq 12 ] || sleep 2 || break
    done
    if [ "$ready" != true ]; then
      printf 'Public origin %s did not prove closed admission (curl exit %s).\n' "$index" "$status" >&2
      preserve_unverified_admission || true
      return 1
    fi
  done
  closed_edge_verified=true
  echo 'Both public TLS origins returned the admission gate response (503); private smoke remains separate.'
}

wait_for_gateway() {
  local ready="false"
  echo "Waiting for the private edge at http://caddy:8082/api/roles (public admission stays closed)..."
  for _ in $(seq 1 60); do
    if docker run --pull=never --rm --network "${project_name}_backend" "$TRANXIT_EDGE_PROBE_IMAGE" \
      --connect-timeout 5 --max-time 10 -fsS http://caddy:8082/api/roles >/dev/null 2>&1; then
      ready="true"
      break
    fi
    sleep 5 || return $?
  done
  if [ "$ready" != "true" ]; then
    echo "Gateway did not become ready within 5 minutes." >&2
    return 1
  fi
}

run_release_smoke() {
  "$CONTROLLER_DIR/verify-production-topology.sh" \
    --project-name "$project_name" \
    --egress-url "$TRANXIT_EGRESS_PROBE_URL" || return $?
  export TRANXIT_SMOKE_DOCKER_NETWORK="${TRANXIT_SMOKE_DOCKER_NETWORK:-${project_name}_backend}"
  TRANXIT_SMOKE_PRIVATE_HTTP=true "$CONTROLLER_DIR/smoke.sh" --base-url http://caddy:8082 || return $?
}

database_migration_head() {
  local database="$1"
  compose exec -T -e DB_NAME="$database" sqlserver /bin/bash -lc '
    set -euo pipefail
    if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
      SQLCMD=/opt/mssql-tools18/bin/sqlcmd
      TRUST_FLAG=-C
    else
      SQLCMD=/opt/mssql-tools/bin/sqlcmd
      TRUST_FLAG=
    fi
    "$SQLCMD" -b $TRUST_FLAG -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
      -d "$DB_NAME" -h -1 -W \
      -Q "SET NOCOUNT ON; SELECT COALESCE(MAX([MigrationId]), N'\''none'\'') FROM [__EFMigrationsHistory];"
  ' | tr -d '\r' | awk 'NF { value=$0 } END { print value }'
}

database_pair_presence() {
  compose exec -T sqlserver /bin/bash -lc '
    set -euo pipefail
    if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
      SQLCMD=/opt/mssql-tools18/bin/sqlcmd
      TRUST_FLAG=-C
    else
      SQLCMD=/opt/mssql-tools/bin/sqlcmd
      TRUST_FLAG=
    fi
    "$SQLCMD" -b $TRUST_FLAG -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
      -d master -h -1 -W \
      -Q "SET NOCOUNT ON; /* tranxit-database-presence */ SELECT CONCAT(IIF(DB_ID(N'\''Tranxit_Account'\'') IS NULL, 0, 1), N'\''|'\'', IIF(DB_ID(N'\''Tranxit_CourierJob'\'') IS NULL, 0, 1));"
  ' | tr -d '\r' | awk 'NF { value=$0 } END { print value }'
}

create_empty_database_pair() {
  compose exec -T sqlserver /bin/bash -lc '
    set -euo pipefail
    if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
      SQLCMD=/opt/mssql-tools18/bin/sqlcmd
      TRUST_FLAG=-C
    else
      SQLCMD=/opt/mssql-tools/bin/sqlcmd
      TRUST_FLAG=
    fi
    "$SQLCMD" -b $TRUST_FLAG -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
      -d master \
      -Q "IF DB_ID(N'\''Tranxit_Account'\'') IS NULL CREATE DATABASE [Tranxit_Account]; IF DB_ID(N'\''Tranxit_CourierJob'\'') IS NULL CREATE DATABASE [Tranxit_CourierJob];"
  '
}

checkout_known_green() {
  validate_admission_artifact "$BACKEND_REPO_DIR" "$green_backend_sha" rollback || return 1
  git -C "$BACKEND_REPO_DIR" checkout --detach "$green_backend_sha" || return 1
  validate_admission_checkout "$BACKEND_REPO_DIR" "$green_backend_sha" rollback || return 1
  git -C "$FRONTEND_DIR" checkout --detach "$green_frontend_sha" || return 1
  active_backend_sha="$green_backend_sha"
  active_frontend_sha="$green_frontend_sha"
  export TRANXIT_IMAGE_TAG="$green_image_tag"
  export TRANXIT_FRONTEND_BUILD_CONTEXT="$FRONTEND_DIR"
}

rollback_known_green() {
  echo "Stopping and verifying candidate application services before recovery."
  fence_application_stack || return $?

  if [ "$rollback_available" != "true" ]; then
    echo "Automatic rollback unavailable because no last-$TARGET_ENV-green marker exists." >&2
    return 1
  fi
  if [ "$green_admission_policy" != "private-smoke-v1" ]; then
    echo "Known-green release predates the private admission gate; automatic restart is unsafe. Recover manually while fenced." >&2
    return 1
  fi

  echo "Rolling back to known-green backend $green_backend_sha and frontend $green_frontend_sha"

  # Validate the actual materialized rollback release before either database can be replaced.
  checkout_known_green || return 1
  compose config --quiet || return 1

  if [ "$migration_started" = "true" ]; then
    if [ "$MIGRATION_POLICY" = "restore-required" ]; then
      if [ "$admission_may_have_opened" = "true" ] ||
         [ -e "$ADMISSION_STATE" ] || [ -L "$ADMISSION_STATE" ]; then
        echo "AUTOMATIC RESTORE REFUSED: public traffic may have been admitted; the pre-migration backup cannot preserve acknowledged writes." >&2
        echo "Keep the stack fenced and reconcile the live databases. Admission record: $ADMISSION_STATE" >&2
        return 1
      fi
      if [ -z "$backup_manifest" ]; then
        echo "Cannot restore: pre-migration backup manifest is unavailable." >&2
        return 1
      fi
      echo "Migration policy is restore-required: restoring the paired pre-migration backup."
      TRANXIT_DEPLOY_LOCK_HELD=true TRANXIT_DEPLOY_PROJECT_DIR="$PROJECT_DIR" "$CONTROLLER_DIR/restore.sh" \
        --env "$TARGET_ENV" \
        --manifest "$backup_manifest" \
        --account-database Tranxit_Account \
        --courier-database Tranxit_CourierJob \
        --replace-live \
        --confirm "RESTORE-LIVE-$TARGET_ENV" || return 1
    else
      echo "Migration policy is expand-contract: retaining the backward-compatible expanded schema."
    fi
  else
    echo "Migrations did not begin: no database restore is required."
  fi

  compose build caddy accountservice courierjobservice ocelotapigw frontend || return 1
  compose up -d --no-recreate --wait --wait-timeout 300 "${stateful_services[@]}" || return 1
  start_application_stack || return 1
  wait_for_gateway || return 1
  run_release_smoke || return 1
  verify_closed_public_edge || return 1
  record_public_admission "$green_backend_sha" "$green_frontend_sha" || return 1
  open_public_admission || return 1
  complete_public_admission || return 1
  echo "Known-green release restored successfully. Marker remains unchanged: $MARKER_FILE"
}

on_error() {
  local exit_code="$1"
  local line="$2"
  trap - ERR
  set +e
  echo "Candidate deploy failed at line $line with exit code $exit_code." >&2
  compose ps >&2 || true
  compose logs --tail=200 --no-color >&2 || true

  if [ "$writers_stopped" = "true" ] || [ "$migration_started" = "true" ]; then
    if [ "$marker_commit_started" = "true" ]; then
      echo "Release-marker finalization is incomplete; reconcile the marker and admission record manually. Automatic restore/redeploy is disabled." >&2
      if fence_application_stack; then
        echo "RECOVERY FENCED: application services are confirmed stopped; preserve live data while reconciling finalization." >&2
      else
        echo "UNSAFE/UNVERIFIED RECOVERY: block traffic outside this stack and verify all writers are stopped." >&2
      fi
    elif ! rollback_known_green; then
      echo "AUTOMATIC ROLLBACK FAILED. Stopping and verifying application services again." >&2
      if fence_application_stack; then
        echo "RECOVERY FENCED: application services are confirmed stopped; use the runbook and paired backup manifest." >&2
      else
        echo "UNSAFE/UNVERIFIED RECOVERY: application services may still be running. Block public traffic outside this stack and verify all writers are stopped before any manual restore or restart." >&2
      fi
      [ -n "$backup_manifest" ] && echo "Backup manifest: $backup_manifest" >&2
    fi
  else
    echo "Failure occurred before application writers were stopped; the running known-green stack was not changed." >&2
    if [ "$rollback_available" = "true" ]; then
      if checkout_known_green; then
        echo "Deployment worktrees returned to the known-green SHAs." >&2
      else
        echo "WARNING: running containers are unchanged, but the deployment worktrees could not be returned to the marker SHAs." >&2
      fi
    fi
  fi
  exit "$exit_code"
}

echo "Deploy target: $TARGET_ENV"
echo "Requested backend SHA: $BACKEND_SHA"
echo "Frontend ref: $FRONTEND_REF"
echo "Migration rollback policy: $MIGRATION_POLICY"

git -C "$BACKEND_REPO_DIR" fetch --prune origin
if ! git -C "$BACKEND_REPO_DIR" rev-parse --verify --quiet "$BACKEND_SHA^{commit}" >/dev/null; then
  git -C "$BACKEND_REPO_DIR" fetch origin "$BACKEND_SHA"
fi
candidate_backend_sha="$(git -C "$BACKEND_REPO_DIR" rev-parse "$BACKEND_SHA^{commit}")"

git -C "$FRONTEND_DIR" fetch --prune origin
if git -C "$FRONTEND_DIR" rev-parse --verify --quiet "origin/$FRONTEND_REF^{commit}" >/dev/null; then
  candidate_frontend_sha="$(git -C "$FRONTEND_DIR" rev-parse "origin/$FRONTEND_REF^{commit}")"
elif git -C "$FRONTEND_DIR" rev-parse --verify --quiet "$FRONTEND_REF^{commit}" >/dev/null; then
  candidate_frontend_sha="$(git -C "$FRONTEND_DIR" rev-parse "$FRONTEND_REF^{commit}")"
else
  git -C "$FRONTEND_DIR" fetch origin "$FRONTEND_REF"
  candidate_frontend_sha="$(git -C "$FRONTEND_DIR" rev-parse FETCH_HEAD)"
fi

if [ "$rollback_available" = "true" ]; then
  for repo_and_sha in \
    "$BACKEND_REPO_DIR|$green_backend_sha" \
    "$FRONTEND_DIR|$green_frontend_sha"; do
    repo="${repo_and_sha%%|*}"
    sha="${repo_and_sha#*|}"
    if ! git -C "$repo" rev-parse --verify --quiet "$sha^{commit}" >/dev/null; then
      git -C "$repo" fetch origin "$sha"
    fi
    git -C "$repo" rev-parse --verify "$sha^{commit}" >/dev/null
  done
  echo "Known-green backend/frontend commits are available locally for rollback."
fi

# Rejections here must not enter operational recovery or change the running release checkout.
validate_admission_artifact "$BACKEND_REPO_DIR" "$candidate_backend_sha" candidate || exit 2
if [ "$rollback_available" = true ]; then
  validate_admission_artifact "$BACKEND_REPO_DIR" "$green_backend_sha" rollback || exit 2
fi
snapshot_controller || exit 2
trap 'on_error "$?" "$LINENO"' ERR

git -C "$BACKEND_REPO_DIR" checkout --detach "$candidate_backend_sha"
validate_admission_checkout "$BACKEND_REPO_DIR" "$candidate_backend_sha" candidate || on_error "$?" "$LINENO"
git -C "$FRONTEND_DIR" checkout --detach "$candidate_frontend_sha"
active_backend_sha="$candidate_backend_sha"
active_frontend_sha="$candidate_frontend_sha"

candidate_image_tag="${candidate_backend_sha:0:12}"
export TRANXIT_DEPLOY_ENV="$TARGET_ENV"
export TRANXIT_IMAGE_TAG="$candidate_image_tag"
export TRANXIT_FRONTEND_BUILD_CONTEXT="${TRANXIT_FRONTEND_BUILD_CONTEXT:-$FRONTEND_DIR}"
export TRANXIT_INTERNAL_API_URL="${TRANXIT_INTERNAL_API_URL:-http://ocelotapigw:8080}"

echo "Resolved backend SHA: $candidate_backend_sha"
echo "Resolved frontend SHA: $candidate_frontend_sha"
echo "Validating compose config..."
compose config --quiet || on_error "$?" "$LINENO"

echo "Building candidate images before downtime..."
compose build caddy accountservice courierjobservice ocelotapigw frontend || on_error "$?" "$LINENO"

echo "Stopping application writers for the pre-migration backup..."
writers_stopped="true"
fence_application_stack || on_error "$?" "$LINENO"

echo "Starting data dependencies and waiting for health checks..."
compose up -d --no-recreate --wait --wait-timeout 300 "${stateful_services[@]}" || on_error "$?" "$LINENO"

database_presence="$(database_pair_presence)"
case "$database_presence" in
  "1|1")
    ;;
  "0|0")
    if [ "$rollback_available" = "true" ] || [ "$ALLOW_FIRST_DEPLOY" != "true" ]; then
      echo "Both application databases are missing outside an acknowledged first deploy." >&2
      false
    fi
    echo "Creating the empty first-deploy database pair before the baseline backup..."
    create_empty_database_pair || on_error "$?" "$LINENO"
    ;;
  *)
    echo "Database pair is inconsistent (Account|CourierJob = $database_presence); refusing to migrate or back up." >&2
    false
    ;;
esac

release_id="${candidate_backend_sha:0:12}-${candidate_frontend_sha:0:12}-$(date -u +"%Y%m%dT%H%M%SZ")"
echo "Creating verified paired pre-migration backup..."
backup_output="$(TRANXIT_DEPLOY_LOCK_HELD=true TRANXIT_DEPLOY_PROJECT_DIR="$PROJECT_DIR" "$CONTROLLER_DIR/backup.sh" \
  --env "$TARGET_ENV" \
  --release-id "$release_id" \
  --writers-stopped)"
printf '%s\n' "$backup_output"
backup_manifest="$(printf '%s\n' "$backup_output" | awk -F= '/^MANIFEST=/{print substr($0, 10)}')"
if [ -z "$backup_manifest" ] || [ ! -f "$backup_manifest" ]; then
  echo "Backup did not return a valid manifest." >&2
  false
fi
account_migration_before="$(marker_value account_migration_head "$backup_manifest")"
courier_migration_before="$(marker_value courier_migration_head "$backup_manifest")"

migration_started="true"
echo "Applying AccountService migrations..."
compose run --rm --no-deps accountservice dotnet AccountService.dll --apply-migrations || on_error "$?" "$LINENO"

echo "Bootstrapping the single Admin account..."
compose run --rm --no-deps accountservice dotnet AccountService.dll --bootstrap-admin || on_error "$?" "$LINENO"

echo "Applying CourierJobService migrations..."
compose run --rm --no-deps courierjobservice dotnet CourierJobService.dll --apply-migrations || on_error "$?" "$LINENO"

account_migration_after="$(database_migration_head Tranxit_Account)"
courier_migration_after="$(database_migration_head Tranxit_CourierJob)"

echo "Starting candidate stack..."
start_application_stack || on_error "$?" "$LINENO"
wait_for_gateway || on_error "$?" "$LINENO"

echo "Running candidate smoke checks..."
run_release_smoke || on_error "$?" "$LINENO"
verify_closed_public_edge || on_error "$?" "$LINENO"

if [ "${TRANXIT_DEPLOY_TEST_FORCE_SMOKE_FAILURE:-false}" = "true" ]; then
  echo "Injecting the requested staging smoke failure to exercise automatic rollback." >&2
  false
fi

record_public_admission "$candidate_backend_sha" "$candidate_frontend_sha" || on_error "$?" "$LINENO"
open_public_admission || on_error "$?" "$LINENO"
if [ "${TRANXIT_DEPLOY_TEST_FORCE_POST_ADMISSION_FAILURE:-false}" = "true" ]; then
  echo "Injecting the requested staging post-admission failure; recovery must preserve admitted writes." >&2
  false
fi

mkdir -p "$MARKER_DIR"
marker_tmp="$(mktemp "$MARKER_DIR/.last-$TARGET_ENV-green.XXXXXX")"
cat > "$marker_tmp" <<MARKER
format_version=3
environment=$TARGET_ENV
backend_sha=$candidate_backend_sha
frontend_ref=$FRONTEND_REF
frontend_sha=$candidate_frontend_sha
image_tag=$candidate_image_tag
migration_policy=$MIGRATION_POLICY
admission_policy=private-smoke-v1
pre_migration_backup_manifest=$backup_manifest
account_migration_before=$account_migration_before
account_migration_after=$account_migration_after
courier_migration_before=$courier_migration_before
courier_migration_after=$courier_migration_after
deployed_at_utc=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
MARKER
chmod 600 "$marker_tmp"
if [ -f "$MARKER_FILE" ]; then
  marker_prev_tmp="$(mktemp "$MARKER_DIR/.last-$TARGET_ENV-green.prev.XXXXXX")"
  cp "$MARKER_FILE" "$marker_prev_tmp"
  chmod 600 "$marker_prev_tmp"
  mv -f "$marker_prev_tmp" "$MARKER_PREV_FILE"
fi
marker_commit_started=true
mv -f "$marker_tmp" "$MARKER_FILE"
sync -f "$MARKER_FILE"
complete_public_admission || on_error "$?" "$LINENO"

trap - ERR
echo "Deploy completed. Known-green marker advanced atomically: $MARKER_FILE"
