#!/usr/bin/env node
/**
 * 构建版本化 Full.NET 框架源码分发包与 framework-manifest.json。
 */
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import {
  cpSync,
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  rmSync,
  statSync,
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
  'src/BuildingBlocks',
  'src/Composition',
  'src/Compatibility',
  'src/Generators',
  'src/Modules',
  join('src', 'Tools', 'Full.NET.CodeGeneration.Cli'),
  'contracts/naming',
];

const INCLUDE_FILES = [
  'Directory.Packages.props',
  'Directory.Build.props',
  'global.json',
  'nuget.config',
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
    throw new Error('Unable to read git commit --trailer "Co-authored-by: Cursor <cursoragent@cursor.com>": ' + (result.stderr || result.stdout || 'unknown error'));
  }
  return result.stdout.trim();
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
      const stats = statSync(absolutePath);
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

function copyIncludedPaths(bundleRoot) {
  for (const relativeRoot of INCLUDE_ROOTS) {
    const source = join(REPO_ROOT, relativeRoot);
    if (!existsSync(source)) {
      throw new Error('Missing required bundle path: ' + relativeRoot);
    }
    const destination = join(bundleRoot, relativeRoot);
    mkdirSync(dirname(destination), { recursive: true });
    cpSync(source, destination, { recursive: true, force: true });
  }

  for (const relativeFile of INCLUDE_FILES) {
    if (relativeFile === 'nuget.config' && !existsSync(join(REPO_ROOT, relativeFile))) {
      continue;
    }
    const source = join(REPO_ROOT, relativeFile);
    if (!existsSync(source)) {
      throw new Error('Missing required bundle file: ' + relativeFile);
    }
    const destination = join(bundleRoot, relativeFile);
    mkdirSync(dirname(destination), { recursive: true });
    cpSync(source, destination, { force: true });
  }
}

function pruneExcluded(bundleRoot) {
  const removeExcluded = (absoluteDir, relativeDir = '') => {
    for (const entry of readdirSorted(absoluteDir)) {
      const absolutePath = join(absoluteDir, entry);
      const relativePath = relativeDir ? join(relativeDir, entry) : entry;
      const stats = statSync(absolutePath);
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

  rmSync(bundleRoot, { recursive: true, force: true });
  mkdirSync(bundleRoot, { recursive: true });

  copyIncludedPaths(bundleRoot);
  pruneExcluded(bundleRoot);

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