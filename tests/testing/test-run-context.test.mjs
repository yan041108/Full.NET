import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtemp, rm, stat, readFile, writeFile, unlink } from 'node:fs/promises';
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
    assert.equal(env.UseSharedCompilation, 'false');
    assert.equal(env.MSBUILDDISABLENODEREUSE, '1');
    assert.ok((await stat(env.TEMP)).isDirectory());
    await new Promise(resolve => setTimeout(resolve, 20));
    assert.equal(testRunEnvironment().TEMP, env.TEMP);
    return env.TEMP;
  })));
  assert.notEqual(paths[0], paths[1]);
  assert.equal(process.env.TEMP, before);
  for (const temporary of paths) {
    assert.equal(path.dirname(path.dirname(temporary)), os.tmpdir());
    await assert.rejects(stat(path.dirname(temporary)), { code: 'ENOENT' });
  }
  await assert.rejects(stat(path.join(root, '.tmp')), { code: 'ENOENT' });
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

test('Windows锁所有者暂时不可读时等待且不抢占', { skip: process.platform !== 'win32', timeout: 10_000 }, async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-lock-read-race-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const lockRoot = path.join(root, 'locks');
  const { mkdir } = await import('node:fs/promises');
  await mkdir(lockRoot);
  const lockFile = path.join(lockRoot, 'heavy.lock');
  const owner = { pid: process.pid, token: 'fixture-owner' };
  await writeFile(lockFile, JSON.stringify(owner));
  const holderCommand = '& { param([string]$lockPath) $held = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None); try { [Console]::Out.WriteLine("held"); Start-Sleep -Milliseconds 500 } finally { $held.Dispose() } } ';
  const holder = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', holderCommand + "'" + lockFile.replaceAll("'", "''") + "'"], { windowsHide: true });
  t.after(() => { if (holder.exitCode === null) holder.kill(); });
  let ready;
  const held = new Promise(resolve => { ready = resolve; });
  holder.stdout.on('data', value => { if (value.toString().includes('held')) ready(); });
  let holderError = '';
  holder.stderr.on('data', value => { holderError += value.toString(); });
  const completed = new Promise((resolve, reject) => {
    holder.on('error', reject);
    holder.on('close', code => code === 0 ? resolve() : reject(new Error('holder exit ' + code + ': ' + holderError)));
  });
  await held;
  const { withTestRun } = await import('../../scripts/testing/test-run-context.mjs');
  let ran = false, waited = false;
  // 先捕获异常，避免旧实现提前失败时产生未处理的Promise拒绝。
  const contender = withTestRun({ cwd: path.join(root, 'contender'), heavy: true, lockRoot, timeoutMs: 4000,
    onWait: () => { waited = true; } }, async () => { ran = true; }).then(() => null, error => error);
  await completed;
  assert.equal(ran, false);
  assert.deepEqual(JSON.parse(await readFile(lockFile, 'utf8')), owner);
  await unlink(lockFile);
  assert.equal(await contender, null);
  assert.equal(waited, true);
  assert.equal(ran, true);
});

test('Windows独占创建遇已有锁的EPERM会等待，锁不存在的EPERM仍失败', { skip: process.platform !== 'win32', timeout: 15_000 }, async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-lock-create-eperm-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const { mkdir } = await import('node:fs/promises');
  const preload = path.join(root, 'fault.cjs');
  await writeFile(preload, `const fs=require('node:fs/promises');const {syncBuiltinESMExports}=require('node:module');
    const original=fs.open;let injected=false;
    fs.open=async (...args)=>{if((!injected || process.env.FULLNET_TEST_FAULT_MODE==='persist') && args[0]===process.env.FULLNET_TEST_FAULT_LOCK && args[1]==='wx'){
      injected=true;const error=new Error('simulated Windows collision');error.code='EPERM';throw error;}return original(...args)};
    syncBuiltinESMExports();`);
  const module = new URL('../../scripts/testing/test-run-context.mjs', import.meta.url).href;
  const code = `import {withTestRun} from ${JSON.stringify(module)};
    await withTestRun({cwd:process.argv[1],lockRoot:process.argv[2],heavy:true,timeoutMs:4000,onWait:()=>process.stdout.write('WAIT\\n')},
      async()=>process.stdout.write('ENTER\\n'));`;
  const lockRoot = path.join(root, 'locks'); await mkdir(lockRoot);
  const lockFile = path.join(lockRoot, 'heavy.lock');
  const owner = { pid: process.pid, token: 'test-owned-existing-lock' };
  await writeFile(lockFile, JSON.stringify(owner));
  function start(cwd, locks, file, mode = 'once') {
    return spawn(process.execPath, ['--require', preload, '--input-type=module', '-e', code, cwd, locks],
      {env:{...process.env,FULLNET_TEST_FAULT_LOCK:file,FULLNET_TEST_FAULT_MODE:mode},windowsHide:true});
  }
  const child = start(path.join(root, 'existing'), lockRoot, lockFile);
  t.after(() => { if (child.exitCode === null) child.kill(); });
  let stdout = '', stderr = '', resolveWait;
  const waited = new Promise(resolve => { resolveWait = resolve; });
  child.stdout.on('data', value => { stdout += value; if (stdout.includes('WAIT\n')) resolveWait(); });
  child.stderr.on('data', value => { stderr += value; });
  const done = new Promise((resolve,reject)=>{child.on('error',reject);child.on('close',resolve);});
  await Promise.race([waited, new Promise((_,reject)=>setTimeout(()=>reject(new Error('contender did not wait: '+stderr)),5000))]);
  assert.doesNotMatch(stdout, /ENTER/);
  assert.deepEqual(JSON.parse(await readFile(lockFile, 'utf8')), owner);
  await unlink(lockFile);
  assert.equal(await done, 0, stderr);
  assert.match(stdout, /ENTER/);
  const deniedRoot = path.join(root, 'denied'); await mkdir(deniedRoot);
  const denied = start(path.join(root, 'denied-cwd'), deniedRoot, path.join(deniedRoot, 'heavy.lock'), 'persist');
  t.after(() => { if (denied.exitCode === null) denied.kill(); });
  let deniedErr=''; denied.stderr.on('data', value => { deniedErr += value; });
  const deniedCode = await new Promise((resolve,reject)=>{denied.on('error',reject);denied.on('close',resolve);});
  assert.notEqual(deniedCode, 0);
  assert.match(deniedErr, /EPERM/);
  await assert.rejects(stat(path.join(deniedRoot, 'heavy.lock')), {code:'ENOENT'});
});

test('Windows自有准入锁被只读句柄短暂占用时有界重试释放', { skip: process.platform !== 'win32', timeout: 10_000 }, async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-lock-release-race-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const lockRoot = path.join(root, 'locks');
  const { withTestRun } = await import('../../scripts/testing/test-run-context.mjs');
  let heldAt, holderCompleted;
  await withTestRun({ cwd: path.join(root, 'runner'), heavy: true, lockRoot }, async () => {
    const file = path.join(lockRoot, 'heavy.lock');
    const command = '& { param([string]$lockPath) $held = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read); try { [Console]::Out.WriteLine("held"); Start-Sleep -Milliseconds 400 } finally { $held.Dispose() } } ';
    const holder = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', command + "'" + file.replaceAll("'", "''") + "'"], { windowsHide: true });
    const held = new Promise((resolve, reject) => {
      holder.once('error', reject);
      holder.stdout.on('data', value => { if (value.toString().includes('held')) resolve(); });
    });
    holderCompleted = new Promise((resolve, reject) => {
      holder.once('error', reject);
      holder.once('close', code => code === 0 ? resolve() : reject(new Error('Read holder exit ' + code)));
    });
    await held;
    heldAt = Date.now();
  });
  await holderCompleted;
  assert.ok(Date.now() - heldAt >= 350, '应等待真实文件共享句柄释放');
  await assert.rejects(stat(path.join(lockRoot, 'heavy.lock')), { code: 'ENOENT' });
  // 新运行必须还能正常准入；不能只是隐藏释放错误后报告成功。
  await withTestRun({ cwd: path.join(root, 'next'), heavy: true, lockRoot }, async () => {});
});

test('释放自有锁时所有者改变必须失败且保留新所有者记录', async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-lock-release-owner-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const lockRoot = path.join(root, 'locks'), file = path.join(lockRoot, 'heavy.lock');
  const replacement = { pid: process.pid, token: 'replacement-owner' };
  const { withTestRun } = await import('../../scripts/testing/test-run-context.mjs');
  await assert.rejects(withTestRun({ cwd: path.join(root, 'runner'), heavy: true, lockRoot }, async () => {
    await writeFile(file, JSON.stringify(replacement));
  }), /所有者已改变/);
  assert.deepEqual(JSON.parse(await readFile(file, 'utf8')), replacement);
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
  const workspace = withTestRun({ cwd: path.join(root, 'same'), lockRoot, build: false }, async () => {
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
