import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { PassThrough } from 'node:stream';
import { runDocxSandbox } from '../../../scripts/ai/docx-sandbox.mjs';
import { executeDocxSandboxCli } from '../../../scripts/ai/docx-sandbox-cli.mjs';
import { createDocx } from './fixtures.mjs';

// 显式传入不可变镜像；生产镜像和对抗探针分别验收，探针不计作正文解析证据。
const [parserImage, probeImage, directory] = process.argv.slice(2);
assert.match(parserImage ?? '', /^sha256:[0-9a-f]{64}$/); assert.match(probeImage ?? '', /^sha256:[0-9a-f]{64}$/);
assert.ok(directory && path.isAbsolute(directory)); mkdirSync(directory, { recursive: true });
const docker = args => execFileSync('docker', args, { windowsHide: true, encoding: 'utf8', timeout: 10000 });
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
for (const image of [parserImage, probeImage]) assert.equal(docker(['image', 'inspect', image, '--format', '{{.Id}}']).trim(), image);
const records = [];
async function check(name, imageId, input, expected, options = {}) {
  const result = await runDocxSandbox({ imageId, input, ...options });
  records.push({ name, imageId, inputSha256: hash(input), inputBytes: input.length, ...result, output: undefined });
  assert.equal(result.code, expected, name); assert.equal(result.stopped, true, name); assert.equal(result.cleaned, true, name); assert.equal(result.policyVerified, true, name);
  assert.equal(docker(['ps', '-a', '--filter', `id=${result.containerId}`, '--format', '{{.ID}}']).trim(), '', name);
  if (expected !== 'ok') assert.equal(result.output, undefined, name);
  console.log(`PASS ${name}: ${result.code}`); return result;
}
const source = createDocx(); writeFileSync(path.join(directory, 'source.docx'), source);
const parsed = await check('real-docx-paragraph-table-unicode', parserImage, source, 'ok');
const wire = JSON.parse(parsed.output); assert.equal(wire.parserVersion, 'docx-ooxml-body-v1'); assert.equal(wire.contentHash, hash(source)); assert.equal(wire.sizeBytes, source.length);
assert.equal(wire.blocks.length, 7); assert.equal(wire.blocks[1].length, 0); assert.equal(wire.blocks[2].kind, 'tableCell');
assert.equal(wire.blocks[2].isHeader, true); assert.equal(wire.blocks[4].isHeader, false); assert.ok(wire.text.includes('😀')); assert.ok(wire.text.includes('<script>原文</script>'));
writeFileSync(path.join(directory, 'output.json'), parsed.output);
const isolated = await check('native-probe-actual-isolation', probeImage, Buffer.from('isolation\n'), 'ok');
const policy = JSON.parse(isolated.output);
for (const field of ['native', 'networkBlocked', 'loopbackOnly', 'readOnly', 'nonRoot', 'noPrivileges', 'zeroCapabilities', 'noCredentials']) assert.equal(policy[field], true, field);
assert.equal(policy.memoryMax, '536870912'); assert.equal(policy.swapMax, '0'); assert.equal(policy.cpuMax, '200000 100000'); assert.equal(policy.pidsMax, '32');
await check('bounded-deadline', probeImage, Buffer.from('hang\n'), 'fn_ai_docx_sandbox_timeout', { timeoutMs: 2000 });
await check('actual-oom', probeImage, Buffer.from('memory\n'), 'fn_ai_docx_sandbox_memory_limit');
await check('bounded-stdout', probeImage, Buffer.from('stdout\n'), 'fn_ai_docx_sandbox_output_limit');
await check('bounded-stderr', probeImage, Buffer.from('stderr\n'), 'fn_ai_docx_sandbox_error_limit');
const correlation = randomUUID(); const cancel = new AbortController();
const pending = runDocxSandbox({ imageId: probeImage, input: Buffer.from('hang\n'), signal: cancel.signal, runId: correlation });
try {
  let id; let parserStarted = false; const deadline = performance.now() + 10000;
  while (performance.now() < deadline) {
    id = docker(['ps', '--filter', `label=fullnet.ai.docx.run=${correlation}`, '--format', '{{.ID}}']).trim();
    if (id && docker(['top', id, '-eo', 'pid,args']).split(/\r?\n/).some(line => /^\s*\d+\s+\/app\/parser\s*$/.test(line))) { parserStarted = true; break; }
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  assert.ok(id); assert.equal(parserStarted, true, 'must observe actual parser before cancellation');
  await check('same-correlation-independent-ownership', probeImage, Buffer.from('isolation\n'), 'ok', { runId: correlation });
  assert.equal(JSON.parse(docker(['inspect', id]))[0].State.Running, true);
} finally { cancel.abort(); await pending; }
const cancelled = await pending; records.push({ name: 'actual-cancel-stops-owned-parser', ...cancelled, output: undefined });
assert.equal(cancelled.code, 'fn_ai_docx_sandbox_cancelled'); assert.equal(cancelled.stopped, true); assert.equal(cancelled.cleaned, true);
assert.equal(docker(['ps', '-a', '--filter', `id=${cancelled.containerId}`, '--format', '{{.ID}}']).trim(), '');
for (const framed of [false, true]) {
  const input = new PassThrough(); const args = ['--image-id', parserImage, '--content-hash', hash(source), '--size-bytes', String(source.length)];
  if (framed) args.push('--input-mode', 'framed-v1');
  const work = executeDocxSandboxCli(args, input); if (framed) input.write(source); else input.end(source);
  try { const result = await work; assert.equal(result.code, 'ok'); assert.deepEqual(JSON.parse(result.output), wire); records.push({ name: framed ? 'actual-cli-framed' : 'actual-cli-eof', code: result.code }); }
  finally { input.destroy(); }
}
writeFileSync(path.join(directory, 'native.json'), JSON.stringify({ parserImage, probeImage, sourceSha256: hash(source), records }, null, 2));
console.log(`Native DOCX execution: ${records.length} passed`);
