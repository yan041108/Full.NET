import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const ownedPath = /^tests\/Shared\/CodeGeneration\/Remediation\/[A-Z][A-Za-z0-9]*(?:Contracts|Feature|Record|Sql|RelationLookup|Aggregate)\.g\.cs$/u;
const hashPattern = /^[a-f0-9]{64}$/u;

/** 只接受生成工具明确拥有的恢复夹具及带摘要的规范内容，不能将目录当成任意写入许可。 */
export function validateCodegenFixturePreview(value) {
  if (value?.schemaVersion !== 1 || value.scope !== 'remediation' || !/^\d{4}\.\d{2}\.\d{2}\.\d+$/u.test(value.generatorVersion ?? '')) throw new Error('无效的生成预览版本或范围。');
  if (!Array.isArray(value.ownedPaths) || value.ownedPaths.length < 1 || value.ownedPaths.length > 128
    || value.ownedPaths.some(name => typeof name !== 'string' || !ownedPath.test(name))
    || new Set(value.ownedPaths).size !== value.ownedPaths.length) throw new Error('无效的生成文件所有权。');
  if (!Array.isArray(value.changes) || value.changes.length > value.ownedPaths.length) throw new Error('无效的生成差异集合。');
  const names = new Set(); let bytes = 0;
  for (const change of value.changes) {
    if (!change || !value.ownedPaths.includes(change.path) || names.has(change.path)) throw new Error('生成差异超出所有权或包含重复路径。');
    names.add(change.path);
    if (change.beforeHash !== null && !hashPattern.test(change.beforeHash ?? '') || !hashPattern.test(change.afterHash ?? '')
      || change.beforeHash === change.afterHash || typeof change.content !== 'string' || change.content.includes('\r')) throw new Error('无效的生成输入或输出摘要。');
    const length = Buffer.byteLength(change.content); bytes += length;
    if (length > 256 * 1024 || bytes > 2 * 1024 * 1024
      || createHash('sha256').update(change.content).digest('hex') !== change.afterHash) throw new Error('生成内容超限或摘要不一致。');
  }
  return { owned: value.ownedPaths.length, changed: value.changes.length, bytes };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const value = JSON.parse(await readFile(process.argv[2], 'utf8'));
    console.log(JSON.stringify(validateCodegenFixturePreview(value)));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
