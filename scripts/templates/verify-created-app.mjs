#!/usr/bin/env node
/**
 * 校验由 fullnet-app 模板创建的应用目录结构。
 */
import { existsSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const REQUIRED_HOSTS = [
  'src/App.Host.Api',
];

const REQUIRED_FILES = [
  'framework-manifest.json',
  'appsettings.json',
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

  for (const relativeHost of REQUIRED_HOSTS) {
    const absolutePath = join(root, relativeHost);
    if (!existsSync(absolutePath)) {
      errors.push('Missing required host directory: ' + relativeHost);
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