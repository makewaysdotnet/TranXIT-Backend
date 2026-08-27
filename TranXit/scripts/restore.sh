#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage:
  scripts/restore.sh --env staging|production --manifest <manifest.env> \
    --account-database <target> --courier-database <target> \
    --confirm <confirmation> [--replace-live]

Scratch confirmation:
  RESTORE-<env>-<account-target>-<courier-target>

Live replacement confirmation (also requires --replace-live):
  RESTORE-LIVE-<env>
USAGE
}

TARGET_ENV=""
MANIFEST=""
ACCOUNT_TARGET=""
COURIER_TARGET=""
CONFIRMATION=""
REPLACE_LIVE="false"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --env)
      TARGET_ENV="${2:-}"
      shift 2
      ;;
    --manifest)
      MANIFEST="${2:-}"
      shift 2
      ;;
    --account-database)
      ACCOUNT_TARGET="${2:-}"
      shift 2
      ;;
    --courier-database)
      COURIER_TARGET="${2:-}"
      shift 2
      ;;
    --confirm)
      CONFIRMATION="${2:-}"
      shift 2
      ;;
    --replace-live)
      REPLACE_LIVE="true"
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
if [ -z "$MANIFEST" ] || [ -z "$ACCOUNT_TARGET" ] ||
   [ -z "$COURIER_TARGET" ] || [ -z "$CONFIRMATION" ]; then
  usage
  exit 2
fi
for database in "$ACCOUNT_TARGET" "$COURIER_TARGET"; do
  if ! [[ "$database" =~ ^[A-Za-z0-9_]+$ ]]; then
    echo "Database names may only contain letters, numbers, and underscores" >&2
    exit 2
  fi
done
if [ "$ACCOUNT_TARGET" = "$COURIER_TARGET" ]; then
  echo "Account and CourierJob targets must be different databases" >&2
  exit 2
fi
if [ ! -f "$MANIFEST" ]; then
  echo "Backup manifest not found: $MANIFEST" >&2
  exit 1
fi
MANIFEST="$(readlink -f -- "$MANIFEST")"

manifest_value() {
  local key="$1"
  local value
  value="$(awk -v key="$key" '
    index($0, key "=") == 1 {
      if (found) exit 2
      print substr($0, length(key) + 2)
      found=1
    }
    END { if (!found) exit 1 }
  ' "$MANIFEST")" || {
    echo "Manifest key is missing or duplicated: $key" >&2
    exit 1
  }
  printf '%s' "$value"
}

manifest_format="$(manifest_value format_version)"
manifest_env="$(manifest_value environment)"
account_source="$(manifest_value account_database)"
account_file="$(manifest_value account_file)"
account_sha256="$(manifest_value account_sha256)"
account_migration_head="$(manifest_value account_migration_head)"
courier_source="$(manifest_value courier_database)"
courier_file="$(manifest_value courier_file)"
courier_sha256="$(manifest_value courier_sha256)"
courier_migration_head="$(manifest_value courier_migration_head)"

if [ "$manifest_format" != "1" ]; then
  echo "Unsupported backup manifest format: $manifest_format" >&2
  exit 1
fi
if [ "$manifest_env" != "$TARGET_ENV" ]; then
  echo "Manifest environment $manifest_env does not match --env $TARGET_ENV" >&2
  exit 1
fi
if [ "$account_source" != "Tranxit_Account" ] ||
   [ "$courier_source" != "Tranxit_CourierJob" ]; then
  echo "Manifest does not contain the canonical TranXIT database pair" >&2
  exit 1
fi

manifest_dir="$(cd "$(dirname "$MANIFEST")" && pwd)"
for backup_file in "$account_file" "$courier_file"; do
  if [ ! -f "$backup_file" ]; then
    echo "Backup file from manifest not found: $backup_file" >&2
    exit 1
  fi
  backup_real="$(cd "$(dirname "$backup_file")" && pwd)/$(basename "$backup_file")"
  case "$backup_real" in
    "$manifest_dir"/*) ;;
    *)
      echo "Backup file must be stored beside its manifest: $backup_file" >&2
      exit 1
      ;;
  esac
done

if [ "$REPLACE_LIVE" = "true" ]; then
  if [ "$ACCOUNT_TARGET" != "$account_source" ] ||
     [ "$COURIER_TARGET" != "$courier_source" ]; then
    echo "--replace-live only permits the canonical live database names" >&2
    exit 2
  fi
  expected_confirmation="RESTORE-LIVE-$TARGET_ENV"
else
  if [ "$ACCOUNT_TARGET" = "$account_source" ] ||
     [ "$COURIER_TARGET" = "$courier_source" ]; then
    echo "Canonical database names require --replace-live" >&2
    exit 2
  fi
  expected_confirmation="RESTORE-$TARGET_ENV-$ACCOUNT_TARGET-$COURIER_TARGET"
fi
if [ "$CONFIRMATION" != "$expected_confirmation" ]; then
  echo "Confirmation mismatch. Expected exactly: $expected_confirmation" >&2
  exit 2
fi

verify_checksum() {
  local file="$1"
  local expected="$2"
  local actual
  gzip -t "$file"
  actual="$(sha256sum "$file" | awk '{print $1}')"
  if [ "$actual" != "$expected" ]; then
    echo "Checksum mismatch for $file" >&2
    exit 1
  fi
}

verify_checksum "$account_file" "$account_sha256"
verify_checksum "$courier_file" "$courier_sha256"

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
export TRANXIT_ADMISSION_DIR="${TRANXIT_ADMISSION_DIR:-${TRANXIT_MARKER_DIR:-/opt/tranxit}/admission-$TARGET_ENV}"

marker_dir="${TRANXIT_MARKER_DIR:-/opt/tranxit}"
if [[ "$marker_dir" != /* ]]; then
  echo "TRANXIT_MARKER_DIR must be an absolute path" >&2
  exit 2
fi
if [ "${TRANXIT_DEPLOY_LOCK_HELD:-false}" != "true" ]; then
  if ! command -v flock >/dev/null 2>&1; then
    echo "The restore host must provide flock (util-linux)." >&2
    exit 1
  fi
  mkdir -p "$marker_dir"
  lock_file="$marker_dir/deploy-$TARGET_ENV.lock"
  exec 7>"$lock_file"
  chmod 600 "$lock_file"
  if ! flock -n 7; then
    echo "A $TARGET_ENV deploy, backup, or restore is already running." >&2
    exit 1
  fi
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

restore_state_file="$marker_dir/restore-$TARGET_ENV-in-progress"
if [ -f "$restore_state_file" ]; then
  if [ "$REPLACE_LIVE" != "true" ]; then
    echo "Refusing scratch restore while a live restore is incomplete: $restore_state_file" >&2
    exit 1
  fi
  state_value() {
    local key="$1"
    awk -v key="$key" '
      index($0, key "=") == 1 {
        value=substr($0, length(key) + 2)
        count++
      }
      END {
        if (count != 1) exit 1
        print value
      }
    ' "$restore_state_file"
  }
  state_format="$(state_value format_version)" ||
    { echo "Incomplete restore state is malformed: $restore_state_file" >&2; exit 1; }
  state_environment="$(state_value environment)" ||
    { echo "Incomplete restore state is malformed: $restore_state_file" >&2; exit 1; }
  state_manifest="$(state_value manifest)" ||
    { echo "Incomplete restore state is malformed: $restore_state_file" >&2; exit 1; }
  if [ "$state_format" != "1" ] ||
     [ "$state_environment" != "$TARGET_ENV" ] ||
     [ "$state_manifest" != "$MANIFEST" ]; then
    echo "Incomplete restore recovery must use its original environment and manifest: $restore_state_file" >&2
    exit 1
  fi
  echo "Resuming recovery from the incomplete live restore recorded at $restore_state_file"
fi

tmp_dir="$(mktemp -d)"
container_files=()
live_restore_complete="false"
restore_phase="not-started"

write_restore_state() {
  local state_tmp
  state_tmp="$(mktemp "$marker_dir/.restore-$TARGET_ENV-in-progress.XXXXXX")"
  cat > "$state_tmp" <<STATE
format_version=1
environment=$TARGET_ENV
manifest=$MANIFEST
phase=$restore_phase
updated_at_utc=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
STATE
  chmod 600 "$state_tmp"
  mv -f "$state_tmp" "$restore_state_file"
}

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
  rm -rf -- "$tmp_dir"

  if [ "$REPLACE_LIVE" = "true" ]; then
    if [ "$live_restore_complete" = "true" ]; then
      rm -f -- "$restore_state_file" || cleanup_status=$?
    elif [ "$original_status" -ne 0 ]; then
      echo "LIVE RESTORE INCOMPLETE at phase '$restore_phase'." >&2
      echo "Application services remain stopped. Re-run the same paired restore command before starting any app service." >&2
      echo "Recovery state: $restore_state_file" >&2
    fi
  fi

  if [ "$original_status" -ne 0 ]; then
    exit "$original_status"
  fi
  exit "$cleanup_status"
}
trap cleanup EXIT

sql_escape() {
  printf '%s' "$1" | sed "s/'/''/g"
}

prepare_backup() {
  local key="$1"
  local compressed_file="$2"
  local target_database="$3"
  local tmp_bak="$tmp_dir/$key.bak"
  local container_file="/var/opt/mssql/backup/restore-${TARGET_ENV}-${key}-$$.bak"
  local filelist="$tmp_dir/$key.filelist"

  gzip -dc "$compressed_file" > "$tmp_bak"
  compose cp "$tmp_bak" "sqlserver:$container_file"
  container_files+=("$container_file")

  compose exec -T \
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
      "$SQLCMD" -b $TRUST_FLAG -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
        -Q "RESTORE VERIFYONLY FROM DISK = N'\''$RESTORE_FILE'\'' WITH CHECKSUM;"
      "$SQLCMD" -b $TRUST_FLAG -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
        -h -1 -W -s "|" \
        -Q "SET NOCOUNT ON; RESTORE FILELISTONLY FROM DISK = N'\''$RESTORE_FILE'\'';"
    ' | tr -d '\r' > "$filelist"

  local moves=""
  local data_index=0
  local log_index=0
  while IFS='|' read -r logical_name _ file_type _; do
    logical_name="$(printf '%s' "$logical_name" | sed 's/^ *//;s/ *$//')"
    file_type="$(printf '%s' "$file_type" | sed 's/^ *//;s/ *$//')"
    [ -n "$logical_name" ] || continue

    local physical_name
    case "$file_type" in
      D)
        data_index=$((data_index + 1))
        if [ "$data_index" -eq 1 ]; then
          physical_name="/var/opt/mssql/data/${target_database}.mdf"
        else
          physical_name="/var/opt/mssql/data/${target_database}_${data_index}.ndf"
        fi
        ;;
      L)
        log_index=$((log_index + 1))
        if [ "$log_index" -eq 1 ]; then
          physical_name="/var/opt/mssql/data/${target_database}_log.ldf"
        else
          physical_name="/var/opt/mssql/data/${target_database}_log_${log_index}.ldf"
        fi
        ;;
      *)
        continue
        ;;
    esac

    logical_name="$(sql_escape "$logical_name")"
    if [ -n "$moves" ]; then
      moves+=", "
    fi
    moves+="MOVE N'$logical_name' TO N'$physical_name'"
  done < "$filelist"

  if [ "$data_index" -eq 0 ] || [ "$log_index" -eq 0 ]; then
    echo "Could not derive data/log MOVE clauses from $compressed_file" >&2
    exit 1
  fi

  printf -v "${key}_container_file" '%s' "$container_file"
  printf -v "${key}_moves" '%s' "$moves"
}

prepare_backup account "$account_file" "$ACCOUNT_TARGET"
prepare_backup courier "$courier_file" "$COURIER_TARGET"

if [ "$REPLACE_LIVE" = "true" ]; then
  mkdir -p "$marker_dir"
  restore_phase="prepared"
  write_restore_state
  echo "Stopping application writers before paired live restore..."
  compose stop caddy frontend ocelotapigw accountservice courierjobservice
else
  compose exec -T \
    -e ACCOUNT_TARGET="$ACCOUNT_TARGET" \
    -e COURIER_TARGET="$COURIER_TARGET" \
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
        -Q "IF DB_ID(N'\''$ACCOUNT_TARGET'\'') IS NOT NULL OR DB_ID(N'\''$COURIER_TARGET'\'') IS NOT NULL THROW 51000, '\''A scratch target database already exists.'\'', 1;"
    '
fi

restore_database() {
  local database="$1"
  local container_file="$2"
  local moves="$3"
  local escaped_file
  escaped_file="$(sql_escape "$container_file")"

  local restore_options="$moves, RECOVERY"
  local preamble=""
  local postamble=""
  if [ "$REPLACE_LIVE" = "true" ]; then
    restore_options+=", REPLACE"
    preamble="IF DB_ID(N'$database') IS NOT NULL ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;"
    postamble="ALTER DATABASE [$database] SET MULTI_USER;"
  fi

  compose exec -T \
    -e RESTORE_SQL="$preamble RESTORE DATABASE [$database] FROM DISK = N'$escaped_file' WITH $restore_options; $postamble" \
    sqlserver /bin/bash -lc '
      set -euo pipefail
      if [ -x /opt/mssql-tools18/bin/sqlcmd ]; then
        SQLCMD=/opt/mssql-tools18/bin/sqlcmd
        TRUST_FLAG=-C
      else
        SQLCMD=/opt/mssql-tools/bin/sqlcmd
        TRUST_FLAG=
      fi
      "$SQLCMD" -b $TRUST_FLAG -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -Q "$RESTORE_SQL"
    '
}

restore_database "$ACCOUNT_TARGET" "$account_container_file" "$account_moves"
if [ "$REPLACE_LIVE" = "true" ]; then
  restore_phase="account-restored"
  write_restore_state
fi
restore_database "$COURIER_TARGET" "$courier_container_file" "$courier_moves"
if [ "$REPLACE_LIVE" = "true" ]; then
  restore_phase="both-databases-restored"
  write_restore_state
fi

verify_migration_head() {
  local database="$1"
  local expected="$2"
  local actual
  actual="$(
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
  )"
  if [ "$actual" != "$expected" ]; then
    echo "Migration-head mismatch for $database: expected $expected, got $actual" >&2
    exit 1
  fi
}

verify_migration_head "$ACCOUNT_TARGET" "$account_migration_head"
verify_migration_head "$COURIER_TARGET" "$courier_migration_head"
if [ "$REPLACE_LIVE" = "true" ]; then
  restore_phase="verified"
  live_restore_complete="true"
fi

echo "Paired restore complete:"
echo "  Account: $ACCOUNT_TARGET"
echo "  CourierJob: $COURIER_TARGET"
if [ "$REPLACE_LIVE" = "true" ]; then
  echo "Application services remain stopped; the deploy rollback must start and smoke the known-green release."
fi
