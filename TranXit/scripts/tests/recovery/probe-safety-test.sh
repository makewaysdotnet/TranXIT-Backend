#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
. "$SCRIPT_DIR/probe.sh"
TEMP_PARENT="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
TEST_ROOT="$(mktemp -d "$TEMP_PARENT/tranxit-probe.XXXXXXXX")"
cleanup() {
  case "$TEST_ROOT" in "$TEMP_PARENT"/tranxit-probe.*) rm -rf -- "$TEST_ROOT" ;; *) exit 1 ;; esac
}
trap cleanup EXIT
export F01_PROBE_STATE_DIR="$TEST_ROOT" F01_PROJECT=tranxit-f01-test-0000000000000000 DOMAIN=probe.example.test

# Model the CLI boundary only. Runtime cases still use real engine inspection and HTTPS.
/usr/local/bin/docker-real() {
  case "$1" in
    ps)
      [ "${ENUMERATE_FAILURE:-false}" != true ] || return 1
      [ "$2" = -aq ] || { echo 'Unexpected running-only enumeration' >&2; return 1; }
      printf '%s' "$CONTAINERS"
      ;;
    inspect)
      [ "${INSPECT_FAILURE:-false}" != true ] || return 1
      printf '%s\n' "$ENGINE_STATE"
      ;;
    *) return 1 ;;
  esac
}
curl() {
  printf 'call\n' >> "$TEST_ROOT/curl-calls"
  printf '%s\n' "$@" > "$TEST_ROOT/curl-args"
  printf '%s' "$RESPONSE_CODE"
  [ "$CURL_STATUS" = 0 ] || printf 'Synthetic curl failure\n' >&2
  return "$CURL_STATUS"
}

count=0
check() {
  local name="$1" expected="$2" expected_calls="${3:-1}" actual=0
  : > "$TEST_ROOT/curl-calls"
  : > "$TEST_ROOT/curl-args"
  request POST /api/jobs '{}' > "$TEST_ROOT/request.log" 2>&1 || actual=$?
  [ "$actual" = "$expected" ] || { cat "$TEST_ROOT/request.log" >&2; fail "$name: expected $expected, got $actual"; exit 1; }
  [ "$(wc -l < "$TEST_ROOT/curl-calls")" = "$expected_calls" ] || { fail 'Unexpected public request count'; exit 1; }
  if [ -s "$TEST_ROOT/curl-calls" ]; then
    local connect_timeout=1 total_timeout=0.5
    case "$ENGINE_STATE" in running|restarting|paused) connect_timeout=5; total_timeout=15 ;; esac
    [ "$(awk '/^--connect-timeout$/ { getline; print; exit }' "$TEST_ROOT/curl-args")" = "$connect_timeout" ] || { fail 'Unexpected connection timeout'; exit 1; }
    [ "$(awk '/^--max-time$/ { getline; print; exit }' "$TEST_ROOT/curl-args")" = "$total_timeout" ] || { fail 'Unexpected request timeout'; exit 1; }
    if grep -E '^--retry|^(-k|--insecure)$' "$TEST_ROOT/curl-args" >/dev/null; then
      fail 'Public probe must not retry or disable TLS verification'; exit 1
    fi
  fi
  count=$((count + 1))
}

# UC-NFR-9 - T-NFR-9.PublicProbeEngineState (no assertion weakens the real-edge contract).
CONTAINERS=fixture-edge ENGINE_STATE=exited RESPONSE_CODE=000 CURL_STATUS=7
check listed-but-engine-exited 0
CONTAINERS='' ENGINE_STATE='' RESPONSE_CODE=000 CURL_STATUS=7
check first-deploy-no-edge 0
CONTAINERS=fixture-edge ENGINE_STATE=running RESPONSE_CODE=000 CURL_STATUS=7
check running-connection-error 1
ENGINE_STATE=running RESPONSE_CODE=000 CURL_STATUS=28
check running-dns-or-connection-timeout 1
ENGINE_STATE=running RESPONSE_CODE=200 CURL_STATUS=28
check running-partial-response-timeout 1
ENGINE_STATE=running RESPONSE_CODE=000 CURL_STATUS=60
check running-tls-error 1
ENGINE_STATE=exited RESPONSE_CODE=000 CURL_STATUS=60
check tls-error-is-never-closed-proof 1
ENGINE_STATE=running RESPONSE_CODE=503 CURL_STATUS=0
check genuine-maintenance-response 0
ENGINE_STATE=running RESPONSE_CODE=200 CURL_STATUS=18
check partial-running-response 1
ENGINE_STATE=restarting RESPONSE_CODE=000 CURL_STATUS=7
check restarting-is-not-stopped 1
ENGINE_STATE=paused
check paused-is-not-stopped 1
ENGINE_STATE=unknown
check unknown-is-not-stopped 1 0
ENGINE_STATE=exited INSPECT_FAILURE=true
check inspection-error 1 0
INSPECT_FAILURE=false ENUMERATE_FAILURE=true
check enumeration-error 1 0
printf 'PASS T-NFR-9.PublicProbeEngineState (%s cases; no HTTP retries)\n' "$count"
