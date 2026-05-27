#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 --database <Tranxit_Account|Tranxit_CourierJob|ScratchDbName> --file <backup.bak.gz>" >&2
}

DATABASE=""
BACKUP_FILE=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --database)
      DATABASE="${2:-}"
      shift 2
      ;;
    --file)
      BACKUP_FILE="${2:-}"
      shift 2
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

if [ -z "$DATABASE" ] || [ -z "$BACKUP_FILE" ]; then
  usage
  exit 2
fi

if ! [[ "$DATABASE" =~ ^[A-Za-z0-9_]+$ ]]; then
  echo "Database name may only contain letters, numbers, and underscores." >&2
  exit 2
fi

if [ ! -f "$BACKUP_FILE" ]; then
  echo "Backup file not found: $BACKUP_FILE" >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="${TRANXIT_ENV_FILE:-/opt/tranxit/.env}"
TARGET_ENV="${TRANXIT_DEPLOY_ENV:-production}"

if [ ! -f "$ENV_FILE" ]; then
  echo "Missing env file: $ENV_FILE" >&2
  exit 1
fi

set -a
# shellcheck source=/dev/null
. "$ENV_FILE"
set +a

project_name="tranxit-$TARGET_ENV"
compose() {
  docker compose --env-file "$ENV_FILE" \
    -p "$project_name" \
    -f "$PROJECT_DIR/docker-compose.yml" \
    -f "$PROJECT_DIR/docker-compose.prod.yml" "$@"
}

tmp_dir="$(mktemp -d)"
trap 'rm -rf "$tmp_dir"' EXIT
tmp_bak="$tmp_dir/restore.bak"
gzip -dc "$BACKUP_FILE" > "$tmp_bak"

container_file="/var/opt/mssql/backup/restore-${DATABASE}.bak"
compose cp "$tmp_bak" "sqlserver:$container_file"

compose exec -T \
  -e DB_NAME="$DATABASE" \
  -e RESTORE_FILE="$container_file" \
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
      -Q "IF DB_ID(N'\''$DB_NAME'\'') IS NOT NULL ALTER DATABASE [$DB_NAME] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [$DB_NAME] FROM DISK = N'\''$RESTORE_FILE'\'' WITH REPLACE; ALTER DATABASE [$DB_NAME] SET MULTI_USER;"
  '

echo "Restore complete for $DATABASE from $BACKUP_FILE"
