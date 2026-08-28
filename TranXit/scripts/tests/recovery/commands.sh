#!/usr/bin/env bash
set -euo pipefail
. /harness/probe.sh
[[ "${F01_PROJECT:-}" =~ ^tranxit-f01-test-[a-f0-9]{16}$ ]] || exit 78
tool="$(basename "$0")"

steady() {
  if [ -f /work/state/recovering ] || [ "${F01_FIRST:-false}" = true ]; then printf closed; else printf open; fi
}

if [ "$tool" != docker ]; then
  real="$(PATH=/usr/bin:/bin command -v "$tool")"
  target="${!#}"
  before=''; after=''; expected=closed
  case "$tool:$target" in
    rm:/work/markers/admission-staging/open) before=close-before; after=closed ;;
    mv:/work/markers/admission-staging/open) before=open-before; after=opened; expected=open ;;
    sync:/work/markers/deploy-staging-admitted) after=journal-synced ;;
    sync:/work/markers/last-staging-green) after=marker-synced; expected=open ;;
    rm:/work/markers/deploy-staging-admitted) after=completed; expected=open ;;
  esac
  if [ -n "$before" ]; then
    before_expected=closed
    [ "$before" != close-before ] || before_expected=either
    point "$before" "$before_expected" || exit $?
  fi
  "$real" "$@"
  [ -z "$after" ] || point "$after" "$expected"
  exit $?
fi

real=/usr/local/bin/docker-real
args=("$@")
action="${1:-}"
printf '%s\n' "$action" >> /work/state/docker-events
case "$action" in
  compose)
    shift
    files=(); configured_project=''; env_file=''
    while [ "$#" -gt 0 ]; do
      case "$1" in
        --env-file) env_file="$2"; shift 2 ;;
        -p) configured_project="$2"; shift 2 ;;
        -f)
          case "$2" in
            /work/backend/TranXit/docker-compose.yml|/work/backend/TranXit/docker-compose.prod.yml|/work/backend/TranXit/docker-compose.staging.yml) files+=(-f "$2") ;;
            *) echo 'Refusing unexpected Compose source' >&2; exit 78 ;;
          esac
          shift 2 ;;
        *) break ;;
      esac
    done
    [ "$configured_project" = tranxit-staging ] && [ "$env_file" = /work/env.staging ] && [ "${#files[@]}" = 6 ] || exit 78
    action="${1:?Missing Compose action}"; shift
    command_args=("$@")
    before=''; after=''; expected=closed
    case "$action" in
      config|build) before="$action-before"; after="$action"; expected="$(steady)" ;;
      ps) [ "$#" -ne 0 ] || touch /work/state/recovering ;;
      logs) ;;
      stop)
        if [[ " $* " == *' frontend '* ]]; then before=stop-writers-before; after=writers-stopped; else before=stop-edge-before; after=edge-stopped; fi ;;
      up)
        if [[ " $* " == *' sqlserver '* ]]; then after=dependencies
        elif [[ " $* " == *' caddy '* ]]; then after=caddy-started
        elif [[ " $* " == *' frontend '* ]]; then after=frontend-started
        elif [[ " $* " == *' ocelotapigw '* ]]; then after=gateway-started
        elif [[ " $* " == *' accountservice '* ]]; then after=services-started
        else after=mail-started; fi ;;
      run)
        if [[ " $* " == *' --apply-migrations '* ]]; then
          if [[ " $* " == *' accountservice '* ]]; then after=migration-account; else after=migration-courier; fi
        elif [[ " $* " == *' --bootstrap-admin '* ]]; then after=bootstrap-admin
        else exit 78; fi ;;
      exec)
        if [[ "$*" == *'BACKUP DATABASE'* ]]; then
          if [[ "$*" == *'DB_NAME=Tranxit_Account'* ]]; then before=backup-account-before; after=backup-account
          else before=backup-courier-before; after=backup-courier; fi
        elif [[ "$*" == *'RESTORE_SQL='* ]]; then
          if [[ "$*" == *'RESTORE DATABASE [Tranxit_Account]'* ]]; then before=restore-account-before; after=restore-account
          else before=restore-courier-before; after=restore-courier; fi
        fi ;;
      cp|start) ;;
      *) echo "Refusing unexpected Compose action: $action" >&2; exit 78 ;;
    esac
    if [ -n "$before" ]; then
      if [[ "$before" = restore-*-before ]]; then assert_verified_stop_before_restore "$before" || exit $?; fi
      before_expected="$expected"
      [[ "$before" != stop-* ]] || before_expected=either
      point "$before" "$before_expected" || exit $?
    fi
    if [ "$action" = up ]; then
      for service in caddy frontend ocelotapigw accountservice courierjobservice; do
        if [[ " $* " == *" $service "* ]]; then /bin/rm -f "/work/state/verified-stops/$service"; fi
      done
      if [ "$after" = caddy-started ]; then install_fixture_edge_artifact; fi
    fi
    "$real" compose --env-file "$env_file" -p "$F01_PROJECT" "${files[@]}" -f /harness/compose.yml "$action" "${command_args[@]}"
    if [ "$after" = dependencies ] && [ ! -f /work/state/network-connected ]; then
      "$real" network connect "${F01_PROJECT}_backend" "$F01_CONTROLLER"
      touch /work/state/network-connected
    fi
    if [ "$after" = dependencies ]; then
      raw_compose exec -T sqlserver sh -c 'test "$(id -u)" -ne 0'
    fi
    if [[ "$after" = backup-* ]]; then
      raw_compose exec -T sqlserver sh -c '
        test "$(id -u)" -ne 0 &&
        test "$(stat -c %u /var/opt/mssql/backup)" = "$(id -u)" &&
        test "$(stat -c %a /var/opt/mssql/backup)" = 700
      '
    fi
    if [ "$after" = caddy-started ]; then
      raw_compose cp caddy:/data/caddy/pki/authorities/local/root.crt /work/state/ca.crt >/dev/null
      cp /work/state/ca.crt /work/settings/ca.crt
      chmod 644 /work/settings/ca.crt
    fi
    if [ -f /work/state/probes-enabled ] && [ ! -f /work/state/recovering ] &&
       [ "$(cat /work/backend/recovery-release.txt)" = candidate ]; then
      case "$after" in
        migration-account) sql Tranxit_Account "IF OBJECT_ID(N'__F01CandidateMarker') IS NULL CREATE TABLE [__F01CandidateMarker] (Id int NOT NULL);" >/dev/null ;;
        migration-courier) sql Tranxit_CourierJob "IF OBJECT_ID(N'__F01CandidateMarker') IS NULL CREATE TABLE [__F01CandidateMarker] (Id int NOT NULL);" >/dev/null ;;
      esac
    fi
    [ -z "$after" ] || point "$after" "$expected"
    ;;
  ps)
    owned=false
    for index in "${!args[@]}"; do
      if [ "${args[$index]}" = label=com.docker.compose.project=tranxit-staging ]; then
        args[$index]="label=com.docker.compose.project=$F01_PROJECT"; owned=true
      fi
    done
    [ "$owned" = true ] || exit 78
    output="$("$real" "${args[@]}")" || exit $?
    if [ -f /work/state/probes-enabled ] && [ -z "$output" ]; then
      for argument in "${args[@]}"; do
        case "$argument" in label=com.docker.compose.service=*)
          service="${argument#*=}"
          service="${service#*=}"
          case "$service" in caddy|frontend|ocelotapigw|accountservice|courierjobservice)
            mkdir -p /work/state/verified-stops
            : > "/work/state/verified-stops/$service" ;;
          esac ;;
        esac
      done
    fi
    printf '%s\n' "$output" ;;
  inspect|exec)
    shift
    if [ "$action" = inspect ]; then
      [ "$1" = --format ] || exit 78
      shift 2
    fi
    id="${1:?Missing owned container}"
    owner="$("$real" inspect --format '{{index .Config.Labels "io.tranxit.recovery-test"}}' "$id")"
    [ "$owner" = "$F01_PROJECT" ] || exit 78
    if [ "$action" = inspect ] && [[ "${args[2]}" == *'.NetworkSettings.Networks'* ]]; then
      # Inverse of the one-to-one project namespace mapping; preserve all actual memberships.
      "$real" "${args[@]}" | sed "s/^${F01_PROJECT}_/tranxit-staging_/"
      exit $?
    fi
    if [ "$action" = inspect ] && [ "${args[2]}" = '{{.State.Status}}' ] && [ -f /work/state/probes-enabled ]; then
      output="$("$real" "${args[@]}")" || exit $?
      service="$("$real" inspect --format '{{index .Config.Labels "com.docker.compose.service"}}' "$id")"
      case "$service" in caddy|frontend|ocelotapigw|accountservice|courierjobservice)
        case "$output" in created|exited|dead)
          mkdir -p /work/state/verified-stops
          printf '%s' "$id" > "/work/state/verified-stops/$service"
          printf '%s\tverify-stop\t%s\t%s\n' "$F01_CASE" "$service" "$output" >> /work/public-results/restore-order.tsv ;;
        *) /bin/rm -f "/work/state/verified-stops/$service" ;;
        esac ;;
      esac
      printf '%s\n' "$output"
      exit 0
    fi
    exec "$real" "${args[@]}" ;;
  network)
    [ "${2:-}" = inspect ] && [ "${3:-}" = --format ] || exit 78
    network="${5:-}"
    case "$network" in
      tranxit-staging_backend|tranxit-staging_egress) args[4]="${F01_PROJECT}_${network#tranxit-staging_}" ;;
      *) exit 78 ;;
    esac
    exec "$real" "${args[@]}" ;;
  run)
    [ "${2:-}" = --rm ] && [ "${3:-}" = --network ] && [ "${4:-}" = tranxit-staging_backend ] && [ "${5:-}" = curlimages/curl:8.13.0 ] || exit 78
    # Private smoke probes get owned names/labels, including on interrupted runs.
    if [[ " $* " == *' --connect-to ::caddy:443 '* ]]; then
      certificate=/fixture/ca.crt
      if [ "${F01_BREAK_PUBLIC_PROBE_TLS:-false}" = true ]; then certificate=/fixture/missing-ca.crt; fi
      "$real" run --rm --name "$F01_PROJECT-http-$(openssl rand -hex 5)" --label "io.tranxit.recovery-test=$F01_PROJECT" \
        --network "${F01_PROJECT}_backend" --mount "type=volume,source=$F01_PROJECT-settings,target=/fixture,readonly" \
        "${args[4]}" --cacert "$certificate" "${args[@]:5}"
      exit $?
    fi
    "$real" run --rm --name "$F01_PROJECT-http-$(openssl rand -hex 5)" --label "io.tranxit.recovery-test=$F01_PROJECT" \
      --network "${F01_PROJECT}_backend" "${args[@]:4}"
    if [[ " $* " == *'http://caddy:8082/api/auth/login'* ]]; then point smoke closed; fi
    ;;
  *) echo "Refusing unscoped Docker command: $action" >&2; exit 78 ;;
esac
