#!/usr/bin/env bash

# Source once from the trusted controller BEFORE any candidate/rollback checkout.
# Strict v1 admits one complete six-file tuple. Changing even harmless protected bytes
# requires explicit controller-contract review; never regenerate these hashes at deploy time.
# Candidate files/manifests are data only: this helper never executes or sources them.
# No shell options are changed. Callers must serialize checkout/use and separately prove
# the running public edge is closed; these read-only checks are not runtime admission proof.

admission_contract() {
  printf '%s\n' \
    'c4415947a810611716bc2a2ee585c13239580ac3c2c0d65f193bd7193c4f798d  TranXit/ops/Caddyfile' \
    '7517ea8cb9a2ff41b9eacd087e6d95500499c8f4b92427b66e5b374704fa4a68  TranXit/ops/Caddyfile.staging' \
    '2539c6398b0337570ebdf66529140bc6651ae76519494c1405301e9228331d57  TranXit/docker-compose.yml' \
    'ef37300a26cad6c3874f5c0f6214bc880c4facc37a18b5604e20831fa08ddf54  TranXit/docker-compose.prod.yml' \
    '6e912d4bd6bfd7872d27471201e175f0df6ac308f899a06315f61ad42fc5bc01  TranXit/docker-compose.staging.yml' \
    '9d1c6e82b1714946606973d4b11c8d99e8e1f95a31cffb13a03eb1d0ee5bd990  TranXit/ops/caddy/Dockerfile'
}

_admission_contract_reject() {
  printf '%s admission contract rejected: %s (%s).\n' "$1" "$2" "$3" >&2
  return 2
}

validate_admission_artifact() {
  if [ "$#" -ne 3 ] || [ -z "${1:-}" ] ||
     { [ "${3:-}" != candidate ] && [ "${3:-}" != rollback ]; }; then
    printf 'Admission contract rejected: expected repository, full SHA and candidate/rollback label.\n' >&2
    return 2
  fi

  local repo="$1" sha="${2,,}" label="$3"
  local kind contract expected path entry mode blob_id blob_path actual count=0
  if ! [[ "$sha" =~ ^[0-9a-f]{40}$ ]]; then
    _admission_contract_reject "$label" '(commit)' 'full commit SHA required'
    return 2
  fi
  if ! kind="$(git --no-optional-locks --no-replace-objects -C "$repo" cat-file -t "$sha" 2>/dev/null)" ||
     [ "$kind" != commit ]; then
    _admission_contract_reject "$label" '(commit)' 'commit unavailable'
    return 2
  fi
  if ! contract="$(admission_contract)"; then
    _admission_contract_reject "$label" '(contract)' 'trusted tuple unavailable'
    return 2
  fi

  while IFS=$' \t' read -r expected path; do
    if ! entry="$(git --no-optional-locks --no-replace-objects -C "$repo" \
      ls-tree --full-tree "$sha" -- "$path" 2>/dev/null)" || [ -z "$entry" ]; then
      _admission_contract_reject "$label" "$path" 'protected file unavailable'
      return 2
    fi
    IFS=$' \t' read -r mode kind blob_id blob_path <<< "$entry"
    if [[ "$entry" == *$'\n'* ]] ||
       { [ "$mode" != 100644 ] && [ "$mode" != 100755 ]; } ||
       [ "$kind" != blob ] || [ "$blob_path" != "$path" ] ||
       ! [[ "$blob_id" =~ ^[0-9a-f]{40}$ ]]; then
      _admission_contract_reject "$label" "$path" 'regular Git blob required'
      return 2
    fi

    # Keep binary bytes out of shell variables and preserve a failing Git read even
    # when the caller has disabled pipefail/errexit (including recovery conditionals).
    if ! actual="$(
      set -o pipefail
      git --no-optional-locks --no-replace-objects -C "$repo" cat-file blob "$blob_id" 2>/dev/null |
        sha256sum 2>/dev/null
    )"; then
      _admission_contract_reject "$label" "$path" 'blob read or checksum failed'
      return 2
    fi
    if [ "${actual%% *}" != "$expected" ]; then
      _admission_contract_reject "$label" "$path" 'checksum mismatch'
      return 2
    fi
    count=$((count + 1))
  done <<< "$contract"

  if [ "$count" -ne 6 ]; then
    _admission_contract_reject "$label" '(contract)' 'incomplete trusted tuple'
    return 2
  fi
  return 0
}

validate_admission_checkout() {
  validate_admission_artifact "$@" || return 2

  local repo="$1" sha="${2,,}" label="$3"
  local inside prefix head root contract expected path remaining component current actual count=0
  if ! inside="$(git --no-optional-locks --no-replace-objects -C "$repo" rev-parse --is-inside-work-tree 2>/dev/null)" ||
     [ "$inside" != true ]; then
    _admission_contract_reject "$label" '(checkout)' 'working repository required'
    return 2
  fi
  if ! prefix="$(git --no-optional-locks --no-replace-objects -C "$repo" rev-parse --show-prefix 2>/dev/null)" ||
     [ -n "$prefix" ]; then
    _admission_contract_reject "$label" '(checkout)' 'repository root required'
    return 2
  fi
  if ! head="$(git --no-optional-locks --no-replace-objects -C "$repo" rev-parse --verify HEAD 2>/dev/null)" ||
     [ "$head" != "$sha" ]; then
    _admission_contract_reject "$label" '(checkout)' 'HEAD differs from requested commit'
    return 2
  fi

  # Preserve logical path components so a symlink cannot disappear through realpath.
  # Backslash/UNC paths are deliberately unsupported; use POSIX or C:/... paths.
  if [[ "$repo" == *\\* ]] || [[ "$repo" == //* ]]; then
    _admission_contract_reject "$label" '(checkout)' 'unsupported repository path'
    return 2
  fi
  case "$repo" in
    /*|[A-Za-z]:/*) root="$repo" ;;
    *) root="$PWD/$repo" ;;
  esac
  if ! contract="$(admission_contract)"; then
    _admission_contract_reject "$label" '(contract)' 'trusted tuple unavailable'
    return 2
  fi

  while IFS=$' \t' read -r expected path; do
    remaining="$root/$path"
    current=""
    if [[ "$remaining" == /* ]]; then
      current=/
      remaining="${remaining#/}"
    fi
    while [ -n "$remaining" ]; do
      component="${remaining%%/*}"
      if [[ "$remaining" == */* ]]; then remaining="${remaining#*/}"; else remaining=""; fi
      [ -n "$component" ] || continue
      if [ "$current" = / ]; then current="/$component"
      elif [ -z "$current" ]; then current="$component"
      else current="$current/$component"; fi
      if [ -L "$current" ]; then
        _admission_contract_reject "$label" "$path" 'symlink in checkout path'
        return 2
      fi
    done
    if [ ! -f "$root/$path" ]; then
      _admission_contract_reject "$label" "$path" 'regular working file required'
      return 2
    fi
    if ! actual="$(sha256sum < "$root/$path" 2>/dev/null)" 2>/dev/null; then
      _admission_contract_reject "$label" "$path" 'working file read or checksum failed'
      return 2
    fi
    if [ "${actual%% *}" != "$expected" ]; then
      _admission_contract_reject "$label" "$path" 'working file checksum mismatch'
      return 2
    fi
    count=$((count + 1))
  done <<< "$contract"

  if [ "$count" -ne 6 ]; then
    _admission_contract_reject "$label" '(contract)' 'incomplete trusted tuple'
    return 2
  fi
  return 0
}

# A later checkout cannot change the already sourced policy or validation functions.
readonly -f admission_contract _admission_contract_reject \
  validate_admission_artifact validate_admission_checkout
