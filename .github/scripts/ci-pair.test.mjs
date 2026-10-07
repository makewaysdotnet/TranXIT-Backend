import assert from "node:assert/strict";
import test from "node:test";
import { execFileSync } from "node:child_process";
import { existsSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fullSha, recordPair, selectPair, validatePair } from "./ci-pair.mjs";
import { main } from "./ci-pair.mjs";

const backend = "makewaysdotnet/TranXIT-Backend";
const frontend = "makewaysdotnet/TranXit-Frontend";
const sha = "a".repeat(40);
const config = { schemaVersion: 1, repository: backend, sha };
const selected = selectPair(config, frontend, {});
test("only a full lowercase immutable SHA is accepted", () => {
  assert.equal(fullSha(sha), sha);
  for (const ref of [undefined, null, "", "main", "feature/web-first-mvp", "v1.0", "a".repeat(7), "a".repeat(39), "a".repeat(41), "A".repeat(40), "g".repeat(40), sha + "\n", "--help"]) {
    assert.throws(() => fullSha(ref), /40-character/);
  }
});
test("counterpart repository and schema cannot silently drift", () => {
  assert.deepEqual(validatePair(config, frontend), { repository: backend, sha });
  for (const invalid of [{ ...config, schemaVersion: 2 }, { ...config, repository: frontend }, { ...config, repository: "other/repo" }, null]) {
    assert.throws(() => validatePair(invalid, frontend), /schema or repository/);
  }
  assert.throws(() => validatePair(config, "other/repo"), /schema or repository/);
});
test("records the actual two SHAs and rejects mismatched checkouts", () => {
  const ownSha = "b".repeat(40);
  assert.deepEqual(recordPair(selected, frontend, ownSha, sha).repositories, { [frontend]: ownSha, [backend]: sha });
  assert.throws(() => recordPair(selected, frontend, ownSha, ownSha), /does not match/);
  assert.throws(() => recordPair(selected, frontend, "main", sha), /40-character/);
  assert.throws(() => recordPair(config, frontend, ownSha, sha), /selection evidence/);
});
test("only explicit dispatch can select an immutable override, with honest provenance", () => {
  const override = "b".repeat(40);
  const env = { GITHUB_EVENT_NAME: "workflow_dispatch", COUNTERPART_SHA: override, GITHUB_ACTOR: "operator", GITHUB_RUN_ID: "123", GITHUB_RUN_ATTEMPT: "1", GITHUB_WORKFLOW_REF: "owner/repo/.github/workflows/test.yml@refs/heads/review" };
  const result = selectPair(config, frontend, env);
  assert.equal(result.sha, override);
  assert.deepEqual(result.selection, { source: "operator-selected-workflow-dispatch", committedSha: sha, selectedSha: override, eventName: "workflow_dispatch", actor: "operator", runId: "123", runAttempt: "1", workflowRef: env.GITHUB_WORKFLOW_REF });
  assert.equal(config.sha, sha);
  assert.equal(recordPair(result, frontend, sha, override).selection.source, "operator-selected-workflow-dispatch");
  assert.throws(() => recordPair(result, frontend, sha, sha), /does not match/);
  for (const eventName of ["push", "pull_request", "", undefined]) {
    assert.throws(() => selectPair(config, frontend, { ...env, GITHUB_EVENT_NAME: eventName }), /only.*workflow_dispatch/);
    assert.equal(selectPair(config, frontend, { ...env, GITHUB_EVENT_NAME: eventName, COUNTERPART_SHA: "" }).sha, sha);
  }
  for (const ref of ["main", "v1", "a".repeat(7), "A".repeat(40), " "]) {
    assert.throws(() => selectPair(config, frontend, { ...env, COUNTERPART_SHA: ref }), /40-character/);
  }
  assert.throws(() => recordPair({ ...result, selection: { ...result.selection, eventName: "push" } }, frontend, sha, override), /workflow_dispatch/);
  assert.throws(() => recordPair({ ...result, selection: { ...result.selection, source: "committed-pin" } }, frontend, sha, override), /committed SHA/);
});
test("actual Git checkouts are verified and dirty tracked inputs cannot be labelled immutable", () => {
  const root = mkdtempSync(join(tmpdir(), "tranxit-pair-test-"));
  const git = (path, ...args) => execFileSync("git", ["-C", path, ...args], { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] }).trim();
  try {
    const paths = ["own", "counterpart"].map((name) => join(root, name));
    for (const path of paths) {
      mkdirSync(path); git(path, "init"); git(path, "config", "core.autocrlf", "false");
      git(path, "config", "user.name", "Pair Test"); git(path, "config", "user.email", "pair-test@example.invalid");
      writeFileSync(join(path, "fixture"), "reviewed\n"); git(path, "add", "."); git(path, "commit", "-m", "fixture");
    }
    const configPath = join(root, "pair.json");
    const selectionPath = join(root, "evidence", "selection.json");
    const outputPath = join(root, "evidence", "pair.json");
    const expected = git(paths[1], "rev-parse", "HEAD");
    writeFileSync(configPath, JSON.stringify({ ...config, sha: expected }));
    const resolveArgs = ["resolve", configPath, frontend, selectionPath];
    assert.throws(() => main(resolveArgs, { GITHUB_EVENT_NAME: "workflow_dispatch", COUNTERPART_SHA: "main" }), /40-character/);
    assert.equal(existsSync(selectionPath), false);
    main(resolveArgs, {});
    main(["record", selectionPath, frontend, ...paths, outputPath], {});
    assert.equal(JSON.parse(readFileSync(outputPath, "utf8")).repositories[backend], expected);
    writeFileSync(configPath, JSON.stringify(config));
    main(resolveArgs, {});
    assert.throws(() => main(["record", selectionPath, frontend, ...paths, outputPath], {}), /does not match/);
    main(resolveArgs, { GITHUB_EVENT_NAME: "workflow_dispatch", COUNTERPART_SHA: expected });
    main(["record", selectionPath, frontend, ...paths, outputPath], {});
    assert.equal(JSON.parse(readFileSync(outputPath, "utf8")).selection.committedSha, sha);
    assert.equal(JSON.parse(readFileSync(outputPath, "utf8")).selection.source, "operator-selected-workflow-dispatch");
    writeFileSync(join(paths[0], "fixture"), "uncommitted\n");
    assert.throws(() => main(["record", selectionPath, frontend, ...paths, outputPath], {}), /dirty tracked/);
  } finally { rmSync(root, { recursive: true, force: true }); }
});
