import assert from 'node:assert/strict';
import test from 'node:test';
import { runDocumentSandbox as rawRun, validParserArguments } from '../../scripts/ai/document-sandbox.mjs';
const parserArguments = ['--format', 'csv', '--encoding', 'utf-8', '--delimiter', 'comma', '--header', 'no', '--hidden', 'exclude'];
const runDocumentSandbox = options => rawRun({ parserArguments, ...options });

test('mutable image tags and oversized input cannot create a parser', async () => {
  for (const imageId of ['latest', 'fullnet/document:approved', 'sha256:short']) {
    await assert.rejects(runDocumentSandbox({ imageId, input: Buffer.from('%DOCX-1.7\n') }), { message: 'fn_ai_document_sandbox_invalid_input' });
  }
  await assert.rejects(runDocumentSandbox({ imageId: `sha256:${'a'.repeat(64)}`, input: Buffer.alloc(8 * 1024 * 1024 + 1) }), { message: 'fn_ai_document_sandbox_invalid_input' });
});
test('resource deadlines cannot be enlarged or disabled', async () => {
  for (const timeoutMs of [0, -1, 120001, NaN, Infinity]) {
    await assert.rejects(runDocumentSandbox({ imageId: `sha256:${'a'.repeat(64)}`, input: Buffer.from('x'), timeoutMs }), { message: 'fn_ai_document_sandbox_invalid_input' });
  }
});
test('already cancelled calls do not access Docker', async () => {
  const result = await runDocumentSandbox({ imageId: `sha256:${'a'.repeat(64)}`, input: Buffer.from('x'), signal: AbortSignal.abort() });
  assert.equal(result.code, 'fn_ai_document_sandbox_cancelled');
  assert.equal(result.output, undefined);
  assert.equal(result.containerId, undefined);
});

test('format arguments are closed and cannot become paths, URLs or runtime options', () => {
  assert.equal(validParserArguments(parserArguments), true);
  for (const values of [[], [...parserArguments, '--url', 'https://invalid.example'],
    parserArguments.with(1, '/private/input'), parserArguments.with(3, 'utf-16'), parserArguments.with(7, 'maybe')])
    assert.equal(validParserArguments(values), false);
});
