import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtemp, rm, stat } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';

test('并行运行的 TEMP/TMP 独立且不改调用进程环境', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-run-context-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const { withTestRun, testRunEnvironment } = await import('../../scripts/testing/test-run-context.mjs');
  const before = process.env.TEMP;
  const paths = await Promise.all([1, 2].map(() => withTestRun({ cwd: root, lock: false }, async () => {
    const env = testRunEnvironment();
    assert.equal(env.TEMP, env.TMP);
    assert.ok((await stat(env.TEMP)).isDirectory());
    await new Promise(resolve => setTimeout(resolve, 20));
    assert.equal(testRunEnvironment().TEMP, env.TEMP);
    return env.TEMP;
  })));
  assert.notEqual(paths[0], paths[1]);
  assert.equal(process.env.TEMP, before);
});

test('集中验收的子进程禁用常驻编译服务，普通测试保持调用环境', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-disposable-build-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const { withTestRun, runConcentratedAcceptance, testRunEnvironment } = await import('../../scripts/testing/test-run-context.mjs');
  const names = ['UseSharedCompilation', 'MSBUILDDISABLENODEREUSE', 'DOTNET_CLI_USE_MSBUILD_SERVER'];
  const before = names.map(name => process.env[name]);
  await runConcentratedAcceptance(t, async () => {
    const env = testRunEnvironment();
    const values = await new Promise((resolve, reject) => {
      const child = spawn(process.execPath, ['-e', `process.stdout.write(JSON.stringify(${JSON.stringify(names)}.map(name => process.env[name])))`],
        { env, windowsHide: true });
      let output = '';
      child.stdout.on('data', value => { output += value; });
      child.on('error', reject);
      child.on('close', code => code === 0 ? resolve(JSON.parse(output)) : reject(new Error(`child exit ${code}`)));
    });
    assert.deepEqual(values, ['false', '1', '0']);
    assert.equal(env.TEMP, env.TMP);
  }, { cwd: root, lock: false });
  assert.deepEqual(names.map(name => process.env[name]), before);
  await withTestRun({ cwd: root, lock: false }, async () => {
    assert.deepEqual(names.map(name => testRunEnvironment()[name]), before);
  });
});

test('共享重型锁跨工作区串行且失败后释放', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-run-lock-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const { withTestRun } = await import('../../scripts/testing/test-run-context.mjs');
  const events = [];
  const lockRoot = path.join(root, 'locks');
  let acquired;
  const ready = new Promise(resolve => { acquired = resolve; });
  const first = withTestRun({ cwd: path.join(root, 'one'), heavy: true, lockRoot }, async () => {
    events.push('first');
    acquired();
    await new Promise(resolve => setTimeout(resolve, 80));
    events.push('release');
    throw new Error('expected failure');
  });
  // 锁不承诺申请顺序；确认首个任务持有资源后，再验证竞争者等待释放。
  await ready;
  const second = withTestRun({ cwd: path.join(root, 'two'), heavy: true, lockRoot }, async () => {
    events.push('second');
  });
  const results = await Promise.allSettled([first, second]);
  assert.equal(results.filter(item => item.status === 'rejected').length, 1);
  assert.deepEqual(events, ['first', 'release', 'second']);
});

test('等待资源可取消，不能开始被取消的验收', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-run-cancel-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const { withTestRun } = await import('../../scripts/testing/test-run-context.mjs');
  const controller = new AbortController();
  controller.abort();
  let ran = false;
  await assert.rejects(withTestRun({ cwd: root, signal: controller.signal }, async () => { ran = true; }));
  assert.equal(ran, false);
});

test('两个真实 Node 进程使用同机重型锁，后者等待前者释放', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-process-lock-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const module = new URL('../../scripts/testing/test-run-context.mjs', import.meta.url).href;
  const code = `import { withTestRun } from ${JSON.stringify(module)};
    await withTestRun({cwd:process.argv[1],heavy:true,lockRoot:process.argv[2],onWait:()=>{}},async()=>{
      process.stdout.write('entered\\n');
      await new Promise(resolve=>setTimeout(resolve,300));
      process.stdout.write('finished\\n');
    });`;
  const events = [];
  function child(name) {
    const process = spawn(globalThis.process.execPath, ['--input-type=module', '-e', code, path.join(root, name), path.join(root, 'locks')], { windowsHide: true });
    let ready;
    const entered = new Promise(resolve => { ready = resolve; });
    process.stdout.on('data', value => {
      for (const line of value.toString().trim().split('\n')) { events.push(`${name}:${line}`); if (line === 'entered') ready(); }
    });
    const completed = new Promise((resolve, reject) => {
      process.on('error', reject);
      process.on('exit', code => code === 0 ? resolve() : reject(new Error(`child exit ${code}`)));
    });
    return { entered, completed };
  }
  const first = child('first');
  await first.entered;
  const second = child('second');
  await Promise.all([first.completed, second.completed]);
  assert.deepEqual(events, ['first:entered', 'first:finished', 'second:entered', 'second:finished']);
});

test('等待中的取消释放已持有的工作区锁，后续运行仍可进入', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-lock-wait-cancel-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const { withTestRun } = await import('../../scripts/testing/test-run-context.mjs');
  const lockRoot = path.join(root, 'locks');
  let entered;
  const ready = new Promise(resolve => { entered = resolve; });
  let finish;
  const first = withTestRun({ cwd: path.join(root, 'one'), heavy: true, lockRoot }, async () => {
    entered(); await new Promise(resolve => { finish = resolve; });
  });
  await ready;
  const controller = new AbortController();
  const second = withTestRun({ cwd: path.join(root, 'two'), heavy: true, lockRoot, signal: controller.signal,
    onWait: () => controller.abort() }, () => assert.fail('取消后不能开始验收'));
  await assert.rejects(second);
  finish(); await first;
  await withTestRun({ cwd: path.join(root, 'two'), heavy: true, lockRoot }, async () => {});
});

test('集中验收排队耗时不占执行预算', { timeout: 5000 }, async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-acceptance-budget-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const { withTestRun, runConcentratedAcceptance } = await import('../../scripts/testing/test-run-context.mjs');
  const lockRoot = path.join(root, 'locks');
  let acquired;
  const ready = new Promise(resolve => { acquired = resolve; });
  const holder = withTestRun({ cwd: path.join(root, 'holder'), heavy: true, lockRoot }, async () => {
    acquired(); await new Promise(resolve => setTimeout(resolve, 400));
  });
  await ready;
  await runConcentratedAcceptance(t, async () => {
    await new Promise(resolve => setTimeout(resolve, 30));
  }, { cwd: path.join(root, 'run'), lockRoot, executionTimeoutMs: 200 });
  await holder;
});

test('先后等待工作区锁和重型锁共享一个排队截止时间', { timeout: 5000 }, async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-two-lock-budget-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const { withTestRun } = await import('../../scripts/testing/test-run-context.mjs');
  const lockRoot = path.join(root, 'locks');
  let readyWorkspace, readyHeavy, releaseWorkspace, releaseHeavy;
  const workspaceReady = new Promise(resolve => { readyWorkspace = resolve; });
  const heavyReady = new Promise(resolve => { readyHeavy = resolve; });
  const workspace = withTestRun({ cwd: path.join(root, 'same'), lockRoot }, async () => {
    readyWorkspace(); await new Promise(resolve => { releaseWorkspace = resolve; });
  });
  const heavy = withTestRun({ cwd: path.join(root, 'other'), heavy: true, lockRoot }, async () => {
    readyHeavy(); await new Promise(resolve => { releaseHeavy = resolve; });
  });
  await Promise.all([workspaceReady, heavyReady]);
  const firstTimer = setTimeout(() => releaseWorkspace(), 180);
  const secondTimer = setTimeout(() => releaseHeavy(), 450);
  try {
    await assert.rejects(withTestRun({ cwd: path.join(root, 'same'), heavy: true, lockRoot, timeoutMs: 300, onWait: () => {} },
      async () => {}), /等待测试资源超时/);
  } finally {
    clearTimeout(firstTimer); clearTimeout(secondTimer);
    releaseWorkspace(); releaseHeavy(); await Promise.all([workspace, heavy]);
  }
});
