#!/usr/bin/env node
/**
 * 对比并（可选）应用新版本框架分发包到已创建应用。
 */
import { existsSync, lstatSync, readFileSync } from 'node:fs';
import { isAbsolute, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { isDeepStrictEqual } from 'node:util';
import { buildPresetMigrationInventory, isUpgradeAllowed } from './framework-manifest-utils.mjs';
import { projectCompositionSource } from './project-preset-composition.mjs';
import { resolvePresetModules } from './preset-modules.mjs';
import { assertUnlinkedPath, hashBytes, readFrameworkSnapshot, readUpgradeManifest, readUpgradePackage } from './framework-upgrade-integrity.mjs';
import { assertNoPendingUpgrade, commitFrameworkUpgrade } from './framework-upgrade-store.mjs';

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

/** 预设是应用已冻结的选择，包升级不得重新启用未选择模块。 */
function prepareUpgrade({ appRoot, packageRoot, allowPending = false }) {
  appRoot = resolve(appRoot);
  packageRoot = resolve(packageRoot);
  const distance = relative(appRoot, packageRoot);
  const reverseDistance = relative(packageRoot, appRoot);
  const isOutside = (path) => isAbsolute(path) || path === '..' || path.startsWith('..' + sep);
  if (!distance || !isOutside(distance) || !isOutside(reverseDistance)) {
    throw new Error('Upgrade package and application roots must be separate');
  }
  if (!allowPending) assertNoPendingUpgrade(appRoot);
  const { manifest: currentManifest } = readUpgradeManifest(appRoot);
  const profilePath = join(appRoot, 'fullnet-app.json');
  let applicationProfile = null;
  if (existsSync(profilePath)) {
    assertUnlinkedPath(profilePath);
    applicationProfile = JSON.parse(readFileSync(profilePath, 'utf8'));
    if (!applicationProfile.preset || applicationProfile.preset !== currentManifest.projectedPreset) {
      throw new Error('Application preset disagrees with the framework manifest');
    }
  }
  const { manifest: packageManifest } = readUpgradeManifest(packageRoot);
  const files = readUpgradePackage(packageRoot, packageManifest);
  const nextManifest = structuredClone(packageManifest);
  if (!isUpgradeAllowed(currentManifest.frameworkVersion, nextManifest.frameworkVersion)) {
    throw new Error('Target framework version is older than the application manifest');
  }
  if (currentManifest.projectedPreset) {
    const preset = currentManifest.projectedPreset;
    const modules = resolvePresetModules(preset);
    if (!isDeepStrictEqual(packageManifest.presetModules?.[preset], modules)
        || !isDeepStrictEqual(currentManifest.presetModules?.[preset], modules)) {
      throw new Error('Preset module closure changed; explicit application migration is required');
    }
    const base = 'src/Composition/Full.NET.Composition/';
    const paths = { catalog: base + 'FullNetModuleCatalog.cs', selection: base + 'FullNetModuleSelection.cs', project: base + 'Full.NET.Composition.csproj' };
    const sources = Object.fromEntries(Object.entries(paths).map(([key, path]) => {
      if (!files.has(path)) throw new Error('Package is missing preset Composition file: ' + path);
      return [key, files.get(path).toString('utf8')];
    }));
    const projected = projectCompositionSource(sources, modules);
    for (const [key, path] of Object.entries(paths)) {
      const content = Buffer.from(projected[key]);
      files.set(path, content);
      nextManifest.managedFiles[path] = hashBytes(content);
    }
    nextManifest.projectedPreset = preset;
    nextManifest.migrationInventory = buildPresetMigrationInventory(packageManifest.migrationInventory, preset, modules);
  }
  const updates = [];
  const conflicts = [];
  const paths = new Set([...Object.keys(currentManifest.managedFiles), ...Object.keys(nextManifest.managedFiles)]);
  const currentFiles = Object.fromEntries([...paths].map((path) => {
    const absolute = join(appRoot, 'framework/fullnet', path);
    const found = assertUnlinkedPath(absolute, true);
    if (found && !lstatSync(absolute).isFile()) throw new Error('Application managed path is not a file: ' + path);
    return [path, found ? hashBytes(readFileSync(absolute)) : null];
  }));
  for (const [relativePath, nextDigest] of Object.entries(nextManifest.managedFiles)) {
    const currentDigest = currentManifest.managedFiles[relativePath];
    const actualDigest = currentFiles[relativePath];
    if (currentDigest && !actualDigest) {
      conflicts.push({ path: relativePath, reason: 'previously managed file is missing' });
      continue;
    }
    if (actualDigest && actualDigest !== currentDigest && actualDigest !== nextDigest) {
      conflicts.push({ path: relativePath, reason: 'framework-managed file was customized locally' });
      continue;
    }
    if (actualDigest !== nextDigest) {
      updates.push({ path: relativePath, digest: nextDigest });
    }
  }

  for (const relativePath of Object.keys(currentManifest.managedFiles)) {
    if (!(relativePath in nextManifest.managedFiles)) {
      conflicts.push({ path: relativePath, reason: 'managed file removal requires an explicit recovery policy' });
    }
  }

  const plan = {
    currentVersion: currentManifest.frameworkVersion,
    targetVersion: nextManifest.frameworkVersion,
    dryRun: true,
    updates,
    conflicts,
    nextManifest,
    currentManifest,
    currentFiles,
    applicationProfile,
    frameworkSnapshot: readFrameworkSnapshot(join(appRoot, 'framework/fullnet')),
  };
  return { plan, files };
}

export function planFrameworkUpgrade(options) {
  return prepareUpgrade(options).plan;
}

export function applyFrameworkUpgrade({ appRoot, packageRoot, plan }) {
  if (plan.conflicts.length > 0) {
    throw new Error('Cannot apply upgrade while conflicts remain');
  }
  const comparable = (candidate) => { const copy = { ...candidate }; delete copy.dryRun; delete copy.recoveryRoot; return copy; };
  const verifyUnchanged = (allowPending = false) => {
    const prepared = prepareUpgrade({ appRoot, packageRoot, allowPending });
    if (!isDeepStrictEqual(comparable(prepared.plan), comparable(plan))) {
      throw new Error('Upgrade preview changed; regenerate the plan before applying');
    }
    return prepared;
  };
  const prepared = verifyUnchanged();
  if (prepared.plan.conflicts.length) throw new Error('Cannot apply upgrade while conflicts remain');
  plan.recoveryRoot = commitFrameworkUpgrade({ appRoot: resolve(appRoot), files: prepared.files,
    nextManifest: prepared.plan.nextManifest, expectedSnapshot: prepared.plan.frameworkSnapshot,
    verifyUnchanged: () => verifyUnchanged(true) });
  return plan;
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
