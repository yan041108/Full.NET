import assert from 'node:assert/strict';
import test from 'node:test';
import { spawn } from 'node:child_process';
import { createWriteStream, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { crashLoggedWorker } from './support/worker-crash-lifecycle.mjs';
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
