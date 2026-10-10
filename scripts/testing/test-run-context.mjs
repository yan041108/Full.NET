import { AsyncLocalStorage } from 'node:async_hooks';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, open, readFile, realpath, unlink, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const context = new AsyncLocalStorage();
export const testRunEnvironment = () => context.getStore()?.env ?? process.env;
export const concentratedAcceptanceTimeout = 45 * 60_000 + 30_000;

/** 排队属于外层预算；取得资源后才开始原来的独立应用执行预算。 */
export async function runConcentratedAcceptance(testContext, action, options = {}) {
  const { executionTimeoutMs = 15 * 60_000, ...runOptions } = options;
  return withTestRun({ cwd: process.cwd(), heavy: true, signal: testContext.signal, ...runOptions, disposableBuild: true }, async () => {
    let pending;
    let completed = false;
    await testContext.test('集中验收执行', { timeout: executionTimeoutMs }, execution => {
      pending = Promise.resolve().then(() => action(execution.signal)).then(value => {
        execution.signal.throwIfAborted();
        completed = true;
        return value;
      });
      return pending;
    });
    // Node 子测试超时可能早于任务清理；清理结束前不能释放共享资源。
    await pending;
    if (!completed) throw new Error('集中验收没有完成，不能登记成功。');
  });
}

async function acquire(file, { signal, deadline, onWait }) {
  let announced = false;
  const token = randomUUID();
  while (true) {
    signal?.throwIfAborted();
    if (Date.now() >= deadline) throw new Error(`等待测试资源超时：${file}`);
    try {
      const handle = await open(file, 'wx');
      try { await handle.writeFile(JSON.stringify({ pid: process.pid, token })); }
      catch (error) { await unlink(file); throw error; }
      finally { await handle.close(); }
      return async () => {
        // 只释放本进程拥有的锁，不能删除另一个运行后来创建的锁。
        const owner = JSON.parse(await readFile(file, 'utf8'));
        if (owner.token !== token) throw new Error('测试资源锁所有者已改变。');
        await unlink(file);
      };
    } catch (error) {
      if (error.code !== 'EEXIST') throw error;
    }
    let owner;
    try { owner = JSON.parse(await readFile(file, 'utf8')); }
    catch (error) { if (error.code !== 'ENOENT' && !(error instanceof SyntaxError)) throw error; }
    if (owner?.pid) {
      try { process.kill(owner.pid, 0); }
      catch (error) {
        if (error.code === 'ESRCH') throw new Error(`测试资源锁进程 ${owner.pid} 已退出；检查遗留子进程/容器后清理 ${file}，不能自动抢占。`);
        if (error.code !== 'EPERM') throw error;
      }
    }
    if (!announced) { onWait?.(file); announced = true; }
    await delay(Math.min(100, Math.max(1, deadline - Date.now())), undefined, { signal });
  }
}

/** 独立 TEMP/TMP 隔离子进程；同工作区运行互斥，重型验收另使用跨工作区的同机锁。 */
export async function withTestRun({ cwd, heavy = false, lock = true, lockRoot,
  signal, disposableBuild = false, timeoutMs = 30 * 60_000, onWait = file => process.stdout.write(`等待测试资源：${file}\n`) }, action) {
  signal?.throwIfAborted();
  if (context.getStore()) throw new Error('测试运行上下文不能嵌套；资源由最外层入口拥有。');
  const runRoot = path.join(cwd, '.tmp', 'test-runs', randomUUID());
  const temporary = path.join(runRoot, 'temp');
  await mkdir(temporary, { recursive: true });
  const releases = [];
  const startedAt = new Date().toISOString();
  let outcome = 'failed';
  try {
    if (lock) {
      lockRoot ??= path.join(os.homedir(), '.fullnet', 'test-resources');
      await mkdir(lockRoot, { recursive: true });
      const canonical = await realpath(cwd);
      const key = process.platform === 'win32' ? canonical.toLowerCase() : canonical;
      const workspace = createHash('sha256').update(key).digest('hex');
      // 所有入口以相同顺序申请锁，避免两个工作区交叉等待。
      // 两个资源共用排队截止时间，不能累加等待后侵占实际验收预算。
      const deadline = Date.now() + timeoutMs;
      releases.push(await acquire(path.join(lockRoot, `${workspace}.lock`), { signal, deadline, onWait }));
      if (heavy) releases.push(await acquire(path.join(lockRoot, 'heavy.lock'), { signal, deadline, onWait }));
    }
    signal?.throwIfAborted();
    // 临时应用结束后必须释放目录；禁止编译服务继续持有 TEMP 内源生成器，普通套件仍保留构建复用。
    const buildEnvironment = disposableBuild ? {
      UseSharedCompilation: 'false', MSBUILDDISABLENODEREUSE: '1', DOTNET_CLI_USE_MSBUILD_SERVER: '0'
    } : {};
    const value = await context.run({ env: { ...process.env, TEMP: temporary, TMP: temporary, TMPDIR: temporary, ...buildEnvironment } }, action);
    outcome = 'passed';
    return value;
  } finally {
    try {
      await writeFile(path.join(runRoot, 'run.json'), JSON.stringify({ startedAt, finishedAt: new Date().toISOString(), outcome, heavy }) + '\n');
    } finally {
      const results = await Promise.allSettled(releases.reverse().map(release => release()));
      const failure = results.find(result => result.status === 'rejected');
      if (failure) throw failure.reason;
    }
  }
}
