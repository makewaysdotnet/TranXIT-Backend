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

request_code() {
  local method="$1"
  local path="$2"
  local body="${3:-}"
  local output

  if [ -n "$body" ]; then
    output="$(curl -sS -o /tmp/tranxit-smoke-body.json -w "%{http_code}" \
      -X "$method" "$BASE_URL$path" \
      -H "Content-Type: application/json" \
      --data "$body")"
  else
    output="$(curl -sS -o /tmp/tranxit-smoke-body.json -w "%{http_code}" \
      -X "$method" "$BASE_URL$path")"
  fi

  echo "$output"
}

expect_code() {
  local description="$1"
  local expected="$2"
  local actual="$3"

  if [ "$actual" != "$expected" ]; then
    echo "FAIL: $description expected HTTP $expected but got $actual" >&2
    echo "Response body:" >&2
    cat /tmp/tranxit-smoke-body.json >&2 || true
    exit 1
  fi

  echo "PASS: $description -> HTTP $actual"
}

roles_code="$(request_code GET /api/roles)"
expect_code "public roles endpoint" "200" "$roles_code"

admin_email="smoke.admin.$(date +%s).$RANDOM@tranxit.local"
admin_body="{\"username\":\"Smoke Admin\",\"email\":\"$admin_email\",\"phone\":\"+920000000000\",\"role\":\"Admin\",\"password\":\"Password1!\",\"confirmPassword\":\"Password1!\"}"
admin_code="$(request_code POST /api/register "$admin_body")"
expect_code "Admin self-register is blocked" "400" "$admin_code"

protected_code="$(request_code GET /api/jobs)"
expect_code "protected jobs route without token" "401" "$protected_code"

if [ -n "${SMOKE_LOGIN_EMAIL:-}" ] && [ -n "${SMOKE_LOGIN_PASSWORD:-}" ]; then
  login_email="$SMOKE_LOGIN_EMAIL"
  login_password="$SMOKE_LOGIN_PASSWORD"
else
  login_email="smoke.customer.$(date +%s).$RANDOM@tranxit.local"
  login_password="Password1!"
  customer_body="{\"username\":\"Smoke Customer\",\"email\":\"$login_email\",\"phone\":\"+920000000001\",\"role\":\"Customer\",\"password\":\"$login_password\",\"confirmPassword\":\"$login_password\"}"
  customer_code="$(request_code POST /api/register "$customer_body")"
  expect_code "temporary Customer self-register" "200" "$customer_code"
fi

login_body="{\"email\":\"$login_email\",\"password\":\"$login_password\"}"
login_code="$(request_code POST /api/login "$login_body")"
expect_code "Customer login" "200" "$login_code"

if [ -n "${TRANXIT_E2E_MAIL_INBOX:-}" ]; then
  if [ -z "${TRANXIT_SMOKE_DOCKER_NETWORK:-}" ]; then
    echo "FAIL: TRANXIT_E2E_MAIL_INBOX is set, but TRANXIT_SMOKE_DOCKER_NETWORK is not set." >&2
    exit 1
  fi

  mailpit_base_url="${TRANXIT_MAILPIT_INTERNAL_URL:-http://mailpit:8025}"
  mailpit_url="${mailpit_base_url%/}/api/v1/info"
  mailpit_args=(run --rm --network "$TRANXIT_SMOKE_DOCKER_NETWORK" curlimages/curl:8.13.0 -fsS)

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
