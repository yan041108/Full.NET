import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {randomUUID} from 'node:crypto';
import test from 'node:test';
import {createCapacityOutput, cleanVerifiedCapacityOutput} from '../../eng/load/cache-capacity/output.mjs';

const workspace = path.resolve(import.meta.dirname, '../..');
const freshOutput = () => path.join(os.tmpdir(), `fullnet-cache-capacity-test-${randomUUID()}`);
const completed = {pid: 123, status: 'completed-locks-released', acceptance: true,
  cleanupConfirmed: true, resourceReleaseConfirmed: true, workloadCompleted: true,
  remainingNewContainers: [], volumeOwners: ''};
const terminal = {Pid: 123, ExitCode: 0, ObservedViaLiveProcessHandle: true};

function removeFixture(directory) {
  assert.equal(path.dirname(path.resolve(directory)), path.resolve(os.tmpdir()));
  assert.match(path.basename(directory), /^fullnet-cache-capacity-test-/);
  fs.rmSync(directory, {recursive: true, force: true});
}

test('capacity output rejects workspace, shared temp, traversal and existing directories', () => {
  for (const output of [path.join(workspace, 'artifacts/new'), os.tmpdir(),
    path.join(os.tmpdir(), 'fullnet-cache-capacity-test-x', '..', 'foreign'), 'relative'])
    assert.throws(() => createCapacityOutput(output, workspace));
  const output = freshOutput();
  try {
    createCapacityOutput(output, workspace);
    assert.throws(() => createCapacityOutput(output, workspace));
    assert.ok(fs.existsSync(path.join(output, 'output-owner.json')));
  } finally { removeFixture(output); }
});

test('verified capacity output is removed only after actual process and resource completion', () => {
  const output = freshOutput();
  try {
    createCapacityOutput(output, workspace);
    fs.writeFileSync(path.join(output, 'sample.prom'), 'raw measurement');
    for (const change of [{acceptance: false}, {cleanupConfirmed: false},
      {resourceReleaseConfirmed: false}, {remainingNewContainers: ['still-running']}, {volumeOwners: 'driver'}]) {
      assert.throws(() => cleanVerifiedCapacityOutput(output, {...completed, ...change}, terminal));
      assert.ok(fs.existsSync(path.join(output, 'sample.prom')));
    }
    for (const change of [{Pid: 124}, {ExitCode: 1}, {ObservedViaLiveProcessHandle: false}])
      assert.throws(() => cleanVerifiedCapacityOutput(output, completed, {...terminal, ...change}));
    assert.equal(cleanVerifiedCapacityOutput(output, completed, terminal), true);
    assert.equal(fs.existsSync(output), false);
  } finally { removeFixture(output); }
});

test('unowned historical evidence and modified ownership are never removed', () => {
  const output = freshOutput();
  try {
    fs.mkdirSync(output);
    assert.equal(cleanVerifiedCapacityOutput(output, completed, terminal), false);
    assert.ok(fs.existsSync(output));
    fs.writeFileSync(path.join(output, 'output-owner.json'), JSON.stringify({schemaVersion: 1, directory: workspace}));
    assert.throws(() => cleanVerifiedCapacityOutput(output, completed, terminal));
    assert.ok(fs.existsSync(output));
  } finally { removeFixture(output); }
});

test('a linked child cannot make successful cleanup traverse another directory', () => {
  const output = freshOutput(), external = freshOutput();
  try {
    createCapacityOutput(output, workspace);
    fs.mkdirSync(external);
    fs.writeFileSync(path.join(external, 'keep.txt'), 'foreign');
    fs.symlinkSync(external, path.join(output, 'linked'), process.platform === 'win32' ? 'junction' : 'dir');
    assert.throws(() => cleanVerifiedCapacityOutput(output, completed, terminal));
    assert.equal(fs.readFileSync(path.join(external, 'keep.txt'), 'utf8'), 'foreign');
    fs.unlinkSync(path.join(output, 'linked'));
  } finally { removeFixture(output); removeFixture(external); }
});
