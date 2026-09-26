import assert from 'node:assert/strict';
import { readdirSync } from 'node:fs';
import { join, resolve } from 'node:path';
import test from 'node:test';
import { buildMigrationInventory } from '../../scripts/templates/framework-manifest-utils.mjs';
import { buildPresetMigrationInventory, inferMigrationModuleOwner } from '../../scripts/templates/migration-script-modules.mjs';
import { resolvePresetModules } from '../../scripts/templates/preset-modules.mjs';

const MIGRATIONS = resolve('src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer');

test('inferMigrationModuleOwner resolves every published script', () => {
  const names = readdirSync(MIGRATIONS).filter((name) => name.endsWith('.sql')).sort();
  for (const name of names) {
    assert.doesNotThrow(() => inferMigrationModuleOwner(name), name);
  }
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
