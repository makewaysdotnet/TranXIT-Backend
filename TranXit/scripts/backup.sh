#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="${TRANXIT_ENV_FILE:-/opt/tranxit/.env}"
TARGET_ENV="${TRANXIT_DEPLOY_ENV:-production}"
BACKUP_DIR="${TRANXIT_BACKUP_DIR:-/opt/tranxit/backups}"
RETENTION_DAYS="${TRANXIT_BACKUP_RETENTION_DAYS:-14}"

if [ ! -f "$ENV_FILE" ]; then
  echo "Missing env file: $ENV_FILE" >&2
  exit 1
fi

set -a
# shellcheck source=/dev/null
. "$ENV_FILE"
set +a

mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

project_name="tranxit-$TARGET_ENV"
compose() {
  docker compose --env-file "$ENV_FILE" \
    -p "$project_name" \
    -f "$PROJECT_DIR/docker-compose.yml" \
    -f "$PROJECT_DIR/docker-compose.prod.yml" "$@"
}

backup_database() {
  local database="$1"
  local stamp="$2"
  local container_file="/var/opt/mssql/backup/${database}-${stamp}.bak"
  local host_file="$BACKUP_DIR/${database}-${stamp}.bak"

  echo "Backing up $database..."
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
      "$SQLCMD" $TRUST_FLAG -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
        -Q "BACKUP DATABASE [$DB_NAME] TO DISK = N'\''$BACKUP_FILE'\'' WITH INIT"
    '
  compose cp "sqlserver:$container_file" "$host_file"
  gzip -f "$host_file"
}

stamp="$(date -u +"%Y%m%dT%H%M%SZ")"
backup_database "Tranxit_Account" "$stamp"
backup_database "Tranxit_CourierJob" "$stamp"

find "$BACKUP_DIR" -type f -name "*.bak.gz" -mtime +"$RETENTION_DAYS" -delete
echo "Backup complete: $BACKUP_DIR"
