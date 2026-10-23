import { execFile } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, readFile, readdir, rename, unlink, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { promisify } from 'node:util';
import { diagnosticsBuildPaths } from './build-paths.mjs';

const exec = promisify(execFile);
const digest = value => createHash('sha256').update(value).digest('hex');
const projectDirectory = 'src/Tools/Full.NET.DatabaseDiagnostics.Cli';

async function tree(directory, exclusions = new Set()) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];
  for (const entry of entries.sort((a, b) => a.name.localeCompare(b.name))) {
    if (exclusions.has(entry.name)) continue;
    const name = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...await tree(name, exclusions));
    else files.push(name);
  }
  return files;
}

async function hashFiles(directory, files) {
  const hash = createHash('sha256');
  for (const file of files.sort()) {
    hash.update(path.relative(directory, file).split(path.sep).join('/')).update('\0');
    hash.update(await readFile(file)).update('\0');
  }
  return hash.digest('hex');
}

/** 源码、中央依赖、SDK 与构建入口共同决定当前工具输入；不读取诊断凭据。 */
export async function diagnosticsInputs(root, { sdkVersion } = {}) {
  sdkVersion ??= (await exec('dotnet', ['--version'], { cwd: root, windowsHide: true, timeout: 10000 })).stdout.trim();
  const files = [
    ...await tree(path.join(root, projectDirectory), new Set(['bin', 'obj'])),
    ...await tree(path.join(root, 'scripts/database')),
    // 属性、目标与 NuGet 配置可由 MSBuild 隐式导入，新增或删除也必须失效。
    ...(await readdir(root)).filter(name => /\.(?:props|targets)$/iu.test(name)
      || /^(?:global\.json|nuget\.config|Directory\.Build\.rsp|\.editorconfig)$/iu.test(name))
      .map(name => path.join(root, name))
  ];
  return { source: await hashFiles(root, files), sdkVersion };
}

const recordPath = paths => path.join(paths.output, 'diagnostics-build.json');

/** 开始构建前撤销旧证明；失败或构建过程中源码改变时不能继续运行旧 DLL。 */
export async function invalidateDiagnosticsBuild(paths) {
  try { await unlink(recordPath(paths)); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
}

/** 仅在锁内完成构建并确认输入稳定后，原子登记完整运行目录摘要。 */
export async function recordDiagnosticsBuild(root, before, options = {}) {
  const paths = options.paths ?? diagnosticsBuildPaths(root);
  const after = await diagnosticsInputs(root, options);
  if (JSON.stringify(before) !== JSON.stringify(after)) throw new Error('构建期间诊断工具输入改变，请重新构建。');
  await readFile(paths.dll);
  const output = await hashFiles(path.dirname(paths.dll), await tree(path.dirname(paths.dll)));
  const record = { schemaVersion: 1, ...after, output };
  await mkdir(paths.output, { recursive: true });
  const temporary = `${recordPath(paths)}.${randomUUID()}.tmp`;
  try {
    await writeFile(temporary, JSON.stringify(record) + '\n', { flag: 'wx' });
    await rename(temporary, recordPath(paths));
  } finally {
    try { await unlink(temporary); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
}

/** 启动和测试共用验证，缺少记录、SDK 漂移、源码或运行依赖被改动均拒绝复用。 */
export async function inspectDiagnosticsBuild(root, options = {}) {
  const paths = options.paths ?? diagnosticsBuildPaths(root);
  try {
    const record = JSON.parse(await readFile(recordPath(paths), 'utf8'));
    if (record.schemaVersion !== 1) return { ok: false, reason: 'unverified_build' };
    const current = await diagnosticsInputs(root, options);
    if (record.source !== current.source || record.sdkVersion !== current.sdkVersion)
      return { ok: false, reason: 'inputs_changed' };
    await readFile(paths.dll);
    const output = await hashFiles(path.dirname(paths.dll), await tree(path.dirname(paths.dll)));
    return record.output === output ? { ok: true } : { ok: false, reason: 'outputs_changed' };
  } catch (error) {
    if (error.code === 'ENOENT' || error instanceof SyntaxError) return { ok: false, reason: 'unverified_build' };
    throw error;
  }
}
