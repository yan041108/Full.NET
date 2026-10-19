import { AsyncLocalStorage } from 'node:async_hooks';
import { createHash, randomUUID } from 'node:crypto';
import { mkdir, mkdtemp, open, readFile, realpath, rm, unlink, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { acquireBuildResources, buildSlotCount } from './build-resource-pool.mjs';

const context = new AsyncLocalStorage();
export const testRunEnvironment = () => context.getStore()?.env ?? process.env;
export const concentratedAcceptanceTimeout = 45 * 60_000 + 30_000;

/** 保持工作区归属，将编译名额切换为重型验收；不能同时占名额再等待自身排空。 */
export async function enterHeavyTestPhase() {
  const run = context.getStore();
  if (!run?.enterHeavy) throw new Error('重型阶段需要已加锁的测试运行上下文。');
  await run.enterHeavy();
  if (process.env.DOTNET_PROCESSOR_COUNT === undefined) delete run.env.DOTNET_PROCESSOR_COUNT;
  else run.env.DOTNET_PROCESSOR_COUNT = process.env.DOTNET_PROCESSOR_COUNT;
}

/** 排队属于外层预算；取得资源后才开始原来的独立应用执行预算。 */
export async function runConcentratedAcceptance(testContext, action, options = {}) {
  const { executionTimeoutMs = 15 * 60_000, ...runOptions } = options;
  return withTestRun({ cwd: process.cwd(), heavy: true, signal: testContext.signal, ...runOptions }, async () => {
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

async function acquire(file, { signal, deadline, onWait, metadata = {} }) {
  let announced = false;
  const token = randomUUID();
  while (true) {
    signal?.throwIfAborted();
    if (Date.now() >= deadline) throw new Error(`等待测试资源超时：${file}`);
    try {
      const handle = await open(file, 'wx');
      try { await handle.writeFile(JSON.stringify({ ...metadata, pid: process.pid, token })); }
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

/** 同输出工作区互斥；独立构建按预算并行，重型验收等待构建排空后独占。 */
export async function withTestRun({ cwd, heavy = false, lock = true, lockRoot,
  build = !heavy, buildBudget, resourceProbe,
  signal, timeoutMs = 30 * 60_000, onWait = file => process.stdout.write(`等待测试资源：${file}\n`) }, action) {
  signal?.throwIfAborted();
  if (context.getStore()) throw new Error('测试运行上下文不能嵌套；资源由最外层入口拥有。');
  // 仅运行期临时数据进入系统临时目录，不为每轮验收在工作树积累副本。
  await mkdir(cwd, { recursive: true });
  const runRoot = await mkdtemp(path.join(os.tmpdir(), 'fullnet-test-run-'));
  const temporary = path.join(runRoot, 'temp');
  await mkdir(temporary, { recursive: true });
  const releases = [];
  const startedAt = new Date().toISOString();
  let outcome = 'failed';
  let admittedBudget;
  let enterHeavy;
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
      const claimHeavy = async phaseDeadline => {
        releases.push(await acquire(path.join(lockRoot, 'heavy.lock'), { signal, deadline: phaseDeadline, onWait }));
        // 先关闭准入，再排空所有构建；运行中的任务继续到自己的安全边界。
        for (let slot = 1; slot <= buildSlotCount; slot += 1) {
          releases.push(await acquire(path.join(lockRoot, `build-${slot}.lock`), { signal, deadline: phaseDeadline, onWait }));
        }
      };
      let buildRelease, transition;
      enterHeavy = () => transition ??= (async () => {
        if (heavy) return;
        if (buildRelease) {
          await buildRelease();
          releases.splice(releases.indexOf(buildRelease), 1);
        }
        heavy = true;
        // 第二阶段重新计算排队预算，先前编译时间不能吃掉等待真实栈资源的时间。
        await claimHeavy(Date.now() + timeoutMs);
      })();
      if (heavy) {
        await claimHeavy(deadline);
      } else if (build) {
        const admission = await acquireBuildResources({ lockRoot, acquire, signal, deadline, onWait, buildBudget, resourceProbe });
        releases.push(admission.release);
        buildRelease = admission.release;
        admittedBudget = admission.budget;
      }
    }
    signal?.throwIfAborted();
    // 编译服务和 MSBuild 节点不能跨运行持有独立 TEMP 中的 DLL，否则 Windows 无法安全清理。
    const value = await context.run({ enterHeavy, env: { ...process.env, TEMP: temporary, TMP: temporary, TMPDIR: temporary,
      ...(admittedBudget ? { DOTNET_PROCESSOR_COUNT: String(admittedBudget.cpu) } : {}),
      UseSharedCompilation: 'false', MSBUILDDISABLENODEREUSE: '1' } }, action);
    outcome = 'passed';
    return value;
  } finally {
    try {
      await writeFile(path.join(runRoot, 'run.json'), JSON.stringify({ startedAt, finishedAt: new Date().toISOString(), outcome, heavy, buildBudget: admittedBudget }) + '\n');
    } finally {
      // 先释放构建名额，再打开重型准入门；并行释放会让新任务读到未清理的名额。
      const results = [];
      for (const release of releases.reverse()) {
        try { await release(); results.push({ status: 'fulfilled' }); }
        catch (reason) { results.push({ status: 'rejected', reason }); }
      }
      const failure = results.find(result => result.status === 'rejected');
      if (outcome === 'passed' && !failure) await rm(runRoot, { recursive: true, force: true });
      else process.stderr.write(`本轮失败记录保存在系统临时目录：${runRoot}\n`);
      if (failure) throw failure.reason;
    }
  }
}
