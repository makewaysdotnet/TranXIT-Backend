# Immutable cross-repository CI pair

`ci-pair.json` is the reviewed counterpart pin for this repository. It never contains
its own future commit SHA: CI records the actual local checkout (including a PR
merge commit) together with the verified counterpart SHA in `source-pair.json`.

Change the counterpart pin in a separately reviewed commit after the counterpart
change exists remotely. There is no same-name branch fallback, default-main
fallback, or manual mutable-ref override. A default pin identifies a specific
committed counterpart, not necessarily its current branch head. Advance it to a
compatible reviewed commit when publishing a cross-repository transport change.
A two-repository release must retain successful evidence for its final exact pair;
updating a pin by itself is not proof of compatibility.

## Final-pair workflow dispatch

PR and push runs always use the committed pin. A permitted workflow operator may
explicitly dispatch an exact final pair using the optional `counterpart_sha`
input. It accepts only a full lowercase 40-character commit SHA, never a branch,
tag or abbreviated hash. The resolver rejects a nonempty override outside
`workflow_dispatch` and validates it **before counterpart checkout**. An operator
must review the intended pair before dispatch; this facility does not itself
prove that review or authorize a deployment.

The resolver writes ignored `pairing-evidence/selection.json` without modifying
the committed pin. Checkout consumes its selected SHA; the recorder verifies the
actual checkout against that same selection and includes its provenance in
`source-pair.json`: original committed SHA, selected SHA, event, actor, run,
attempt and workflow ref. A dispatch override is labeled
`operator-selected-workflow-dispatch`, not a reviewed committed pin. Both selection
and exact-pair evidence are uploaded. Dirty tracked checkouts still fail closed.

This avoids an impossible reciprocal future-HEAD pin: for example, commit frontend
F0, backend B0 pinned to F0, then frontend F1 pinned to B0. Dispatch backend B0
recovery with `counterpart_sha=F1` and frontend F1 suites with
`counterpart_sha=B0` (using the actual full hashes). Successful artifacts must
show the same B0/F1 pair. A green default B0/F0 run is not acceptance for B0/F1.

Prerequisite: GitHub only permits manual dispatch once the workflow exists on the
repository's default branch. A new recovery/edge workflow on a candidate branch
may therefore need a separately approved workflow-registration/bootstrap change
before this recipe is available. This document does not authorize that merge.
Verify availability first; do not claim final-pair CI from default-pin runs if
dispatch is unavailable. The existing frontend E2E workflow can be dispatched
independently when registered. See [GitHub's manual-run requirements](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow).

These files establish immutable source identity only. Deployment still builds
images from source; staging-to-production build-once image digest promotion remains
a separate release gate. A source SHA does not identify an immutable container.

Run: `node --test .github/scripts/ci-pair.test.mjs`.
