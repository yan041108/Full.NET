import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

async function exists(relativePath) {
  try {
    await access(path.join(repositoryRoot, relativePath));
    return true;
  } catch {
    return false;
  }
}

test('foundation preset release docs and migrations exist', async () => {
  assert.equal(await exists('docs/operations/application-upgrade.md'), true);
  assert.equal(await exists('docs/operations/application-recovery.md'), true);
  assert.equal(await exists('docs/verification/f16-release-checklist.md'), true);
  assert.equal(
    await exists(
      'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/226_TenancyTenantSubscription.sql'
    ),
    true
  );
  assert.equal(
    await exists(
      'src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/226_TenancyTenantSubscription.sql'
    ),
    true
  );
});

test('FullNetModuleSelection lists Webhooks official module', async () => {
  const selection = await read(
    'src/Composition/Full.NET.Composition/FullNetModuleSelection.cs'
  );
  assert.match(selection, /"Webhooks"/);
});

test('Enterprise preset closure includes workflow delivery and EnterpriseRequest', async () => {
  const selection = await read(
    'src/Composition/Full.NET.Composition/FullNetModuleSelection.cs'
  );
  assert.match(selection, /EnterprisePresetModuleNames/);
  const block = selection.slice(
    selection.indexOf('EnterprisePresetModuleNames'),
    selection.indexOf('/// <summary>', selection.indexOf('EnterprisePresetModuleNames') + 1)
  );
  assert.match(block, /"EnterpriseRequest"/);
  assert.match(block, /"Workflow"/);
  assert.match(block, /"ImportExport"/);
  const options = await read(
    'src/Composition/Full.NET.Composition/FullNetModuleSelectionOptions.cs'
  );
  assert.match(options, /Presets\.Enterprise|Enterprise = "Enterprise"/);
});

test('Saas preset closure includes Payments and Webhooks', async () => {
  const selection = await read(
    'src/Composition/Full.NET.Composition/FullNetModuleSelection.cs'
  );
  assert.match(selection, /SaasPresetModuleNames/);
  const saasBlock = selection.slice(
    selection.indexOf('SaasPresetModuleNames'),
    selection.indexOf('/// <summary>', selection.indexOf('SaasPresetModuleNames') + 1)
  );
  assert.match(saasBlock, /"Payments"/);
  assert.match(saasBlock, /"Webhooks"/);
  const options = await read(
    'src/Composition/Full.NET.Composition/FullNetModuleSelectionOptions.cs'
  );
  assert.match(options, /Presets\.Saas|Saas = "Saas"/);
});

test('release script keeps migrator worker api ordering', async () => {
  const script = await read('eng/deploy/Invoke-FullNetRelease.ps1');
  const migrator = script.indexOf('fullnet-migrator');
  const worker = script.indexOf('fullnet-worker');
  const api = script.indexOf('fullnet-api');
  assert.ok(migrator >= 0 && worker > migrator && api > worker);
});
