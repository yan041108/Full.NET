import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { createApp } from '../../scripts/templates/create-app.mjs';
import { verifyDistributionManifest, writeDistributionManifest } from '../../scripts/templates/distribution-manifest.mjs';

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
