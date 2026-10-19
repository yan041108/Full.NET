import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, mkdtemp, readdir, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { enterHeavyTestPhase, testRunEnvironment, withTestRun } from '../../scripts/testing/test-run-context.mjs';
import { selectBuildBudget } from '../../scripts/testing/build-resource-pool.mjs';
import { parseBuildArguments } from '../../scripts/testing/run-build-with-resources.mjs';

const abundant = async () => ({ cpuCount: 20, freePhysicalGiB: 48, freeCommitGiB: 48 });

test('独立输出最多三个构建同时进入，第四个等到名额释放', { timeout: 5000 }, async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-build-pool-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const lockRoot = path.join(root, 'locks');
  let active = 0, peak = 0, entered, release;
  const ready = new Promise(resolve => { entered = resolve; });
  const hold = new Promise(resolve => { release = resolve; });
  const running = [1, 2, 3].map(id => withTestRun({
    cwd: path.join(root, String(id)), lockRoot, resourceProbe: abundant, onWait: () => {}
  }, async () => {
    active += 1; peak = Math.max(peak, active);
    if (active === 3) entered();
    try { await hold; }
    finally { active -= 1; }
  }));
  await ready;
  let waited, fourthEntered = false;
  const waiting = new Promise(resolve => { waited = resolve; });
  const fourth = withTestRun({ cwd: path.join(root, 'four'), lockRoot, resourceProbe: abundant,
    onWait: waited }, async () => { fourthEntered = true; });
  try { await waiting; assert.equal(fourthEntered, false); }
  finally { release(); }
  await Promise.all([...running, fourth]);
  assert.equal(peak, 3);
  assert.equal(fourthEntered, true);
  assert.deepEqual(await readdir(lockRoot), []);
});

test('按 CPU、物理内存和提交内存准入，不能只看总内存', () => {
  const host = { cpuCount: 20, freePhysicalGiB: 48, freeCommitGiB: 48 };
  const active = [{ cpu: 4, memoryGiB: 6 }, { cpu: 4, memoryGiB: 6 }];
  assert.deepEqual(selectBuildBudget(host, active), { cpu: 4, memoryGiB: 6 });
  assert.equal(selectBuildBudget({ ...host, cpuCount: 10 }, active), null);
  assert.equal(selectBuildBudget({ ...host, freePhysicalGiB: 21 }, active), null);
  assert.equal(selectBuildBudget({ ...host, freeCommitGiB: 9 }, []), null);
  assert.equal(selectBuildBudget(host, [], { cpu: 4, memoryGiB: 48 }), null);
  assert.throws(() => selectBuildBudget(host, [], { cpu: 0 }), /预算/);
  assert.throws(() => selectBuildBudget({ ...host, freeCommitGiB: NaN }, []), /采样/);
});

test('可用预算仅够两个构建时第三个排队，取消后释放工作区锁', { timeout: 5000 }, async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-two-builds-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const lockRoot = path.join(root, 'locks');
  let entered = 0, ready, release;
  const both = new Promise(resolve => { ready = resolve; });
  const hold = new Promise(resolve => { release = resolve; });
  const probe = async () => ({ cpuCount: 10, freePhysicalGiB: 48, freeCommitGiB: 48 });
  const running = [1, 2].map(id => withTestRun({ cwd: path.join(root, String(id)), lockRoot,
    resourceProbe: probe, onWait: () => {} }, async () => {
    if (++entered === 2) ready();
    await hold;
  }));
  await both;
  const controller = new AbortController();
  try {
    await assert.rejects(withTestRun({ cwd: path.join(root, 'three'), lockRoot, resourceProbe: probe,
      signal: controller.signal, onWait: () => controller.abort() }, () => assert.fail('预算不足不能进入')));
  } finally { release(); await Promise.all(running); }
  await withTestRun({ cwd: path.join(root, 'three'), lockRoot, resourceProbe: probe }, async () => {});
  assert.deepEqual(await readdir(lockRoot), []);
});

test('重型验收先等构建排空，持有期间新构建不能进入', { timeout: 5000 }, async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-heavy-build-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const lockRoot = path.join(root, 'locks'), events = [];
  let ready, release, heavyReady, releaseHeavy, waiting;
  const started = new Promise(resolve => { ready = resolve; });
  const hold = new Promise(resolve => { release = resolve; });
  const heavyStarted = new Promise(resolve => { heavyReady = resolve; });
  const heavyHold = new Promise(resolve => { releaseHeavy = resolve; });
  const heavyWaiting = new Promise(resolve => { waiting = resolve; });
  const first = withTestRun({ cwd: path.join(root, 'first'), lockRoot, resourceProbe: abundant }, async () => {
    events.push('build'); ready(); await hold; events.push('build-end');
  });
  await started;
  const heavy = withTestRun({ cwd: path.join(root, 'heavy'), heavy: true, lockRoot, onWait: waiting }, async () => {
    events.push('heavy'); heavyReady(); await heavyHold; events.push('heavy-end');
  });
  await heavyWaiting; release(); await heavyStarted;
  let queued;
  const queue = new Promise(resolve => { queued = resolve; });
  const next = withTestRun({ cwd: path.join(root, 'next'), lockRoot, resourceProbe: abundant, onWait: queued }, async () => {
    events.push('next');
  });
  await queue; releaseHeavy();
  await Promise.all([first, heavy, next]);
  assert.deepEqual(events, ['build', 'build-end', 'heavy', 'heavy-end', 'next']);
});

test('非法共享并发策略拒绝启动，锁仍能正确释放', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-build-policy-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const lockRoot = path.join(root, 'locks');
  await mkdir(lockRoot);
  await writeFile(path.join(lockRoot, 'build-policy.json'), JSON.stringify({ maxParallelBuilds: 4 }));
  await assert.rejects(withTestRun({ cwd: root, lockRoot, resourceProbe: abundant }, () => assert.fail()), /1～3/);
  assert.deepEqual(await readdir(lockRoot), ['build-policy.json']);
});

test('四个真实 Node 进程共享三个名额，释放后第四个才进入', { timeout: 10000 }, async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-build-processes-'));
  const children = [];
  t.after(async () => {
    for (const child of children) if (child.exitCode === null) child.kill();
    await rm(root, { recursive: true, force: true });
  });
  const module = new URL('../../scripts/testing/test-run-context.mjs', import.meta.url).href;
  const code = `import {withTestRun} from ${JSON.stringify(module)};
    await withTestRun({cwd:process.argv[1],lockRoot:process.argv[2],
      resourceProbe:async()=>({cpuCount:20,freePhysicalGiB:48,freeCommitGiB:48}),
      onWait:()=>process.send('waiting')},async()=>{
        process.send('entered'); await new Promise(resolve=>process.once('message',resolve));
    }); process.disconnect();`;
  function launch(name) {
    const child = spawn(process.execPath, ['--input-type=module', '-e', code, path.join(root, name), path.join(root, 'locks')],
      { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe', 'ipc'] });
    children.push(child);
    let entered, waiting;
    const result = { entered: new Promise(resolve => { entered = resolve; }),
      waiting: new Promise(resolve => { waiting = resolve; }), inside: false, child };
    child.on('message', message => {
      if (message === 'entered') { result.inside = true; entered(); }
      if (message === 'waiting') waiting();
    });
    result.finished = new Promise((resolve, reject) => {
      child.once('error', reject);
      child.once('close', code => code === 0 ? resolve() : reject(new Error(`进程退出 ${code}`)));
    });
    return result;
  }
  const first = [launch('one'), launch('two'), launch('three')];
  await Promise.all(first.map(child => child.entered));
  const fourth = launch('four');
  await fourth.waiting;
  assert.equal(fourth.inside, false);
  first[0].child.send('release');
  await first[0].finished; await fourth.entered;
  for (const child of [...first.slice(1), fourth]) child.child.send('release');
  await Promise.all([...first, fourth].map(child => child.finished));
});

test('同工作区仍互斥，运行环境传递申请的编译并行度', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-same-output-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const { testRunEnvironment } = await import('../../scripts/testing/test-run-context.mjs');
  let active = 0, peak = 0;
  await Promise.all([1, 2].map(() => withTestRun({ cwd: root, lockRoot: path.join(root, 'locks'),
    resourceProbe: abundant, buildBudget: { cpu: 6, memoryGiB: 12 }, onWait: () => {} }, async () => {
    assert.equal(testRunEnvironment().DOTNET_PROCESSOR_COUNT, '6');
    peak = Math.max(peak, ++active);
    await new Promise(resolve => setTimeout(resolve, 40));
    active -= 1;
  })));
  assert.equal(peak, 1);
});

test('前台构建入口保留参数边界并拒绝未知选项', () => {
  const result = parseBuildArguments(['--cpu', '4', '--memory-gib', '12', '--', 'dotnet', 'build', 'path with spaces.csproj']);
  assert.deepEqual(result.options.buildBudget, { cpu: 4, memoryGiB: 12 });
  assert.equal(result.command, 'dotnet');
  assert.deepEqual(result.args, ['build', 'path with spaces.csproj']);
  assert.throws(() => parseBuildArguments(['--unknown', '2', '--', 'dotnet']), /未知/);
  assert.throws(() => parseBuildArguments(['--cpu', '--']), /缺少/);
});

test('编译转重型阶段先归还自身名额，保留工作区锁并恢复运行时 CPU 配额', { timeout: 5000 }, async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-build-promote-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const lockRoot = path.join(root, 'locks'), events = [];
  let ready, release, transitionWaiting;
  const started = new Promise(resolve => { ready = resolve; });
  const hold = new Promise(resolve => { release = resolve; });
  const waiting = new Promise(resolve => { transitionWaiting = resolve; });
  const other = withTestRun({ cwd: path.join(root, 'other'), lockRoot, resourceProbe: abundant }, async () => {
    ready(); await hold; events.push('other-end');
  });
  await started;
  const current = withTestRun({ cwd: path.join(root, 'current'), lockRoot,
    resourceProbe: abundant, onWait: file => { if (file.includes('build-')) transitionWaiting(); } }, async () => {
    assert.equal(testRunEnvironment().DOTNET_PROCESSOR_COUNT, '4');
    events.push('compiled');
    await enterHeavyTestPhase();
    assert.equal(testRunEnvironment().DOTNET_PROCESSOR_COUNT, process.env.DOTNET_PROCESSOR_COUNT);
    events.push('heavy');
  });
  await waiting; release();
  await Promise.all([other, current]);
  assert.deepEqual(events, ['compiled', 'other-end', 'heavy']);
  assert.deepEqual(await readdir(lockRoot), []);
});

test('提交内存不足时等待超时而不启动构建，失败后预算和锁均释放', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-build-low-memory-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const lockRoot = path.join(root, 'locks');
  await assert.rejects(withTestRun({ cwd: root, lockRoot, timeoutMs: 40, onWait: () => {},
    resourceProbe: async () => ({ cpuCount: 20, freePhysicalGiB: 48, freeCommitGiB: 5 }) },
  () => assert.fail('提交内存不足不能启动')), /等待测试资源超时/);
  assert.deepEqual(await readdir(lockRoot), []);
  await assert.rejects(withTestRun({ cwd: root, lockRoot, resourceProbe: abundant },
    async () => { throw new Error('模拟构建失败'); }), /模拟构建失败/);
  await withTestRun({ cwd: root, lockRoot, resourceProbe: abundant }, async () => {});
  assert.deepEqual(await readdir(lockRoot), []);
});
