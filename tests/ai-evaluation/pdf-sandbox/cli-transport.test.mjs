import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { PassThrough, Writable } from 'node:stream';
import test from 'node:test';
import * as cli from '../../../scripts/ai/pdf-sandbox-cli.mjs';

const { executePdfSandboxCli } = cli;

const bytes = Buffer.from('%PDF-1.7\n');
const args = ['--image-id', `sha256:${'a'.repeat(64)}`, '--content-hash', createHash('sha256').update(bytes).digest('hex'),
  '--size-bytes', String(bytes.length)];
const output = Buffer.from('{"protocolVersion":1}');
const accepted = () => ({ code: 'ok', output, outputBytes: output.length, errorBytes: 0, exitCode: 0,
  stopped: true, cleaned: true, policyVerified: true });
const stream = () => { const input = new PassThrough(); input.end(bytes); return input; };
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

test('CLI hands off unchanged bytes only after successful stop and cleanup', async () => {
  let release; let entered;
  const started = new Promise(resolve => { entered = resolve; });
  const cleanup = new Promise(resolve => { release = resolve; });
  let completed = false;
  const work = executePdfSandboxCli(args, stream(), { runSandbox: async options => {
    assert.deepEqual(options.input, bytes); assert.equal(options.imageId, args[1]);
    assert.ok(options.timeoutMs > 0 && options.timeoutMs <= 30000);
    entered(); await cleanup; return accepted();
  } }).then(result => { completed = true; return result; });
  await started;
  assert.equal(completed, false);
  release();
  const result = await work;
  assert.equal(result.code, 'ok'); assert.equal(result.output, output);
});

test('CLI refuses missing stop, cleanup, policy, clean stderr, exit and complete output evidence', async () => {
  for (const patch of [{ stopped: false }, { cleaned: false }, { policyVerified: false }, { exitCode: 137 },
    { errorBytes: 1 }, { output: Buffer.alloc(0) }, { output: 'private source' }, { outputBytes: output.length + 1 },
    { output: Buffer.alloc(2 * 1024 * 1024 * 6 + 128 * 96 + 513) }]) {
    const result = await executePdfSandboxCli(args, stream(), { runSandbox: async () => ({ ...accepted(), ...patch }) });
    assert.equal(result.code, 'fn_ai_pdf_sandbox_transport_failed'); assert.equal(result.output, undefined);
  }
});

test('CLI preserves cleanup failure and drops source even after a deadline abort', async () => {
  const result = await executePdfSandboxCli([...args, '--timeout-ms', '50'], stream(), { runSandbox: async ({ signal }) => {
    await new Promise(resolve => signal.addEventListener('abort', resolve, { once: true }));
    return { ...accepted(), code: 'fn_ai_pdf_sandbox_cleanup_failed', cleaned: false };
  } });
  assert.equal(result.code, 'fn_ai_pdf_sandbox_cleanup_failed'); assert.equal(result.output, undefined);
});

test('CLI keeps cancellation as the first reason while waiting past deadline for cleanup', async () => {
  const controller = new AbortController();
  const result = await executePdfSandboxCli([...args, '--timeout-ms', '100'], stream(), {
    signal: controller.signal, runSandbox: async ({ signal }) => {
      controller.abort(); assert.equal(signal.aborted, true);
      await delay(150);
      return { code: 'fn_ai_pdf_sandbox_cancelled', stopped: true, cleaned: true };
    }
  });
  assert.equal(result.code, 'fn_ai_pdf_sandbox_cancelled'); assert.equal(result.output, undefined);
});

test('CLI rejects pre-cancellation and source mismatch without calling the sandbox', async () => {
  let calls = 0;
  const runSandbox = async () => { calls++; return accepted(); };
  const cancelled = await executePdfSandboxCli(args, stream(), { signal: AbortSignal.abort(), runSandbox });
  assert.equal(cancelled.code, 'fn_ai_pdf_sandbox_cancelled');
  const mismatch = await executePdfSandboxCli([...args.slice(0, 3), 'b'.repeat(64), ...args.slice(4)], stream(), { runSandbox });
  assert.equal(mismatch.code, 'fn_ai_pdf_sandbox_source_mismatch');
  assert.equal(calls, 0);
});

test('CLI suppresses exceptions containing source, paths or credentials', async () => {
  const result = await executePdfSandboxCli(args, stream(), { runSandbox: async () => { throw new Error('private source /secret token'); } });
  assert.deepEqual(result, { code: 'fn_ai_pdf_sandbox_transport_failed' });
});

test('CLI publication terminates when the caller leaves a full stdout pipe unread', async () => {
  assert.equal(typeof cli.publishPdfSandboxOutput, 'function');
  const unread = new PassThrough({ highWaterMark: 16 });
  const code = await cli.publishPdfSandboxOutput(unread, Buffer.alloc(65536), { timeoutMs: 50 });
  assert.equal(code, 'fn_ai_pdf_sandbox_timeout'); assert.equal(unread.destroyed, true);
});

test('CLI publication responds to cancellation during output backpressure', async () => {
  assert.equal(typeof cli.publishPdfSandboxOutput, 'function');
  const unread = new PassThrough({ highWaterMark: 16 }); const controller = new AbortController();
  const work = cli.publishPdfSandboxOutput(unread, Buffer.alloc(65536), { timeoutMs: 1000, signal: controller.signal });
  controller.abort();
  assert.equal(await work, 'fn_ai_pdf_sandbox_cancelled'); assert.equal(unread.destroyed, true);
});

test('CLI publication confirms complete bytes and rejects a broken destination', async () => {
  assert.equal(typeof cli.publishPdfSandboxOutput, 'function');
  const destination = new PassThrough(); const received = [];
  destination.on('data', bytes => received.push(bytes));
  assert.equal(await cli.publishPdfSandboxOutput(destination, output, { timeoutMs: 1000 }), 'ok');
  assert.deepEqual(Buffer.concat(received), output);
  const broken = new PassThrough({ highWaterMark: 16 });
  const work = cli.publishPdfSandboxOutput(broken, Buffer.alloc(65536), { timeoutMs: 1000 });
  broken.destroy();
  assert.equal(await work, 'fn_ai_pdf_sandbox_transport_failed');
});

test('CLI publication waits for terminal close and rejects a late close error', async () => {
  const destination = new Writable({ write(_bytes, _encoding, callback) { callback(); },
    destroy(_error, callback) { queueMicrotask(() => callback(new Error('private close diagnostic'))); } });
  // RED 时也不让未处理错误中断整个套件；预期失败由发布状态断言表达。
  destination.on('error', () => {});
  assert.equal(await cli.publishPdfSandboxOutput(destination, output, { timeoutMs: 1000 }), 'fn_ai_pdf_sandbox_transport_failed');
});

test('CLI publication rejects cancellation after write completion before terminal close', async () => {
  const controller = new AbortController();
  const destination = new Writable({ write(_bytes, _encoding, callback) { callback(); },
    destroy(_error, callback) { queueMicrotask(() => { controller.abort(); callback(); }); } });
  assert.equal(await cli.publishPdfSandboxOutput(destination, output, { timeoutMs: 1000, signal: controller.signal }),
    'fn_ai_pdf_sandbox_cancelled');
});
