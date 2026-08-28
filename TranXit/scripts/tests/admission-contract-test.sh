#!/usr/bin/env bash
set -euo pipefail
# UC-NFR-9 - Strict v1 artifact/checkout admission contracts. No Docker or deployment.
# All Git writes and mutations below are confined to owned private repositories.

# Parse the full body before fixtures run. A host edit must not shift Bash's
# on-disk read offset partway through a long container execution.
admission_contract_test_main() {
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONTRACT_SOURCE="$SCRIPT_DIR/../admission-contract.sh"
TEMP_PARENT="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
TMP_ROOT="$(mktemp -d "$TEMP_PARENT/tranxit-admission-contract.XXXXXXXX")"
readonly TEMP_PARENT TMP_ROOT

cleanup() {
  case "$TMP_ROOT" in
    "$TEMP_PARENT"/tranxit-admission-contract.*)
      if [ -L "$TMP_ROOT" ] || [ "$(cd "$TMP_ROOT" && pwd -P)" != "$TMP_ROOT" ]; then
        echo 'Refusing cleanup of a changed test directory' >&2
        return 1
      fi
      rm -rf -- "$TMP_ROOT"
      ;;
    *) echo 'Refusing cleanup outside the test temp directory' >&2; return 1 ;;
  esac
}
trap cleanup EXIT

fail() {
  printf 'FAIL: %s\n' "$*" >&2
  exit 1
}

# Do not let an invoking Git hook redirect fixture writes into the source repository.
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_COMMON_DIR GIT_OBJECT_DIRECTORY \
  GIT_ALTERNATE_OBJECT_DIRECTORIES GIT_CONFIG_COUNT GIT_CONFIG_PARAMETERS
export GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_SYSTEM=/dev/null

SOURCE_REPO="$(git --no-optional-locks -C "$SCRIPT_DIR" rev-parse --show-toplevel)"
SOURCE_SHA="$(git --no-optional-locks --no-replace-objects -C "$SOURCE_REPO" rev-parse HEAD)"
SOURCE_HELPER_HASH="$(sha256sum < "$CONTRACT_SOURCE")"
BASE_REPO="$TMP_ROOT/base"
EXPECTED="$TMP_ROOT/expected.sha256"

# Independent reviewed expectations, not output regenerated from admission_contract().
cat > "$EXPECTED" <<'CONTRACT'
c4415947a810611716bc2a2ee585c13239580ac3c2c0d65f193bd7193c4f798d  TranXit/ops/Caddyfile
7517ea8cb9a2ff41b9eacd087e6d95500499c8f4b92427b66e5b374704fa4a68  TranXit/ops/Caddyfile.staging
2539c6398b0337570ebdf66529140bc6651ae76519494c1405301e9228331d57  TranXit/docker-compose.yml
ef37300a26cad6c3874f5c0f6214bc880c4facc37a18b5604e20831fa08ddf54  TranXit/docker-compose.prod.yml
6e912d4bd6bfd7872d27471201e175f0df6ac308f899a06315f61ad42fc5bc01  TranXit/docker-compose.staging.yml
9d1c6e82b1714946606973d4b11c8d99e8e1f95a31cffb13a03eb1d0ee5bd990  TranXit/ops/caddy/Dockerfile
CONTRACT

configure_fixture() {
  git -C "$1" config user.name 'Admission Contract Fixture'
  git -C "$1" config user.email 'admission-contract@example.invalid'
  git -C "$1" config core.autocrlf false
  git -C "$1" config commit.gpgsign false
}

mkdir -p "$BASE_REPO/TranXit/scripts"
git init -q --initial-branch=main "$BASE_REPO"
configure_fixture "$BASE_REPO"
while read -r expected path; do
  mkdir -p "$BASE_REPO/$(dirname "$path")"
  git --no-optional-locks --no-replace-objects -C "$SOURCE_REPO" \
    cat-file blob "$SOURCE_SHA:$path" > "$BASE_REPO/$path" 2>/dev/null ||
    fail "Required source blob unavailable: $path"
  actual="$(sha256sum < "$BASE_REPO/$path")"
  [ "${actual%% *}" = "$expected" ] || fail "Source blob differs from reviewed v1: $path"
done < "$EXPECTED"
cp "$CONTRACT_SOURCE" "$BASE_REPO/TranXit/scripts/admission-contract.sh"
git -C "$BASE_REPO" add .
git -C "$BASE_REPO" commit -qm 'fixture: approved original'
BASE_SHA="$(git -C "$BASE_REPO" rev-parse HEAD)"

flags_before="$-"
options_before="$(set +o)"
# Source exactly once; the self-authorization case later checks out a hostile copy here.
. "$BASE_REPO/TranXit/scripts/admission-contract.sh" > "$TMP_ROOT/source.out"
[ ! -s "$TMP_ROOT/source.out" ] || fail 'Sourcing the helper printed output'
[ "$-" = "$flags_before" ] && [ "$(set +o)" = "$options_before" ] || fail 'Sourcing changed shell options'
admission_contract > "$TMP_ROOT/actual.sha256"
cmp -s "$EXPECTED" "$TMP_ROOT/actual.sha256" || fail 'Trusted v1 tuple differs from reviewed expectations'

checks=0
check() {
  local expected_status="$1" name="$2" validator="$3" repo="$4" sha="$5"
  local expected_path="${6:-}" reason="${7:-}" label status
  for label in candidate rollback; do
    status=0
    "$validator" "$repo" "$sha" "$label" > "$TMP_ROOT/check.out" 2> "$TMP_ROOT/check.err" || status=$?
    [ "$status" -eq "$expected_status" ] || fail "$name/$label returned $status; expected $expected_status"
    [ ! -s "$TMP_ROOT/check.out" ] || fail "$name/$label printed validation output"
    if [ "$expected_status" -eq 2 ]; then
      grep -F "$label admission contract rejected:" "$TMP_ROOT/check.err" >/dev/null || fail "$name/$label lacks a fixed rejection diagnostic"
      [ -z "$expected_path" ] || grep -F "$expected_path" "$TMP_ROOT/check.err" >/dev/null || fail "$name/$label rejected the wrong path"
      [ -z "$reason" ] || grep -F "$reason" "$TMP_ROOT/check.err" >/dev/null || fail "$name/$label rejected for the wrong reason"
      if grep -F "$TMP_ROOT" "$TMP_ROOT/check.err" >/dev/null; then fail "$name/$label disclosed the repository path"; fi
    else
      [ ! -s "$TMP_ROOT/check.err" ] || fail "$name/$label printed unexpected diagnostics"
    fi
    checks=$((checks + 1))
  done
}

new_fixture() {
  FIXTURE_REPO="$TMP_ROOT/$1"
  git -c core.autocrlf=false clone -q --no-hardlinks "$BASE_REPO" "$FIXTURE_REPO"
  configure_fixture "$FIXTURE_REPO"
}

commit_fixture() {
  git -C "$FIXTURE_REPO" add -A
  git -C "$FIXTURE_REPO" commit -qm 'fixture: protected artifact mutation'
  FIXTURE_SHA="$(git -C "$FIXTURE_REPO" rev-parse HEAD)"
}

reject_fixture() {
  local name="$1" path="$2" reason="${3:-checksum mismatch}"
  check 2 "$name-artifact" validate_admission_artifact "$FIXTURE_REPO" "$FIXTURE_SHA" "$path" "$reason"
  check 2 "$name-checkout" validate_admission_checkout "$FIXTURE_REPO" "$FIXTURE_SHA" "$path" "$reason"
}

check 0 original-artifact validate_admission_artifact "$BASE_REPO" "$BASE_SHA"
check 0 original-checkout validate_admission_checkout "$BASE_REPO" "$BASE_SHA"
new_fixture application-only
printf 'Unprotected application change\n' > "$FIXTURE_REPO/TranXit/application-fixture.txt"
commit_fixture
check 0 descendant-artifact validate_admission_artifact "$FIXTURE_REPO" "$FIXTURE_SHA"
check 0 descendant-checkout validate_admission_checkout "$FIXTURE_REPO" "$FIXTURE_SHA"
check 0 exact-original-object validate_admission_artifact "$FIXTURE_REPO" "$BASE_SHA"
check 2 wrong-checkout-head validate_admission_checkout "$FIXTURE_REPO" "$BASE_SHA" '(checkout)' 'HEAD differs'

check 2 abbreviated-sha validate_admission_artifact "$BASE_REPO" "${BASE_SHA:0:7}" '(commit)'
check 2 unavailable-sha validate_admission_artifact "$BASE_REPO" 0000000000000000000000000000000000000000 '(commit)'
tree_sha="$(git -C "$BASE_REPO" rev-parse 'HEAD^{tree}')"
check 2 tree-not-commit validate_admission_artifact "$BASE_REPO" "$tree_sha" '(commit)'

new_fixture historical
git --no-optional-locks --no-replace-objects -C "$SOURCE_REPO" \
  cat-file blob '57d05712be91bf8d001ce51ae90a235c8ac97133:TranXit/ops/Caddyfile' \
  > "$FIXTURE_REPO/TranXit/ops/Caddyfile" 2>/dev/null || fail 'Required historical Caddyfile blob unavailable'
commit_fixture
reject_fixture historical TranXit/ops/Caddyfile

new_fixture missing
rm -- "$FIXTURE_REPO/TranXit/ops/Caddyfile"
commit_fixture
reject_fixture missing TranXit/ops/Caddyfile 'protected file unavailable'

new_fixture git-symlink
# Keep the approved bytes: only the Git mode changes, so a checksum-only guard fails.
link_blob="$(git -C "$FIXTURE_REPO" rev-parse "$BASE_SHA:TranXit/ops/Caddyfile")"
git -C "$FIXTURE_REPO" update-index --cacheinfo "120000,$link_blob,TranXit/ops/Caddyfile"
git -C "$FIXTURE_REPO" commit -qm 'fixture: symlink instead of regular Git file'
FIXTURE_SHA="$(git -C "$FIXTURE_REPO" rev-parse HEAD)"
reject_fixture git-symlink TranXit/ops/Caddyfile 'regular Git blob required'

for file in Caddyfile Caddyfile.staging; do
  for mutation in gate-removal bypass; do
    new_fixture "$file-$mutation"
    target="$FIXTURE_REPO/TranXit/ops/$file"
    if [ "$mutation" = gate-removal ]; then
      sed '/^(public_tranxit_site) {$/,/^}$/d; s/import public_tranxit_site/import tranxit_site/g' "$target" > "$TMP_ROOT/mutated"
    else
      # Preserve the complete gate snippet while the public sites stop using it.
      sed 's/import public_tranxit_site/import tranxit_site/g' "$target" > "$TMP_ROOT/mutated"
    fi
    cmp -s "$target" "$TMP_ROOT/mutated" && fail "$file/$mutation did not change the fixture"
    cp "$TMP_ROOT/mutated" "$target"
    commit_fixture
    reject_fixture "$file-$mutation" "TranXit/ops/$file"
  done
done

for mutation in missing-mount wrong-source wrong-target writable-mount published-private; do
  new_fixture "$mutation"
  target="$FIXTURE_REPO/TranXit/docker-compose.prod.yml"
  case "$mutation" in
    missing-mount) sed '/      - type: bind/,+3d' "$target" > "$TMP_ROOT/mutated" ;;
    wrong-source) sed 's/source: .*/source: \/tmp\/wrong-admission/' "$target" > "$TMP_ROOT/mutated" ;;
    wrong-target) sed 's@target: /run/tranxit/admission@target: /run/tranxit/other@' "$target" > "$TMP_ROOT/mutated" ;;
    writable-mount) sed 's/read_only: true/read_only: false/' "$target" > "$TMP_ROOT/mutated" ;;
    published-private) sed '/      - "443:443"/a\
      - "8082:8082"' "$target" > "$TMP_ROOT/mutated" ;;
  esac
  cmp -s "$target" "$TMP_ROOT/mutated" && fail "$mutation did not change the fixture"
  cp "$TMP_ROOT/mutated" "$target"
  commit_fixture
  reject_fixture "$mutation" TranXit/docker-compose.prod.yml
done

new_fixture staging-mount-override
sed 's/    volumes:/    volumes: !override/' "$FIXTURE_REPO/TranXit/docker-compose.staging.yml" > "$TMP_ROOT/mutated"
cp "$TMP_ROOT/mutated" "$FIXTURE_REPO/TranXit/docker-compose.staging.yml"
commit_fixture
reject_fixture staging-mount-override TranXit/docker-compose.staging.yml

new_fixture base-compose-drift
printf '\n# Unreviewed base Compose drift\n' >> "$FIXTURE_REPO/TranXit/docker-compose.yml"
commit_fixture
reject_fixture base-compose-drift TranXit/docker-compose.yml

new_fixture caddy-dockerfile
printf '\nENTRYPOINT ["sh", "-c", "exit 0"]\n' >> "$FIXTURE_REPO/TranXit/ops/caddy/Dockerfile"
commit_fixture
reject_fixture caddy-dockerfile TranXit/ops/caddy/Dockerfile

new_fixture checkout-drift
printf '\n# Working file differs from its approved Git blob\n' >> "$FIXTURE_REPO/TranXit/ops/Caddyfile"
check 0 unchanged-blob validate_admission_artifact "$FIXTURE_REPO" "$BASE_SHA"
check 2 checkout-drift validate_admission_checkout "$FIXTURE_REPO" "$BASE_SHA" TranXit/ops/Caddyfile 'working file checksum mismatch'
rm -- "$FIXTURE_REPO/TranXit/ops/Caddyfile"
check 2 checkout-missing validate_admission_checkout "$FIXTURE_REPO" "$BASE_SHA" TranXit/ops/Caddyfile 'regular working file required'

new_fixture checkout-file-symlink
mv -- "$FIXTURE_REPO/TranXit/ops/Caddyfile" "$FIXTURE_REPO/TranXit/ops/approved-Caddyfile"
ln -s approved-Caddyfile "$FIXTURE_REPO/TranXit/ops/Caddyfile"
[ -L "$FIXTURE_REPO/TranXit/ops/Caddyfile" ] || fail 'Fixture filesystem must support real symbolic links'
check 2 checkout-file-symlink validate_admission_checkout "$FIXTURE_REPO" "$BASE_SHA" TranXit/ops/Caddyfile 'symlink in checkout path'

new_fixture checkout-directory-symlink
mv -- "$FIXTURE_REPO/TranXit/ops" "$FIXTURE_REPO/TranXit/approved-ops"
ln -s approved-ops "$FIXTURE_REPO/TranXit/ops"
[ -L "$FIXTURE_REPO/TranXit/ops" ] || fail 'Fixture filesystem must support real symbolic links'
check 2 checkout-directory-symlink validate_admission_checkout "$FIXTURE_REPO" "$BASE_SHA" TranXit/ops/Caddyfile 'symlink in checkout path'

ln -s "$TMP_ROOT" "$TMP_ROOT/linked-parent"
[ -L "$TMP_ROOT/linked-parent" ] || fail 'Fixture filesystem must support real symbolic links'
check 2 checkout-parent-symlink validate_admission_checkout "$TMP_ROOT/linked-parent/base" "$BASE_SHA" TranXit/ops/Caddyfile 'symlink in checkout path'

# A bad artifact must remain bad even after its own allowlist/helper replaces the
# exact pathname from which the trusted functions were sourced earlier in this process.
FIXTURE_REPO="$BASE_REPO"
sed 's/import public_tranxit_site/import tranxit_site/g' "$BASE_REPO/TranXit/ops/Caddyfile" > "$TMP_ROOT/mutated"
cp "$TMP_ROOT/mutated" "$BASE_REPO/TranXit/ops/Caddyfile"
actual="$(sha256sum < "$BASE_REPO/TranXit/ops/Caddyfile")"
sed "s/^c4415947a810611716bc2a2ee585c13239580ac3c2c0d65f193bd7193c4f798d/${actual%% *}/" "$EXPECTED" \
  > "$BASE_REPO/TranXit/scripts/candidate-expected.sha256"
{
  printf '#!/usr/bin/env bash\n'
  printf ': > %q\n' "$TMP_ROOT/candidate-helper-executed"
  printf 'admission_contract() { cat %q; }\n' "$BASE_REPO/TranXit/scripts/candidate-expected.sha256"
  printf 'validate_admission_artifact() { return 0; }\nvalidate_admission_checkout() { return 0; }\n'
} > "$BASE_REPO/TranXit/scripts/admission-contract.sh"
commit_fixture
git -C "$BASE_REPO" checkout -q --detach "$BASE_SHA"
git -C "$BASE_REPO" checkout -q --detach "$FIXTURE_SHA"
reject_fixture self-authored-allowlist TranXit/ops/Caddyfile
[ ! -e "$TMP_ROOT/candidate-helper-executed" ] || fail 'Candidate helper was executed or sourced'
admission_contract > "$TMP_ROOT/actual.sha256"
cmp -s "$EXPECTED" "$TMP_ROOT/actual.sha256" || fail 'Checkout replaced the trusted tuple'

# These are library functions, including when recovery has disabled strict shell flags.
set +e +u
set +o pipefail
check 2 non-strict-caller validate_admission_artifact "$BASE_REPO" "$FIXTURE_SHA" TranXit/ops/Caddyfile 'checksum mismatch'
set -euo pipefail

[ "$(git --no-optional-locks -C "$SOURCE_REPO" rev-parse HEAD)" = "$SOURCE_SHA" ] || fail 'Source HEAD changed during the test'
[ "$(sha256sum < "$CONTRACT_SOURCE")" = "$SOURCE_HELPER_HASH" ] || fail 'Source helper changed during the test'
printf 'PASS T-NFR-9.AdmissionContract (%s candidate/rollback assertions; source files and HEAD preserved)\n' "$checks"
}

admission_contract_test_main "$@"
