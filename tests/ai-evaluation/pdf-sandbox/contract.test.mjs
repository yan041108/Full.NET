import assert from 'node:assert/strict';
import test from 'node:test';
import { runPdfSandbox } from '../../../scripts/ai/pdf-sandbox.mjs';

test('mutable image tags and oversized input cannot create a parser', async () => {
  for (const imageId of ['latest', 'fullnet/pdf:approved', 'sha256:short']) {
    await assert.rejects(runPdfSandbox({ imageId, input: Buffer.from('%PDF-1.7\n') }), { message: 'fn_ai_pdf_sandbox_invalid_input' });
  }
  await assert.rejects(runPdfSandbox({ imageId: `sha256:${'a'.repeat(64)}`, input: Buffer.alloc(5 * 1024 * 1024 + 1) }), { message: 'fn_ai_pdf_sandbox_invalid_input' });
});
test('resource deadlines cannot be enlarged or disabled', async () => {
  for (const timeoutMs of [0, -1, 30001, NaN, Infinity]) {
    await assert.rejects(runPdfSandbox({ imageId: `sha256:${'a'.repeat(64)}`, input: Buffer.from('x'), timeoutMs }), { message: 'fn_ai_pdf_sandbox_invalid_input' });
  }
});
test('already cancelled calls do not access Docker', async () => {
  const result = await runPdfSandbox({ imageId: `sha256:${'a'.repeat(64)}`, input: Buffer.from('x'), signal: AbortSignal.abort() });
  assert.equal(result.code, 'fn_ai_pdf_sandbox_cancelled');
  assert.equal(result.output, undefined);
  assert.equal(result.containerId, undefined);
});
