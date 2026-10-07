import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const source = fileURLToPath(new URL('./runtime.mjs', import.meta.url));

function rejectedRun(endpoint, collision = false) {
  assert.equal(process.platform, 'linux', 'Run these CLI-boundary tests in the documented Linux Node container.');
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'tranxit-recovery-safety-'));
  try {
    const backend = path.join(root, 'TranXIT-Backend');
    const frontend = path.join(root, 'TranXit-Frontend');
    const harness = path.join(backend, 'TranXit/scripts/tests/recovery');
    const bin = path.join(root, 'bin');
    const calls = path.join(root, 'calls.jsonl');
    const state = path.join(root, 'state.json');
    for (const directory of [path.join(backend, '.git'), path.join(frontend, '.git'), harness, bin]) {
      fs.mkdirSync(directory, { recursive: true });
    }
    fs.copyFileSync(source, path.join(harness, 'runtime.mjs'));
    fs.writeFileSync(calls, '');
    fs.writeFileSync(state, JSON.stringify({ collision, project: '' }));
    // This CLI double has no Docker socket. It tests refusal/cleanup boundaries, not the live stack.
    const shim = `#!/usr/bin/env node
const fs = require('node:fs');
const args = process.argv.slice(2);
fs.appendFileSync(${JSON.stringify(calls)}, JSON.stringify(args) + '\\n');
if (args[0] === '--context') args.splice(0, 2);
const file = ${JSON.stringify(state)};
const state = JSON.parse(fs.readFileSync(file, 'utf8'));
if (args[0] === 'context' && args[1] === 'show') console.log('fixture-context');
else if (args[0] === 'context' && args[1] === 'inspect') console.log(${JSON.stringify(JSON.stringify(endpoint))});
else if (args[0] === 'ps') {
  state.project = args.find(value => value.startsWith('label=io.tranxit.recovery-test=')).split('=').at(-1);
  fs.writeFileSync(file, JSON.stringify(state));
  if (state.collision) console.log('already-present');
} else if (args[0] === 'container' && args[1] === 'inspect') {
  console.log(JSON.stringify([{ Id: 'already-present', Name: '/' + state.project + '-existing',
    Config: { Labels: { 'io.tranxit.recovery-test': state.project } } }]));
} else if (args[0] === 'container' && args[1] === 'rm') {
  state.collision = false;
  fs.writeFileSync(file, JSON.stringify(state));
} else if (!(['network', 'volume'].includes(args[0]) && args[1] === 'ls')) process.exit(99);
`;
    fs.writeFileSync(path.join(bin, 'docker'), shim, { mode: 0o700 });
    const result = spawnSync(process.execPath, [path.join(harness, 'runtime.mjs')], {
      encoding: 'utf8', timeout: 10_000,
      env: { ...process.env, PATH: `${bin}:${process.env.PATH}`, TRANXIT_RECOVERY_FRONTEND_DIR: frontend },
    });
    assert.equal(result.status, 1, result.stderr);
    return {
      stderr: result.stderr,
      calls: fs.readFileSync(calls, 'utf8').trim().split('\n').filter(Boolean).map(line => {
        const args = JSON.parse(line);
        return args[0] === '--context' ? args.slice(2) : args;
      }),
    };
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
}

for (const endpoint of ['ssh://deploy@example.invalid', 'tcp://example.invalid:2376']) {
  test(`T-NFR-9.RuntimeIsolationGuards rejects ${endpoint.split(':')[0]} before any engine command`, () => {
    // UC-NFR-9
    const result = rejectedRun(endpoint);
    assert.match(result.stderr, /Only a local Docker socket/);
    assert.deepEqual(result.calls.map(args => args.slice(0, 2)), [['context', 'show'], ['context', 'inspect']]);
  });
}

test('T-NFR-9.RuntimeIsolationGuards does not clean up a pre-existing namespace', () => {
  // UC-NFR-9
  const result = rejectedRun('unix:///var/run/docker.sock', true);
  assert.deepEqual(result.calls.map(args => args.slice(0, 2)), [
    ['context', 'show'], ['context', 'inspect'], ['ps', '-aq'],
  ]);
  assert.ok(result.calls.every(args => !args.includes('rm')), 'No resources belong to this rejected run.');
});
