#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage: scripts/backup.sh --env staging|production [--release-id <id>]

Creates one verified, paired backup set for Tranxit_Account and Tranxit_CourierJob.
Standalone invocation briefly stops and then restarts the currently running application services.
The final output line is MANIFEST=<absolute-path>.
USAGE
}

TARGET_ENV=""
RELEASE_ID=""
WRITERS_STOPPED="false"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --env)
      TARGET_ENV="${2:-}"
      shift 2
      ;;
    --release-id)
      RELEASE_ID="${2:-}"
      shift 2
      ;;
    --writers-stopped)
      WRITERS_STOPPED="true"
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
  echo "--env must be explicitly set to staging or production" >&2
  exit 2
fi

if [ -z "$RELEASE_ID" ]; then
  RELEASE_ID="manual-$(date -u +"%Y%m%dT%H%M%SZ")"
fi
if ! [[ "$RELEASE_ID" =~ ^[A-Za-z0-9._-]+$ ]]; then
  echo "--release-id may only contain letters, numbers, dots, underscores, and dashes" >&2
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="${TRANXIT_ENV_FILE:-/opt/tranxit/.env}"

if [ ! -f "$ENV_FILE" ]; then
  echo "Missing env file: $ENV_FILE" >&2
  exit 1
fi

set -a
# shellcheck source=/dev/null
. "$ENV_FILE"
set +a

BACKUP_DIR="${TRANXIT_BACKUP_DIR:-/opt/tranxit/backups}"
RETENTION_DAYS="${TRANXIT_BACKUP_RETENTION_DAYS:-14}"
if [[ "$BACKUP_DIR" != /* ]]; then
  echo "TRANXIT_BACKUP_DIR must be an absolute path" >&2
  exit 2
fi
if ! [[ "$RETENTION_DAYS" =~ ^[0-9]+$ ]]; then
  echo "TRANXIT_BACKUP_RETENTION_DAYS must be a non-negative integer" >&2
  exit 2
fi
if [ "$WRITERS_STOPPED" = "true" ] &&
   [ "${TRANXIT_DEPLOY_LOCK_HELD:-false}" != "true" ]; then
  echo "--writers-stopped is reserved for deploy.sh while it holds the environment lock" >&2
  exit 2
fi

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

container_files=()
restart_services=()
cleanup() {
  local original_status=$?
  local cleanup_status=0
  trap - EXIT
  set +e

  for container_file in "${container_files[@]}"; do
    compose exec -T -e BACKUP_FILE="$container_file" sqlserver \
      /bin/bash -lc 'rm -f -- "$BACKUP_FILE"' >/dev/null 2>&1 ||
      cleanup_status=$?
  done

  if [ "${#restart_services[@]}" -gt 0 ]; then
    echo "Restarting application services after the paired backup..."
    compose start "${restart_services[@]}" || cleanup_status=$?
  fi

  if [ "$original_status" -ne 0 ]; then
    exit "$original_status"
  fi
  exit "$cleanup_status"
}
trap cleanup EXIT

marker_dir="${TRANXIT_MARKER_DIR:-/opt/tranxit}"
if [[ "$marker_dir" != /* ]]; then
  echo "TRANXIT_MARKER_DIR must be an absolute path" >&2
  exit 2
fi

if [ "$WRITERS_STOPPED" != "true" ]; then
  if ! command -v flock >/dev/null 2>&1; then
    echo "The backup host must provide flock (util-linux)." >&2
    exit 1
  fi
  mkdir -p "$marker_dir"
  lock_file="$marker_dir/deploy-$TARGET_ENV.lock"
  exec 8>"$lock_file"
  chmod 600 "$lock_file"
  if ! flock -n 8; then
    echo "A $TARGET_ENV deploy or backup is already running." >&2
    exit 1
  fi

fi

incomplete_restore_state="$marker_dir/restore-$TARGET_ENV-in-progress"
if [ -f "$incomplete_restore_state" ]; then
  echo "Refusing backup while a live restore is incomplete: $incomplete_restore_state" >&2
  exit 1
fi

if [ "$WRITERS_STOPPED" != "true" ]; then
  running_output="$(compose ps --services --filter status=running)"
  running_services=()
  while IFS= read -r service; do
    [ -n "$service" ] && running_services+=("$service")
  done <<< "$running_output"
  for service in "${running_services[@]}"; do
    case "$service" in
      caddy|frontend|ocelotapigw|accountservice|courierjobservice)
        restart_services+=("$service")
        ;;
    esac
  done
  if [ "${#restart_services[@]}" -gt 0 ]; then
    echo "Stopping application services for a cross-database-consistent backup..."
    compose stop "${restart_services[@]}"
  fi
fi

backup_set_dir="$BACKUP_DIR/$TARGET_ENV/$RELEASE_ID"
if [ -e "$backup_set_dir" ]; then
  echo "Backup set already exists: $backup_set_dir" >&2
  exit 1
fi
mkdir -p "$backup_set_dir"
chmod 700 "$BACKUP_DIR" "$BACKUP_DIR/$TARGET_ENV" "$backup_set_dir"

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
      -Q "SET NOCOUNT ON; IF OBJECT_ID(N'\''[__EFMigrationsHistory]'\'') IS NULL SELECT N'\''none'\''; ELSE SELECT COALESCE(MAX([MigrationId]), N'\''none'\'') FROM [__EFMigrationsHistory];"
  ' | tr -d '\r' | awk 'NF { value=$0 } END { print value }'
}

backup_database() {
  local key="$1"
  local database="$2"
  local container_file="/var/opt/mssql/backup/${database}-${RELEASE_ID}.bak"
  local host_file="$backup_set_dir/${database}.bak"
  container_files+=("$container_file")

  echo "Backing up and verifying $database..."
  compose exec -T \
    -e DB_NAME="$database" \
    -e BACKUP_FILE="$container_file" \
    sqlserver /bin/bash -lc '
      set -euo pipefail
      if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
        SQLCMD=/opt/mssql-tools18/bin/sqlcmd
        TRUST_FLAG=-C
      else
        SQLCMD=/opt/mssql-tools/bin/sqlcmd
        TRUST_FLAG=
      fi
      "$SQLCMD" -b $TRUST_FLAG -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
        -Q "BACKUP DATABASE [$DB_NAME] TO DISK = N'\''$BACKUP_FILE'\'' WITH COPY_ONLY, INIT, CHECKSUM; RESTORE VERIFYONLY FROM DISK = N'\''$BACKUP_FILE'\'' WITH CHECKSUM;"
    '

  compose cp "sqlserver:$container_file" "$host_file"
  gzip -n -f "$host_file"

  local compressed_file="${host_file}.gz"
  local checksum
  checksum="$(sha256sum "$compressed_file" | awk '{print $1}')"
  local migration_head
  migration_head="$(database_migration_head "$database")"

  printf -v "${key}_file" '%s' "$compressed_file"
  printf -v "${key}_sha256" '%s' "$checksum"
  printf -v "${key}_migration_head" '%s' "$migration_head"
}

backup_database account Tranxit_Account
backup_database courier Tranxit_CourierJob

manifest="$backup_set_dir/manifest.env"
manifest_tmp="$manifest.tmp"
cat > "$manifest_tmp" <<MANIFEST
format_version=1
environment=$TARGET_ENV
release_id=$RELEASE_ID
created_at_utc=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
account_database=Tranxit_Account
account_file=$account_file
account_sha256=$account_sha256
account_migration_head=$account_migration_head
courier_database=Tranxit_CourierJob
courier_file=$courier_file
courier_sha256=$courier_sha256
courier_migration_head=$courier_migration_head
MANIFEST
chmod 600 "$manifest_tmp"
mv -f "$manifest_tmp" "$manifest"

find "$BACKUP_DIR/$TARGET_ENV" -type f \
  \( -name "*.bak.gz" -o -name "manifest.env" \) \
  -mtime +"$RETENTION_DAYS" -delete
find "$BACKUP_DIR/$TARGET_ENV" -mindepth 1 -type d -empty -delete

echo "Verified paired backup complete: $backup_set_dir"
echo "MANIFEST=$manifest"
