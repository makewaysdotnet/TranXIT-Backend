import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { checkAdmission, validateInputs, validateProductionApproval } from "./deploy-admission.mjs";

const env = { TARGET_ENV: "production", BACKEND_SHA: "a".repeat(40), FRONTEND_REF: "b".repeat(40), MIGRATION_POLICY: "expand-contract", ALLOW_FIRST_DEPLOY: "false", GITHUB_ACTOR: "owner", GITHUB_TRIGGERING_ACTOR: "owner", GITHUB_RUN_ATTEMPT: "1", GITHUB_RUN_ID: "123", GITHUB_REPOSITORY: "makewaysdotnet/TranXIT-Backend" };
const environment = { name: "production", id: 7, protection_rules: [{ type: "required_reviewers", prevent_self_review: true, reviewers: [{ type: "User", reviewer: { id: 2, login: "reviewer" } }] }] };
const reviews = [{ state: "approved", environments: [{ id: 7 }], user: { id: 2, login: "reviewer" } }];
const clone = (value) => structuredClone(value);
test("deploy rejects every mutable or missing source identifier", () => {
  assert.doesNotThrow(() => validateInputs(env));
  for (const key of ["BACKEND_SHA", "FRONTEND_REF"]) {
    for (const value of [undefined, "", "main", "v1", "a".repeat(7), "A".repeat(40)]) assert.throws(() => validateInputs({ ...env, [key]: value }), /40-character/);
  }
  for (const [key, value] of [["TARGET_ENV", "other"], ["MIGRATION_POLICY", "auto"], ["ALLOW_FIRST_DEPLOY", "yes"]]) assert.throws(() => validateInputs({ ...env, [key]: value }), /Invalid/);
});
test("a configured independent direct user approval is accepted", () => {
  assert.equal(validateProductionApproval(environment, reviews, env), "reviewer");
});
test("missing policy, self-review, teams-only and incomplete policy fail closed", () => {
  for (const invalid of [null, {}, { ...environment, name: "staging" }, { ...environment, protection_rules: [] }]) assert.throws(() => validateProductionApproval(invalid, reviews, env));
  for (const mutate of [
    (e) => { e.protection_rules[0].prevent_self_review = false; },
    (e) => { delete e.protection_rules[0].prevent_self_review; },
    (e) => { e.protection_rules[0].reviewers = []; },
    (e) => { e.protection_rules[0].reviewers[0].type = "Team"; },
  ]) { const invalid = clone(environment); mutate(invalid); assert.throws(() => validateProductionApproval(invalid, reviews, env)); }
});
test("bypass, self-approval, other environment, rejected history and reruns fail closed", () => {
  const invalidReviews = [[], null,
    [{ ...reviews[0], user: { id: 99, login: "admin" } }],
    [{ ...reviews[0], environments: [{ id: 8 }] }],
    [{ ...reviews[0], state: "rejected" }],
    [...reviews, { ...reviews[0], state: "rejected" }],
  ];
  for (const invalid of invalidReviews) assert.throws(() => validateProductionApproval(environment, invalid, env));
  for (const override of [{ GITHUB_ACTOR: "REVIEWER" }, { GITHUB_TRIGGERING_ACTOR: "reviewer" }, { GITHUB_RUN_ATTEMPT: "2" }, { GITHUB_ACTOR: "" }]) assert.throws(() => validateProductionApproval(environment, reviews, { ...env, ...override }));
});
test("API denial/errors never admit production and staging makes no policy API request", async () => {
  const unavailable = async () => { throw new Error("HTTP 403"); };
  await assert.rejects(() => checkAdmission(env, unavailable), /403/);
  let calls = 0;
  const api = async (path) => { calls++; return path.endsWith("/production") ? environment : reviews; };
  assert.match(await checkAdmission(env, api), /independent approval verified/);
  assert.equal(calls, 2);
  assert.match(await checkAdmission({ ...env, TARGET_ENV: "staging" }, unavailable), /Staging/);
  await assert.rejects(() => checkAdmission({ ...env, GITHUB_RUN_ID: "missing" }, api), /identity/);
});
test("the workflow runs fail-closed admission before handling SSH credentials", () => {
  const workflow = readFileSync(new URL("../workflows/deploy.yml", import.meta.url), "utf8");
  const admission = workflow.indexOf("node .github/scripts/deploy-admission.mjs");
  const sshKey = workflow.indexOf("- name: Install SSH key");
  const remote = workflow.indexOf("- name: Run remote deploy");
  assert.ok(admission > 0 && sshKey > admission && remote > sshKey);
  assert.ok(workflow.includes("actions: read"));
  assert.ok(!workflow.includes("default: main"));
});
