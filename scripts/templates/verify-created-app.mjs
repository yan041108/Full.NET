#!/usr/bin/env node
/**
 * 校验由 fullnet-app 模板创建的应用目录结构。
 */
import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { resolvePresetModules, validateOwnerKey } from './preset-modules.mjs';

const REQUIRED_FILES = [
  'framework-manifest.json',
  'appsettings.json',
  'fullnet-app.json',
  'pnpm-lock.yaml',
  'ui/admin/package.json',
  'packages/client-contracts/package.json',
  'packages/admin-i18n/package.json',
  'packages/admin-form-designer/package.json',
  'packages/design-tokens/package.json',
];

export function verifyCreatedApp(appRoot) {
  const root = resolve(appRoot);
  const errors = [];
  const configurationFiles = ['appsettings.json'];

  for (const relativeFile of REQUIRED_FILES) {
    const absolutePath = join(root, relativeFile);
    if (!isRegularFile(absolutePath)) {
      errors.push('Missing required file: ' + relativeFile);
    }
  }

  const sourceRoot = join(root, 'src');
  const hosts = existsSync(sourceRoot)
    ? readdirSync(sourceRoot, { withFileTypes: true })
      .filter((entry) => entry.isDirectory() && entry.name.endsWith('.Host.Api'))
    : [];
  if (hosts.length !== 1) {
    errors.push('Created app must contain exactly one application API host');
  } else {
    const hostRoot = join(sourceRoot, hosts[0].name);
    configurationFiles.push('src/' + hosts[0].name + '/appsettings.json');
    for (const hostFile of ['Program.cs', hosts[0].name + '.csproj', 'appsettings.json']) {
      if (!isRegularFile(join(hostRoot, hostFile))) {
        errors.push('Missing required host file: ' + join('src', hosts[0].name, hostFile));
      }
    }
    // 应用清单必须与唯一 API 宿主同名，其他应用的 Composition 不能补位。
    const compositionName = hosts[0].name.slice(0, -'.Host.Api'.length) + '.Composition';
    for (const compositionFile of [compositionName + '.csproj', 'ApplicationModuleCatalog.cs']) {
      if (!isRegularFile(join(sourceRoot, compositionName, compositionFile))) {
        errors.push('Missing required composition file: ' + join('src', compositionName, compositionFile));
      }
    }
  }

  const configurations = [];
  for (const relativePath of configurationFiles) {
    const configPath = join(root, relativePath);
    if (!isRegularFile(configPath)) continue;
    try {
      const config = JSON.parse(readFileSync(configPath, 'utf8'));
      if (!config?.FullNet?.Modules) {
        errors.push(relativePath + ' must define FullNet:Modules');
      }
      configurations.push({ relativePath, preset: config?.FullNet?.Modules?.Preset, provider: config?.Database?.Provider });
    } catch (error) {
      errors.push(relativePath + ' is not valid JSON: ' + (error instanceof Error ? error.message : String(error)));
    }
  }

  const profilePath = join(root, 'fullnet-app.json');
  let profilePreset;
  let profileProvider;
  if (existsSync(profilePath)) {
    try {
      const profile = JSON.parse(readFileSync(profilePath, 'utf8'));
      profilePreset = profile.preset;
      profileProvider = profile.databaseProvider;
      validateOwnerKey(profile.ownerKey);
      resolvePresetModules(profile.preset);
      if (!['sqlserver', 'mysql'].includes(profileProvider)) {
        errors.push('fullnet-app.json has an invalid databaseProvider');
      }
    } catch (error) {
      errors.push('fullnet-app.json is invalid: ' + (error instanceof Error ? error.message : String(error)));
    }
  }

  // 文件中的预设和数据库必须与应用冻结档案一致；环境覆盖由运行期诊断另行验证。
  for (const { relativePath, preset, provider } of configurations) {
    if (profilePreset !== undefined && preset !== profilePreset) {
      errors.push(relativePath + ' module preset does not match fullnet-app.json');
    }
    if (['sqlserver', 'mysql'].includes(profileProvider) && provider !== profileProvider) {
      errors.push(relativePath + ' database provider does not match fullnet-app.json');
    }
  }

  const manifestPath = join(root, 'framework-manifest.json');
  if (existsSync(manifestPath)) {
    try {
      const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
      if (!manifest.frameworkVersion) {
        errors.push('framework-manifest.json must include frameworkVersion');
      }
      if (!manifest.managedFiles || typeof manifest.managedFiles !== 'object') {
        errors.push('framework-manifest.json must include managedFiles');
      }
      if (![1, 2, 3].includes(manifest.schemaVersion)) {
        errors.push('framework-manifest.json has an unsupported schemaVersion');
      }
      if (manifest.schemaVersion >= 2) {
        const status = manifest.migrationInventory?.selectionStatus;
        const preset = profilePreset ?? manifest.projectedPreset;
        const expectedStatus = preset ? `preset-${preset}` : 'unscoped';
        if (status !== expectedStatus
          || !Array.isArray(manifest.migrationInventory?.scripts)
          || manifest.migrationInventory.scripts.length === 0) {
          errors.push('framework-manifest.json must include a preset-scoped migration inventory for the application profile');
        }
      }
      if (manifest.schemaVersion >= 3 && (!Array.isArray(manifest.seedInventory?.contributors)
        || manifest.seedInventory.contributors.length === 0
        || !manifest.seedInventory.presets || !Array.isArray(manifest.seedInventory.presets[profilePreset]))) {
        errors.push('framework-manifest.json must include the selected seed inventory');
      }
      if (manifest.projectedPreset && profilePreset && manifest.projectedPreset !== profilePreset) {
        errors.push('framework-manifest.json projectedPreset does not match fullnet-app.json');
      }
    } catch (error) {
      errors.push('framework-manifest.json is not valid JSON: ' + (error instanceof Error ? error.message : String(error)));
    }
  }

  return {
    ok: errors.length === 0,
    errors,
  };
}

function isRegularFile(path) {
  try {
    return statSync(path).isFile();
  } catch {
    return false;
  }
}

function isMainModule() {
  const entry = process.argv[1] ? resolve(process.argv[1]) : '';
  return entry === fileURLToPath(import.meta.url);
}

if (isMainModule() && process.argv[2]) {
  const result = verifyCreatedApp(process.argv[2]);
  if (!result.ok) {
    for (const error of result.errors) {
      console.error(error);
    }
    process.exit(1);
  }
  console.log('Created app structure is valid: ' + resolve(process.argv[2]));
}
