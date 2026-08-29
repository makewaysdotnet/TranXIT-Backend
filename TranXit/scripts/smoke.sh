#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 --base-url <https://host>" >&2
}

BASE_URL=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    --base-url)
      BASE_URL="${2:-}"
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

if [ -z "$BASE_URL" ]; then
  usage
  exit 2
fi

BASE_URL="${BASE_URL%/}"
SMOKE_BODY="$(mktemp "${TMPDIR:-/tmp}/tranxit-smoke.XXXXXXXX")"
trap 'rm -f -- "$SMOKE_BODY"' EXIT

http_command=(curl)
if [ "${TRANXIT_SMOKE_PRIVATE_HTTP:-false}" = true ]; then
  : "${TRANXIT_SMOKE_DOCKER_NETWORK:?Set TRANXIT_SMOKE_DOCKER_NETWORK for private smoke}"
  : "${TRANXIT_HTTP_PROBE_IMAGE:?Set TRANXIT_HTTP_PROBE_IMAGE to the controller-pinned probe image}"
  http_command=(docker run --pull=never --rm --network "$TRANXIT_SMOKE_DOCKER_NETWORK" "$TRANXIT_HTTP_PROBE_IMAGE")
fi

request_code() {
  local method="$1"
  local path="$2"
  local body="${3:-}"
  local output

  if [ -n "$body" ]; then
    output="$("${http_command[@]}" -sS --connect-timeout 10 --max-time 30 -w $'\n%{http_code}' \
      -X "$method" "$BASE_URL$path" \
      -H "Content-Type: application/json" \
      --data "$body")" || return $?
  else
    output="$("${http_command[@]}" -sS --connect-timeout 10 --max-time 30 -w $'\n%{http_code}' \
      -X "$method" "$BASE_URL$path")" || return $?
  fi

  printf '%s\n' "${output%$'\n'*}" > "$SMOKE_BODY"
  printf '%s\n' "${output##*$'\n'}"
}

expect_code() {
  local description="$1"
  local expected="$2"
  local actual="$3"

  if [ "$actual" != "$expected" ]; then
    echo "FAIL: $description expected HTTP $expected but got $actual" >&2
    echo "Response body:" >&2
    cat "$SMOKE_BODY" >&2 || true
    exit 1
  fi

  echo "PASS: $description -> HTTP $actual"
}

roles_code="$(request_code GET /api/roles)"
expect_code "public roles endpoint" "200" "$roles_code"

admin_email="smoke.admin.$(date +%s).$RANDOM@tranxit.local"
admin_body="{\"username\":\"Smoke Admin\",\"email\":\"$admin_email\",\"phone\":\"+920000000000\",\"role\":\"Admin\",\"password\":\"Password1!\",\"confirmPassword\":\"Password1!\"}"
admin_code="$(request_code POST /api/auth/register "$admin_body")"
expect_code "Admin self-register is blocked" "400" "$admin_code"

protected_code="$(request_code POST /api/jobs '{}')"
expect_code "protected jobs route without session" "401" "$protected_code"

raw_refresh_code="$(request_code POST /api/refresh '{}')"
expect_code "raw refresh alias is not public" "404" "$raw_refresh_code"

if [ -n "${SMOKE_LOGIN_EMAIL:-}" ] && [ -n "${SMOKE_LOGIN_PASSWORD:-}" ]; then
  login_email="$SMOKE_LOGIN_EMAIL"
  login_password="$SMOKE_LOGIN_PASSWORD"
  login_expected="200"
  login_description="verified Customer login"
else
  login_email="smoke.customer.$(date +%s).$RANDOM@tranxit.local"
  login_password="Password1!"
  customer_body="{\"username\":\"Smoke Customer\",\"email\":\"$login_email\",\"phone\":\"+920000000001\",\"role\":\"Customer\",\"password\":\"$login_password\",\"confirmPassword\":\"$login_password\"}"
  customer_code="$(request_code POST /api/auth/register "$customer_body")"
  expect_code "temporary Customer self-register" "200" "$customer_code"
  login_expected="400"
  login_description="unverified Customer login is blocked"
fi

login_body="{\"email\":\"$login_email\",\"password\":\"$login_password\"}"
login_code="$(request_code POST /api/auth/login "$login_body")"
expect_code "$login_description" "$login_expected" "$login_code"

if [ -n "${TRANXIT_E2E_MAIL_INBOX:-}" ]; then
  if [ -z "${TRANXIT_SMOKE_DOCKER_NETWORK:-}" ]; then
    echo "FAIL: TRANXIT_E2E_MAIL_INBOX is set, but TRANXIT_SMOKE_DOCKER_NETWORK is not set." >&2
    exit 1
  fi

  mailpit_base_url="${TRANXIT_MAILPIT_INTERNAL_URL:-http://mailpit:8025}"
  mailpit_url="${mailpit_base_url%/}/api/v1/info"
  : "${TRANXIT_HTTP_PROBE_IMAGE:?Set TRANXIT_HTTP_PROBE_IMAGE to the controller-pinned probe image}"
  mailpit_args=(run --pull=never --rm --network "$TRANXIT_SMOKE_DOCKER_NETWORK" "$TRANXIT_HTTP_PROBE_IMAGE" -fsS)

  if [ -n "${TRANXIT_E2E_MAIL_INBOX_USER:-}" ] || [ -n "${TRANXIT_E2E_MAIL_INBOX_PASSWORD:-}" ]; then
    if [ -z "${TRANXIT_E2E_MAIL_INBOX_USER:-}" ] || [ -z "${TRANXIT_E2E_MAIL_INBOX_PASSWORD:-}" ]; then
      echo "FAIL: Mailpit basic-auth user and password must both be set or both omitted." >&2
      exit 1
    fi
    mailpit_args+=(-u "$TRANXIT_E2E_MAIL_INBOX_USER:$TRANXIT_E2E_MAIL_INBOX_PASSWORD")
  fi

  if ! docker "${mailpit_args[@]}" "$mailpit_url" >/tmp/tranxit-smoke-mailpit.json; then
    echo "FAIL: Mailpit health endpoint was not reachable from Docker network $TRANXIT_SMOKE_DOCKER_NETWORK" >&2
    exit 1
  fi

  echo "PASS: Mailpit health endpoint reachable from Docker network"
fi

echo "TranXIT smoke checks passed for $BASE_URL"
