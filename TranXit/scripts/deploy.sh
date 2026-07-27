#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage: scripts/deploy.sh --env staging|production --sha <backend-commit> [--frontend-ref <ref>]

Environment:
  TRANXIT_ENV_FILE      Defaults to /opt/tranxit/.env.
  TRANXIT_FRONTEND_DIR  Defaults to ../../frontend from the backend repo root.
USAGE
}

TARGET_ENV=""
BACKEND_SHA=""
FRONTEND_REF="main"

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

if [ -z "$BACKEND_SHA" ]; then
  echo "--sha is required" >&2
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
BACKEND_REPO_DIR="$(git -C "$PROJECT_DIR" rev-parse --show-toplevel)"
ENV_FILE="${TRANXIT_ENV_FILE:-/opt/tranxit/.env}"

if [ ! -f "$ENV_FILE" ]; then
  echo "Missing env file: $ENV_FILE" >&2
  exit 1
fi

set -a
# shellcheck source=/dev/null
. "$ENV_FILE"
set +a

if [ "$TARGET_ENV" = "production" ]; then
  for mailpit_var in MAILPIT_DOMAIN MAILPIT_BASIC_AUTH_USER MAILPIT_BASIC_AUTH_HASH TRANXIT_E2E_MAIL_INBOX; do
    if [ -n "${!mailpit_var:-}" ]; then
      echo "Refusing production deploy: $mailpit_var is set, but Mailpit is staging-only." >&2
      exit 1
    fi
  done
fi

FRONTEND_DIR="${TRANXIT_FRONTEND_DIR:-$(cd "$BACKEND_REPO_DIR/../frontend" 2>/dev/null && pwd || true)}"
if [ -z "$FRONTEND_DIR" ] || [ ! -d "$FRONTEND_DIR/.git" ]; then
  echo "Frontend repo not found. Set TRANXIT_FRONTEND_DIR in $ENV_FILE." >&2
  exit 1
fi

echo "Deploy target: $TARGET_ENV"
echo "Backend SHA: $BACKEND_SHA"
echo "Frontend ref: $FRONTEND_REF"

previous_backend="$(git -C "$BACKEND_REPO_DIR" rev-parse HEAD || true)"
previous_frontend="$(git -C "$FRONTEND_DIR" rev-parse HEAD || true)"

git -C "$BACKEND_REPO_DIR" fetch --prune origin
if ! git -C "$BACKEND_REPO_DIR" rev-parse --verify --quiet "$BACKEND_SHA^{commit}" >/dev/null; then
  git -C "$BACKEND_REPO_DIR" fetch origin "$BACKEND_SHA"
fi
git -C "$BACKEND_REPO_DIR" checkout --detach "$BACKEND_SHA"

git -C "$FRONTEND_DIR" fetch --prune origin
if git -C "$FRONTEND_DIR" rev-parse --verify --quiet "origin/$FRONTEND_REF^{commit}" >/dev/null; then
  git -C "$FRONTEND_DIR" checkout --detach "origin/$FRONTEND_REF"
elif git -C "$FRONTEND_DIR" rev-parse --verify --quiet "$FRONTEND_REF^{commit}" >/dev/null; then
  git -C "$FRONTEND_DIR" checkout --detach "$FRONTEND_REF"
else
  git -C "$FRONTEND_DIR" fetch origin "$FRONTEND_REF"
  git -C "$FRONTEND_DIR" checkout --detach FETCH_HEAD
fi
frontend_sha="$(git -C "$FRONTEND_DIR" rev-parse HEAD)"

export TRANXIT_DEPLOY_ENV="$TARGET_ENV"
export TRANXIT_IMAGE_TAG="${TRANXIT_IMAGE_TAG:-${BACKEND_SHA:0:12}}"
export TRANXIT_FRONTEND_BUILD_CONTEXT="${TRANXIT_FRONTEND_BUILD_CONTEXT:-$FRONTEND_DIR}"
export TRANXIT_INTERNAL_API_URL="${TRANXIT_INTERNAL_API_URL:-http://ocelotapigw:8080}"

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

echo "Validating compose config..."
compose config --quiet

echo "Building production images..."
compose build caddy accountservice courierjobservice ocelotapigw frontend

echo "Starting data dependencies..."
compose up -d sqlserver rabbitmq

echo "Applying AccountService migrations..."
compose run --rm --no-deps accountservice dotnet AccountService.dll --apply-migrations

echo "Bootstrapping the single Admin account..."
compose run --rm --no-deps accountservice dotnet AccountService.dll --bootstrap-admin

echo "Applying CourierJobService migrations..."
compose run --rm --no-deps courierjobservice dotnet CourierJobService.dll --apply-migrations

echo "Starting TranXIT stack..."
compose up -d

echo "Verifying production network isolation and egress..."
"$SCRIPT_DIR/verify-production-topology.sh" \
  --project-name "$project_name" \
  --egress-url "${TRANXIT_EGRESS_PROBE_URL:?Set TRANXIT_EGRESS_PROBE_URL}"

base_url="${PUBLIC_APP_URL:?Set PUBLIC_APP_URL}"
if [ "$TARGET_ENV" = "staging" ]; then
  base_url="${STAGING_APP_URL:?Set STAGING_APP_URL}"
fi

echo "Waiting for gateway through $base_url/api/roles ..."
ready="false"
for _ in $(seq 1 60); do
  if curl -fsS "$base_url/api/roles" >/dev/null 2>&1; then
    ready="true"
    break
  fi
  sleep 5
done

if [ "$ready" != "true" ]; then
  echo "Gateway did not become ready within 5 minutes." >&2
  echo "Diagnostics:" >&2
  compose ps >&2 || true
  compose logs --tail=200 --no-color >&2 || true
  exit 1
fi

echo "Running smoke checks..."
export TRANXIT_SMOKE_DOCKER_NETWORK="${TRANXIT_SMOKE_DOCKER_NETWORK:-${project_name}_backend}"
"$SCRIPT_DIR/smoke.sh" --base-url "$base_url"

marker_dir="${TRANXIT_MARKER_DIR:-/opt/tranxit}"
mkdir -p "$marker_dir"
cat > "$marker_dir/last-$TARGET_ENV-green" <<MARKER
environment=$TARGET_ENV
backend_sha=$BACKEND_SHA
frontend_ref=$FRONTEND_REF
frontend_sha=$frontend_sha
previous_backend_sha=$previous_backend
previous_frontend_sha=$previous_frontend
deployed_at_utc=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
MARKER
chmod 600 "$marker_dir/last-$TARGET_ENV-green"

echo "Deploy completed. Marker: $marker_dir/last-$TARGET_ENV-green"
