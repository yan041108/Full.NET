import { lstatSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';

const files = [
  ['scripts/openapi/generate-fullnet-client.mjs', '.fullnet-tools/openapi/generate-fullnet-client.mjs'],
  ['scripts/openapi/validate-client-generation-readiness.mjs', '.fullnet-tools/openapi/validate-client-generation-readiness.mjs'],
  ['contracts/openapi/fullnet-client-v1.openapi.json', 'contracts/openapi/fullnet-client-v1.openapi.json'],
  ['contracts/openapi/client-generation-manifest-v1.json', 'contracts/openapi/client-generation-manifest-v1.json'],
];

function stat(path) {
  try { return lstatSync(path); } catch (error) {
    if (error.code === 'ENOENT' || error.code === 'ENOTDIR') return undefined;
    throw error;
  }
}

// 仅从已冻结源码包复制完整工具闭包；先检查全部源与目标，禁止覆盖应用拥有的文件。
export function copyApplicationClientTools(bundleRoot, applicationRoot) {
  const root = resolve(applicationRoot);
  const prepared = files.map(([sourcePath, targetPath]) => {
    const source = join(bundleRoot, sourcePath);
    const target = join(root, targetPath);
    const sourceStat = stat(source);
    if (!sourceStat?.isFile() || sourceStat.isSymbolicLink()) {
      throw new Error('client tool source missing or invalid: ' + sourcePath);
    }
    if (stat(target)) throw new Error('client tool target occupied: ' + targetPath);
    for (let parent = dirname(target); parent !== dirname(root); parent = dirname(parent)) {
      const parentStat = stat(parent);
      if (parentStat && (!parentStat.isDirectory() || parentStat.isSymbolicLink())) {
        throw new Error('client tool target parent occupied: ' + targetPath);
      }
    }
    return { target, content: readFileSync(source) };
  });
  for (const { target, content } of prepared) {
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, content, { flag: 'wx' });
  }
}
