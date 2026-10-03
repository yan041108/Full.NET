import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import test from 'node:test';
import { assertArchiveEntryModes, assertCleanBundleInputStatus, assertManagedPath, areBundleInputsClean, buildSourceBundle } from '../../scripts/templates/build-source-bundle.mjs';
import { buildMigrationInventory, buildSeedInventory } from '../../scripts/templates/framework-manifest-utils.mjs';

const skipBundleIntegration = areBundleInputsClean()
  ? false
  : 'source bundle inputs have uncommitted changes';

test('source bundle rejects a dirty source tree before assigning a commit', () => {
  assert.throws(() => assertCleanBundleInputStatus(' M src/Modules/Full.NET.Modules.Identity/IdentityModule.cs'), /uncommitted/);
  assert.doesNotThrow(() => assertCleanBundleInputStatus(''));
});

test('source bundle rejects symlinks and submodules in committed inputs', () => {
  const digest = '0'.repeat(40);
  assert.throws(() => assertArchiveEntryModes(`120000 blob ${digest}\tsrc/link\0`), /unsupported git entry mode/);
  assert.throws(() => assertArchiveEntryModes(`160000 commit ${digest}\tsrc/submodule\0`), /unsupported git entry mode/);
  assert.doesNotThrow(() => assertArchiveEntryModes(`100644 blob ${digest}\tsrc/file.cs\0`));
});

test('build-source-bundle writes manifest with sha256 managed files', { skip: skipBundleIntegration }, async () => {
  const output = mkdtempSync(join(tmpdir(), 'fullnet-bundle-'));
  try {
    const { bundleRoot, manifest } = buildSourceBundle({ output });
    const manifestPath = join(bundleRoot, 'framework-manifest.json');
    const onDisk = JSON.parse(readFileSync(manifestPath, 'utf8'));

    assert.equal(manifest.schemaVersion, 3);
    assert.match(manifest.sourceCommit, /^[0-9a-f]{40}$/);
    assert.equal(manifest.frameworkVersion, '0.1.0');
    assert.ok(Object.keys(manifest.managedFiles).length > 0);
    assert.deepEqual(onDisk.managedFiles, manifest.managedFiles);
    assert.equal(existsSync(join(bundleRoot, 'src/Hosts/Full.NET.Host.Api/App_Data')), false);
    assert.ok(existsSync(join(bundleRoot, 'ui/admin/package.json')));
    assert.ok(existsSync(join(bundleRoot, 'packages/client-contracts/package.json')));
    assert.ok(existsSync(join(bundleRoot, 'packages/admin-i18n/package.json')));
    assert.ok(existsSync(join(bundleRoot, 'packages/admin-form-designer/package.json')));
    assert.ok(existsSync(join(bundleRoot, 'packages/design-tokens/package.json')));
    assert.ok(existsSync(join(bundleRoot, 'pnpm-lock.yaml')));
    assert.equal(manifest.migrationInventory.selectionStatus, 'unscoped');
    assert.equal(manifest.migrationInventory.scripts.length, 240);
    const entitlementSeed = manifest.seedInventory.contributors.find((entry) => entry.name === 'TenancyEntitlementCatalogBaselineSeedContributor');
    assert.ok(entitlementSeed, 'commercial feature catalog must be included in the source bundle');
    assert.equal(entitlementSeed.module, 'Tenancy');
    const developmentMembershipSeed = manifest.seedInventory.contributors.find(
      (entry) => entry.name === 'DevelopmentBootstrapAdminTenantMembershipSeedContributor');
    assert.ok(developmentMembershipSeed, 'first development seed must include local admin membership');
    assert.equal(developmentMembershipSeed.module, 'Identity');
    for (const [preset, modules] of Object.entries(manifest.presetModules)) {
      const expectedSeeds = manifest.seedInventory.contributors
        .filter((entry) => modules.includes(entry.module)).map((entry) => entry.path).sort();
      assert.deepEqual(manifest.seedInventory.presets[preset], expectedSeeds, preset + ' seed closure');
    }
    for (const script of manifest.migrationInventory.scripts) {
      for (const provider of ['SqlServer', 'MySql']) {
        const relative = `src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/${provider}/${script.name}`;
        assert.equal(script.providers[provider], manifest.managedFiles[relative]);
      }
    }

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

test('migration inventory rejects a missing provider counterpart', () => {
  const prefix = 'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/';
  assert.throws(() => buildMigrationInventory({ [`${prefix}SqlServer/001_Foundation.sql`]: '0'.repeat(64) }), /missing MySql counterpart/);
});

test('seed inventory follows migration registrations and selected module closure', () => {
  const path = 'src/Modules/Full.NET.Modules.Identity/Seeding/HostAdministratorSeedContributor.cs';
  const modulePath = 'src/Modules/Full.NET.Modules.Identity/IdentityModule.cs';
  const managedFiles = { [path]: 'a'.repeat(64), [modulePath]: 'b'.repeat(64) };
  const source = { [path]: 'public sealed class HostAdministratorSeedContributor {}',
    [modulePath]: 'public void AddMigrationServices() { services.AddScoped<IDataSeedContributor, HostAdministratorSeedContributor>(); }' };
  const presets = { minimal: ['Identity'], empty: ['Settings'] };
  assert.deepEqual(buildSeedInventory(managedFiles, (file) => source[file], presets), {
    contributors: [{ module: 'Identity', name: 'HostAdministratorSeedContributor', path }],
    presets: { minimal: [path], empty: [] },
  });
  assert.throws(() => buildSeedInventory(managedFiles, (file) => source[file], { minimal: ['Settings'] }), /not selected by any preset/);
  source[modulePath] = 'public void AddMigrationServices() {}';
  assert.throws(() => buildSeedInventory(managedFiles, (file) => source[file], presets), /not registered/);
  source[modulePath] = 'public void AddMigrationServices() { /* IDataSeedContributor, HostAdministratorSeedContributor> */ }';
  assert.throws(() => buildSeedInventory(managedFiles, (file) => source[file], presets), /not registered/);
  source[modulePath] = 'public void AddMigrationServices() {\n#if false\n services.AddScoped<IDataSeedContributor, HostAdministratorSeedContributor>();\n#endif\n}';
  assert.throws(() => buildSeedInventory(managedFiles, (file) => source[file], presets), /Conditional seed registration/);
  source[modulePath] = 'public void AddMigrationServices() { services.AddScoped<IDataSeedContributor, HostAdministratorSeedContributor>(); services.AddScoped<IDataSeedContributor, MissingSeedContributor>(); }';
  assert.throws(() => buildSeedInventory(managedFiles, (file) => source[file], presets), /no managed source/);
});

test('build-source-bundle rejects bad managed paths', () => {
  assert.throws(() => assertManagedPath('../escape.txt'), /outside bundle root/);
  assert.throws(() => assertManagedPath('src/obj/cache.txt'), /excluded path/);
});

test('build-source-bundle preserves an existing output directory', { skip: skipBundleIntegration }, () => {
  const output = mkdtempSync(join(tmpdir(), 'fullnet-bundle-owned-'));
  const marker = join(output, 'keep.txt');
  try {
    writeFileSync(marker, 'user content');
    assert.throws(() => buildSourceBundle({ output }), /must be empty/);
    assert.equal(readFileSync(marker, 'utf8'), 'user content');
  } finally {
    rmSync(output, { recursive: true, force: true });
  }
});

test('build-source-bundle contains the API host and its project reference closure', { skip: skipBundleIntegration }, () => {
  const output = mkdtempSync(join(tmpdir(), 'fullnet-bundle-closure-'));
  try {
    const { bundleRoot } = buildSourceBundle({ output });
    const hostProject = join(bundleRoot, 'src/Hosts/Full.NET.Host.Api/Full.NET.Host.Api.csproj');
    assert.ok(existsSync(hostProject), 'the created application must contain the framework API host');
    assert.ok(existsSync(join(dirname(hostProject), 'Program.cs')));
    assert.ok(existsSync(join(bundleRoot, 'Directory.Build.targets')), 'the project build targets must ship with the source');

    const visited = new Set();
    const visit = (projectPath) => {
      const absolutePath = resolve(projectPath);
      if (visited.has(absolutePath)) return;
      visited.add(absolutePath);
      const xml = readFileSync(absolutePath, 'utf8');
      for (const match of xml.matchAll(/<ProjectReference\s+Include="([^"]+)"/g)) {
        const dependency = resolve(dirname(absolutePath), match[1].replaceAll('\\', '/'));
        assert.ok(existsSync(dependency), `missing project dependency: ${dependency}`);
        visit(dependency);
      }
    };
    visit(hostProject);
  } finally {
    rmSync(output, { recursive: true, force: true });
  }
});
