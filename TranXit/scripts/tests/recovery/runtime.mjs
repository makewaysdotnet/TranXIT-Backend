import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const harness = path.dirname(fileURLToPath(import.meta.url));
const backend = path.resolve(harness, '../../../..');
const frontend = fs.realpathSync(process.env.TRANXIT_RECOVERY_FRONTEND_DIR || path.resolve(backend, '../TranXit-Frontend'));
const project = `tranxit-f01-test-${randomBytes(8).toString('hex')}`;
const controller = `${project}-controller`;
const label = `io.tranxit.recovery-test=${project}`;
const output = path.join(backend, 'TranXit/Tests/TranXit.IntegrationTests/TestResults', project);
const env = Object.fromEntries(Object.entries(process.env).filter(([name]) => /^(PATH|PATHEXT|SYSTEMROOT|SYSTEMDRIVE|WINDIR|COMSPEC|HOME|USERPROFILE|APPDATA|LOCALAPPDATA|TEMP|TMP|PROGRAMFILES|PROGRAMDATA|CI)$/i.test(name)));
let context;
let running;
let interrupted = false;
let ownsNamespace = false;

for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => { interrupted = true; running?.kill(signal); });

async function execute(args, { allowFailure = false, print = false, input } = {}) {
  const result = await new Promise((resolve) => {
    let stdout = '', stderr = '';
    const child = spawn('docker', context ? ['--context', context, ...args] : args, {
      env, windowsHide: true, shell: false, stdio: [input === undefined ? 'ignore' : 'pipe', 'pipe', 'pipe'],
    });
    running = child;
    const heartbeat = setInterval(() => console.log(`[recovery] ${args[0]} still running (${project}).`), 20_000);
    child.stdout.on('data', (chunk) => { stdout += chunk; if (print) process.stdout.write(chunk); });
    child.stderr.on('data', (chunk) => { stderr += chunk; if (print) process.stderr.write(chunk); });
    child.on('error', () => { stderr += 'Could not launch local Docker.'; });
    child.on('close', (code) => { clearInterval(heartbeat); running = undefined; resolve({ code: code ?? 1, stdout, stderr }); });
    if (input !== undefined) { child.stdin.on('error', () => {}); child.stdin.end(input); }
  });
  if (!allowFailure) assert.equal(result.code, 0, `Docker ${args[0]} failed: ${result.stderr.slice(-3000)}`);
  return result;
}

async function ids(type) {
  const command = type === 'container' ? ['ps', '-aq'] : [type, 'ls', '-q'];
  const result = await execute([...command, '--filter', `label=${label}`]);
  return result.stdout.trim().split(/\s+/).filter(Boolean);
}

async function cleanup() {
  for (const type of ['container', 'network', 'volume']) {
    const resources = [];
    for (const id of await ids(type)) {
      resources.push(JSON.parse((await execute([type, 'inspect', id])).stdout)[0]);
    }
    // Stop the controller first so an interrupted run cannot create more resources during cleanup.
    resources.sort((a, b) => Number(b.Name === `/${controller}`) - Number(a.Name === `/${controller}`));
    for (const resource of resources) {
      const labels = resource.Config?.Labels ?? resource.Labels;
      assert.equal(labels?.['io.tranxit.recovery-test'], project, 'Cleanup ownership mismatch.');
      assert.ok((resource.Name || '').replace(/^\//, '').startsWith(project), 'Cleanup namespace mismatch.');
      await execute([type, 'rm', ...(type === 'container' ? ['--force'] : []), resource.Id || resource.Name]);
    }
  }
  for (const type of ['container', 'network', 'volume']) assert.equal((await ids(type)).length, 0, `Leaked ${type} resources.`);
}

try {
  assert.ok(fs.existsSync(path.join(backend, '.git')) && fs.existsSync(path.join(frontend, '.git')), 'Both source repositories are required.');
  context = (await execute(['context', 'show'])).stdout.trim();
  assert.match(context, /^[A-Za-z0-9_.-]+$/);
  const endpoint = JSON.parse((await execute(['context', 'inspect', context, '--format', '{{json .Endpoints.docker.Host}}'])).stdout);
  assert.ok(endpoint.startsWith('unix:///') || /^npipe:\/\/\/+\.\/pipe\//i.test(endpoint), 'Only a local Docker socket/named pipe is allowed.');
  for (const type of ['container', 'network', 'volume']) assert.equal((await ids(type)).length, 0);
  ownsNamespace = true;
  console.log(`[recovery] Owned local fixture: ${project}. Source repositories are mounted read-only.`);
  await execute(['build', '-t', `${project}:controller`, harness], { print: true });
  for (const name of ['work', 'gate', 'settings']) await execute(['volume', 'create', '--label', label, `${project}-${name}`]);
  await execute(['network', 'create', '--label', label, `${project}_control`]);
  await execute(['create', '--name', controller, '--label', label, '--network', `${project}_control`,
    '--mount', 'type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock',
    '--mount', `type=bind,source=${backend},target=/source/backend,readonly`,
    '--mount', `type=bind,source=${frontend},target=/source/frontend,readonly`,
    '--mount', `type=volume,source=${project}-work,target=/work`,
    '--mount', `type=volume,source=${project}-gate,target=/work/markers/admission-staging`,
    '--mount', `type=volume,source=${project}-settings,target=/work/settings`,
    '-e', `F01_PROJECT=${project}`, '-e', `F01_CONTROLLER=${controller}`, `${project}:controller`]);
  const run = await execute(['start', '--attach', controller], { allowFailure: true, print: true });
  fs.mkdirSync(output, { recursive: true });
  await execute(['cp', `${controller}:/work/public-results/.`, output], { allowFailure: true });
  const state = JSON.parse((await execute(['inspect', '--format', '{{json .State}}', controller])).stdout);
  assert.equal(run.code, 0, 'Runtime harness did not complete.');
  assert.equal(state.ExitCode, 0, `Recovery tests failed; sanitized evidence: ${output}`);
  assert.ok(!interrupted, 'Interrupted recovery run.');
  console.log(`[recovery] PASS. Sanitized evidence: ${output}`);
} catch (error) {
  console.error(`[recovery] FAIL: ${error.message}`);
  process.exitCode = 1;
} finally {
  if (ownsNamespace) {
    try { await cleanup(); console.log(`[recovery] Removed owned containers, networks and volumes: ${project}`); }
    catch (error) { console.error(`[recovery] Cleanup incomplete: ${error.message}`); process.exitCode = 1; }
  }
}
