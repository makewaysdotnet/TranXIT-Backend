#!/usr/bin/env bash
# Shared by the fixture driver and command adapters; never sourced by production.

raw_compose() {
  /usr/local/bin/docker-real compose --env-file /work/env.staging -p "$F01_PROJECT" \
    -f /work/backend/TranXit/docker-compose.yml \
    -f /work/backend/TranXit/docker-compose.prod.yml \
    -f /work/backend/TranXit/docker-compose.staging.yml \
    -f /harness/compose.yml "$@"
}

sql() {
  local database="$1" query="$2"
  printf '%s\n' "$query" | raw_compose exec -T -e F01_DATABASE="$database" sqlserver bash -lc '
    set -euo pipefail
    /opt/mssql-tools18/bin/sqlcmd -C -b -r1 -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
      -d "$F01_DATABASE" -h -1 -W -i /dev/stdin
  ' | tr -d '\r' | sed '/^[[:space:]]*$/d'
}

fail() { printf 'FAIL: %s\n' "$*" >&2; return 1; }

caddy_running() {
  [ -n "$(/usr/local/bin/docker-real ps -q \
    --filter "label=com.docker.compose.project=$F01_PROJECT" \
    --filter label=com.docker.compose.service=caddy --filter status=running)" ]
}

request() {
  local method="$1" route="$2" body="${3:-}" jar="${4:-}" host="${5:-$DOMAIN}"
  local certificate=/work/state/ca.crt
  local timeout=0.5
  if caddy_running; then timeout=8; fi
  [ -f "$certificate" ] || certificate=/etc/ssl/certs/ca-certificates.crt
  local args=(-sS --connect-timeout 1 --max-time "$timeout" --cacert "$certificate"
    --connect-to "$host:443:caddy:443" -H "Origin: https://$host"
    -H 'Content-Type: application/json' -X "$method" -o /work/state/response.json -w '%{http_code}')
  [ -z "$body" ] || args+=(--data "$body")
  [ -z "$jar" ] || args+=(-b "$jar" -c "$jar")
  HTTP_CODE="$(curl "${args[@]}" "https://$host$route" 2>/work/state/curl-error.log)" || true
  HTTP_CODE="${HTTP_CODE:-000}"
  if [ "$HTTP_CODE" = 000 ] && caddy_running; then
    fail "Live Caddy was unreachable or TLS failed; this is not proof of closed admission."
    return 1
  fi
}

assert_result() {
  local expected="$1"
  [ "$HTTP_CODE" = "$expected" ] || { fail "HTTP expected $expected, got $HTTP_CODE"; return 1; }
  jq -e '.isSuccess == true' /work/state/response.json >/dev/null || { fail 'Missing success envelope'; return 1; }
}

prepare_actor() {
  local email="actor-$F01_PROJECT@example.test" body message code
  body="$(jq -nc --arg email "$email" --arg password "$F01_ACTOR_PASSWORD" \
    '{username:"Recovery Customer",email:$email,phone:"+920000000001",role:"Customer",password:$password,confirmPassword:$password}')"
  request POST /api/auth/register "$body" || return 1
  assert_result 200 || return 1
  jq -er '.value.id | select(type == "number" and . > 0)' /work/state/response.json > /work/state/actor.id || return 1
  message=""
  for _ in $(seq 1 40); do
    message="$(curl -fsS --get http://mailpit:8025/api/v1/search --data-urlencode "query=to:$email" --data-urlencode limit=1 | jq -r '.messages[0].ID // empty')" || return 1
    [ -z "$message" ] || break
    sleep 0.5
  done
  [ -n "$message" ] || { fail 'Actor verification mail did not arrive'; return 1; }
  code="$(curl -fsS "http://mailpit:8025/api/v1/message/$message" | jq -r '(if (.Text // "" | length) > 0 then .Text else .HTML end) | match("(?<![0-9])[0-9]{6}(?![0-9])").string')" || return 1
  [[ "$code" =~ ^[0-9]{6}$ ]] || { fail 'No actor OTP in SMTP message'; return 1; }
  request POST /api/auth/verify-email "$(jq -nc --arg email "$email" --arg code "$code" '{email:$email,code:$code}')" || return 1
  assert_result 200 || return 1
  for host in "$DOMAIN" "$STAGING_DOMAIN"; do
    request POST /api/auth/login "$(jq -nc --arg email "$email" --arg password "$F01_ACTOR_PASSWORD" '{email:$email,password:$password}')" "/work/state/$host.cookies" "$host" || return 1
    assert_result 200 || return 1
  done
  request GET /api/lookups '' "/work/state/$DOMAIN.cookies" || return 1
  assert_result 200 || return 1
  jq '.value | {courierModeId:.courierModes[0].id,cargoModeId:.cargoModes[0].id,
    itemTypeId:.itemTypes[0].id,recipientContact:"+920000000002",recipientName:"Recovery Recipient",
    jobItems:[{itemName:"Recovery fixture",quantity:1,weight:1,declaredValue:1,itemTypeId:.itemTypes[0].id}]}' \
    /work/state/response.json > /work/state/job-template.json || return 1
  touch /work/state/actor-ready
}

probe() {
  local point="$1" expected="$2" number host other nonce email id body accepted=false
  [ -f /work/state/probes-enabled ] || return 0
  number="$(cat /work/state/sequence)"; number=$((number + 1)); printf '%s' "$number" > /work/state/sequence
  host="$DOMAIN"; other="$STAGING_DOMAIN"
  if [ $((number % 2)) = 0 ]; then host="$STAGING_DOMAIN"; other="$DOMAIN"; fi
  request GET /api/roles '' '' "$host" || return 1
  case "$expected:$HTTP_CODE" in
    open:200|either:200) accepted=true ;;
    closed:503|closed:000|either:503|either:000) ;;
    *) fail "$point admission expected $expected, got $HTTP_CODE"; return 1 ;;
  esac
  request GET /api/roles '' '' "$other" || return 1
  if [ "$accepted" = true ]; then
    assert_result 200 || return 1
    if [ ! -f /work/state/actor-ready ]; then prepare_actor || return 1; fi
  else
    [[ "$HTTP_CODE" = 503 || "$HTTP_CODE" = 000 ]] || { fail "$point secondary origin admitted traffic"; return 1; }
  fi

  nonce="$F01_CASE_KEY.$number"
  email="a.$nonce@example.test"
  body="$(jq -nc --arg email "$email" --arg password "$F01_ACTOR_PASSWORD" \
    '{username:"Recovery Probe",email:$email,phone:"+920000000003",role:"Customer",password:$password,confirmPassword:$password}')"
  request POST /api/auth/register "$body" '' "$host" || return 1
  if [ "$accepted" = true ]; then
    assert_result 200 || return 1
    id="$(jq -er '.value.id | select(type == "number" and . > 0)' /work/state/response.json)" || return 1
    printf '%s|%s\n' "$email" "$id" >> "/work/state/$F01_CASE_KEY.accounts"
  else
    [[ "$HTTP_CODE" = 503 || "$HTTP_CODE" = 000 ]] || { fail "$point registration bypassed closed admission"; return 1; }
  fi

  email="j.$nonce@example.test"
  if [ -f /work/state/job-template.json ]; then
    body="$(jq -c --arg email "$email" '. + {recipientEmail:$email}' /work/state/job-template.json)"
  else
    body="$(jq -nc --arg email "$email" '{courierModeId:1,cargoModeId:1,recipientEmail:$email,recipientName:"Recovery",recipientContact:"+920000000003"}')"
  fi
  request POST /api/jobs "$body" "/work/state/$host.cookies" "$host" || return 1
  if [ "$accepted" = true ]; then
    assert_result 201 || return 1
    id="$(jq -er '.value.jobId | select(type == "number" and . > 0)' /work/state/response.json)" || return 1
    printf '%s|%s\n' "$email" "$id" >> "/work/state/$F01_CASE_KEY.jobs"
  else
    [[ "$HTTP_CODE" = 503 || "$HTTP_CODE" = 000 ]] || { fail "$point shipment bypassed closed admission"; return 1; }
  fi
  printf '%s\t%s\t%s\t%s\n' "$F01_CASE" "$point" "$host" "$accepted" >> /work/public-results/phases.tsv
}

stage() { if [ -f /work/state/recovering ]; then printf recovery; else printf forward; fi; }

point() {
  [ -f /work/state/probes-enabled ] || return 0
  local name="$1" expected="$2" point="$(stage):$1"
  probe "$point" "$expected" || return 87
  if [[ ",${F01_FAULT_POINTS:-}," == *",$point,"* ]]; then
    if [ "${F01_PERSISTENT:-false}" = true ] || [ ! -f "/work/state/hit-$point" ]; then
      touch "/work/state/hit-$point"
      printf '%s\t%s\n' "$F01_CASE" "$point" >> /work/public-results/faults.tsv
      return 91
    fi
  fi
}

assert_writes() {
  local key="$1" database table field suffix query
  for suffix in accounts jobs; do
    if [ "$suffix" = accounts ]; then database=Tranxit_Account; table=Users; field=Email; else database=Tranxit_CourierJob; table=Jobs; field=RecipientEmail; fi
    query="SET NOCOUNT ON; IF OBJECT_ID(N'[$table]') IS NOT NULL SELECT CONCAT([$field], '|', [Id]) FROM [$table] WHERE [$field] LIKE '[aj].$key.%@example.test' ORDER BY [$field];"
    sql "$database" "$query" | LC_ALL=C sort > /work/state/observed || return 1
    LC_ALL=C sort "/work/state/$key.$suffix" > /work/state/expected
    cmp /work/state/observed /work/state/expected >/dev/null || {
      fail "T-NFR-9.AcknowledgedWritesPreserved: $key $suffix differs (expected $(wc -l < /work/state/expected), observed $(wc -l < /work/state/observed))"
      return 1
    }
  done
}

snapshot_preflight_state() {
  local service ids id state root path repo database
  for service in caddy frontend ocelotapigw accountservice courierjobservice sqlserver rabbitmq mailpit; do
    ids="$(/usr/local/bin/docker-real ps -aq --no-trunc \
      --filter "label=com.docker.compose.project=$F01_PROJECT" \
      --filter "label=com.docker.compose.service=$service")" || return 1
    [ -n "$ids" ] || { fail "Preflight baseline is missing $service"; return 1; }
    for id in $(printf '%s\n' "$ids" | LC_ALL=C sort); do
      state="$(/usr/local/bin/docker-real inspect --format '{{.State.Status}}' "$id")" || return 1
      [ "$state" = running ] || { fail "Preflight baseline $service is not running"; return 1; }
      printf 'container|%s|' "$service"
      /usr/local/bin/docker-real inspect --format \
        '{{.Id}}|{{.State.Status}}|{{.State.StartedAt}}|{{.State.FinishedAt}}|{{.RestartCount}}|{{.Config.Image}}' "$id" || return 1
    done
  done

  # Include metadata, not just contents: re-truncating an empty deploy lock is a mutation.
  for root in /work/markers /work/backups; do
    find "$root" -print0 | LC_ALL=C sort -z | while IFS= read -r -d '' path; do
      stat -c 'file|%n|%F|%i|%a|%u|%g|%s|%y|%z' "$path" || return 1
      if [ -L "$path" ]; then readlink "$path" || return 1
      elif [ -f "$path" ]; then sha256sum "$path" || return 1; fi
    done || return 1
  done
  for repo in backend frontend; do
    printf 'repo|%s\n' "$repo"
    git -C "/work/$repo" rev-parse HEAD || return 1
    git -C "/work/$repo" for-each-ref --format='%(refname)|%(objectname)' || return 1
    # A checkout followed by a checkout back must not pass an unchanged-HEAD comparison.
    git -C "/work/$repo" reflog --all --format='%H|%gD' | sha256sum || return 1
    sha256sum "/work/$repo/.git/HEAD" || return 1
  done
  sha256sum /work/env.staging || return 1
  sql master "SET NOCOUNT ON;
    SELECT CONCAT('restore|',destination_database_name,'|',COUNT_BIG(*),'|',MAX(restore_history_id))
      FROM msdb.dbo.restorehistory WHERE destination_database_name IN ('Tranxit_Account','Tranxit_CourierJob')
      GROUP BY destination_database_name ORDER BY destination_database_name;
    SELECT CONCAT('backup|',database_name,'|',COUNT_BIG(*),'|',MAX(backup_set_id))
      FROM msdb.dbo.backupset WHERE database_name IN ('Tranxit_Account','Tranxit_CourierJob')
      GROUP BY database_name ORDER BY database_name;" || return 1
  for database in Tranxit_Account Tranxit_CourierJob; do
    printf 'schema|%s\n' "$database"
    sql "$database" "SET NOCOUNT ON;
      SELECT CONCAT('candidate|',IIF(OBJECT_ID(N'__F01CandidateMarker') IS NULL,0,1));
      SELECT CONCAT('migration|',MigrationId,'|',ProductVersion) FROM [__EFMigrationsHistory] ORDER BY MigrationId;" || return 1
  done
}

sanitize() {
  jq -Rrs 'reduce (env | to_entries[] | select(.key | test("PASSWORD|SECRET|HASH")) | .value | select(length > 0)) as $secret
    (. ; split($secret) | join("[redacted]")) |
    gsub("eyJ[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+\\.[A-Za-z0-9_-]+"; "[redacted-jwt]") |
    gsub("[0-9]+\\.[A-Za-z0-9_-]{32,}"; "[redacted-refresh]")'
}
