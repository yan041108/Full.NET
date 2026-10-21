import { execFile } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { promisify } from 'node:util';

const exec = promisify(execFile);
export const buildSlotCount = 3;
const GiB = 1024 ** 3;
let sample;

/** 提交内存可能先于物理内存耗尽；Windows 必须同时采样，失败不静默放行。 */
export async function probeBuildResources() {
  if (sample && Date.now() - sample.at < 2000) return sample.value;
  const value = { cpuCount: os.availableParallelism(), freePhysicalGiB: os.freemem() / GiB,
    freeCommitGiB: os.freemem() / GiB };
  if (process.platform === 'win32') {
    const { stdout } = await exec('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command',
      '$m = Get-CimInstance Win32_OperatingSystem; [Console]::Write(($m.FreeVirtualMemory * 1KB).ToString([Globalization.CultureInfo]::InvariantCulture))'],
    { windowsHide: true, timeout: 15000, maxBuffer: 4096 });
    value.freeCommitGiB = Number(stdout.trim()) / GiB;
  }
  // WMI 在负载下可能需要数秒；返回前重采物理可用量，不能用等待前的旧值准入。
  value.freePhysicalGiB = os.freemem() / GiB;
  sample = { at: Date.now(), value };
  return value;
}

/** 单机策略统一放在锁目录；上限只允许 1～3，调小不会终止已经进入的任务。 */
async function policyAt(lockRoot) {
  let policy = {};
  try { policy = JSON.parse(await readFile(path.join(lockRoot, 'build-policy.json'), 'utf8')); }
  catch (error) { if (error.code !== 'ENOENT') throw error; }
  const maxParallelBuilds = policy.maxParallelBuilds ?? buildSlotCount;
  if (!Number.isInteger(maxParallelBuilds) || maxParallelBuilds < 1 || maxParallelBuilds > buildSlotCount) {
    throw new Error('maxParallelBuilds 必须为 1～3。');
  }
  return maxParallelBuilds;
}

/** 活动预算采用完整预留值，避免刚启动但尚未分配内存的进程造成过量准入。 */
export function selectBuildBudget(resources, active, requested = {}) {
  for (const name of ['cpuCount', 'freePhysicalGiB', 'freeCommitGiB']) {
    if (!Number.isFinite(resources[name]) || resources[name] < 0) throw new Error(`构建资源采样无效：${name}`);
  }
  if (!Number.isInteger(resources.cpuCount) || resources.cpuCount < 1) throw new Error('CPU 采样无效。');
  const cpu = requested.cpu ?? Math.max(1, Math.min(4, resources.cpuCount - 2));
  const memoryGiB = requested.memoryGiB ?? 6;
  if (!Number.isInteger(cpu) || cpu < 1 || !Number.isFinite(memoryGiB) || memoryGiB <= 0) {
    throw new Error('构建预算必须提供正整数 cpu 和正数 memoryGiB。');
  }
  const reservedCpu = active.reduce((sum, item) => sum + item.cpu, 0);
  const reservedMemory = active.reduce((sum, item) => sum + item.memoryGiB, 0);
  // 至少保留两个逻辑核（小机器保留到能运行一个任务）与 4 GiB，保护桌面和共享服务。
  const availableCpu = Math.max(1, resources.cpuCount - 2);
  const memory = Math.min(resources.freePhysicalGiB, resources.freeCommitGiB);
  return reservedCpu + cpu <= availableCpu && reservedMemory + memoryGiB + 4 <= memory
    ? { cpu, memoryGiB } : null;
}

async function activeSlots(lockRoot) {
  const active = [];
  for (let slot = 1; slot <= buildSlotCount; slot += 1) {
    try {
      const owner = JSON.parse(await readFile(path.join(lockRoot, `build-${slot}.lock`), 'utf8'));
      if (!Number.isInteger(owner.pid) || !owner.token || !Number.isInteger(owner.cpu) || owner.cpu < 1
        || !Number.isFinite(owner.memoryGiB) || owner.memoryGiB <= 0) throw new Error('构建名额记录无效，需检查归属后处理。');
      try { process.kill(owner.pid, 0); }
      catch (error) {
        if (error.code === 'ESRCH') throw new Error(`构建名额进程 ${owner.pid} 已退出；确认子进程后处理遗留锁，不能自动抢占。`);
        if (error.code !== 'EPERM') throw error;
      }
      active.push({ ...owner, slot });
    } catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
  return active;
}

/** heavy.lock 同时充当短暂准入门；重型验收持有它时新构建不能进入。 */
export async function acquireBuildResources({ lockRoot, acquire, signal, deadline, onWait,
  buildBudget, resourceProbe = probeBuildResources }) {
  let announced = false;
  while (true) {
    const gate = await acquire(path.join(lockRoot, 'heavy.lock'), { signal, deadline, onWait });
    try {
      const maximum = await policyAt(lockRoot);
      const active = await activeSlots(lockRoot);
      if (active.length < maximum) {
        const budget = selectBuildBudget(await resourceProbe(), active, buildBudget);
        signal?.throwIfAborted();
        if (budget) {
          const slot = Array.from({ length: buildSlotCount }, (_, index) => index + 1)
            .find(index => !active.some(owner => owner.slot === index));
          const release = await acquire(path.join(lockRoot, `build-${slot}.lock`),
            { signal, deadline, onWait, metadata: budget });
          return { release, budget };
        }
      }
    } finally { await gate(); }
    if (!announced) { onWait?.('构建名额或 CPU/内存预算'); announced = true; }
    if (Date.now() >= deadline) throw new Error('等待测试资源超时：构建预算');
    await delay(Math.min(250, Math.max(1, deadline - Date.now())), undefined, { signal });
  }
}
