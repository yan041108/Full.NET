import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { existsSync, mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { PassThrough, Writable } from 'node:stream';
import test from 'node:test';
import { cleanupCreatedApp } from './support/created-app-cleanup.mjs';

function fixture(t, { shutdownError = false } = {}) {
  const workspace = mkdtempSync(join(tmpdir(), 'fullnet-app-cleanup-'));
  t.after(() => rmSync(workspace, { recursive: true, force: true }));
  const apiProcess = new EventEmitter();
  Object.assign(apiProcess, { killed: false, exitCode: null, signalCode: null,
    stdout: new PassThrough(), stderr: new PassThrough() });
  const events = [];
  const chunks = [];
  const apiLogStream = new Writable({ write(chunk, encoding, done) { chunks.push(chunk.toString()); done(); } });
  apiProcess.stdout.pipe(apiLogStream, { end: false });
  apiProcess.stderr.pipe(apiLogStream, { end: false });
  apiProcess.kill = () => {
    apiProcess.killed = true;
    setImmediate(() => {
      if (shutdownError) apiProcess.emit('error', new Error('shutdown-probe'));
      else {
        apiProcess.stderr.write('application-shutdown-final\n');
        apiProcess.stdout.end();
        apiProcess.stderr.end();
        apiProcess.exitCode = 0;
        apiProcess.emit('close', 0, null);
      }
    });
    return true;
  };
  t.after(() => { apiProcess.stdout.destroy(); apiProcess.stderr.destroy(); });
  const container = name => ({ async stop() {
    events.push(apiLogStream.writableFinished ? name : 'before-log-flush:' + name);
  } });
  return { workspace, apiProcess, apiLogStream, dbContainer: container('db'),
    redisContainer: container('redis'), events, chunks };
}

test('created app cleanup drains shutdown output before stopping dependencies and removing the workspace', async t => {
  const value = fixture(t);
  await cleanupCreatedApp(value);
  assert.equal(value.chunks.join(''), 'application-shutdown-final\n');
  assert.deepEqual(value.events, ['db', 'redis']);
  assert.equal(existsSync(value.workspace), false);
});

test('created app shutdown failure still cleans dependencies and workspace and is reported', async t => {
  const value = fixture(t, { shutdownError: true });
  await assert.rejects(cleanupCreatedApp(value), /shutdown-probe/u);
  assert.deepEqual(value.events, ['db', 'redis']);
  assert.equal(existsSync(value.workspace), false);
});

test('created app cleanup stops its worker before database dependencies', async t => {
  const value = fixture(t);
  const workerProcess = new EventEmitter();
  Object.assign(workerProcess, { killed: false, exitCode: null, signalCode: null,
    stdout: new PassThrough(), stderr: new PassThrough() });
  const workerLogStream = new Writable({ write(chunk, encoding, done) { done(); } });
  workerProcess.stdout.pipe(workerLogStream, { end: false });
  workerProcess.stderr.pipe(workerLogStream, { end: false });
  workerProcess.kill = () => {
    value.events.push('worker-stop');
    setImmediate(() => {
      workerProcess.stdout.end();
      workerProcess.stderr.end();
      workerProcess.exitCode = 0;
      workerProcess.emit('close', 0, null);
    });
    return true;
  };
  t.after(() => { workerProcess.stdout.destroy(); workerProcess.stderr.destroy(); });
  await cleanupCreatedApp({ ...value, workerProcess, workerLogStream });
  assert.deepEqual(value.events, ['worker-stop', 'db', 'redis']);
});
