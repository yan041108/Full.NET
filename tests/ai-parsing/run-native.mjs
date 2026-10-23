import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { runDocumentSandbox } from '../../scripts/ai/document-sandbox.mjs';

const [imageId, fixtureRoot, outputRoot] = process.argv.slice(2);
assert.match(imageId ?? '', /^sha256:[0-9a-f]{64}$/);
assert.ok(path.isAbsolute(fixtureRoot ?? '') && path.isAbsolute(outputRoot ?? ''));
await mkdir(outputRoot, { recursive: true });
const kinds = ['csv', 'html', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'jpg', 'jfif', 'png', 'webp', 'bmp', 'tif', 'gif', 'heic', 'heif', 'avif', 'jp2', 'j2k', 'jxl', 'ico', 'tga', 'pcx', 'pnm', 'pbm', 'pgm', 'ppm', 'svg', 'svgz', 'wmf', 'emf', 'psd'];
const cases = kinds.map(kind => ({ name: kind, source: `source.${kind}`, kind: ({ jfif: 'jpg', j2k: 'jp2', svgz: 'svg' })[kind] ?? kind }));
cases.push({ name: 'jpeg', source: 'source.jpeg', kind: 'jpg' }, { name: 'tiff', source: 'source.tiff', kind: 'tif' },
  { name: 'htm', source: 'source.htm', kind: 'html' }, { name: 'doc-embedded', source: 'embedded.doc', kind: 'doc' });
cases.push({ name: 'pdf-native', source: 'native.pdf', kind: 'pdf' }, { name: 'pdf-scanned', source: 'scanned.pdf', kind: 'pdf' },
  { name: 'pdf-mixed', source: 'mixed.pdf', kind: 'pdf' }, { name: 'orientation', source: 'orientation.jpg', kind: 'jpg' },
  { name: 'tilt', source: 'tilt.png', kind: 'png' }, { name: 'table', source: 'table.png', kind: 'png' }, { name: 'frames-gif', source: 'frames.gif', kind: 'gif' },
  { name: 'frames-tif', source: 'frames.tif', kind: 'tif' }, { name: 'frames-apng', source: 'frames.apng', kind: 'apng' },
  { name: 'csv-gb18030', source: 'gb18030.csv', kind: 'csv', encoding: 'gb18030', delimiter: 'semicolon', header: 'yes', policyKey: 'csv_gb18030_semicolon_header' },
  { name: 'xlsx-hidden', source: 'source.xlsx', kind: 'xlsx', hidden: 'include', policyKey: 'include_hidden' });
cases.push({ name: 'ppt-visible-only', source: 'hidden.ppt', kind: 'ppt' },
  { name: 'ppt-hidden', source: 'hidden.ppt', kind: 'ppt', hidden: 'include', policyKey: 'include_hidden' });
const accepted = []; const failures = [];
for (const item of cases) {
  const input = await readFile(path.join(fixtureRoot, item.source));
  const parserArguments = ['--format', item.kind, '--encoding', item.encoding ?? 'utf-8', '--delimiter', item.delimiter ?? 'comma', '--header', item.header ?? 'no', '--hidden', item.hidden ?? 'exclude'];
  const start = performance.now();
  const result = await runDocumentSandbox({ imageId, input, parserArguments });
  const seconds = Number(((performance.now()-start)/1000).toFixed(3));
  try {
    assert.equal(result.code, 'ok'); assert.equal(result.cleaned, true); assert.equal(result.stopped, true); assert.equal(result.policyVerified, true);
    const value = JSON.parse(result.output.toString('utf8'));
    assert.equal(value.contentHash, createHash('sha256').update(input).digest('hex')); assert.equal(value.sizeBytes, input.length);
    assert.ok(value.blocks.some(block => block.text.trim()));
    assert.equal(value.policy.inputFormat, item.kind);
    if (item.source === 'hidden.ppt') {
      const text = value.blocks.map(block => block.text).join('\n'); assert.match(text, /VISIBLE_SHAPE/);
      for (const secret of ['PRIVATE_SHAPE_20261023', 'PRIVATE_GROUP_20261023']) assert.equal(text.includes(secret), item.hidden === 'include');
    }
    const images = value.blocks.filter(block => block.position.kind === 11);
    assert.ok(images.every(block => Number.isFinite(block.position.image.confidence) && block.position.image.confidence >= 0.8));
    if (item.name === 'pdf-native') { assert.equal(images.length, 0); assert.match(value.blocks[0].text, /Native PDF heading/); }
    if (item.name === 'table') assert.ok(images.some(block => block.position.image.tableNumber === 1));
    if (['doc-embedded', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'pdf-scanned', 'pdf-mixed'].includes(item.name)) assert.ok(images.length > 0 && images.every(block => block.position.image.parent));
    if (item.name.startsWith('frames-')) assert.ok(new Set(images.map(block => block.position.image.frameNumber)).size === 2);
    if (item.name === 'orientation') assert.ok(images.every(block => block.position.image.orientation === 6));
    if (item.name === 'ico') assert.ok(images.some(block => block.text === '知识库'));
    if (item.name === 'table') for (const expected of ['项目名称', '项目编号', '知识库', '2026']) assert.ok(images.some(block => block.text === expected));
    if (images.length && item.name !== 'ico' && item.name !== 'table') {
      assert.ok(images.some(block => block.text === '知识库解析测试'));
      assert.ok(images.some(block => block.text.replace(/\s+/g, '').toLowerCase() === 'full.netdocument2026'));
    }
    await writeFile(path.join(outputRoot, `${item.name}.json`), result.output);
    accepted.push({ ...item, policyKey: item.policyKey ?? 'default', output: `${item.name}.json`, format: value.format, seconds, bytes: input.length, blocks: value.blocks.length });
    process.stdout.write(`${item.name}: accepted (${seconds}s)\n`);
  } catch (error) {
    failures.push({ name: item.name, code: result.code, message: error.message }); process.stdout.write(`${item.name}: FAILED ${result.code}\n`);
  }
}
await writeFile(path.join(outputRoot, 'manifest.json'), JSON.stringify({ imageId, fixtureRoot, accepted, failures }, null, 2)+'\n');
assert.equal(failures.length, 0, JSON.stringify(failures));
