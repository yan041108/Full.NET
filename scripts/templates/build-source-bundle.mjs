#!/usr/bin/env node
/**
 * 构建版本化 Full.NET 框架源码分发包与 framework-manifest.json。
 */
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import {
  existsSync,
  lstatSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  rmSync,
  writeFileSync,
} from 'node:fs';
import { dirname, join, posix, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { PRESET_MODULE_CLOSURE, VALID_PRESETS } from './preset-modules.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(__dirname, '..', '..');
const DEFAULT_OUTPUT = resolve(REPO_ROOT, 'artifacts', 'templates', 'fullnet-source-bundle');
const FRAMEWORK_VERSION = '0.1.0';
const SCHEMA_VERSION = 1;

const INCLUDE_ROOTS = [
  'src/AI',
  'src/BuildingBlocks',
  'src/Composition',
  'src/Compatibility',
  'src/Generators',
  'src/Hosts',
  'src/Modules',
  join('src', 'Tools', 'Full.NET.CodeGeneration.Cli'),
  'samples/enterprise-request',
  'contracts/naming',
  'ui/admin',
  'packages/client-contracts',
  'packages/admin-i18n',
  'packages/admin-form-designer',
  'packages/design-tokens',
];

const INCLUDE_FILES = [
  'Directory.Packages.props',
  'Directory.Build.props',
  'Directory.Build.targets',
  'global.json',
  'nuget.config',
  'package.json',
  'pnpm-lock.yaml',
  'pnpm-workspace.yaml',
];

const EXCLUDED_DIR_NAMES = new Set(['bin', 'obj', '.vs', 'node_modules']);
const SECRET_FILE_PATTERN = /(?:^|\/)(?:\.env|secrets?\.json|appsettings\.(?:Production|Local)\.json)$/i;

function parseArgs(argv) {
  let output = DEFAULT_OUTPUT;
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--output') {
      const value = argv[i + 1];
      if (!value) {
        throw new Error('Missing value for --output');
      }
      output = resolve(value);
      i += 1;
      continue;
    }
    if (arg === '--help' || arg === '-h') {
      console.log('Usage: node scripts/templates/build-source-bundle.mjs [--output dir]');
      process.exit(0);
    }
    throw new Error('Unknown argument: ' + arg);
  }
  return { output };
}

function readGitCommit() {
  const result = spawnSync('git', ['rev-parse', 'HEAD'], {
    cwd: REPO_ROOT,
    encoding: 'utf8',
  });
  if (result.status !== 0 || !result.stdout?.trim()) {
    throw new Error('Unable to read source git commit: ' + (result.stderr || result.stdout || 'unknown error'));
  }
  return result.stdout.trim();
}

export function assertCleanBundleInputStatus(statusOutput) {
  if (statusOutput.trim()) {
    throw new Error('Source bundle inputs contain uncommitted changes; commit them before assigning sourceCommit');
  }
}

export function assertArchiveEntryModes(treeOutput) {
  for (const entry of treeOutput.split('\0')) {
    if (!entry) continue;
    const mode = entry.slice(0, entry.indexOf(' '));
    if (mode !== '100644' && mode !== '100755') {
      throw new Error('Source bundle contains unsupported git entry mode: ' + mode);
    }
  }
}

function assertCommittedBundleInputs() {
  const pathspecs = [...INCLUDE_ROOTS, ...INCLUDE_FILES].map(toPosixPath);
  const result = spawnSync('git', ['status', '--porcelain', '--untracked-files=all', '--', ...pathspecs], {
    cwd: REPO_ROOT,
    encoding: 'utf8',
  });
  if (result.status !== 0) {
    throw new Error('Unable to check source bundle inputs: ' + (result.stderr || 'unknown error'));
  }
  assertCleanBundleInputStatus(result.stdout);
}

function toPosixPath(path) {
  return path.split(sep).join(posix.sep);
}

export function shouldExclude(relativePath, isDirectory) {
  const normalized = toPosixPath(relativePath);
  const segments = normalized.split('/');
  if (segments.some((segment) => EXCLUDED_DIR_NAMES.has(segment))) {
    return true;
  }
  if (!isDirectory && SECRET_FILE_PATTERN.test(normalized)) {
    return true;
  }
  return false;
}

export function assertManagedPath(relativePath) {
  const normalized = toPosixPath(relativePath);
  if (normalized.includes('..')) {
    throw new Error('Rejected path outside bundle root: ' + relativePath);
  }
  if (normalized.startsWith('/') || /^[a-zA-Z]:/.test(normalized)) {
    throw new Error('Rejected absolute path: ' + relativePath);
  }
  if (shouldExclude(normalized, false)) {
    throw new Error('Rejected excluded path: ' + relativePath);
  }
}

function readdirSorted(absoluteDir) {
  return readdirSync(absoluteDir).sort();
}

function collectManagedFiles(bundleRoot) {
  const managedFiles = {};
  const walk = (absoluteDir, relativeDir = '') => {
    for (const entry of readdirSorted(absoluteDir)) {
      const absolutePath = join(absoluteDir, entry);
      const relativePath = relativeDir ? join(relativeDir, entry) : entry;
      const stats = lstatSync(absolutePath);
      if (stats.isSymbolicLink()) {
        throw new Error('Source bundle contains a symbolic link: ' + relativePath);
      }
      if (stats.isDirectory()) {
        if (shouldExclude(relativePath, true)) {
          continue;
        }
        walk(absolutePath, relativePath);
        continue;
      }
      if (shouldExclude(relativePath, false)) {
        continue;
      }
      assertManagedPath(relativePath);
      const content = readFileSync(absolutePath);
      managedFiles[toPosixPath(relativePath)] = createHash('sha256').update(content).digest('hex');
    }
  };
  walk(bundleRoot);
  return managedFiles;
}

function copyIncludedPaths(bundleRoot, sourceCommit) {
  const pathspecs = [...INCLUDE_ROOTS, ...INCLUDE_FILES]
    .filter((relativePath) => existsSync(join(REPO_ROOT, relativePath)))
    .map(toPosixPath);
  const archivePath = join(bundleRoot, '.fullnet-source.tar');
  const tree = spawnSync('git', ['ls-tree', '-r', '-z', sourceCommit, '--', ...pathspecs], {
    cwd: REPO_ROOT,
    encoding: 'utf8',
    maxBuffer: 16 * 1024 * 1024,
  });
  if (tree.status !== 0) {
    throw new Error('Unable to inspect committed source: ' + (tree.stderr || 'unknown error'));
  }
  assertArchiveEntryModes(tree.stdout);
  // 直接导出固定提交，忽略文件和本机密钥永远不进入分发包。
  try {
    const archive = spawnSync('git', [
      'archive', '--format=tar', '--output', archivePath, sourceCommit, ...pathspecs,
    ], { cwd: REPO_ROOT, encoding: 'utf8' });
    if (archive.status !== 0) {
      throw new Error('Unable to archive committed source: ' + (archive.stderr || 'unknown error'));
    }
    const extract = spawnSync('tar', ['-xf', archivePath, '-C', bundleRoot], {
      cwd: REPO_ROOT,
      encoding: 'utf8',
    });
    if (extract.status !== 0) {
      throw new Error('Unable to extract committed source: ' + (extract.stderr || 'unknown error'));
    }
  } finally {
    rmSync(archivePath, { force: true });
  }
}

function pruneExcluded(bundleRoot) {
  const removeExcluded = (absoluteDir, relativeDir = '') => {
    for (const entry of readdirSorted(absoluteDir)) {
      const absolutePath = join(absoluteDir, entry);
      const relativePath = relativeDir ? join(relativeDir, entry) : entry;
      const stats = lstatSync(absolutePath);
      if (stats.isSymbolicLink()) {
        throw new Error('Source bundle contains a symbolic link: ' + relativePath);
      }
      if (stats.isDirectory()) {
        if (shouldExclude(relativePath, true)) {
          rmSync(absolutePath, { recursive: true, force: true });
          continue;
        }
        removeExcluded(absolutePath, relativePath);
        continue;
      }
      if (shouldExclude(relativePath, false)) {
        rmSync(absolutePath, { force: true });
      }
    }
  };
  removeExcluded(bundleRoot);
}

function buildManifest(sourceCommit, managedFiles) {
  return {
    schemaVersion: SCHEMA_VERSION,
    sourceCommit,
    frameworkVersion: FRAMEWORK_VERSION,
    presetModules: PRESET_MODULE_CLOSURE,
    validPresets: VALID_PRESETS,
    managedFiles,
  };
}

export function buildSourceBundle({ output = DEFAULT_OUTPUT } = {}) {
  const bundleRoot = resolve(output);
  const sourceCommit = readGitCommit();
  assertCommittedBundleInputs();

  // 构建器不拥有调用方既有目录；拒绝覆盖，避免删除应用文件或人工修改。
  if (existsSync(bundleRoot) && readdirSync(bundleRoot).length > 0) {
    throw new Error('Bundle output directory must be empty: ' + bundleRoot);
  }
  mkdirSync(bundleRoot, { recursive: true });

  copyIncludedPaths(bundleRoot, sourceCommit);
  pruneExcluded(bundleRoot);
  assertCommittedBundleInputs();

  const managedFiles = collectManagedFiles(bundleRoot);
  if (Object.keys(managedFiles).length === 0) {
    throw new Error('Bundle contains no managed files');
  }

  const manifest = buildManifest(sourceCommit, managedFiles);
  writeFileSync(join(bundleRoot, 'framework-manifest.json'), JSON.stringify(manifest, null, 2) + '\n', 'utf8');
  return { bundleRoot, manifest };
}

function isMainModule() {
  const entry = process.argv[1] ? resolve(process.argv[1]) : '';
  return entry === fileURLToPath(import.meta.url);
}

if (isMainModule()) {
  try {
    const { output } = parseArgs(process.argv.slice(2));
    const { bundleRoot, manifest } = buildSourceBundle({ output });
    console.log('Wrote framework bundle to ' + bundleRoot);
    console.log('Managed files: ' + Object.keys(manifest.managedFiles).length);
    console.log('Source commit: ' + manifest.sourceCommit);
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exit(1);
  }
}
