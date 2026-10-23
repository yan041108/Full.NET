import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { runDocumentSandbox } from '../../scripts/ai/document-sandbox.mjs';

const [imageId, probeImageId, outputFile] = process.argv.slice(2);
const args = kind => ['--format', kind, '--encoding', 'utf-8', '--delimiter', 'comma', '--header', 'no', '--hidden', 'exclude'];
const evidence = [];
async function check(name, options, expected, success = false) {
  const result = await runDocumentSandbox({ imageId, parserArguments: args('csv'), ...options });
  assert.equal(result.code, expected, name); assert.equal(result.policyVerified, true, name);
  assert.equal(result.cleaned, true, name); assert.equal(result.stopped, true, name);
  if (!success) assert.equal(result.output, undefined, name);
  evidence.push({ name, code: result.code, cleaned: result.cleaned, stopped: result.stopped, policyVerified: result.policyVerified, oomKilled: result.oomKilled });
}
await check('late-csv-shape', { input: Buffer.from('a,b\nvalid,row\nragged') }, 'fn_ai_parse_csv_shape');
await check('html-no-text', { input: Buffer.from('<script>only code</script>'), parserArguments: args('html') }, 'fn_ai_parse_no_text');
await check('svg-external', { input: Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="100" height="100"><image href="https://invalid.example/x"/></svg>'), parserArguments: args('svg') }, 'fn_ai_parse_svg_active_content');
await check('svg-pixel-bomb', { input: Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" width="1000000" height="1000000"/>'), parserArguments: args('svg') }, 'fn_ai_parse_image_pixel_limit');
for (const [mode, expected] of [['isolation', 'ok'], ['stdout', 'fn_ai_document_sandbox_output_limit'], ['stderr', 'fn_ai_document_sandbox_error_limit'],
  ['timeout', 'fn_ai_document_sandbox_timeout'], ['cancel', 'fn_ai_document_sandbox_cancelled'], ['oom', 'fn_ai_document_sandbox_memory_limit']]) {
  const stop = new AbortController();
  const timer = mode === 'cancel' ? setTimeout(() => stop.abort(), 2000) : undefined;
  try {
    await check(mode, { imageId: probeImageId, input: Buffer.from(mode), signal: stop.signal, timeoutMs: mode === 'timeout' ? 2500 : 120000 }, expected, mode === 'isolation');
    if (mode === 'oom') assert.equal(evidence.at(-1).oomKilled, true);
  } finally { clearTimeout(timer); }
}
await writeFile(outputFile, JSON.stringify(evidence, null, 2) + '\n');
console.log(`Native failure/isolation boundaries: ${evidence.length} passed`);
