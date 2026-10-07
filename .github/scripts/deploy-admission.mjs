import { resolve } from "node:path";
import { pathToFileURL } from "node:url";
import { fullSha } from "./ci-pair.mjs";

export function validateInputs(env) {
  if (!["staging", "production"].includes(env.TARGET_ENV)) throw new Error("Invalid target environment.");
  fullSha(env.BACKEND_SHA);
  fullSha(env.FRONTEND_REF);
  if (!["expand-contract", "restore-required"].includes(env.MIGRATION_POLICY)) throw new Error("Invalid migration policy.");
  if (!["true", "false"].includes(env.ALLOW_FIRST_DEPLOY)) throw new Error("Invalid first-deploy acknowledgement.");
}

export function validateProductionApproval(environment, reviews, env) {
  // GitHub's review-history response has no run-attempt identifier/timestamp. A new
  // workflow dispatch is required so a prior attempt's approval cannot be reused.
  if (env.GITHUB_RUN_ATTEMPT !== "1") throw new Error("Production requires a fresh workflow dispatch, not a rerun.");
  if (environment?.name !== "production" || !Number.isInteger(environment.id)) throw new Error("Production environment is missing or invalid.");
  const rule = environment.protection_rules?.find((item) => item.type === "required_reviewers");
  if (rule?.prevent_self_review !== true || !Array.isArray(rule.reviewers) || !rule.reviewers.length) {
    throw new Error("Production must require reviewers and prevent self-review.");
  }
  const allowed = rule.reviewers.filter((item) => item.type === "User" && Number.isInteger(item.reviewer?.id));
  if (!allowed.length) throw new Error("A directly configured User reviewer is required; team membership is not inferred.");
  if (!env.GITHUB_ACTOR || !env.GITHUB_TRIGGERING_ACTOR || !Array.isArray(reviews)) throw new Error("Production approval evidence is incomplete.");
  const productionReviews = reviews.filter((review) => review.environments?.some((item) => item.id === environment.id));
  if (productionReviews.some((review) => review.state !== "approved")) {
    throw new Error("Conflicting or non-approved production review history; use a fresh workflow dispatch.");
  }
  const approval = productionReviews.find((review) => {
    const login = review.user?.login?.toLowerCase();
    return review.state === "approved" && login &&
      ![env.GITHUB_ACTOR.toLowerCase(), env.GITHUB_TRIGGERING_ACTOR.toLowerCase()].includes(login) &&
      allowed.some((item) => item.reviewer.id === review.user.id && item.reviewer.login?.toLowerCase() === login);
  });
  if (!approval) throw new Error("No independent configured User approved this production run; bypass alone is insufficient.");
  return approval.user.login;
}

export async function checkAdmission(env, api) {
  validateInputs(env);
  if (env.TARGET_ENV !== "production") return "Staging source inputs validated; this is not production approval or image promotion evidence.";
  if (!/^\d+$/.test(env.GITHUB_RUN_ID ?? "") || !/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(env.GITHUB_REPOSITORY ?? "")) {
    throw new Error("GitHub run identity is missing or invalid.");
  }
  const base = `/repos/${env.GITHUB_REPOSITORY}`;
  const environment = await api(`${base}/environments/production`);
  const reviews = await api(`${base}/actions/runs/${env.GITHUB_RUN_ID}/approvals`);
  const reviewer = validateProductionApproval(environment, reviews, env);
  return `Production policy and independent approval verified for this run (reviewer: ${reviewer}). Admin-bypass configuration is not exposed by this API; image promotion remains a separate gate.`;
}

export async function main(env = process.env) {
  const api = async (path) => {
    if (!env.GITHUB_TOKEN) throw new Error("GitHub read token is missing; refusing production deployment.");
    const response = await fetch(`https://api.github.com${path}`, {
      headers: { Authorization: `Bearer ${env.GITHUB_TOKEN}`, Accept: "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28" },
      signal: AbortSignal.timeout(15_000),
    });
    if (!response.ok) throw new Error(`GitHub production approval evidence unavailable (HTTP ${response.status}).`);
    return response.json();
  };
  console.log(await checkAdmission(env, api));
}
if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main().catch((error) => { console.error(error.message); process.exitCode = 1; });
}
