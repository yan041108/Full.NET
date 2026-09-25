#!/usr/bin/env node
/**
 * 校验源码包和创建参数后，在暂存目录中实例化并发布 Full.NET 应用。
 */
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { existsSync, lstatSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { PRESET_MODULE_CLOSURE, resolvePresetModules, validateOwnerKey } from './preset-modules.mjs';
import { buildMigrationInventory, buildSeedInventory } from './framework-manifest-utils.mjs';
import { projectPresetComposition } from './project-preset-composition.mjs';
import { verifyCreatedApp } from './verify-created-app.mjs';

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));

function assertPackageIntegrity(root, verifyFrontendSkeleton = false) {
  const manifest = JSON.parse(readFileSync(join(root, 'framework-manifest.json'), 'utf8'));
  const bundleRoot = join(root, 'framework', 'fullnet');
  const bundleManifest = readFileSync(join(bundleRoot, 'framework-manifest.json'));
  if (!bundleManifest.equals(readFileSync(join(root, 'framework-manifest.json')))) {
    throw new Error('Framework manifest copies differ');
  }
  if (!manifest.managedFiles || Object.keys(manifest.managedFiles).length === 0) {
    throw new Error('Framework manifest has no managed files');
  }
  if (manifest.schemaVersion >= 2 && !manifest.migrationInventory) {
    throw new Error('Framework migration inventory is missing');
  }
  if (manifest.migrationInventory) {
    const expectedInventory = buildMigrationInventory(manifest.managedFiles);
    if (JSON.stringify(manifest.migrationInventory) !== JSON.stringify(expectedInventory)) {
      throw new Error('Framework migration inventory does not match managed files');
    }
  } else if (Object.keys(manifest.managedFiles).some((path) => path.includes('/Migrations/'))) {
    throw new Error('Framework migration inventory is missing');
  }
  for (const [relativePath, expectedHash] of Object.entries(manifest.managedFiles)) {
    const segments = relativePath.split('/');
    if (segments.some((segment) => !segment || segment === '.' || segment === '..' || segment.includes('\\'))) {
      throw new Error('Invalid managed path: ' + relativePath);
    }
    let current = bundleRoot;
    for (const segment of segments) {
      current = join(current, segment);
      if (lstatSync(current).isSymbolicLink()) {
        throw new Error('Framework bundle contains a symbolic link: ' + relativePath);
      }
    }
    if (!lstatSync(current).isFile()) {
      throw new Error('Framework managed path is not a file: ' + relativePath);
    }
    const actualHash = createHash('sha256').update(readFileSync(current)).digest('hex');
    if (actualHash !== expectedHash) {
      throw new Error('Framework managed file digest mismatch: ' + relativePath);
    }
  }
  if (manifest.schemaVersion >= 3) {
    if (JSON.stringify(manifest.presetModules) !== JSON.stringify(PRESET_MODULE_CLOSURE)) {
      throw new Error('Framework preset module closure does not match the creator');
    }
    if (!manifest.seedInventory) throw new Error('Framework seed inventory is missing');
    const expected = buildSeedInventory(manifest.managedFiles,
      (path) => readFileSync(join(bundleRoot, path), 'utf8'), PRESET_MODULE_CLOSURE);
    if (JSON.stringify(manifest.seedInventory) !== JSON.stringify(expected)) {
      throw new Error('Framework seed inventory does not match managed source');
    }
  }
  const expectedPaths = new Set([...Object.keys(manifest.managedFiles), 'framework-manifest.json']);
  const inspect = (directory, prefix = '') => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const relativePath = prefix ? prefix + '/' + entry.name : entry.name;
      const absolutePath = join(directory, entry.name);
      if (lstatSync(absolutePath).isSymbolicLink()) {
        throw new Error('Framework bundle contains a symbolic link: ' + relativePath);
      }
      if (entry.isDirectory()) {
        inspect(absolutePath, relativePath);
      } else if (!entry.isFile() || !expectedPaths.has(relativePath)) {
        throw new Error('Unexpected framework file: ' + relativePath);
      }
    }
  };
  inspect(bundleRoot);
  if (verifyFrontendSkeleton) {
    const isFrontendPath = (relativePath) =>
      relativePath.startsWith('ui/admin/') || relativePath.startsWith('packages/')
      || ['package.json', 'pnpm-lock.yaml', 'pnpm-workspace.yaml'].includes(relativePath);
    const copiedPaths = new Set(Object.keys(manifest.managedFiles).filter(isFrontendPath));
    for (const relativePath of copiedPaths) {
      let current = root;
      for (const segment of relativePath.split('/')) {
        current = join(current, segment);
        if (!existsSync(current) || lstatSync(current).isSymbolicLink()) {
          throw new Error('Frontend skeleton path is missing or linked: ' + relativePath);
        }
      }
      if (!lstatSync(current).isFile()) throw new Error('Frontend skeleton path is not a file: ' + relativePath);
      const actualHash = createHash('sha256').update(readFileSync(current)).digest('hex');
      if (actualHash !== manifest.managedFiles[relativePath]) {
        throw new Error('Frontend skeleton digest mismatch: ' + relativePath);
      }
    }
    const inspectCopied = (directory, prefix) => {
      if (!existsSync(directory)) return;
      for (const entry of readdirSync(directory, { withFileTypes: true })) {
        const relativePath = prefix + '/' + entry.name;
        const absolutePath = join(directory, entry.name);
        if (lstatSync(absolutePath).isSymbolicLink()) {
          throw new Error('Frontend skeleton contains a symbolic link: ' + relativePath);
        }
        if (entry.isDirectory()) inspectCopied(absolutePath, relativePath);
        else if (!entry.isFile() || !copiedPaths.has(relativePath)) {
          throw new Error('Unexpected frontend skeleton file: ' + relativePath);
        }
      }
    };
    inspectCopied(join(root, 'ui'), 'ui');
    inspectCopied(join(root, 'packages'), 'packages');
  }
}

function runDotnet(args) {
  const result = spawnSync('dotnet', args, { encoding: 'utf8', timeout: 120_000, windowsHide: true });
  if (result.status !== 0) {
    throw new Error('dotnet ' + args.slice(0, 2).join(' ') + ' failed: ' + (result.stderr || result.stdout || result.error?.message || 'unknown error'));
  }
}

function projectFrontendProxy(appRoot, httpPort) {
  const configPath = join(appRoot, 'ui', 'admin', 'vite.config.ts');
  const source = readFileSync(configPath, 'utf8');
  const target = "process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:5149'";
  if (source.split(target).length !== 2) throw new Error('Cannot locate the Vue API proxy default');
  writeFileSync(configPath, source.replace(target,
    `process.env.VITE_API_PROXY_TARGET ?? 'http://localhost:${httpPort}'`), 'utf8');
  const examplePath = join(appRoot, 'ui', 'admin', '.env.example');
  const example = readFileSync(examplePath, 'utf8');
  if (!example.includes('http://localhost:5149')) throw new Error('Cannot locate the Vue API example default');
  writeFileSync(examplePath, example.replaceAll('http://localhost:5149', `http://localhost:${httpPort}`), 'utf8');
}

export function createApp({ packageRoot, output, name, ownerKey, database = 'sqlserver', preset = 'minimal', httpPort = 5180 }) {
  validateOwnerKey(ownerKey);
  const modules = resolvePresetModules(preset);
  if (!['sqlserver', 'mysql'].includes(database)) throw new Error('Unknown database provider');
  if (!/^[A-Za-z][A-Za-z0-9_]*$/.test(name)) throw new Error('Invalid application name');
  if (!Number.isInteger(Number(httpPort)) || Number(httpPort) < 1 || Number(httpPort) > 65535) {
    throw new Error('Invalid HTTP port');
  }
  if (!output) throw new Error('Missing application output directory');
  const appRoot = resolve(output);
  if (existsSync(appRoot)) {
    throw new Error('Application output directory must not exist: ' + appRoot);
  }
  if (!packageRoot) throw new Error('Missing template package directory');
  const templateRoot = resolve(packageRoot);
  assertPackageIntegrity(templateRoot, true);

  mkdirSync(dirname(appRoot), { recursive: true });
  const stagedRoot = mkdtempSync(join(dirname(appRoot), '.fullnet-create-'));
  const hive = mkdtempSync(join(tmpdir(), 'fullnet-template-hive-'));
  try {
    runDotnet(['new', 'install', templateRoot, '--debug:custom-hive', hive]);
    runDotnet([
      'new', 'fullnet-app', '--name', name, '--owner-key', ownerKey,
      '--database', database, '--preset', preset, '--http-port', String(httpPort),
      '--output', stagedRoot, '--debug:custom-hive', hive,
    ]);
    projectPresetComposition(stagedRoot, preset, modules);
    projectFrontendProxy(stagedRoot, httpPort);
    assertPackageIntegrity(stagedRoot);
    const verification = verifyCreatedApp(stagedRoot);
    if (!verification.ok) throw new Error('Created application is invalid: ' + verification.errors.join('; '));
    if (existsSync(appRoot)) throw new Error('Application output directory already exists: ' + appRoot);
    renameSync(stagedRoot, appRoot);
    return { appRoot };
  } finally {
    if (existsSync(stagedRoot)) rmSync(stagedRoot, { recursive: true, force: true });
    rmSync(hive, { recursive: true, force: true });
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const options = {};
    for (let i = 2; i < process.argv.length; i += 2) {
      const key = process.argv[i];
      const value = process.argv[i + 1];
      if (!key?.startsWith('--') || !value) throw new Error('Expected --key value arguments');
      options[key.slice(2)] = value;
    }
    const packageRoot = options.package
      ?? (basename(SCRIPT_DIR) === '.fullnet-tools' ? dirname(SCRIPT_DIR) : undefined);
    const { appRoot } = createApp({
      packageRoot,
      output: options.output,
      name: options.name,
      ownerKey: options['owner-key'],
      database: options.database,
      preset: options.preset,
      httpPort: options['http-port'] ?? 5180,
    });
    console.log('Created Full.NET application: ' + appRoot);
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  }
}
