import assert from 'node:assert/strict';
import { readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import test from 'node:test';
import { buildMigrationInventory } from '../../scripts/templates/framework-manifest-utils.mjs';
import { buildPresetMigrationInventory, inferMigrationModuleOwner } from '../../scripts/templates/migration-script-modules.mjs';
import { resolvePresetModules, VALID_PRESETS } from '../../scripts/templates/preset-modules.mjs';

const MIGRATIONS = resolve('src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer');

test('preview cancellation and lease migrations follow their platform table owner', () => {
  for (const name of ['261_DocumentPreviewTaskCancellation.sql', '262_DocumentPreviewTaskLease.sql']) {
    assert.equal(inferMigrationModuleOwner(name), 'Platform');
    const inventory = { scripts: [{ name: '001_Foundation.sql' }, { name }] };
    for (const preset of ['minimal', 'platform', 'enterprise']) {
      const selected = buildPresetMigrationInventory(inventory, preset, resolvePresetModules(preset)).scripts;
      assert.equal(selected.some(script => script.name === name), preset !== 'minimal');
    }
  }
});

test('document write guards follow the platform migration closure', () => {
  for (const name of ['250_DocumentItemWriteGuard.sql', '366_DocumentTaxonomyWriteGuard.sql']) {
    assert.equal(inferMigrationModuleOwner(name), 'Platform');
    const inventory = { scripts: [{ name: '001_Foundation.sql' }, { name }] };
    assert.ok(!buildPresetMigrationInventory(inventory, 'minimal', resolvePresetModules('minimal')).scripts.some(script => script.name === name));
    assert.ok(buildPresetMigrationInventory(inventory, 'platform', resolvePresetModules('platform')).scripts.some(script => script.name === name));
  }
});

test('inferMigrationModuleOwner resolves every published script', () => {
  const names = readdirSync(MIGRATIONS).filter((name) => name.endsWith('.sql')).sort();
  for (const name of names) {
    assert.doesNotThrow(() => inferMigrationModuleOwner(name), name);
  }
});

test('new delivery migrations follow preset closure and cross-module index prerequisites', () => {
  const names = ['001_Foundation.sql', '351_PersonalScheduleCompletion.sql', '352_MessagingCdcHeartbeat.sql',
    '354_OwnTaskCursorIndexes.sql', '355_SettingsQuerySchemes.sql', '356_DocumentRecentSearchIndex.sql',
    '357_ImportProfilesAndInputSnapshots.sql', '358_ImportTaskCancellation.sql', '359_ReportingExportTaskCancellation.sql'];
  const full = { scripts: names.map(name => ({ name })) };
  const selected = modules => buildPresetMigrationInventory(full, 'focused', modules).scripts.map(item => item.name);
  assert.deepEqual(selected(resolvePresetModules('minimal')), ['001_Foundation.sql', '355_SettingsQuerySchemes.sql']);
  const platform = ['001_Foundation.sql', '351_PersonalScheduleCompletion.sql', '352_MessagingCdcHeartbeat.sql',
    '355_SettingsQuerySchemes.sql', '356_DocumentRecentSearchIndex.sql'];
  assert.deepEqual(selected(resolvePresetModules('platform')), platform);
  assert.deepEqual(selected(resolvePresetModules('saas')), platform);
  assert.deepEqual(selected(resolvePresetModules('enterprise')), names);
  for (const modules of [['ImportExport'], ['Reporting']]) assert.ok(!selected(modules).includes('354_OwnTaskCursorIndexes.sql'));
  assert.ok(selected(['ImportExport', 'Reporting']).includes('354_OwnTaskCursorIndexes.sql'));
});

test('knowledge migrations retain Ai ownership and stay outside generated presets for both providers', () => {
  for (const provider of ['SqlServer', 'MySql']) {
    const migrationDirectory = resolve('src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations', provider);
    const names = readdirSync(migrationDirectory).filter((name) => name.endsWith('.sql'));
    const aiNames = names.filter((name) => /^\d+_Ai.+\.sql$/.test(name));
    assert.ok(aiNames.includes('240_AiKnowledgeBase.sql'), provider);
    assert.ok(aiNames.includes('255_AiKnowledgeDocxPersistence.sql'), provider);
    for (const name of aiNames) assert.equal(inferMigrationModuleOwner(name), 'Ai', provider + '/' + name);
    const full = buildMigrationInventory(Object.fromEntries(names.flatMap((name) => [
      [`src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/${name}`, '0'.repeat(64)],
      [`src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/${name}`, '0'.repeat(64)],
    ])));
    for (const preset of VALID_PRESETS) {
      const inventory = buildPresetMigrationInventory(full, preset, resolvePresetModules(preset));
      assert.ok(!inventory.scripts.some(({ name }) => aiNames.includes(name)), provider + '/' + preset);
    }
  }
});

test('AI knowledge migrations retain AI ownership and stay outside preset inventories', () => {
  assert.equal(inferMigrationModuleOwner('240_AiKnowledgeBase.sql'), 'Ai');
  assert.equal(inferMigrationModuleOwner('241_AiKnowledgeMember.sql'), 'Ai');
  assert.equal(inferMigrationModuleOwner('242_AiKnowledgeDocument.sql'), 'Ai');
  const full = { scripts: [{ name: '001_Foundation.sql' }, { name: '240_AiKnowledgeBase.sql' }] };
  const minimal = buildPresetMigrationInventory(full, 'minimal', resolvePresetModules('minimal'));
  assert.deepEqual(minimal.scripts, [{ name: '001_Foundation.sql' }]);
});

test('preset-minimal migration inventory is smaller than unscoped inventory', () => {
  const managedFiles = Object.fromEntries(
    readdirSync(MIGRATIONS)
      .filter((name) => name.endsWith('.sql'))
      .flatMap((name) => [
        [`src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/${name}`, '0'.repeat(64)],
        [`src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/${name}`, '0'.repeat(64)],
      ]),
  );
  const full = buildMigrationInventory(managedFiles);
  const minimal = buildPresetMigrationInventory(full, 'minimal', resolvePresetModules('minimal'));
  assert.equal(minimal.selectionStatus, 'preset-minimal');
  assert.ok(minimal.scripts.length < full.scripts.length);
  assert.ok(minimal.scripts.length > 40);
  assert.ok(!minimal.scripts.some(({ name }) => name.startsWith('102_Workflow')));
});

test('sample migrations cannot enter an official preset migration inventory', () => {
  for (const name of ['003_EnterpriseRequestImportRows.sql', '005_EnterpriseRequestApprovalSubmission.sql', '006_EnterpriseRequestApprovalRepair.sql']) {
    assert.throws(() => inferMigrationModuleOwner(name), /Unable to infer migration module owner/);
  }
});

test('media binding generation migration belongs to every official foundation preset', () => {
  const name = '367_MediaBindingClaimGeneration.sql';
  assert.equal(inferMigrationModuleOwner(name), 'Shared');
  for (const preset of ['minimal', 'platform', 'saas', 'enterprise']) {
    const modules = resolvePresetModules(preset);
    assert.ok(modules.includes('Identity') && modules.includes('Tenancy'));
    assert.ok(buildPresetMigrationInventory({ scripts: [{ name }] }, preset, modules).scripts.some(script => script.name === name));
  }
});
