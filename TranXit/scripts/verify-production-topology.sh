#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 --project-name <compose-project> --egress-url <https-url>" >&2
}

PROJECT_NAME=""
EGRESS_URL=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --project-name)
      PROJECT_NAME="${2:-}"
      shift 2
      ;;
    --egress-url)
      EGRESS_URL="${2:-}"
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

if [ -z "$PROJECT_NAME" ] || [ -z "$EGRESS_URL" ]; then
  usage
  exit 2
fi

case "$EGRESS_URL" in
  https://*) ;;
  *)
    echo "--egress-url must use https://" >&2
    exit 2
    ;;
esac

backend_network="${PROJECT_NAME}_backend"
egress_network="${PROJECT_NAME}_egress"

container_id() {
  local service="$1"
  docker ps \
    --filter "label=com.docker.compose.project=$PROJECT_NAME" \
    --filter "label=com.docker.compose.service=$service" \
    --format '{{.ID}}' |
    head -n 1
}

require_container() {
  local service="$1"
  local container
  container="$(container_id "$service")"
  if [ -z "$container" ]; then
    echo "FAIL: $service is not running in Compose project $PROJECT_NAME." >&2
    exit 1
  fi
  echo "$container"
}

assert_network_internal() {
  local network="$1"
  local expected="$2"
  local actual
  actual="$(docker network inspect --format '{{.Internal}}' "$network")"
  if [ "$actual" != "$expected" ]; then
    echo "FAIL: network $network internal=$actual; expected $expected." >&2
    exit 1
  fi
}

assert_exact_networks() {
  local service="$1"
  shift
  local container actual expected
  container="$(require_container "$service")"
  actual="$(
    docker inspect \
      --format '{{range $name, $_ := .NetworkSettings.Networks}}{{println $name}}{{end}}' \
      "$container" |
      sed '/^$/d' |
      sort
  )"
  expected="$(printf '%s\n' "$@" | sort)"
  if [ "$actual" != "$expected" ]; then
    echo "FAIL: $service network membership differs." >&2
    echo "Expected:" >&2
    echo "$expected" >&2
    echo "Actual:" >&2
    echo "$actual" >&2
    exit 1
  fi
  echo "PASS: $service has only the expected networks."
}

assert_no_host_ports() {
  local container service published
  while IFS= read -r container; do
    [ -n "$container" ] || continue
    service="$(docker inspect --format '{{index .Config.Labels "com.docker.compose.service"}}' "$container")"
    if [ "$service" = "caddy" ]; then
      continue
    fi
    published="$(docker inspect --format '{{json .HostConfig.PortBindings}}' "$container")"
    if [ "$published" != "{}" ] && [ "$published" != "null" ]; then
      echo "FAIL: $service configures host port bindings:" >&2
      echo "$published" >&2
      exit 1
    fi
  done < <(
    docker ps \
      --filter "label=com.docker.compose.project=$PROJECT_NAME" \
      --format '{{.ID}}'
  )
  echo "PASS: no non-Caddy service publishes a host port."
}

assert_network_internal "$backend_network" "true"
assert_network_internal "$egress_network" "false"
assert_exact_networks "accountservice" "$backend_network" "$egress_network"
assert_exact_networks "courierjobservice" "$backend_network" "$egress_network"
assert_exact_networks "sqlserver" "$backend_network"
assert_exact_networks "rabbitmq" "$backend_network"
assert_no_host_ports

account_container="$(require_container accountservice)"
courier_container="$(require_container courierjobservice)"

docker exec "$account_container" \
  curl -fsS --connect-timeout 15 --max-time 30 -o /dev/null "$EGRESS_URL"
docker exec "$courier_container" \
  curl -fsS --connect-timeout 15 --max-time 30 -o /dev/null "$EGRESS_URL"
echo "PASS: AccountService and CourierJobService completed outbound HTTPS probes."

docker exec "$account_container" sh -c '
  : "${MailSettings__Server:?Missing MailSettings__Server}"
  : "${MailSettings__Port:?Missing MailSettings__Port}"
  : "${MailSettings__SenderEmail:?Missing MailSettings__SenderEmail}"
  : "${AdminBootstrap__Email:?Missing AdminBootstrap__Email}"

  case "$MailSettings__Port" in
    465)
      scheme="smtps"
      tls_args=""
      ;;
    587)
      scheme="smtp"
      tls_args="--ssl-reqd"
      ;;
    *)
      scheme="smtp"
      tls_args=""
      ;;
  esac

  send_probe() {
    if [ -n "${MailSettings__UserName:-}" ] || [ -n "${MailSettings__Password:-}" ]; then
      if [ -z "${MailSettings__UserName:-}" ] || [ -z "${MailSettings__Password:-}" ]; then
        echo "SMTP username and password must both be set or both omitted." >&2
        return 1
      fi
      curl -fsS --connect-timeout 15 --max-time 30 $tls_args \
        --url "$scheme://$MailSettings__Server:$MailSettings__Port" \
        --user "$MailSettings__UserName:$MailSettings__Password" \
        --mail-from "$MailSettings__SenderEmail" \
        --mail-rcpt "$AdminBootstrap__Email" \
        --upload-file - >/dev/null
    else
      curl -fsS --connect-timeout 15 --max-time 30 $tls_args \
        --url "$scheme://$MailSettings__Server:$MailSettings__Port" \
        --mail-from "$MailSettings__SenderEmail" \
        --mail-rcpt "$AdminBootstrap__Email" \
        --upload-file - >/dev/null
    fi
  }

  printf "From: %s\r\nTo: %s\r\nSubject: TranXIT deployment SMTP probe\r\n\r\nProduction topology SMTP connectivity verified.\r\n" \
    "$MailSettings__SenderEmail" \
    "$AdminBootstrap__Email" |
    send_probe
'
echo "PASS: AccountService completed an SMTP delivery transaction."

echo "TranXIT production topology verification passed."
