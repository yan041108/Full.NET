import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { runDocumentSandbox } from '../../scripts/ai/document-sandbox.mjs';
const [imageId, file] = process.argv.slice(2);
const input = await readFile(file);
for (const hidden of ['exclude', 'include']) {
  const result = await runDocumentSandbox({ imageId, input, parserArguments: ['--format', 'ppt', '--encoding', 'utf-8', '--delimiter', 'comma', '--header', 'no', '--hidden', hidden] });
  assert.equal(result.code, 'ok'); assert.equal(result.policyVerified, true); assert.equal(result.cleaned, true); assert.equal(result.stopped, true);
  const value = JSON.parse(result.output.toString('utf8')); const text = value.blocks.map(block => block.text).join('\n');
  assert.match(text, /VISIBLE_SHAPE/);
  for (const secret of ['PRIVATE_SHAPE_20261023', 'PRIVATE_GROUP_20261023']) assert.equal(text.includes(secret), hidden === 'include', `${hidden}: ${secret}`);
}
console.log('Native PPT visibility policy: 2 passed');
