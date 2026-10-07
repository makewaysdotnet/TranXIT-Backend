# Deployment admission boundaries

Both deployment inputs must be explicit full lowercase commit SHAs. The workflow
and installed controller reject branches, tags, abbreviated hashes and missing
frontend identifiers. Resolution verifies exact object identity before checkout.
This does not bind the pair to successful staging or to immutable built images.

Before installing an SSH key or connecting, production uses GitHub's read-only
environment and workflow-review-history APIs to require:

- a `production` environment with required reviewers and self-review prevention;
- an actual approval for that environment from a directly configured User reviewer,
  distinct from both the actor and triggering actor;
- the first run attempt. Re-dispatch instead of rerunning: review history has no
  attempt identifier or timestamp with which to prove a fresh approval.

Missing policy, API denial/outage, absent approval or conflicting review history
fails closed. Teams-only configurations are not accepted: no org membership
permission is requested and membership must not be guessed. An administrator's
bypass does not substitute for a configured independent User approval. The current
single-owner identity cannot approve its own production request.

GitHub's supported Environment API/schema does not expose whether administrators
may bypass protection rules. Check that setting separately in the UI and retain
dated evidence; this code does **not** claim to verify that setting. Like any
workflow control, it cannot protect against an administrator changing the trusted
workflow or host directly. Keep environment secrets scoped and workflow changes
reviewed.

Open production gates: exact-pair successful staging evidence, build-once digest
promotion (deploy currently builds from source), host/controller installation,
secret rotation, off-server recovery, monitored smoke and owner go/no-go. No live
environment, credentials, reviewer configuration or deployment is created here.

References:
- https://docs.github.com/en/rest/deployments/environments#get-an-environment
- https://docs.github.com/en/rest/actions/workflow-runs#get-the-review-history-for-a-workflow-run

Tests: `node --test .github/scripts/*.test.mjs` and
`bash TranXit/scripts/tests/deploy-safety-test.sh`.
