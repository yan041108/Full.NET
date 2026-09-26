import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { compareFrameworkVersions } from '../../scripts/templates/framework-manifest-utils.mjs';
import { applyFrameworkUpgrade, planFrameworkUpgrade, upgradeFramework } from '../../scripts/templates/upgrade-framework.mjs';

test('compareFrameworkVersions orders semantic versions', () => {
  assert.equal(compareFrameworkVersions('0.1.0', '0.1.0'), 0);
  assert.ok(compareFrameworkVersions('0.1.1', '0.1.0') > 0);
  assert.ok(compareFrameworkVersions('0.1.0', '0.2.0') < 0);
});

test('compareFrameworkVersions rejects invalid input', () => {
  assert.throws(() => compareFrameworkVersions('bad', '0.1.0'), /Invalid framework version/);
});

test('upgrade-framework reports customized managed files as conflicts', () => {
  const workspace = mkdtempSync(join(tmpdir(), 'fullnet-upgrade-'));
  try {
    const appRoot = join(workspace, 'app');
    const packageRoot = join(workspace, 'package');
    const managedPath = 'global.json';
    const original = '{"framework":"0.1.0"}';
    const customized = '{"framework":"0.1.0","local":true}';
    const next = '{"framework":"0.1.1"}';
    const digest = (value) => createHash('sha256').update(value).digest('hex');
    const manifest = (version, content) => ({
      schemaVersion: 3,
      sourceCommit: '0'.repeat(40),
      frameworkVersion: version,
      presetModules: { minimal: ['Identity'] },
      validPresets: ['minimal'],
      migrationInventory: { selectionStatus: 'preset-minimal', preset: 'minimal', scripts: [{ name: '001_Foundation.sql', providers: { SqlServer: digest('x'), MySql: digest('x') } }] },
      seedInventory: { contributors: [], presets: { minimal: [] } },
      managedFiles: { [managedPath]: digest(content) },
    });
    mkdirSync(join(appRoot, 'framework/fullnet'), { recursive: true });
    mkdirSync(join(packageRoot, 'framework/fullnet'), { recursive: true });
    writeFileSync(join(appRoot, 'framework/fullnet', managedPath), customized);
    writeFileSync(join(packageRoot, 'framework/fullnet', managedPath), next);
    writeFileSync(join(appRoot, 'framework-manifest.json'), JSON.stringify(manifest('0.1.0', original)));
    writeFileSync(join(packageRoot, 'framework-manifest.json'), JSON.stringify(manifest('0.1.1', next)));
    const plan = planFrameworkUpgrade({ appRoot, packageRoot });
    assert.ok(plan.conflicts.some((entry) => entry.path === managedPath));
    assert.throws(() => applyFrameworkUpgrade({ appRoot, packageRoot, plan }), /conflicts remain/);
  } finally {
    rmSync(workspace, { recursive: true, force: true });
  }
});

test('upgrade-framework applies managed updates without touching customized app host files', () => {
  const workspace = mkdtempSync(join(tmpdir(), 'fullnet-upgrade-apply-'));
  try {
    const appRoot = join(workspace, 'app');
    const packageRoot = join(workspace, 'package');
    const managedPath = 'global.json';
    const original = '{"framework":"0.1.0"}';
    const next = '{"framework":"0.1.1"}';
    const digest = (value) => createHash('sha256').update(value).digest('hex');
    const manifest = (version, content) => ({
      schemaVersion: 3,
      sourceCommit: '0'.repeat(40),
      frameworkVersion: version,
      presetModules: { minimal: ['Identity'] },
      validPresets: ['minimal'],
      migrationInventory: { selectionStatus: 'preset-minimal', preset: 'minimal', scripts: [{ name: '001_Foundation.sql', providers: { SqlServer: digest('x'), MySql: digest('x') } }] },
      seedInventory: { contributors: [], presets: { minimal: [] } },
      managedFiles: { [managedPath]: digest(content) },
    });
    mkdirSync(join(appRoot, 'framework/fullnet'), { recursive: true });
    mkdirSync(join(appRoot, 'src/Demo.Host.Api'), { recursive: true });
    mkdirSync(join(packageRoot, 'framework/fullnet'), { recursive: true });
    writeFileSync(join(appRoot, 'framework/fullnet', managedPath), original);
    writeFileSync(join(packageRoot, 'framework/fullnet', managedPath), next);
    writeFileSync(join(appRoot, 'src/Demo.Host.Api/Program.cs'), '// customized host');
    writeFileSync(join(appRoot, 'framework-manifest.json'), JSON.stringify(manifest('0.1.0', original)));
    writeFileSync(join(packageRoot, 'framework-manifest.json'), JSON.stringify(manifest('0.1.1', next)));
    const plan = upgradeFramework({ appRoot, packageRoot, dryRun: false });
    assert.equal(plan.targetVersion, '0.1.1');
    assert.equal(readFileSync(join(appRoot, 'framework/fullnet', managedPath), 'utf8'), next);
    assert.match(readFileSync(join(appRoot, 'src/Demo.Host.Api/Program.cs'), 'utf8'), /customized host/);
  } finally {
    rmSync(workspace, { recursive: true, force: true });
  }
});