import assert from 'node:assert/strict';
import test from 'node:test';
import { spawn } from 'node:child_process';
import { createWriteStream, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { crashLoggedWorker, isOutboxDrained } from './support/worker-crash-lifecycle.mjs';
import * as workerLifecycle from './support/worker-crash-lifecycle.mjs';
import { stopLoggedProcess } from '../e2e/admin-real-stack/scripts/stop-logged-process.mjs';

test('强制退出自有进程并等待日志落盘，不能走优雅停机回调', { timeout: 15_000 }, async () => {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-worker-crash-'));
  const log = join(root, 'worker.log'); const stream = createWriteStream(log);
  const child = spawn(process.execPath, ['-e', "process.on('SIGTERM',()=>process.stdout.write('graceful'));process.stdout.write('ready');setInterval(()=>{},1000)"],
    { windowsHide: true, stdio: 'pipe' });
  child.stdout.pipe(stream, { end: false }); child.stderr.pipe(stream, { end: false });
  try {
    await new Promise((resolve, reject) => { child.stdout.once('data', resolve); child.once('error', reject); });
    const receipt = await crashLoggedWorker(child, stream);
    assert.equal(receipt.pid, child.pid); assert.equal(receipt.signal, 'SIGKILL');
    assert.equal(readFileSync(log, 'utf8'), 'ready'); assert.equal(stream.writableFinished, true);
  } finally {
    await stopLoggedProcess(child, stream); rmSync(root, { recursive: true, force: true });
  }
});

test('缺少自有进程或误传当前进程时拒绝注入', async () => {
  await assert.rejects(crashLoggedWorker(null), /owned child process/u);
  await assert.rejects(crashLoggedWorker({ pid: process.pid }), /owned child process/u);
});

test('已退出的 Worker 不能被登记为本次成功注入', async () => {
  let killed = false;
  await assert.rejects(crashLoggedWorker({ pid: process.pid + 1, exitCode: 0, signalCode: null,
    kill: () => { killed = true; } }), /already exited/u);
  assert.equal(killed, false);
});

test('信号未发送成功不能伪造恢复证据', async () => {
  await assert.rejects(crashLoggedWorker({ pid: process.pid + 1, exitCode: null, signalCode: null,
    kill: signal => { assert.equal(signal, 'SIGKILL'); return false; } }), /not accepted/u);
});

test('发布排空必须同时排除待处理、重试、活动租约与死信', () => {
  const empty = { pendingCount: 0, dueRetryCount: 0, activeLeaseCount: 0, deadLetterCount: 0 };
  assert.equal(isOutboxDrained(empty), true);
  assert.equal(isOutboxDrained(Object.fromEntries(Object.keys(empty).map(key => [key, '0']))), true);
  for (const key of Object.keys(empty)) {
    assert.equal(isOutboxDrained({ ...empty, [key]: 1 }), false, key);
    assert.equal(isOutboxDrained({ ...empty, [key]: undefined }), false, key);
  }
  assert.equal(isOutboxDrained(null), false); assert.equal(isOutboxDrained({}), false);
});

test('重启基线必须包含排空时正常补投的迟到待办通知', async () => {
  const calls = []; const inbox = { items: [{ id: 'completed' }] }; let drains = 0;
  const empty = { pendingCount: 0, dueRetryCount: 0, activeLeaseCount: 0, deadLetterCount: 0 };
  const result = await workerLifecycle.verifyDrainedWorkerRestart({
    drain: async () => { calls.push('drain'); if (++drains === 1) inbox.items.push({ id: 'late-todo' }); return empty; },
    readInbox: async () => { calls.push('read'); return inbox; },
    restart: async () => { calls.push('restart'); },
  });
  assert.deepEqual(calls, ['drain', 'read', 'restart', 'drain', 'read']);
  assert.deepEqual(result.messageIds, ['completed', 'late-todo']);
});

test('排空后重启真正新增的通知仍失败，即使客户端复用同一快照对象', async () => {
  const inbox = { items: [{ id: 'completed' }] };
  const empty = { pendingCount: 0, dueRetryCount: 0, activeLeaseCount: 0, deadLetterCount: 0 };
  await assert.rejects(() => workerLifecycle.verifyDrainedWorkerRestart({
    drain: async () => empty, readInbox: async () => inbox,
    restart: async () => { inbox.items.push({ id: 'duplicate-completed' }); },
  }), /restart duplicated notifications/u);
});

test('任一重启前积压未排空时不能读取基线或注入强杀', async () => {
  const empty = { pendingCount: 0, dueRetryCount: 0, activeLeaseCount: 0, deadLetterCount: 0 };
  for (const key of Object.keys(empty)) {
    let read = false; let restarted = false;
    await assert.rejects(() => workerLifecycle.verifyDrainedWorkerRestart({
      drain: async () => ({ ...empty, [key]: 1 }),
      readInbox: async () => { read = true; }, restart: async () => { restarted = true; },
    }), /before restart is not drained/u);
    assert.equal(read, false); assert.equal(restarted, false);
  }
});

test('重启后积压未排空时不能提前比较消息集合', async () => {
  let drains = 0; let reads = 0;
  const empty = { pendingCount: 0, dueRetryCount: 0, activeLeaseCount: 0, deadLetterCount: 0 };
  await assert.rejects(() => workerLifecycle.verifyDrainedWorkerRestart({
    drain: async () => ++drains === 1 ? empty : { ...empty, activeLeaseCount: 1 },
    readInbox: async () => { reads++; return { items: [{ id: 'completed' }] }; }, restart: async () => {},
  }), /after restart is not drained/u);
  assert.equal(reads, 1);
});

for (const phase of ['before', 'after']) {
  test(`排空${phase}阶段出现重复终态通知时必须拒绝，不能只比较重启前后ID`, async () => {
    const inbox = { items: [{ id: 'completed', kind: 'final' }] }; let restarted = false;
    const empty = { pendingCount: 0, dueRetryCount: 0, activeLeaseCount: 0, deadLetterCount: 0 };
    await assert.rejects(() => workerLifecycle.verifyDrainedWorkerRestart({
      drain: async current => { if (current === phase) inbox.items.push({ id: 'duplicate', kind: 'final' }); return empty; },
      readInbox: async () => inbox, restart: async () => { restarted = true; },
      validateInbox: page => assert.equal(page.items.filter(item => item.kind === 'final').length, 1, 'terminal notification must be unique'),
    }), /terminal notification must be unique/u);
    assert.equal(restarted, phase === 'after');
  });
}
