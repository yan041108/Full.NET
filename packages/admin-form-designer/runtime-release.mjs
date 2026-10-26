import { createHash } from 'node:crypto';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { createRequire } from 'node:module';
import { basicFields, advancedFields, containers } from './vendor/vform3/upstream/src/components/form-designer/widget-panel/widgetsConfig.js';
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex');
// 独立进程隔离外层 Vite 已缓存的 development 编译状态，开发与交付共用同一生产构建入口。
export async function buildRuntimeOutput(configFile, outDir) {
  const vite = path.join(path.dirname(createRequire(new URL('../../ui/admin/package.json', import.meta.url)).resolve('vite/package.json')), 'bin/vite.js');
  await promisify(execFile)(process.execPath, [vite, 'build', '--config', configFile, '--outDir', outDir, '--mode', 'production', '--logLevel', 'error'],
    { env: { ...process.env, NODE_ENV: 'production' }, windowsHide: true, maxBuffer: 2_000_000 });
}
const metadata = {
  formatVersion: 1, sourceCommit: 'c67479e496bab56a93a3dff168a4f529d8293c67',
  adapterVersion: 'fullnet.vform3.2', compilerVersion: 2, bindingVersion: 1,
  components: [...new Set([...basicFields, ...advancedFields, ...containers].map(item => item.type).concat(['alert', 'card', 'grid-col', 'table-cell', 'tab-pane']))].sort()
};
async function files(directory, root = directory) {
  const result = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const name = path.join(directory, entry.name);
    if (entry.isSymbolicLink()) throw new Error('vform.release_symlink_forbidden');
    if (entry.isDirectory()) result.push(...await files(name, root));
    else if (path.relative(root, name) !== 'runtime-release.json') {
      const content = await readFile(name);
      result.push({ path: path.relative(root, name).replaceAll(path.sep, '/'), bytes: content.length, sha256: sha256(content) });
    }
  }
  return result.sort((a, b) => a.path < b.path ? -1 : a.path > b.path ? 1 : 0);
}
// 摘要覆盖实际产物和协议元数据；不包含自身文件，避免循环摘要。
export async function createRuntimeRelease(directory) {
  const release = { ...metadata, assets: await files(directory) };
  return { ...release, contentHash: sha256(JSON.stringify(release)) };
}
export async function verifyRuntimeRelease(directory, expected) {
  expected ??= JSON.parse(await readFile(new URL('../../contracts/workflow/vform3-runtime-release.json', import.meta.url), 'utf8'));
  const emitted = JSON.parse(await readFile(path.join(directory, 'runtime-release.json'), 'utf8'));
  const actual = await createRuntimeRelease(directory);
  if (JSON.stringify(actual) !== JSON.stringify(expected) || JSON.stringify(emitted) !== JSON.stringify(expected)) throw new Error('vform.runtime_release_mismatch: regenerate the reviewed runtime release before deployment');
  return actual;
}
