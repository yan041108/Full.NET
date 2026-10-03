/** 提供符合 schema 3 的最小真实升级包，包含双库迁移与已注册种子。 */
import { createHash } from 'node:crypto';
import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { buildMigrationInventory, buildSeedInventory } from '../../../scripts/templates/framework-manifest-utils.mjs';
import { projectPresetComposition } from '../../../scripts/templates/project-preset-composition.mjs';
import { PRESET_MODULE_CLOSURE, VALID_PRESETS, resolvePresetModules } from '../../../scripts/templates/preset-modules.mjs';

export const digest = (value) => createHash('sha256').update(value).digest('hex');
export const composition = 'src/Composition/Full.NET.Composition/';

export function fixture(t, { projected = false } = {}) {
  const workspace = mkdtempSync(join(tmpdir(), 'fullnet-upgrade-safety-'));
  t.after(() => rmSync(workspace, { recursive: true, force: true }));
  const appRoot = join(workspace, 'app');
  const packageRoot = join(workspace, 'package');
  const oldFiles = { 'global.json': '{"sdk":"old"}', 'Directory.Packages.props': '<Project />' };
  oldFiles['src/Modules/Full.NET.Modules.Identity/Seeding/HostAdministratorSeedContributor.cs'] = 'public sealed class HostAdministratorSeedContributor {}';
  oldFiles['src/Modules/Full.NET.Modules.Identity/IdentityModule.cs'] = 'public void AddMigrationServices() { services.AddScoped<IDataSeedContributor, HostAdministratorSeedContributor>(); }';
  for (const provider of ['SqlServer', 'MySql']) {
    oldFiles[`src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/${provider}/001_Foundation.sql`] = '-- 基础结构';
  }
  if (projected) {
    for (const name of ['FullNetModuleCatalog.cs', 'FullNetModuleSelection.cs', 'Full.NET.Composition.csproj']) {
      oldFiles[composition + name] = readFileSync(composition + name, 'utf8');
    }
    for (const provider of ['SqlServer', 'MySql']) {
      oldFiles[`src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/${provider}/002_WorkflowFoundation.sql`] = '-- 流程结构';
    }
  }
  const newFiles = { ...oldFiles, 'global.json': '{"sdk":"new"}' };
  const writeTree = (root, files, version) => {
    const managedFiles = Object.fromEntries(Object.entries(files).map(([path, content]) => [path, digest(content)]));
    const manifest = { schemaVersion: 3, sourceCommit: '0'.repeat(40), frameworkVersion: version,
      presetModules: PRESET_MODULE_CLOSURE, validPresets: VALID_PRESETS,
      migrationInventory: buildMigrationInventory(managedFiles),
      seedInventory: buildSeedInventory(managedFiles, (path) => files[path], PRESET_MODULE_CLOSURE), managedFiles };
    for (const [path, content] of Object.entries(files)) {
      const target = join(root, 'framework/fullnet', path);
      mkdirSync(dirname(target), { recursive: true });
      writeFileSync(target, content);
    }
    saveManifest(root, manifest);
  };
  writeTree(appRoot, oldFiles, '0.1.0');
  writeTree(packageRoot, newFiles, '0.1.1');
  if (projected) {
    const preset = typeof projected === 'string' ? projected : 'minimal';
    projectPresetComposition(appRoot, preset, resolvePresetModules(preset));
    writeFileSync(join(appRoot, 'fullnet-app.json'), JSON.stringify({ preset }));
  }
  return { appRoot, packageRoot, workspace, oldFiles, newFiles };
}

export function saveManifest(root, manifest) {
  const content = JSON.stringify(manifest, null, 2) + '\n';
  writeFileSync(join(root, 'framework-manifest.json'), content);
  writeFileSync(join(root, 'framework/fullnet/framework-manifest.json'), content);
}
