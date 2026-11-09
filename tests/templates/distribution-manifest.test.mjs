import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { createApp } from '../../scripts/templates/create-app.mjs';
import { verifyDistributionManifest, writeDistributionManifest } from '../../scripts/templates/distribution-manifest.mjs';
import { applyFrameworkUpgrade, planFrameworkUpgrade } from '../../scripts/templates/upgrade-framework.mjs';
import { fixture } from './support/framework-upgrade-fixture.mjs';

test('candidate distribution describes outer inputs and rejects edits, additions and a missing manifest', () => {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-distribution-fixture-'));
  try {
    const commit = 'a'.repeat(40);
    writeFileSync(join(root, 'framework-manifest.json'), JSON.stringify({ sourceCommit: commit, frameworkVersion: '0.1.0' }));
    mkdirSync(join(root, 'framework/fullnet'), { recursive: true });
    writeFileSync(join(root, 'framework/fullnet/managed.txt'), 'managed by inner manifest');
    const config = join(root, 'appsettings.json');
    writeFileSync(config, '{}');
    const digest = writeDistributionManifest(root);
    const manifest = join(root, 'distribution-manifest.json');
    const declared = JSON.parse(readFileSync(manifest, 'utf8'));
    assert.equal(declared.sourceCommit, commit);
    assert.deepEqual(Object.keys(declared.files), ['appsettings.json', 'framework-manifest.json']);
    assert.doesNotThrow(() => verifyDistributionManifest(root, digest));
    assert.throws(() => verifyDistributionManifest(root, 'b'.repeat(64)), /digest mismatch/);

    writeFileSync(config, '{"changed":true}');
    const output = join(root, 'created');
    const run = () => assert.fail('must validate before invoking dotnet');
    assert.throws(() => createApp({ packageRoot: root, output, name: 'Candidate', ownerKey: 'acme', distributionDigest: digest, run }),
      /Distribution files do not match/);
    assert.equal(existsSync(output), false);
    writeFileSync(config, '{}');
    const extra = join(root, 'extra.txt');
    writeFileSync(extra, 'unexpected');
    assert.throws(() => verifyDistributionManifest(root, digest), /Distribution files do not match/);
    rmSync(extra);
    rmSync(manifest);
    assert.throws(() => verifyDistributionManifest(root, digest), /manifest is missing/);
  } finally {
    // 夹具只创建固定文件与两层目录，清理范围限于本次系统临时目录。
    for (const file of ['framework/fullnet/managed.txt', 'framework-manifest.json', 'appsettings.json', 'distribution-manifest.json', 'extra.txt'])
      rmSync(join(root, file), { force: true });
    for (const directory of ['framework/fullnet', 'framework', ''])
      if (existsSync(join(root, directory))) rmdirSync(join(root, directory));
  }
});

test('candidate upgrade CLI verifies the recorded digest before changing framework files', (t) => {
  const f = fixture(t, { projected: true });
  const entry = fileURLToPath(new URL('../../scripts/templates/upgrade-framework.mjs', import.meta.url));
  const run = (stage, ...digestOptions) => spawnSync(process.execPath,
    [entry, '--app', f.appRoot, '--package', f.packageRoot, stage, ...digestOptions], { encoding: 'utf8' });
  const installed = join(f.appRoot, 'framework/fullnet/global.json');
  const digest = writeDistributionManifest(f.packageRoot);
  const wrong = run('--apply', '--distribution-sha256', 'b'.repeat(64));
  assert.equal(wrong.status, 1, wrong.stdout);
  assert.match(wrong.stderr, /digest mismatch/);
  const missingValue = run('--apply', '--distribution-sha256');
  assert.equal(missingValue.status, 1, missingValue.stdout);
  assert.match(missingValue.stderr, /Expected one SHA-256/);
  assert.equal(readFileSync(installed, 'utf8'), f.oldFiles['global.json']);

  const preview = run('--dry-run', '--distribution-sha256', digest);
  assert.equal(preview.status, 0, preview.stderr);
  assert.equal(JSON.parse(preview.stdout).distributionDigest, digest);
  assert.equal(readFileSync(installed, 'utf8'), f.oldFiles['global.json']);
  const applied = run('--apply', '--distribution-sha256', digest);
  assert.equal(applied.status, 0, applied.stderr);
  assert.equal(readFileSync(installed, 'utf8'), f.newFiles['global.json']);
});

test('upgrade rejects changed or missing distribution evidence between preview and apply', (t) => {
  const f = fixture(t);
  const config = join(f.packageRoot, 'appsettings.json');
  writeFileSync(config, '{}');
  const distributionDigest = writeDistributionManifest(f.packageRoot);
  const options = { ...f, distributionDigest };
  const plan = planFrameworkUpgrade(options);
  const unpinnedPlan = planFrameworkUpgrade(f);

  writeFileSync(config, '{"changed":true}');
  assert.throws(() => applyFrameworkUpgrade({ ...options, plan }), /Distribution files do not match/);
  assert.throws(() => planFrameworkUpgrade(f), /Distribution files do not match/);
  rmSync(join(f.packageRoot, 'distribution-manifest.json'));
  assert.throws(() => applyFrameworkUpgrade({ ...options, plan }), /manifest is missing/);
  assert.throws(() => applyFrameworkUpgrade({ ...f, plan: unpinnedPlan }), /preview changed/);
  writeDistributionManifest(f.packageRoot);
  assert.throws(() => applyFrameworkUpgrade({ ...options, plan }), /digest mismatch/);
  assert.throws(() => applyFrameworkUpgrade({ ...f, plan: unpinnedPlan }), /preview changed/);
  assert.equal(readFileSync(join(f.appRoot, 'framework/fullnet/global.json'), 'utf8'), f.oldFiles['global.json']);
  assert.equal(existsSync(join(f.appRoot, '.fullnet-upgrade.lock')), false);
});
