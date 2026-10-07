import assert from 'node:assert/strict';
import test from 'node:test';
import childProcess from 'node:child_process';
import { EventEmitter } from 'node:events';
import { PassThrough } from 'node:stream';
import { syncBuiltinESMExports } from 'node:module';
import { promisify } from 'node:util';

// 仅本测试文件的独立 Node 测试进程替换 Docker 控制客户端，确定性覆盖故障分支。
// 真实禁网、强杀、watchdog 与 OOM 仍由 run-native.mjs 证明，模拟不充当原生证据。
const imageId = `sha256:${'a'.repeat(64)}`;
const id = 'b'.repeat(64);
let simulation;
const originalExec = childProcess.execFile;
const originalSpawn = childProcess.spawn;
const fakeExec = () => { throw new Error('use promisified control'); };
fakeExec[promisify.custom] = async (_command, args) => {
  const state = simulation;
  state.commands.push(args);
  if (args[0] === 'image') return { stdout: JSON.stringify([{ Id: imageId, Config: { Env: [], Volumes: null } }]) };
  if (args[0] === 'create') {
    const labels = Object.fromEntries(args.flatMap((value, index) => value === '--label' ? [args[index + 1].split('=')] : []));
    state.container = { Id: id, Image: imageId, Name: `/${args[args.indexOf('--name') + 1]}`,
      Config: { Labels: labels, User: '65532:65532', Env: [], Volumes: null }, Mounts: [],
      HostConfig: { NetworkMode: 'none', ReadonlyRootfs: true, Privileged: false, Memory: 536870912,
        MemorySwap: 536870912, NanoCpus: 2000000000, PidsLimit: 32, IpcMode: 'none', LogConfig: { Type: 'none' },
        CapDrop: ['ALL'], SecurityOpt: ['no-new-privileges'], Ulimits: [{ Name: 'nofile', Soft: 64, Hard: 64 }] },
      State: { Running: false, ExitCode: 0, OOMKilled: false } };
    if (state.abortOnCreate) state.controller.abort();
    if (state.lostCreateResponse) throw Object.assign(new Error('transport lost'), { code: 'ETIMEDOUT' });
    return { stdout: `${id}\n` };
  }
  if (args[0] === 'container') {
    if (!state.container) throw Object.assign(new Error('missing'), { code: 1, stderr: 'No such container' });
    return { stdout: JSON.stringify([state.container]) };
  }
  if (args[0] === 'kill') {
    if (state.watchdogBeforeKill || state.falseStoppedResponse) {
      if (state.watchdogBeforeKill) {
        state.container.State = { Running: false, ExitCode: 137, OOMKilled: false };
        state.child?.kill();
      }
      state.watchdogBeforeKill = false; state.falseStoppedResponse = false;
      throw Object.assign(new Error('already stopped'), { code: 1, stderr: `container ${id} is not running` });
    }
    if (state.failKillOnce) { state.failKillOnce = false; throw new Error('daemon refused kill'); }
    state.container.State = { Running: false, ExitCode: 137, OOMKilled: false };
    state.child?.kill();
    return { stdout: id };
  }
  if (args[0] === 'rm') {
    if (state.failRemove) throw new Error('daemon refused remove');
    assert.equal(state.container.State.Running, false);
    state.container = undefined;
    return { stdout: id };
  }
  throw new Error('unexpected control');
};
childProcess.execFile = fakeExec;
childProcess.spawn = (_command, args) => {
  assert.deepEqual(args, ['start', '--attach', '--interactive', id]);
  const state = simulation;
  const child = new EventEmitter();
  child.stdin = new PassThrough();
  child.stdout = new PassThrough();
  child.stderr = new PassThrough();
  let closed = false;
  child.kill = () => {
    if (closed) return;
    closed = true;
    setImmediate(() => child.emit('close', 137));
  };
  state.child = child;
  state.container.State.Running = true;
  setImmediate(() => {
    if (closed) return;
    child.stdout.write('started\n');
    if (state.abortAfterStart) state.controller.abort();
  });
  return child;
};
syncBuiltinESMExports();
const { runDocxSandbox } = await import('../../../scripts/ai/docx-sandbox.mjs');
test.after(() => {
  childProcess.execFile = originalExec;
  childProcess.spawn = originalSpawn;
  syncBuiltinESMExports();
});

test('cancel during container creation never yields output and removes only its own container', async () => {
  simulation = { commands: [], controller: new AbortController(), abortOnCreate: true };
  const result = await runDocxSandbox({ imageId, input: Buffer.from('hang\n'), signal: simulation.controller.signal });
  assert.equal(result.code, 'fn_ai_docx_sandbox_cancelled');
  assert.equal(result.output, undefined);
  assert.equal(result.stopped, true);
  assert.equal(result.cleaned, true);
  assert.equal(simulation.container, undefined);
  for (const args of simulation.commands.filter(args => args[0] === 'kill' || args[0] === 'rm')) assert.equal(args.at(-1), id);
});
test('a rejected kill remains failure even if later cleanup confirms termination', async () => {
  simulation = { commands: [], controller: new AbortController(), abortAfterStart: true, failKillOnce: true };
  const result = await runDocxSandbox({ imageId, input: Buffer.from('hang\n'), signal: simulation.controller.signal });
  assert.equal(result.code, 'fn_ai_docx_sandbox_stop_unconfirmed');
  assert.equal(result.output, undefined);
  assert.equal(result.cleaned, true);
  assert.equal(simulation.container, undefined);
});
test('failed removal discards all otherwise available output', async () => {
  simulation = { commands: [], controller: new AbortController(), abortAfterStart: true, failRemove: true };
  const result = await runDocxSandbox({ imageId, input: Buffer.from('hang\n'), signal: simulation.controller.signal });
  assert.equal(result.code, 'fn_ai_docx_sandbox_cleanup_failed');
  assert.equal(result.output, undefined);
  assert.equal(result.cleaned, false);
  assert.equal(simulation.container.State.Running, false);
});
test('lost create response recovers only internally owned identity and stays failed', async () => {
  simulation = { commands: [], lostCreateResponse: true };
  const result = await runDocxSandbox({ imageId, input: Buffer.from('hang\n') });
  assert.equal(result.code, 'fn_ai_docx_sandbox_control_failed');
  assert.equal(result.output, undefined);
  assert.equal(result.cleaned, true);
  assert.equal(simulation.container, undefined);
  assert.equal(simulation.commands.some(args => args[0] === 'kill'), false);
});

test('watchdog termination between inspect and kill still confirms the owned stopped container', async () => {
  simulation = { commands: [], controller: new AbortController(), abortAfterStart: true, watchdogBeforeKill: true };
  const result = await runDocxSandbox({ imageId, input: Buffer.from('hang\n'), signal: simulation.controller.signal });
  assert.equal(result.code, 'fn_ai_docx_sandbox_cancelled'); assert.equal(result.output, undefined);
  assert.equal(result.stopped, true); assert.equal(result.cleaned, true); assert.equal(result.exitCode, 137);
});

test('a not-running response alone cannot prove an owned running container stopped', async () => {
  simulation = { commands: [], controller: new AbortController(), abortAfterStart: true, falseStoppedResponse: true };
  const result = await runDocxSandbox({ imageId, input: Buffer.from('hang\n'), signal: simulation.controller.signal });
  assert.equal(result.code, 'fn_ai_docx_sandbox_stop_unconfirmed'); assert.equal(result.output, undefined);
  assert.equal(result.cleaned, true);
});
