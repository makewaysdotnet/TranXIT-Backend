import { appendFileSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { execFileSync } from "node:child_process";
import { dirname, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const repositories = ["makewaysdotnet/TranXIT-Backend", "makewaysdotnet/TranXit-Frontend"];
export function fullSha(value) {
  if (typeof value !== "string" || !/^[0-9a-f]{40}$/.test(value)) {
    throw new Error("A lowercase full 40-character commit SHA is required; branches, tags and abbreviated hashes are forbidden.");
  }
  return value;
}
export function validatePair(config, ownRepository) {
  if (config?.schemaVersion !== 1 || !repositories.includes(ownRepository) ||
      config.repository !== repositories.find((repo) => repo !== ownRepository)) {
    throw new Error("Invalid CI counterpart schema or repository.");
  }
  return { repository: config.repository, sha: fullSha(config.sha) };
}
export function selectPair(config, ownRepository, env) {
  const committed = validatePair(config, ownRepository);
  const override = env.COUNTERPART_SHA ?? "";
  if (override !== "" && env.GITHUB_EVENT_NAME !== "workflow_dispatch") {
    throw new Error("A counterpart override is allowed only for an explicit workflow_dispatch.");
  }
  const selectedSha = override === "" ? committed.sha : fullSha(override);
  return {
    schemaVersion: 1,
    repository: committed.repository,
    sha: selectedSha,
    selection: {
      source: override === "" ? "committed-pin" : "operator-selected-workflow-dispatch",
      committedSha: committed.sha,
      selectedSha,
      eventName: env.GITHUB_EVENT_NAME ?? null,
      actor: env.GITHUB_ACTOR ?? null,
      runId: env.GITHUB_RUN_ID ?? null,
      runAttempt: env.GITHUB_RUN_ATTEMPT ?? null,
      workflowRef: env.GITHUB_WORKFLOW_REF ?? null,
    },
  };
}
function selectionEvidence(config) {
  const selection = config.selection;
  if (!selection || !["committed-pin", "operator-selected-workflow-dispatch"].includes(selection.source) ||
      fullSha(selection.selectedSha) !== config.sha) {
    throw new Error("Missing or inconsistent source selection evidence; run resolve before record.");
  }
  fullSha(selection.committedSha);
  if (selection.source === "committed-pin" && selection.committedSha !== selection.selectedSha) {
    throw new Error("Committed-pin selection does not match the committed SHA.");
  }
  if (selection.source === "operator-selected-workflow-dispatch" && selection.eventName !== "workflow_dispatch") {
    throw new Error("Operator-selected source evidence must identify a workflow_dispatch.");
  }
  return selection;
}
export function recordPair(config, ownRepository, ownSha, counterpartSha) {
  const counterpart = validatePair(config, ownRepository);
  const selection = selectionEvidence(config);
  fullSha(ownSha);
  if (fullSha(counterpartSha) !== counterpart.sha) {
    throw new Error("Checked-out counterpart does not match the selected immutable SHA.");
  }
  return {
    schemaVersion: 1,
    repositories: { [ownRepository]: ownSha, [counterpart.repository]: counterpartSha },
    selection,
    evidenceScope: "source-pair-only; not build-once image promotion",
  };
}
function head(path) {
  // A commit pair is not evidence for uncommitted tracked changes. Ignore
  // untracked CI artifacts and the nested counterpart checkout.
  const trackedChanges = execFileSync("git", ["-C", path, "status", "--porcelain=v1", "--untracked-files=no"], { encoding: "utf8" }).trim();
  if (trackedChanges) throw new Error("Cannot record immutable source evidence from a dirty tracked checkout.");
  return execFileSync("git", ["-C", path, "rev-parse", "--verify", "HEAD^{commit}"], { encoding: "utf8" }).trim();
}
export function main(args, env = process.env) {
  const [command, configPath, ownRepository, ownPath, counterpartPath, outputPath] = args;
  const config = JSON.parse(readFileSync(configPath, "utf8"));
  if (command === "resolve" && args.length === 4) {
    // Validate any dispatch input before emitting a checkout ref or touching the
    // counterpart. Retain a separate selection file; never rewrite the pin.
    const selected = selectPair(config, ownRepository, env);
    mkdirSync(dirname(ownPath), { recursive: true });
    writeFileSync(ownPath, JSON.stringify(selected, null, 2) + "\n");
    const output = `repository=${selected.repository}\nsha=${selected.sha}\n`;
    if (env.GITHUB_OUTPUT) appendFileSync(env.GITHUB_OUTPUT, output);
    process.stdout.write(output);
  } else if (command === "record" && args.length === 6) {
    const evidence = {
      ...recordPair(config, ownRepository, head(ownPath), head(counterpartPath)),
      runId: env.GITHUB_RUN_ID ?? null,
      runAttempt: env.GITHUB_RUN_ATTEMPT ?? null,
    };
    mkdirSync(dirname(outputPath), { recursive: true });
    writeFileSync(outputPath, JSON.stringify(evidence, null, 2) + "\n");
    process.stdout.write(JSON.stringify(evidence, null, 2) + "\n");
  } else {
    throw new Error("Usage: ci-pair.mjs resolve <pin> <own-repository> <selection-output> OR record <selection> <own-repository> <own-path> <counterpart-path> <output>");
  }
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try { main(process.argv.slice(2)); }
  catch (error) { console.error(error.message); process.exitCode = 1; }
}
