import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { resolveRealStackBuildLayout } from '../e2e/admin-real-stack/scripts/stack-build-layout.mjs';

const root = fileURLToPath(new URL('../..', import.meta.url));
const probe = fileURLToPath(new URL('./support/real-stack-build-probe.mjs', import.meta.url));

for (const provider of ['SqlServer', 'MySql']) {
  test(`real stack entry builds and runs Release from the assigned temporary output (${provider})`, () => {
    const parent = mkdtempSync(path.join(tmpdir(), 'fullnet-stack-build-probe-'));
    try {
      const buildRoot = path.join(parent, 'shared-build');
      const report = path.join(parent, 'probe.json');
      const env = {...process.env, FULLNET_E2E_DATABASE_PROVIDER: provider,
        FULLNET_E2E_STACK_PROFILE: 'development', FULLNET_E2E_BUILD_ROOT: buildRoot,
        FULLNET_E2E_LOG_ROOT: path.join(parent, 'logs'), FULLNET_STACK_BUILD_PROBE_REPORT: report};
      delete env.FULLNET_E2E_RESOURCE_PROFILE;
      const result = spawnSync(process.execPath, [probe], {cwd: root, env, encoding: 'utf8',
        timeout: 20_000, windowsHide: true});
      assert.equal(result.status, 0, result.stderr || result.stdout || result.error?.message);
      const actual = JSON.parse(readFileSync(report, 'utf8'));
      assert.equal(actual.realDockerOrDotnetExecuted, false);
      assert.equal(actual.calls.length, 2);
      const args = actual.calls[0].args;
      assert.equal(args[0], 'build');
      assert.equal(args[args.indexOf('-c') + 1], 'Release', 'real bootstrap must explicitly select Release');
      assert.equal(args.find(value => value.startsWith('-p:ArtifactsPath=')), `-p:ArtifactsPath=${buildRoot}`);
      assert.equal(actual.calls[1].args[0], path.join(buildRoot,
        'bin/Full.NET.Host.Migrator/release/Full.NET.Host.Migrator.dll'));
    } finally {
      rmSync(parent, {recursive: true, force: true});
    }
  });
}

test('default outputs stay in system temp, reuse the same build across providers and isolate workspaces', () => {
  const workspace = path.join(tmpdir(), 'fullnet-layout-workspace');
  const sql = resolveRealStackBuildLayout(workspace, {env: {}, provider: 'SqlServer'});
  const mysql = resolveRealStackBuildLayout(workspace, {env: {}, provider: 'MySql'});
  const other = resolveRealStackBuildLayout(workspace + '-other', {env: {}});
  assert.equal(sql.artifactsPath, mysql.artifactsPath);
  assert.notEqual(sql.artifactsPath, other.artifactsPath);
  assert.notEqual(sql.logPath('Api'), mysql.logPath('Api'));
  const relative = path.relative(path.join(tmpdir(), 'fullnet-real-stack'), sql.artifactsPath);
  assert.ok(relative && !relative.startsWith('..') && !path.isAbsolute(relative));
  assert.ok(path.relative(workspace, sql.artifactsPath).startsWith('..'));
});

test('explicit outputs with spaces retain all three Release binaries and separate Host logs', () => {
  const build = path.join(tmpdir(), 'fullnet build with spaces');
  const logs = path.join(tmpdir(), 'fullnet logs with spaces');
  const layout = resolveRealStackBuildLayout(root, {env: {
    FULLNET_E2E_BUILD_ROOT: build, FULLNET_E2E_LOG_ROOT: logs
  }});
  for (const host of ['Migrator', 'Api', 'Worker']) {
    assert.equal(layout.assemblyPath(host), path.join(build, 'bin', `Full.NET.Host.${host}`,
      'release', `Full.NET.Host.${host}.dll`));
  }
  assert.equal(layout.logPath('Api'), path.join(logs, 'api.log'));
  assert.equal(layout.logPath('Worker'), path.join(logs, 'worker.log'));
});

test('relative output overrides are rejected before their meaning can change with the working directory', () => {
  for (const key of ['FULLNET_E2E_BUILD_ROOT', 'FULLNET_E2E_LOG_ROOT']) {
    assert.throws(() => resolveRealStackBuildLayout(root, {env: {[key]: 'relative-output'}}),
      new RegExp(`${key} must be an absolute path`));
  }
});

test('the real bootstrap rejects an invalid output before acquiring any container', () => {
  const parent = mkdtempSync(path.join(tmpdir(), 'fullnet-stack-build-probe-'));
  try {
    const report = path.join(parent, 'probe.json');
    const result = spawnSync(process.execPath, [probe], {cwd: root, env: {...process.env,
      FULLNET_E2E_BUILD_ROOT: 'relative-output', FULLNET_STACK_BUILD_PROBE_REPORT: report},
      encoding: 'utf8', timeout: 20_000, windowsHide: true});
    assert.notEqual(result.status, 0);
    const actual = JSON.parse(readFileSync(report, 'utf8'));
    assert.match(actual.error, /FULLNET_E2E_BUILD_ROOT must be an absolute path/);
    assert.equal(actual.startedContainers, 0);
    assert.deepEqual(actual.calls, []);
  } finally {
    rmSync(parent, {recursive: true, force: true});
  }
});

test('unknown provider and Host cannot resolve an unintended output', () => {
  assert.throws(() => resolveRealStackBuildLayout(root, {provider: 'unknown', env: {}}), /Unknown.*provider/);
  const layout = resolveRealStackBuildLayout(root, {env: {}});
  assert.throws(() => layout.assemblyPath('../Api'), /Unknown.*Host/);
});
