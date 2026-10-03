import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { EventEmitter, once } from 'node:events';
import { PassThrough, Writable } from 'node:stream';
import test from 'node:test';
import { stopLoggedProcess } from './stop-logged-process.mjs';

function fixture({ requiresForce = false, neverCloses = false } = {}) {
  const child = new EventEmitter();
  Object.assign(child, { killed: false, exitCode: null, signalCode: null,
    stdout: new PassThrough(), stderr: new PassThrough() });
  const chunks = [];
  const errors = [];
  const signals = [];
  const log = new Writable({ write(chunk, encoding, done) { chunks.push(chunk.toString()); done(); } });
  log.on('error', error => errors.push(error));
  child.stdout.pipe(log, { end: false });
  child.stderr.pipe(log, { end: false });
  child.kill = (signal = 'SIGTERM') => {
    child.killed = true;
    signals.push(signal);
    if (neverCloses || (requiresForce && signal !== 'SIGKILL')) return true;
    setImmediate(() => {
      child.stdout.write('shutdown-start\n');
      child.exitCode = 0;
      child.emit('exit', 0, signal);
      setImmediate(() => {
        child.stderr.write('shutdown-final\n');
        child.stdout.end();
        child.stderr.end();
        child.emit('close', 0, signal);
      });
    });
    return true;
  };
  return { child, log, chunks, errors, signals };
}

test('shutdown waits for close and drains both output streams before ending the log', async () => {
  const value = fixture();
  await stopLoggedProcess(value.child, value.log);
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(value.errors, []);
  assert.equal(value.chunks.join(''), 'shutdown-start\nshutdown-final\n');
  assert.equal(value.log.writableFinished, true);
});

test('a previously signalled process is still awaited until stdio closes', async () => {
  const value = fixture();
  value.child.kill();
  await stopLoggedProcess(value.child, value.log);
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(value.errors, []);
  assert.equal(value.chunks.join(''), 'shutdown-start\nshutdown-final\n');
  assert.deepEqual(value.signals, ['SIGTERM']);
});

test('an already closed process only flushes its remaining log', async () => {
  const value = fixture();
  value.child.stdout.end();
  value.child.stderr.end();
  await Promise.all([once(value.child.stdout, 'end'), once(value.child.stderr, 'end')]);
  value.child.exitCode = 0;
  await stopLoggedProcess(value.child, value.log);
  assert.equal(value.log.writableFinished, true);
  assert.deepEqual(value.signals, []);
});

test('a noncooperative process is force killed and its close is awaited', async () => {
  const value = fixture({ requiresForce: true });
  await stopLoggedProcess(value.child, value.log, { timeoutMs: 10, forceTimeoutMs: 50 });
  assert.deepEqual(value.signals, ['SIGTERM', 'SIGKILL']);
  assert.deepEqual(value.errors, []);
  assert.equal(value.chunks.join(''), 'shutdown-start\nshutdown-final\n');
});

test('a process that never closes fails within the bound and detaches the log safely', async () => {
  const value = fixture({ neverCloses: true });
  await assert.rejects(stopLoggedProcess(value.child, value.log, { timeoutMs: 10, forceTimeoutMs: 10 }), /did not close/u);
  value.child.stdout.write('late output');
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(value.errors, []);
  assert.equal(value.log.writableFinished, true);
  value.child.stdout.destroy();
  value.child.stderr.destroy();
});

test('a real Node child closes and flushes its log without a teardown stream error', async t => {
  const child = spawn(process.execPath, ['-e', `
    process.on('SIGTERM', () => {
      process.stderr.write('shutdown-final\\n');
      setTimeout(() => process.exit(0), 10);
    });
    console.log('ready');
    setInterval(() => {}, 1000);
  `], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
  t.after(() => {
    if (child.exitCode === null && child.signalCode === null) child.kill('SIGKILL');
  });
  const chunks = [];
  const errors = [];
  const log = new Writable({ write(chunk, encoding, done) { chunks.push(chunk.toString()); done(); } });
  log.on('error', error => errors.push(error));
  child.stdout.pipe(log, { end: false });
  child.stderr.pipe(log, { end: false });
  await once(child.stdout, 'data');
  await stopLoggedProcess(child, log);
  assert.equal(log.writableFinished, true);
  assert.equal(child.exitCode !== null || child.signalCode !== null, true);
  assert.match(chunks.join(''), /ready/u);
  // Windows进程终止不执行SIGTERM处理器，其他平台必须保留真实停机末尾日志。
  if (process.platform !== 'win32') assert.match(chunks.join(''), /shutdown-final/u);
  assert.deepEqual(errors, []);
});
