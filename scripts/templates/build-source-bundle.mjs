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
import { buildMigrationInventory, buildPresetMigrationInventory, buildSeedInventory } from './framework-manifest-utils.mjs';

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(__dirname, '..', '..');
const DEFAULT_OUTPUT = resolve(REPO_ROOT, 'artifacts', 'templates', 'fullnet-source-bundle');
const FRAMEWORK_VERSION = '0.1.0';
const SCHEMA_VERSION = 3;

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
  'packages/braces',
  'patches',
];

const INCLUDE_FILES = [
  'Directory.Packages.props',
  'Directory.Build.props',
  'Directory.Build.targets',
  'global.json',
  'nuget.config',
  'LICENSE',
  'THIRD-PARTY-NOTICES',
  'package.json',
  'pnpm-lock.yaml',
  'pnpm-workspace.yaml',
  'templates/fullnet-app/package.json',
  'scripts/openapi/generate-fullnet-client.mjs',
  'scripts/openapi/validate-client-generation-readiness.mjs',
  'contracts/openapi/fullnet-client-v1.openapi.json',
  'contracts/openapi/client-generation-manifest-v1.json',
  'contracts/workflow/gateway-limits.json',
  'contracts/workflow/vform3-runtime-release.json',
];

// 模板与创建器同属正式分发输入；不能仅检查框架运行源码是否干净。
export const TEMPLATE_INPUT_ROOTS = ['templates/fullnet-app', 'scripts/templates'];

const EXCLUDED_DIR_NAMES = new Set(['bin', 'obj', '.vs', 'node_modules']);
const SECRET_FILE_PATTERN = /(?:^|\/)(?:\.env|secrets?\.json|appsettings\.(?:Production|Local)\.json)$/i;

export function parseBundleArguments(argv, defaultOutput = DEFAULT_OUTPUT) {
  const values = new Map();
  for (let i = 0; i < argv.length; i += 2) {
    const [key, value] = argv.slice(i, i + 2);
    if (!['--output', '--source-commit'].includes(key) || values.has(key) || !value || value.startsWith('--')) {
      throw new Error('Expected unique --output dir or --source-commit full-sha arguments');
    }
    values.set(key, value);
  }
  return { output: resolve(values.get('--output') ?? defaultOutput), sourceCommit: values.get('--source-commit') };
}

function readGitCommit(run) {
  const result = run('git', ['rev-parse', 'HEAD'], {
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

/** 供模板测试在脏工作区跳过需完整打包的用例；CI 干净提交仍必须跑绿。 */
export function readBundleInputStatus(run = spawnSync) {
  return readStatus(run, [...INCLUDE_ROOTS, ...INCLUDE_FILES, ...TEMPLATE_INPUT_ROOTS]);
}

function readStatus(run, paths) {
  const result = run('git', ['status', '--porcelain', '--untracked-files=all', '--', ...paths.map(toPosixPath)], {
    cwd: REPO_ROOT,
    encoding: 'utf8',
  });
  if (result.status !== 0) {
    throw new Error('Unable to check source bundle inputs: ' + (result.stderr || 'unknown error'));
  }
  return result.stdout ?? '';
}

/** 显式候选只从已提交树归档，仍要求正在执行的打包工具与该提交一致。 */
function assertBundleInputs(run, sourceCommit, pinnedCommit) {
  if (pinnedCommit === undefined) {
    assertCleanBundleInputStatus(readBundleInputStatus(run));
    return;
  }
  if (!/^[a-f0-9]{40}$/.test(pinnedCommit) || pinnedCommit !== sourceCommit || readGitCommit(run) !== sourceCommit) {
    throw new Error('Explicit source commit must be the full commit SHA of current HEAD');
  }
  if (readStatus(run, ['scripts/templates']).trim()) {
    throw new Error('Template packaging tools contain uncommitted changes');
  }
}

export function areBundleInputsClean() {
  return readBundleInputStatus().trim().length === 0;
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

function copyIncludedPaths(bundleRoot, sourceCommit, run) {
  const pathspecs = [...INCLUDE_ROOTS, ...INCLUDE_FILES].map(toPosixPath);
  const archivePath = join(bundleRoot, '.fullnet-source.tar');
  const tree = run('git', ['ls-tree', '-r', '-z', sourceCommit, '--', ...pathspecs], {
    cwd: REPO_ROOT,
    encoding: 'utf8',
    maxBuffer: 16 * 1024 * 1024,
  });
  if (tree.status !== 0) {
    throw new Error('Unable to inspect committed source: ' + (tree.stderr || 'unknown error'));
  }
  assertArchiveEntryModes(tree.stdout);
  const committedPaths = tree.stdout.split('\0').filter(Boolean).map(entry => entry.slice(entry.indexOf('\t') + 1));
  const committedPathspecs = pathspecs.filter(spec => committedPaths.some(path => path === spec || path.startsWith(spec + '/')));
  if (!committedPathspecs.length) throw new Error('Source commit has no included paths');
  // 直接导出固定提交，忽略文件和本机密钥永远不进入分发包。
  try {
    const archive = run('git', [
      'archive', '--format=tar', '--output', archivePath, sourceCommit, ...committedPathspecs,
    ], { cwd: REPO_ROOT, encoding: 'utf8' });
    if (archive.status !== 0) {
      throw new Error('Unable to archive committed source: ' + (archive.stderr || 'unknown error'));
    }
    const extract = run('tar', ['-xf', archivePath, '-C', bundleRoot], {
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

function buildManifest(sourceCommit, managedFiles, bundleRoot) {
  const migrationInventory = buildMigrationInventory(managedFiles);
  // 分发前验证各预设的迁移归属，避免缺失映射直到安装才暴露。
  for (const [preset, modules] of Object.entries(PRESET_MODULE_CLOSURE)) {
    buildPresetMigrationInventory(migrationInventory, preset, modules);
  }
  return {
    schemaVersion: SCHEMA_VERSION,
    sourceCommit,
    frameworkVersion: FRAMEWORK_VERSION,
    presetModules: PRESET_MODULE_CLOSURE,
    validPresets: VALID_PRESETS,
    migrationInventory,
    seedInventory: buildSeedInventory(managedFiles, (path) => readFileSync(join(bundleRoot, path), 'utf8'), PRESET_MODULE_CLOSURE),
    managedFiles,
  };
}

export function buildSourceBundle({ output = DEFAULT_OUTPUT, sourceCommit: pinnedCommit, run = spawnSync } = {}) {
  const bundleRoot = resolve(output);
  const sourceCommit = readGitCommit(run);
  assertBundleInputs(run, sourceCommit, pinnedCommit);

  // 构建器不拥有调用方既有目录；拒绝覆盖，避免删除应用文件或人工修改。
  if (existsSync(bundleRoot) && readdirSync(bundleRoot).length > 0) {
    throw new Error('Bundle output directory must be empty: ' + bundleRoot);
  }
  mkdirSync(bundleRoot, { recursive: true });

  copyIncludedPaths(bundleRoot, sourceCommit, run);
  pruneExcluded(bundleRoot);
  assertBundleInputs(run, sourceCommit, pinnedCommit);

  const managedFiles = collectManagedFiles(bundleRoot);
  if (Object.keys(managedFiles).length === 0) {
    throw new Error('Bundle contains no managed files');
  }

  const manifest = buildManifest(sourceCommit, managedFiles, bundleRoot);
  writeFileSync(join(bundleRoot, 'framework-manifest.json'), JSON.stringify(manifest, null, 2) + '\n', 'utf8');
  return { bundleRoot, manifest };
}

function isMainModule() {
  const entry = process.argv[1] ? resolve(process.argv[1]) : '';
  return entry === fileURLToPath(import.meta.url);
}

if (isMainModule()) {
  try {
    const args = process.argv.slice(2);
    if (args.length === 1 && ['--help', '-h'].includes(args[0])) {
      console.log('Usage: node scripts/templates/build-source-bundle.mjs [--output dir] [--source-commit full-sha]');
      process.exit(0);
    }
    const { bundleRoot, manifest } = buildSourceBundle(parseBundleArguments(args));
    console.log('Wrote framework bundle to ' + bundleRoot);
    console.log('Managed files: ' + Object.keys(manifest.managedFiles).length);
    console.log('Source commit: ' + manifest.sourceCommit);
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exit(1);
  }
}
