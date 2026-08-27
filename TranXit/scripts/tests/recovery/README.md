# Local deployment recovery matrix

Run from `TranXIT-Backend/TranXit` with Node and a local Linux Docker engine:

```sh
node scripts/tests/recovery/runtime.mjs
```

The sibling `TranXit-Frontend` checkout is required. Set
`TRANXIT_RECOVERY_FRONTEND_DIR` to an absolute checkout path when it is elsewhere.
The command never invokes SSH, a deployment workflow, or a remote Docker context.

The small CLI-refusal tests run on Linux with `node --test
scripts/tests/recovery/runtime.safety.test.mjs`. On Windows, run them in a Node
container with this directory mounted read-only at `/harness`, no Docker socket,
and `--network none`, using `node --test /harness/runtime.safety.test.mjs`.
These three `T-NFR-9.RuntimeIsolationGuards` tests use a CLI double to prove that
remote contexts and a pre-existing namespace are rejected before cleanup can
touch them. They are separate from the real-stack matrix below.

`checkout-safety-test.sh attached` and `checkout-safety-test.sh detached` run
inside an empty controller-image container, with no Docker socket and
`--network none`. They use real Git repositories to check PR-style detached
commits and prove both source checkouts remain unchanged. Override the image
entrypoint with `bash` and pass `/harness/checkout-safety-test.sh` plus the mode.

## Isolation and fidelity

- `runtime.mjs` accepts only local Unix sockets or Windows named pipes. It creates
  a random project namespace, labels all resources, and checks both names and
  ownership labels before cleanup. Only Caddy publishes a loopback HTTPS port.
- A disposable Linux controller runs the real `deploy.sh`, `backup.sh`,
  `restore.sh`, `smoke.sh` and `verify-production-topology.sh` entry points. It
  uses private repository clones and local bare Git origins; user checkouts are
  mounted read-only. It needs the local Docker socket and must run only trusted
  test code, on a development machine or disposable CI runner.
- The base, production and staging Compose layers are loaded unchanged. The
  final test override changes resource names, mounts and healthcheck intervals;
  it disables automatic restarts and forces the frontend's production OTP guard.
  SQL/RabbitMQ, real .NET services, Ocelot, production Next standalone, Caddy and
  Mailpit all run. There are no HTTP, auth, SQL or backup mocks.
- `commands.sh` translates the script's literal `tranxit-staging` project to the
  owned namespace, including the inverse translation in network-name inspection.
  Other observed Docker properties are unchanged. The adapter injects selected
  command failures before or after real side effects and probes phase boundaries.
- Known-green and candidate fixture commits contain the same application code.
  A test-only additive SQL table marks the candidate's migration boundary in each
  database, after the real migrations run. This proves partial-schema restoration
  and code-ref/image selection, not arbitrary old/new application compatibility.

Credentials are generated inside the private controller volume. Do not pass a
real `.env` file or credentials. SMTP goes only to fixture Mailpit. The actual
topology script makes a harmless external HTTPS probe to `https://example.com`.
The fixture also uses a high auth request quota and a 120-minute token lifetime
so long-running phase probes are not throttled or expired. Signature, issuer,
audience, session and ownership validation remain enabled.

## Assertions

`UC-NFR-9` / `T-NFR-9.RuntimeDeploymentMatrix` covers:

| Case | Required outcome |
| --- | --- |
| First deploy fails during Account migration | No green marker; services fenced |
| First deploy fails after starting the stack | No rollback target; running services must be stopped |
| First deploy and candidate success | Private smoke before admission; correct green marker |
| Config/build failure before fencing | Running green release and writes unchanged |
| Incomplete backup pair | No migration or restore; known-green code recovered |
| Partial migration | Both actual SQL backups restored; schema markers removed |
| Pre-admission failure, expand/contract | Known-green code with expanded schema |
| Pre-admission failure, restore-required | Paired restore before green code restarts |
| Post-admission failure, expand/contract | Code recovery retains acknowledged writes |
| Post-admission failure, restore-required | No restore; fenced with restart-blocking journal |
| Recovery smoke failure | Failed recovery stays fenced |
| Second database restore fails | Incomplete-restore journal blocks a new deploy |
| Gate cannot close | Failure is reported, all possible stops attempted |
| Gate opens but its command fails | Actual admitted writes prevent automatic restore |
| Admission journal sync fails | Conservative fencing; persisted journal blocks restart |
| Green marker finalization fails | Preserve previous marker and live data; manual reconciliation |
| Services refuse to stop | Report unsafe/unverified recovery; closed edge returns 503 |

At deterministic boundaries, requests alternate between both HTTPS origins. A
verified customer uses the real BFF to create shipments; separate registration
requests also write to Account SQL. Every successful response's exact email/id
pair must remain in SQL. Rejected requests must be absent. A network/TLS failure
while Caddy is running is a test failure, not evidence of a closed gate.

`T-NFR-9.AcknowledgedWritesPreserved` is the credibility assertion. The controller
temporarily removes the post-admission restore refusal only in its private
script copy. The test must fail on acknowledged-write loss, not merely a log or
exit-code difference. It then restores the exact original script and reruns the
matching case. No mutation is committed to either source repository.

## Evidence and limits

Sanitized case logs, phase/fault traces, JSON outcomes, source hashes and the
credibility diff are copied to
`Tests/TranXit.IntegrationTests/TestResults/tranxit-f01-test-<random>/` before
owned containers, networks and volumes are removed. Raw credentials, cookies,
mail, database backups and unredacted logs are never exported. Build images may
remain in Docker's local cache.

This is a deterministic local integration test, not a throughput test, physical
power-loss test, offsite-backup check, production deployment or independent
review. Real staging rollout, restore drills, migration compatibility review
and cutover approval remain separate release gates.

## CI

`.github/workflows/recovery.yml` runs on backend PRs, pushes to `main` or
`codex/**`, and manual dispatch. It pairs with frontend `main` by default;
manual dispatch can select `frontend_ref`. The artifact records both actual
source SHAs. Both checkouts have full history and no persisted checkout token.

The job first checks wrapper isolation and attached/detached Git preparation,
then runs the real matrix and its red/green credibility check. Only sanitized
evidence is uploaded. The token has read-only repository access. No deployment
environment or live credentials are configured, and no real server is deployed.
Branch-protection enforcement of the new check is a separate owner setting.
