/** 校验升级清单、受管路径和分发包内容，拒绝链接与跨平台路径别名。 */
import { createHash } from 'node:crypto';
import { lstatSync, readdirSync, readFileSync } from 'node:fs';
import { dirname, join, parse, resolve } from 'node:path';
import { isDeepStrictEqual } from 'node:util';
import { buildMigrationInventory, buildSeedInventory } from './framework-manifest-utils.mjs';
import { PRESET_MODULE_CLOSURE, VALID_PRESETS } from './preset-modules.mjs';

export const hashBytes = (content) => createHash('sha256').update(content).digest('hex');

/** 每一层目录都需由实际目录拥有，避免中间链接绕过末端文件检查。 */
export function assertUnlinkedPath(path, allowMissing = false) {
  const absolute = resolve(path);
  const ancestors = [];
  for (let current = absolute; current !== parse(current).root; current = dirname(current)) ancestors.unshift(current);
  for (let index = 0; index < ancestors.length; index += 1) {
    const current = ancestors[index];
    let stat;
    try { stat = lstatSync(current); } catch (error) {
      if (allowMissing && error.code === 'ENOENT') return false;
      throw error;
    }
    if (stat.isSymbolicLink()) throw new Error('Framework path contains a symbolic link: ' + current);
    if (index < ancestors.length - 1 && !stat.isDirectory()) throw new Error('Framework parent is not a directory: ' + current);
  }
  return true;
}

export function readUpgradeManifest(root) {
  assertUnlinkedPath(root);
  const manifestPath = join(root, 'framework-manifest.json');
  assertUnlinkedPath(manifestPath);
  const encoded = readFileSync(manifestPath);
  const manifest = JSON.parse(encoded);
  if (manifest.schemaVersion !== 3 || !/^[a-f0-9]{40}$/.test(manifest.sourceCommit ?? '')
      || !manifest.managedFiles || Array.isArray(manifest.managedFiles)
      || Object.keys(manifest.managedFiles).length === 0
      || !isDeepStrictEqual(manifest.presetModules, PRESET_MODULE_CLOSURE)
      || !isDeepStrictEqual(manifest.validPresets, VALID_PRESETS)
      || !manifest.migrationInventory || !manifest.seedInventory) throw new Error('Invalid framework upgrade manifest');
  const aliases = new Map();
  for (const [path, digest] of Object.entries(manifest.managedFiles)) {
    const segments = path.split('/');
    if (segments.some((part) => !part || part === '.' || part === '..'
      || /[\\:\x00-\x1f<>"|?*]/.test(part) || /[ .]$/.test(part)
      || /^(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(part)
      || ['bin', 'obj', 'node_modules', '.git'].includes(part.toLowerCase()))
      || path.toLowerCase() === 'framework-manifest.json') throw new Error('Invalid managed path: ' + path);
    for (let count = 1; count <= segments.length; count += 1) {
      const prefix = segments.slice(0, count).join('/');
      const alias = prefix.toLowerCase();
      if (aliases.has(alias) && aliases.get(alias) !== prefix) throw new Error('Managed path has a case alias: ' + path);
      aliases.set(alias, prefix);
    }
    if (!/^[a-f0-9]{64}$/.test(digest)) throw new Error('Invalid managed file digest: ' + path);
  }
  const copy = join(root, 'framework/fullnet/framework-manifest.json');
  assertUnlinkedPath(copy);
  if (!encoded.equals(readFileSync(copy))) throw new Error('Framework manifest copies differ');
  return { manifest, encoded };
}

/** 只读取清单拥有的文件；包内额外文件不能悄悄进入应用。 */
export function readUpgradePackage(root, manifest) {
  const bundleRoot = join(root, 'framework/fullnet');
  assertUnlinkedPath(bundleRoot);
  const files = new Map();
  for (const [path, expected] of Object.entries(manifest.managedFiles)) {
    const absolute = join(bundleRoot, path);
    assertUnlinkedPath(absolute);
    if (!lstatSync(absolute).isFile()) throw new Error('Package managed path is not a file: ' + path);
    const content = readFileSync(absolute);
    if (hashBytes(content) !== expected) throw new Error('Package managed file digest mismatch: ' + path);
    files.set(path, content);
  }
  const visit = (directory, prefix = '') => {
    for (const entry of readdirSync(directory, { withFileTypes: true })) {
      const path = prefix ? prefix + '/' + entry.name : entry.name;
      if (entry.isSymbolicLink()) throw new Error('Package contains a symbolic link: ' + path);
      if (entry.isDirectory()) visit(join(directory, entry.name), path);
      else if (!entry.isFile() || (!files.has(path) && path !== 'framework-manifest.json')) {
        throw new Error('Unexpected package framework file: ' + path);
      }
    }
  };
  visit(bundleRoot);
  if (!isDeepStrictEqual(manifest.migrationInventory, buildMigrationInventory(manifest.managedFiles))) {
    throw new Error('Package migration inventory does not match managed files');
  }
  if (!isDeepStrictEqual(manifest.seedInventory, buildSeedInventory(manifest.managedFiles,
        (path) => files.get(path).toString('utf8'), manifest.presetModules))) {
    throw new Error('Package seed inventory does not match managed files');
  }
  return files;
}

/** 保存原框架时也不跟随未登记的链接，防止复制读取应用目录之外的内容。 */
export function assertFrameworkTreeUnlinked(root) {
  assertUnlinkedPath(root);
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const path = join(root, entry.name);
    if (entry.isSymbolicLink()) throw new Error('Application framework contains a symbolic link: ' + path);
    if (entry.isDirectory()) assertFrameworkTreeUnlinked(path);
    else if (!entry.isFile()) throw new Error('Unsupported framework entry: ' + path);
  }
}

/** 包括未登记文件与空目录，保证整目录切换不会使最新人工内容失效。 */
export function readFrameworkSnapshot(root) {
  assertUnlinkedPath(root);
  const snapshot = new Map();
  const visit = (directory, prefix = '') => {
    for (const entry of readdirSync(directory, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name, 'en'))) {
      const path = prefix ? prefix + '/' + entry.name : entry.name;
      const absolute = join(directory, entry.name);
      if (entry.isSymbolicLink()) throw new Error('Application framework contains a symbolic link: ' + path);
      if (entry.isDirectory()) { snapshot.set(path, 'directory'); visit(absolute, path); }
      else if (entry.isFile()) snapshot.set(path, hashBytes(readFileSync(absolute)));
      else throw new Error('Unsupported framework entry: ' + path);
    }
  };
  visit(root);
  return Object.fromEntries(snapshot);
}
