import assert from 'node:assert/strict';
import test from 'node:test';
import { runDocxSandbox } from '../../../scripts/ai/docx-sandbox.mjs';

test('mutable image tags and oversized input cannot create a parser', async () => {
  for (const imageId of ['latest', 'fullnet/docx:approved', 'sha256:short']) {
    await assert.rejects(runDocxSandbox({ imageId, input: Buffer.from('%DOCX-1.7\n') }), { message: 'fn_ai_docx_sandbox_invalid_input' });
  }
  await assert.rejects(runDocxSandbox({ imageId: `sha256:${'a'.repeat(64)}`, input: Buffer.alloc(5 * 1024 * 1024 + 1) }), { message: 'fn_ai_docx_sandbox_invalid_input' });
});
test('resource deadlines cannot be enlarged or disabled', async () => {
  for (const timeoutMs of [0, -1, 30001, NaN, Infinity]) {
    await assert.rejects(runDocxSandbox({ imageId: `sha256:${'a'.repeat(64)}`, input: Buffer.from('x'), timeoutMs }), { message: 'fn_ai_docx_sandbox_invalid_input' });
  }
});
test('already cancelled calls do not access Docker', async () => {
  const result = await runDocxSandbox({ imageId: `sha256:${'a'.repeat(64)}`, input: Buffer.from('x'), signal: AbortSignal.abort() });
  assert.equal(result.code, 'fn_ai_docx_sandbox_cancelled');
  assert.equal(result.output, undefined);
  assert.equal(result.containerId, undefined);
});
