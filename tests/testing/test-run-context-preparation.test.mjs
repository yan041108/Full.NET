import assert from 'node:assert/strict';
import { mkdtemp, readdir, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { setTimeout as delay } from 'node:timers/promises';
import * as runs from '../../scripts/testing/test-run-context.mjs';

async function workspace(t) {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-prepare-budget-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  return { cwd: root, lockRoot: path.join(root, 'locks'), onWait: () => {} };
}

test('集中验收准备不消耗执行预算，准备执行清理始终持有同一组锁', async t => {
  const options = await workspace(t);
  const events = [];
  let temporary;
  await runs.runPreparedConcentratedAcceptance(t, async execute => {
    temporary = runs.testRunEnvironment().TEMP;
    events.push('prepare');
    assert.equal((await readdir(options.lockRoot)).filter(name => name.endsWith('.lock')).length, 2);
    await delay(250);
    try {
      await execute(async () => {
        assert.equal(runs.testRunEnvironment().TEMP, temporary);
        events.push('execute');
        await delay(10);
      });
    } finally {
      await delay(20);
      events.push('cleanup');
      assert.equal(runs.testRunEnvironment().TEMP, temporary);
      assert.equal((await readdir(options.lockRoot)).filter(name => name.endsWith('.lock')).length, 2);
    }
  }, { ...options, preparationTimeoutMs: 2000, executionTimeoutMs: 100 });
  assert.deepEqual(events, ['prepare', 'execute', 'cleanup']);
  assert.deepEqual(await readdir(options.lockRoot), []);
});

test('准备失败不进入执行且等待清理后释放锁', async t => {
  const options = await workspace(t);
  let executed = false;
  let cleaned = false;
  await assert.rejects(runs.runPreparedConcentratedAcceptance(t, async execute => {
    try {
      await Promise.reject(new Error('expected preparation failure'));
      await execute(async () => { executed = true; });
    } finally {
      await delay(20);
      cleaned = true;
      assert.equal((await readdir(options.lockRoot)).filter(name => name.endsWith('.lock')).length, 2);
    }
  }, options), /expected preparation failure/);
  assert.equal(cleaned, true);
  assert.equal(executed, false);
  assert.deepEqual(await readdir(options.lockRoot), []);
});

test('准备超过自己的期限后禁止开始执行', async t => {
  const options = await workspace(t);
  let executed = false;
  await assert.rejects(runs.runPreparedConcentratedAcceptance(t, async execute => {
    await delay(60);
    await execute(async () => { executed = true; });
  }, { ...options, preparationTimeoutMs: 20 }), /准备.*超时/);
  assert.equal(executed, false);
  assert.deepEqual(await readdir(options.lockRoot), []);
});

test('没有进入执行的准备不能登记成功', async t => {
  const options = await workspace(t);
  await assert.rejects(runs.runPreparedConcentratedAcceptance(t, async () => {}, options), /没有进入.*执行/);
  assert.deepEqual(await readdir(options.lockRoot), []);
});

test('同一个准备只允许进入一次执行', async t => {
  const options = await workspace(t);
  let count = 0;
  await assert.rejects(runs.runPreparedConcentratedAcceptance(t, async execute => {
    await execute(async () => { count++; });
    await execute(async () => { count++; });
  }, options), /只能进入一次/);
  assert.equal(count, 1);
  assert.deepEqual(await readdir(options.lockRoot), []);
});

test('准备期间取消后不再开始执行', async t => {
  const options = await workspace(t);
  const controller = new AbortController();
  let executed = false;
  await assert.rejects(runs.runPreparedConcentratedAcceptance(t, async execute => {
    controller.abort(new Error('cancel during preparation'));
    await execute(async () => { executed = true; });
  }, { ...options, signal: controller.signal }), /cancel during preparation/);
  assert.equal(executed, false);
  assert.deepEqual(await readdir(options.lockRoot), []);
});

test('调用方未等待执行时仍等待在途操作后才释放锁', async t => {
  const options = await workspace(t);
  let finished = false;
  await runs.runPreparedConcentratedAcceptance(t, execute => {
    execute(async () => {
      await delay(40);
      assert.equal((await readdir(options.lockRoot)).filter(name => name.endsWith('.lock')).length, 2);
      finished = true;
    });
  }, options);
  assert.equal(finished, true);
  assert.deepEqual(await readdir(options.lockRoot), []);
});

test('准备期限到达时取消在途异步准备并等待清理', async t => {
  const options = await workspace(t);
  let cleaned = false;
  await assert.rejects(runs.runPreparedConcentratedAcceptance(t, async (_execute, preparation) => {
    try {
      await delay(10_000, undefined, { signal: preparation.signal });
    } finally {
      await delay(20);
      assert.equal((await readdir(options.lockRoot)).filter(name => name.endsWith('.lock')).length, 2);
      cleaned = true;
    }
  }, { ...options, preparationTimeoutMs: 50 }), /准备.*超时/);
  assert.equal(cleaned, true);
  assert.deepEqual(await readdir(options.lockRoot), []);
});

test('同步准备子进程沿用剩余预算且逾期不启动下一步', async t => {
  const options = await workspace(t);
  const started = performance.now();
  let nextStarted = false;
  await assert.rejects(runs.runPreparedConcentratedAcceptance(t, async (_execute, preparation) => {
    preparation.run(process.execPath, ['-e', 'setTimeout(() => {}, 10000)'], { encoding: 'utf8', timeout: 10_000 });
    nextStarted = true;
  }, { ...options, preparationTimeoutMs: 250 }), /准备.*超时/);
  assert.equal(nextStarted, false);
  assert.ok(performance.now() - started < 5000, '没有等待子进程原来的十秒超时');
  assert.deepEqual(await readdir(options.lockRoot), []);
});

test('进入执行后停止准备计时，执行使用自己的预算', async t => {
  const options = await workspace(t);
  await runs.runPreparedConcentratedAcceptance(t, async (execute, preparation) => {
    await execute(async () => {
      await delay(100);
      assert.equal(preparation.signal.aborted, false);
      assert.throws(preparation.check, /准备已经结束/);
    });
  }, { ...options, preparationTimeoutMs: 50, executionTimeoutMs: 1000 });
  assert.deepEqual(await readdir(options.lockRoot), []);
});
