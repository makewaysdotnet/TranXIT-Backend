#!/usr/bin/env bash
set -euo pipefail

TEST_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_SOURCE="${TRANXIT_TEST_DEPLOY_SOURCE:-$TEST_DIR/../deploy.sh}"
TEMP_PARENT="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
TMP_ROOT="$(mktemp -d "$TEMP_PARENT/tranxit-public-edge.XXXXXXXX")"
cleanup() {
  case "$TMP_ROOT" in "$TEMP_PARENT"/tranxit-public-edge.*) rm -rf -- "$TMP_ROOT" ;; *) exit 1 ;; esac
}
trap cleanup EXIT
fail() { echo "FAIL: $*" >&2; cat "$TMP_ROOT/result.log" >&2; exit 1; }

for name in record_public_admission preserve_unverified_admission verify_closed_public_edge open_public_admission; do
  definition="$(awk -v name="$name" '
    $0 == name "() {" { selected=1 }
    selected { print }
    selected && $0 == "}" { found=1; exit }
    END { if (!found) exit 1 }
  ' "$DEPLOY_SOURCE")"
  eval "$definition"
done

MARKER_DIR="$TMP_ROOT"
TARGET_ENV=staging
project_name=tranxit-public-edge-contract
TRANXIT_ADMISSION_DIR="$TMP_ROOT/admission"
ADMISSION_OPEN="$TRANXIT_ADMISSION_DIR/open"
ADMISSION_STATE="$TMP_ROOT/admitted"
PUBLIC_APP_URL=https://primary.example.test
STAGING_APP_URL=https://secondary.example.test
active_backend_sha=1111111111111111111111111111111111111111
active_frontend_sha=2222222222222222222222222222222222222222
backup_manifest="$TMP_ROOT/manifest.env"
mkdir "$TRANXIT_ADMISSION_DIR"

docker() {
  [[ " $* " == *' --connect-to ::caddy:443 '* ]] || return 77
  [[ " $* " == *" --network ${project_name}_backend "* ]] || return 77
  [[ " $* " != *' --insecure '* && " $* " != *' -k '* && " $* " != *' --location '* && " $* " != *' POST '* ]] || return 77
  printf '%s\n' "${*: -1}" >> "$TMP_ROOT/requests"
  if [ "${*: -1}" = "$PUBLIC_APP_URL/api/roles" ]; then
    printf '%s' "$PRIMARY_RESPONSE"
    return "$PRIMARY_STATUS"
  fi
  [ "${*: -1}" = "$STAGING_APP_URL/api/roles" ] || return 77
  printf '%s' "$SECONDARY_RESPONSE"
  return "$SECONDARY_STATUS"
}
sleep() { :; }

reset_case() {
  PRIMARY_RESPONSE='Temporarily unavailable|503|60'
  SECONDARY_RESPONSE="$PRIMARY_RESPONSE"
  PRIMARY_STATUS=0 SECONDARY_STATUS=0
  closed_edge_verified=false admission_may_have_opened=false
  rm -f -- "$ADMISSION_STATE" "$ADMISSION_OPEN"
  : > "$TMP_ROOT/requests"
  : > "$TMP_ROOT/result.log"
}

count=0
check() {
  local name="$1" expected="$2" requests="$3" actual=0
  verify_closed_public_edge > "$TMP_ROOT/result.log" 2>&1 || actual=$?
  [ "$actual" = "$expected" ] || fail "$name returned $actual instead of $expected"
  [ "$(wc -l < "$TMP_ROOT/requests")" = "$requests" ] || fail "$name had unexpected request count"
  if [ "$expected" = 0 ]; then
    [ "$closed_edge_verified" = true ] && [ "$admission_may_have_opened" = false ] && [ ! -e "$ADMISSION_STATE" ] || fail "$name changed admission prematurely"
  else
    [ "$closed_edge_verified" = false ] && [ "$admission_may_have_opened" = true ] && [ -f "$ADMISSION_STATE" ] || fail "$name did not preserve uncertain admission"
  fi
  count=$((count + 1))
}

# UC-NFR-9 - T-NFR-9.ClosedPublicEdgeContract (CLI contract, real HTTPS is covered by recovery).
reset_case
check two-closed-origins 0 2
record_public_admission "$active_backend_sha" "$active_frontend_sha"
open_public_admission >> "$TMP_ROOT/result.log"
[ -f "$ADMISSION_OPEN" ] || fail 'Verified closed edge could not be admitted'

reset_case
record_public_admission "$active_backend_sha" "$active_frontend_sha"
if open_public_admission > "$TMP_ROOT/result.log" 2>&1; then fail 'An unverified edge was admitted'; fi
[ ! -e "$ADMISSION_OPEN" ] || fail 'Unverified edge created the sentinel'
count=$((count + 1))

for response in 'roles|200|' 'redirect|308|' 'Temporarily unavailable|503|' 'unrelated outage|503|60' 'error|500|'; do
  reset_case
  PRIMARY_RESPONSE="$response"
  check unexpected-primary-response 1 1
done
reset_case
SECONDARY_RESPONSE='roles|200|'
check secondary-open 1 2
for status in 7 28 60; do
  reset_case
  PRIMARY_STATUS="$status" PRIMARY_RESPONSE='|000|'
  check unverified-network-or-tls 1 12
done
reset_case
SECONDARY_STATUS=60 SECONDARY_RESPONSE='|000|'
check secondary-tls-failure 1 13
reset_case
printf 'open\n' > "$ADMISSION_OPEN"
check already-open-sentinel 1 0
printf 'PASS T-NFR-9.ClosedPublicEdgeContract (%s cases)\n' "$count"
