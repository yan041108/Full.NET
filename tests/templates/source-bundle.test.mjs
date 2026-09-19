import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { assertManagedPath, buildSourceBundle } from '../../scripts/templates/build-source-bundle.mjs';

test('build-source-bundle writes manifest with sha256 managed files', async () => {
  const output = mkdtempSync(join(tmpdir(), 'fullnet-bundle-'));
  try {
    const { bundleRoot, manifest } = buildSourceBundle({ output });
    const manifestPath = join(bundleRoot, 'framework-manifest.json');
    const onDisk = JSON.parse(readFileSync(manifestPath, 'utf8'));

    assert.equal(manifest.schemaVersion, 1);
    assert.match(manifest.sourceCommit, /^[0-9a-f]{40}$/);
    assert.equal(manifest.frameworkVersion, '0.1.0');
    assert.ok(Object.keys(manifest.managedFiles).length > 0);
    assert.deepEqual(onDisk.managedFiles, manifest.managedFiles);

    for (const [relativePath, digest] of Object.entries(manifest.managedFiles)) {
      assert.match(digest, /^[a-f0-9]{64}$/, relativePath + ' digest');
      assert.ok(!relativePath.includes('..'), 'managed path must stay relative');
      assert.ok(!relativePath.includes('bin/'), 'bin must be excluded');
      assert.ok(!relativePath.includes('obj/'), 'obj must be excluded');
    }
  } finally {
    rmSync(output, { recursive: true, force: true });
  }
});

test('build-source-bundle rejects bad managed paths', () => {
  assert.throws(() => assertManagedPath('../escape.txt'), /outside bundle root/);
  assert.throws(() => assertManagedPath('src/obj/cache.txt'), /excluded path/);
});