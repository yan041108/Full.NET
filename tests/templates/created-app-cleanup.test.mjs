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

test('cleanup preserves the original error alongside a workspace removal failure', async t => {
  const value = fixture(t); const originalError = new Error('original-build-probe'); const removalError = new Error('removal-probe');
  await assert.rejects(cleanupCreatedApp({ ...value, originalError }, { removeWorkspace(path, options) {
    assert.equal(path, value.workspace); assert.equal(options.maxRetries, 5); throw removalError;
  } }), error => error instanceof AggregateError && error.errors[0] === originalError && error.errors[1] === removalError);
  assert.deepEqual(value.events, ['db', 'redis']);
});

test('browser shutdown failure does not prevent application and dependency cleanup', async t => {
  const value = fixture(t);
  await assert.rejects(cleanupCreatedApp({ ...value, browserRuntime: { async close() { throw new Error('browser-probe'); } } }), /browser-probe/u);
  assert.deepEqual(value.events, ['db', 'redis']); assert.equal(existsSync(value.workspace), false);
});

test('dependency failure is reported after attempting the other dependency and workspace', async t => {
  const value = fixture(t);
  value.dbContainer.stop = async () => { value.events.push('db-failed'); throw new Error('dependency-probe'); };
  await assert.rejects(cleanupCreatedApp(value), /dependency-probe/u);
  assert.deepEqual(value.events, ['db-failed', 'redis']); assert.equal(existsSync(value.workspace), false);
});

test('a still running application retains its workspace after failed shutdown', async t => {
  const value = fixture(t, { shutdownError: true }); value.apiProcess.pid = process.pid;
  await assert.rejects(cleanupCreatedApp(value), error => error instanceof AggregateError
    && error.errors.some(item => item.message.includes('workspace is retained')));
  assert.deepEqual(value.events, ['db', 'redis']); assert.equal(existsSync(value.workspace), true);
});
