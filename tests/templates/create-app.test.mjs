import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';
import { createApp } from '../../scripts/templates/create-app.mjs';
import { areBundleInputsClean } from '../../scripts/templates/build-source-bundle.mjs';
import { buildAppTemplate } from '../../scripts/templates/build-app-template.mjs';
import { buildMigrationInventory } from '../../scripts/templates/framework-manifest-utils.mjs';
import { PRESET_MODULE_CLOSURE } from '../../scripts/templates/preset-modules.mjs';

const skipBundleIntegration = areBundleInputsClean()
  ? false
  : 'source bundle inputs have uncommitted changes';

test('create-app rejects invalid owner key before creating output', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const output = join(parent, 'app');
    assert.throws(() => createApp({ packageRoot: parent, output, name: 'Demo', ownerKey: 'acme-team' }), /Invalid owner-key/);
    assert.equal(existsSync(output), false);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app preserves an existing output directory', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  const marker = join(parent, 'keep.txt');
  try {
    writeFileSync(marker, 'user content');
    assert.throws(() => createApp({ packageRoot: parent, output: parent, name: 'Demo', ownerKey: 'acme' }), /must not exist/);
    assert.equal(readFileSync(marker, 'utf8'), 'user content');
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app preserves even an existing empty output directory', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const output = join(parent, 'app');
    mkdirSync(output);
    assert.throws(() => createApp({ packageRoot: parent, output, name: 'Demo', ownerKey: 'acme' }), /must not exist/);
    assert.deepEqual(readdirSync(output), []);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app rejects a modified managed framework file before creating output', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const packageRoot = join(parent, 'package');
    const bundleRoot = join(packageRoot, 'framework', 'fullnet');
    mkdirSync(bundleRoot, { recursive: true });
    const manifest = JSON.stringify({ managedFiles: { 'global.json': '0'.repeat(64) } });
    writeFileSync(join(packageRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'global.json'), '{}');
    const output = join(parent, 'app');
    assert.throws(() => createApp({ packageRoot, output, name: 'Demo', ownerKey: 'acme' }), /digest mismatch/);
    assert.equal(existsSync(output), false);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app rejects unlisted framework files before creating output', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const packageRoot = join(parent, 'package');
    const bundleRoot = join(packageRoot, 'framework', 'fullnet');
    mkdirSync(bundleRoot, { recursive: true });
    const content = '{}';
    const digest = createHash('sha256').update(content).digest('hex');
    const manifest = JSON.stringify({ managedFiles: { 'global.json': digest } });
    writeFileSync(join(packageRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'global.json'), content);
    writeFileSync(join(bundleRoot, 'Directory.Build.targets'), '<Project />');
    const output = join(parent, 'app');
    assert.throws(() => createApp({ packageRoot, output, name: 'Demo', ownerKey: 'acme' }), /Unexpected framework file/);
    assert.equal(existsSync(output), false);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app rejects a modified Vue skeleton before creating output', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const packageRoot = join(parent, 'package');
    const bundleRoot = join(packageRoot, 'framework', 'fullnet');
    mkdirSync(join(bundleRoot, 'ui', 'admin'), { recursive: true });
    mkdirSync(join(packageRoot, 'ui', 'admin'), { recursive: true });
    const content = '{}';
    const digest = createHash('sha256').update(content).digest('hex');
    const manifest = JSON.stringify({ managedFiles: { 'ui/admin/package.json': digest } });
    writeFileSync(join(packageRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'ui/admin/package.json'), content);
    writeFileSync(join(packageRoot, 'ui/admin/package.json'), '{"tampered":true}');
    const output = join(parent, 'app');
    assert.throws(() => createApp({ packageRoot, output, name: 'Demo', ownerKey: 'acme' }), /Frontend skeleton digest mismatch/);
    assert.equal(existsSync(output), false);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app rejects a migration inventory that omits a managed script', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const packageRoot = join(parent, 'package');
    const bundleRoot = join(packageRoot, 'framework', 'fullnet');
    const prefix = 'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations';
    for (const provider of ['SqlServer', 'MySql']) {
      mkdirSync(join(bundleRoot, prefix, provider), { recursive: true });
      writeFileSync(join(bundleRoot, prefix, provider, '001_Foundation.sql'), 'SELECT 1;');
    }
    const digest = createHash('sha256').update('SELECT 1;').digest('hex');
    const managedFiles = Object.fromEntries(['SqlServer', 'MySql'].map((provider) =>
      [`${prefix}/${provider}/001_Foundation.sql`, digest]));
    const manifest = JSON.stringify({ managedFiles, migrationInventory: { selectionStatus: 'unscoped', scripts: [] } });
    writeFileSync(join(packageRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'framework-manifest.json'), manifest);
    const output = join(parent, 'app');
    assert.throws(() => createApp({ packageRoot, output, name: 'Demo', ownerKey: 'acme' }), /migration inventory does not match/);
    assert.equal(existsSync(output), false);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app requires migration inventory for schema version two', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const packageRoot = join(parent, 'package');
    const bundleRoot = join(packageRoot, 'framework', 'fullnet');
    mkdirSync(bundleRoot, { recursive: true });
    const content = '{}';
    const digest = createHash('sha256').update(content).digest('hex');
    const manifest = JSON.stringify({ schemaVersion: 2, managedFiles: { 'global.json': digest } });
    writeFileSync(join(packageRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'global.json'), content);
    const output = join(parent, 'app');
    assert.throws(() => createApp({ packageRoot, output, name: 'Demo', ownerKey: 'acme' }), /migration inventory is missing/);
    assert.equal(existsSync(output), false);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app rejects a seed inventory that omits registered contributors', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-seed-inventory-'));
  try {
    const packageRoot = join(parent, 'package');
    const bundleRoot = join(packageRoot, 'framework', 'fullnet');
    const modulePath = 'src/Modules/Full.NET.Modules.Identity/IdentityModule.cs';
    const seedPath = 'src/Modules/Full.NET.Modules.Identity/Seeding/HostAdministratorSeedContributor.cs';
    const files = {
      [modulePath]: 'public void AddMigrationServices() { services.AddScoped<IDataSeedContributor, HostAdministratorSeedContributor>(); }',
      [seedPath]: 'internal sealed class HostAdministratorSeedContributor {}',
      'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/001_Foundation.sql': 'SELECT 1;',
      'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/001_Foundation.sql': 'SELECT 1;',
    };
    const managedFiles = {};
    for (const [path, content] of Object.entries(files)) {
      const absolute = join(bundleRoot, path);
      mkdirSync(join(absolute, '..'), { recursive: true });
      writeFileSync(absolute, content);
      managedFiles[path] = createHash('sha256').update(content).digest('hex');
    }
    const manifest = JSON.stringify({ schemaVersion: 3, managedFiles,
      presetModules: PRESET_MODULE_CLOSURE, migrationInventory: buildMigrationInventory(managedFiles),
      seedInventory: { contributors: [], presets: {} } });
    writeFileSync(join(packageRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'framework-manifest.json'), manifest);
    const output = join(parent, 'app');
    assert.throws(() => createApp({ packageRoot, output, name: 'Demo', ownerKey: 'acme' }), /seed inventory does not match/);
    assert.equal(existsSync(output), false);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app removes staging after template installation fails', () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const packageRoot = join(parent, 'package');
    const bundleRoot = join(packageRoot, 'framework', 'fullnet');
    mkdirSync(bundleRoot, { recursive: true });
    const content = '{}';
    const digest = createHash('sha256').update(content).digest('hex');
    const manifest = JSON.stringify({ managedFiles: { 'global.json': digest } });
    writeFileSync(join(packageRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'framework-manifest.json'), manifest);
    writeFileSync(join(bundleRoot, 'global.json'), content);
    const output = join(parent, 'app');
    assert.throws(() => createApp({ packageRoot, output, name: 'Demo', ownerKey: 'acme' }), /dotnet new fullnet-app failed/);
    assert.equal(existsSync(output), false);
    assert.deepEqual(readdirSync(parent).filter((entry) => entry.startsWith('.fullnet-create-')), []);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});

test('create-app accepts the packaged Vue skeleton and writes the selected proxy port', { skip: skipBundleIntegration }, () => {
  const parent = mkdtempSync(join(tmpdir(), 'fullnet-create-app-'));
  try {
    const { templateRoot } = buildAppTemplate({ output: join(parent, 'package') });
    const output = join(parent, 'app');
    createApp({ packageRoot: templateRoot, output, name: 'Demo', ownerKey: 'acme', httpPort: 5500 });
    assert.match(readFileSync(join(output, 'ui/admin/vite.config.ts'), 'utf8'), /http:\/\/localhost:5500/);
    assert.match(readFileSync(join(output, 'ui/admin/.env.example'), 'utf8'), /http:\/\/localhost:5500/);
  } finally {
    rmSync(parent, { recursive: true, force: true });
  }
});
