#!/usr/bin/env node
/**
 * 校验由 fullnet-app 模板创建的应用目录结构。
 */
import { existsSync, readdirSync, readFileSync } from 'node:fs';
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

  for (const relativeFile of REQUIRED_FILES) {
    const absolutePath = join(root, relativeFile);
    if (!existsSync(absolutePath)) {
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
    for (const hostFile of ['Program.cs', hosts[0].name + '.csproj', 'appsettings.json']) {
      if (!existsSync(join(hostRoot, hostFile))) {
        errors.push('Missing required host file: ' + join('src', hosts[0].name, hostFile));
      }
    }
  }

  const appsettingsPath = join(root, 'appsettings.json');
  if (existsSync(appsettingsPath)) {
    let config;
    try {
      config = JSON.parse(readFileSync(appsettingsPath, 'utf8'));
    } catch (error) {
      errors.push('appsettings.json is not valid JSON: ' + (error instanceof Error ? error.message : String(error)));
      config = null;
    }

    if (config && !config.FullNet?.Modules) {
      errors.push('appsettings.json must define FullNet:Modules');
    }
  }

  const profilePath = join(root, 'fullnet-app.json');
  let profilePreset;
  if (existsSync(profilePath)) {
    try {
      const profile = JSON.parse(readFileSync(profilePath, 'utf8'));
      profilePreset = profile.preset;
      validateOwnerKey(profile.ownerKey);
      resolvePresetModules(profile.preset);
      if (!['sqlserver', 'mysql'].includes(profile.databaseProvider)) {
        errors.push('fullnet-app.json has an invalid databaseProvider');
      }
    } catch (error) {
      errors.push('fullnet-app.json is invalid: ' + (error instanceof Error ? error.message : String(error)));
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
