import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { PassThrough } from 'node:stream';
import test from 'node:test';
import { executeDocumentSandboxCli } from '../../scripts/ai/document-sandbox-cli.mjs';

const bytes = Buffer.from('%DOCX-1.7\n');
const output = Buffer.from('{"protocolVersion":1}');
const args = ['--image-id', `sha256:${'a'.repeat(64)}`, '--content-hash', createHash('sha256').update(bytes).digest('hex'),
  '--size-bytes', String(bytes.length), '--input-mode', 'framed-v1', '--format', 'csv'];
const accepted = () => ({ code: 'ok', output, outputBytes: output.length, errorBytes: 0, exitCode: 0,
  stopped: true, cleaned: true, policyVerified: true });

test('framed CLI starts from exact source length while stdin remains open', async () => {
  const input = new PassThrough(); let calls = 0;
  const work = executeDocumentSandboxCli(args, input, { runSandbox: async options => {
    calls++; assert.deepEqual(options.input, bytes); assert.equal(input.readableEnded, false); return accepted();
  } });
  input.write(bytes.subarray(0, 3)); input.write(bytes.subarray(3));
  const result = await work; input.destroy();
  assert.equal(result.code, 'ok'); assert.equal(result.output, output); assert.equal(calls, 1);
});

for (const close of ['eof', 'destroy', 'extra']) {
  test(`framed CLI ${close} cancels parsing and waits for owned cleanup`, async () => {
    const input = new PassThrough(); let entered, release, aborted;
    const started = new Promise(resolve => { entered = resolve; });
    const cancelled = new Promise(resolve => { aborted = resolve; });
    const cleanup = new Promise(resolve => { release = resolve; });
    let completed = false;
    const work = executeDocumentSandboxCli(args, input, { runSandbox: async ({ signal }) => {
      entered(); signal.addEventListener('abort', aborted, { once: true }); await cancelled; await cleanup;
      return { code: 'fn_ai_document_sandbox_cancelled', stopped: true, cleaned: true };
    } }).then(result => { completed = true; return result; });
    input.write(bytes);
    await Promise.race([started, work.then(() => { input.destroy(); assert.fail('隔离执行器必须先启动'); })]);
    if (close === 'eof') input.end(); else if (close === 'destroy') input.destroy(); else input.write(Buffer.from('C'));
    await cancelled; assert.equal(completed, false); release();
    const result = await work; input.destroy();
    assert.equal(result.code, 'fn_ai_document_sandbox_cancelled'); assert.equal(result.output, undefined);
  });
}

test('framed CLI validates source digest before touching the sandbox', async () => {
  const input = new PassThrough(); const changed = [...args]; changed[3] = 'b'.repeat(64);
  const work = executeDocumentSandboxCli(changed, input, { runSandbox: async () => { assert.fail('来源不符不得启动解析器'); } });
  input.write(bytes); const result = await work; input.destroy();
  assert.equal(result.code, 'fn_ai_document_sandbox_source_mismatch'); assert.equal(result.output, undefined);
});

test('framed CLI deadline covers an incomplete source frame', async () => {
  const input = new PassThrough();
  const result = await executeDocumentSandboxCli([...args, '--timeout-ms', '50'], input, { runSandbox: async () => assert.fail('不完整输入不得派发') });
  input.destroy(); assert.equal(result.code, 'fn_ai_document_sandbox_timeout');
});

test('framed CLI rejects unknown input modes and removes control listeners', async () => {
  const input = new PassThrough(); const changed = [...args]; changed[changed.length - 1] = 'other';
  assert.equal((await executeDocumentSandboxCli(changed, input)).code, 'fn_ai_document_sandbox_invalid_input');
  assert.equal(input.listenerCount('data'), 0); assert.equal(input.listenerCount('end'), 0); input.destroy();
});
