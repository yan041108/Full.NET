#!/usr/bin/env node
/**
 * 对比并（可选）应用新版本框架分发包到已创建应用。
 */
import { createHash } from 'node:crypto';
import { existsSync, lstatSync, mkdirSync, readFileSync, readdirSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { compareFrameworkVersions, isUpgradeAllowed } from './framework-manifest-utils.mjs';

function parseArgs(argv) {
  let appRoot;
  let packageRoot;
  let dryRun = true;
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--app') {
      appRoot = argv[++i];
      continue;
    }
    if (arg === '--package') {
      packageRoot = argv[++i];
      continue;
    }
    if (arg === '--apply') {
      dryRun = false;
      continue;
    }
    if (arg === '--dry-run') {
      dryRun = true;
      continue;
    }
    if (arg === '--help' || arg === '-h') {
      console.log('Usage: node upgrade-framework.mjs --app <dir> --package <template-package> [--dry-run|--apply]');
      process.exit(0);
    }
    throw new Error('Unknown argument: ' + arg);
  }
  if (!appRoot || !packageRoot) {
    throw new Error('Both --app and --package are required');
  }
  return { appRoot: resolve(appRoot), packageRoot: resolve(packageRoot), dryRun };
}

function sha256File(path) {
  return createHash('sha256').update(readFileSync(path)).digest('hex');
}

function isApplicationOwnedPath(relativePath) {
  return relativePath.startsWith('src/')
    && !relativePath.startsWith('src/Composition/')
    || relativePath.startsWith('ui/')
    || relativePath.startsWith('packages/')
    || relativePath === 'fullnet-app.json';
}

export function planFrameworkUpgrade({ appRoot, packageRoot }) {
  const currentManifest = JSON.parse(readFileSync(join(appRoot, 'framework-manifest.json'), 'utf8'));
  const nextManifest = JSON.parse(readFileSync(join(packageRoot, 'framework-manifest.json'), 'utf8'));
  if (!isUpgradeAllowed(currentManifest.frameworkVersion, nextManifest.frameworkVersion)) {
    throw new Error('Target framework version is older than the application manifest');
  }

  const updates = [];
  const conflicts = [];
  for (const [relativePath, nextDigest] of Object.entries(nextManifest.managedFiles)) {
    const currentDigest = currentManifest.managedFiles[relativePath];
    const appFrameworkPath = join(appRoot, 'framework', 'fullnet', relativePath);
    const actualDigest = existsSync(appFrameworkPath) ? sha256File(appFrameworkPath) : null;
    if (actualDigest && actualDigest !== currentDigest && actualDigest !== nextDigest) {
      conflicts.push({ path: relativePath, reason: 'framework-managed file was customized locally' });
      continue;
    }
    if (nextDigest !== currentDigest) {
      updates.push({ path: relativePath, digest: nextDigest });
    }
  }

  for (const relativePath of Object.keys(currentManifest.managedFiles)) {
    if (isApplicationOwnedPath(relativePath)) continue;
    const appPath = join(appRoot, relativePath);
    if (!existsSync(appPath) || !lstatSync(appPath).isFile()) continue;
    const recorded = currentManifest.managedFiles[relativePath];
    const actual = sha256File(appPath);
    if (recorded && actual !== recorded) {
      conflicts.push({ path: relativePath, reason: 'application-owned file diverged from recorded digest' });
    }
  }

  return {
    currentVersion: currentManifest.frameworkVersion,
    targetVersion: nextManifest.frameworkVersion,
    dryRun: true,
    updates,
    conflicts,
    nextManifest,
  };
}

export function applyFrameworkUpgrade({ appRoot, packageRoot, plan }) {
  if (plan.conflicts.length > 0) {
    throw new Error('Cannot apply upgrade while conflicts remain');
  }
  const stagingRoot = join(dirname(appRoot), '.fullnet-upgrade-' + basename(appRoot));
  if (existsSync(stagingRoot)) {
    rmSync(stagingRoot, { recursive: true, force: true });
  }
  mkdirSync(stagingRoot, { recursive: true });
  try {
    for (const update of plan.updates) {
      const source = join(packageRoot, 'framework', 'fullnet', update.path);
      const target = join(appRoot, 'framework', 'fullnet', update.path);
      if (!existsSync(source)) {
        throw new Error('Package is missing managed file: ' + update.path);
      }
      mkdirSync(dirname(target), { recursive: true });
      writeFileSync(target, readFileSync(source));
    }
    const encoded = JSON.stringify(plan.nextManifest, null, 2) + '\n';
    writeFileSync(join(appRoot, 'framework-manifest.json'), encoded, 'utf8');
    writeFileSync(join(appRoot, 'framework', 'fullnet', 'framework-manifest.json'), encoded, 'utf8');
    for (const relative of ['pnpm-lock.yaml', 'Directory.Packages.props', 'global.json']) {
      const source = join(packageRoot, 'framework', 'fullnet', relative);
      if (existsSync(source)) {
        writeFileSync(join(appRoot, 'framework', 'fullnet', relative), readFileSync(source));
      }
    }
  } catch (error) {
    rmSync(stagingRoot, { recursive: true, force: true });
    throw error;
  }
  rmSync(stagingRoot, { recursive: true, force: true });
}

export function upgradeFramework(options) {
  const plan = planFrameworkUpgrade(options);
  plan.dryRun = options.dryRun;
  if (!options.dryRun) {
    applyFrameworkUpgrade({ ...options, plan });
  }
  return plan;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const options = parseArgs(process.argv.slice(2));
    const plan = upgradeFramework(options);
    console.log(JSON.stringify(plan, null, 2));
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  }
}
