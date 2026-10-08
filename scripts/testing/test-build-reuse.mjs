import { execFile } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdir, readFile, readdir, rename, unlink, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { promisify } from 'node:util';
import { testRunEnvironment } from './test-run-context.mjs';

const exec = promisify(execFile);
const digest = value => createHash('sha256').update(value).digest('hex');

async function inputs(cwd) {
  const { stdout } = await exec('git', ['ls-files', '-z', '--cached', '--others', '--exclude-standard'], {
    cwd, maxBuffer: 32 * 1024 * 1024
  });
  // 保守覆盖全部非文档仓库输入；未跟踪的新源码和删除也参与摘要，不能只检查 HEAD。
  const listed = stdout.split('\0').filter(Boolean);
  // SDK 会隐式加载项目旁的 .user，Git 的 *.user 忽略规则不能使其绕过构建校验。
  // 缺失也登记为输入状态，因此新增、删除和被引用项目的配置变化都能失效。
  const implicitInputs = listed.filter(file => /\.[a-z]*proj$/iu.test(file)).map(file => `${file}.user`);
  const files = [...new Set([...listed, ...implicitInputs])].filter(file =>
    !/^(?:docs|rules|\.tmp|\.cache)\//u.test(file)
      && !(file.endsWith('.md') && !file.includes('/'))
  ).sort();
  const hash = createHash('sha256');
  for (const file of files) {
    hash.update(file).update('\0');
    try { hash.update(digest(await readFile(path.join(cwd, file)))); }
    catch (error) { if (error.code !== 'ENOENT') throw error; hash.update('deleted'); }
  }
  return hash.digest('hex');
}

async function outputs(directory) {
  const hash = createHash('sha256');
  async function visit(root, relative = '') {
    const entries = await readdir(root, { withFileTypes: true });
    for (const entry of entries.sort((left, right) => left.name.localeCompare(right.name))) {
      if (entry.name === 'TestResults') continue;
      const name = path.posix.join(relative, entry.name);
      if (entry.isDirectory()) await visit(path.join(root, entry.name), name);
      else hash.update(name).update('\0').update(digest(await readFile(path.join(root, entry.name))));
    }
  }
  await visit(directory);
  return hash.digest('hex');
}

/** 只复用由本入口成功构建且输入/产物仍一致的记录；verify 模式不自动补构建。 */
export async function prepareTestBuild({ cwd, project, assembly, args, build, mode = 'reuse', sdkVersion }) {
  if (!['reuse', 'verify', 'fresh'].includes(mode)) throw new Error('未知构建复用模式。');
  // pnpm 将测试筛选与缓存模式放入此元数据；从实际构建环境和指纹同时移除。
  // 保留其他 MSBuild 环境属性，且不修改调用进程或测试进程的环境。
  const env = Object.fromEntries(Object.entries(testRunEnvironment()).filter(([name]) =>
    name.toLowerCase() !== 'npm_lifecycle_script'
  ));
  sdkVersion ??= (await exec('dotnet', ['--version'], { cwd, env })).stdout.trim();
  // MSBuild 可以读取任意合法环境属性；只排除每轮隔离的临时目录，不维护易漏项的前缀清单。
  // 环境值仅参与摘要，不写入记录，避免保存凭据。
  const environment = Object.fromEntries(Object.entries(env).filter(([name]) =>
    !/^(?:TEMP|TMP|TMPDIR)$/iu.test(name)
  ).sort(([left], [right]) => left.localeCompare(right)));
  const source = await inputs(cwd);
  const identity = digest(JSON.stringify({ source, sdkVersion, args, environment }));
  const recordPath = path.join(cwd, '.tmp', 'test-builds', `${digest(project)}.json`);
  let record;
  try { record = JSON.parse(await readFile(recordPath, 'utf8')); }
  catch (error) { if (error.code !== 'ENOENT' && !(error instanceof SyntaxError)) throw error; }
  let output;
  if (mode !== 'fresh' && record?.identity === identity) {
    try {
      await readFile(path.join(cwd, assembly));
      output = await outputs(path.dirname(path.join(cwd, assembly)));
    } catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
  if (mode !== 'fresh' && record?.schemaVersion === 1
      && record.identity === identity && record.output === output && output) {
    return { reused: true, recordPath };
  }
  if (mode === 'verify') throw new Error('构建输入或产物已改变/缺少有效记录；先执行 --reuse-build 或正常构建入口。');
  try { await unlink(recordPath); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  await build(env);
  if (await inputs(cwd) !== source) throw new Error('构建期间源码发生变化，不能登记或运行该产物。');
  await readFile(path.join(cwd, assembly));
  output = await outputs(path.dirname(path.join(cwd, assembly)));
  await mkdir(path.dirname(recordPath), { recursive: true });
  const temporary = `${recordPath}.${process.pid}.tmp`;
  await writeFile(temporary, JSON.stringify({ schemaVersion: 1, identity, output }) + '\n');
  await rename(temporary, recordPath);
  return { reused: false, recordPath };
}
