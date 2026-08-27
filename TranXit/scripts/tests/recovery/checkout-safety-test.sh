#!/usr/bin/env bash
set -euo pipefail
# UC-NFR-9 - T-NFR-9.RuntimeDetachedCheckout
# Run only in an empty disposable controller container, without a Docker socket.
[ ! -S /var/run/docker.sock ] || exit 78
for target in /source/backend /source/frontend /work; do
  [ ! -e "$target" ] || { echo "Refusing existing fixture path: $target" >&2; exit 78; }
done
mode="${1:-detached}"
[[ "$mode" = attached || "$mode" = detached || "$mode" = foreign-owner ]] || exit 78
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_SYSTEM=/dev/null
export F01_PROJECT=tranxit-f01-test-0000000000000000

for name in backend frontend; do
  root="/source/$name"
  mkdir -p "$root"
  git -C "$root" init -q --initial-branch=main
  git -C "$root" config user.name 'Recovery Fixture'
  git -C "$root" config user.email 'recovery@example.test'
  printf 'source fixture\n' > "$root/source.txt"
  if [ "$name" = backend ]; then
    mkdir -p "$root/TranXit/scripts"
    for script in deploy backup restore smoke verify-production-topology; do
      printf '# fixture source\n' > "$root/TranXit/scripts/$script.sh"
    done
  fi
  git -C "$root" add .
  git -C "$root" commit -qm 'fixture: source commit'
  if [ "$mode" = detached ]; then
    git -C "$root" checkout -q --detach HEAD
    printf 'PR-only commit\n' > "$root/pr-state.txt"
    git -C "$root" add pr-state.txt
    git -C "$root" commit -qm 'fixture: detached PR head'
  fi
done
backend_before="$(git -C /source/backend rev-parse HEAD)"
frontend_before="$(git -C /source/frontend rev-parse HEAD)"
if [ "$mode" = foreign-owner ]; then
  chown -R 1001:1001 /source/backend /source/frontend
fi

bash /harness/controller.sh --prepare-repos
. /work/refs.env
[ "$(git -c safe.directory=/source/backend -C /source/backend rev-parse HEAD)" = "$backend_before" ]
[ "$(git -c safe.directory=/source/frontend -C /source/frontend rev-parse HEAD)" = "$frontend_before" ]
[ "$(git -C /work/backend rev-parse "$GREEN_BACKEND^")" = "$backend_before" ]
[ "$(git -C /work/frontend rev-parse "$GREEN_FRONTEND^")" = "$frontend_before" ]

for name in backend frontend; do
  if [ "$mode" = detached ]; then
    if git -C "/source/$name" symbolic-ref -q HEAD; then
      echo 'Source checkout was changed' >&2; exit 1
    fi
  else
    [ "$(git -c safe.directory="/source/$name" -C "/source/$name" symbolic-ref --short HEAD)" = main ]
  fi
  [ "$(git -C "/work/$name" symbolic-ref --short HEAD)" = main ]
  [ "$(git -C "/work/$name" rev-parse HEAD)" = "$(git --git-dir="/work/origins/$name.git" rev-parse main)" ]
done
echo "PASS T-NFR-9.RuntimeDetachedCheckout ($mode; both source refs unchanged)"
