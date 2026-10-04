import assert from 'node:assert/strict';
import { execFileSync, spawn } from 'node:child_process';
import { randomUUID, createHash } from 'node:crypto';
import { readFileSync, writeFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { runPdfSandbox } from '../../../scripts/ai/pdf-sandbox.mjs';
import { createPdf } from './fixtures.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const output = path.join(root, 'artifacts/ai-evaluation/r05e2');
const images = JSON.parse(readFileSync(path.join(output, 'images.json'), 'utf8'));
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const sources = ['Directory.Packages.props', 'scripts/ai/pdf-sandbox.mjs',
  ...['src/Tools/Full.NET.Ai.PdfParser', 'tests/ai-evaluation/pdf-sandbox'].flatMap(directory =>
    readdirSync(path.join(root, directory), { recursive: true, withFileTypes: true })
      .filter(item => item.isFile()).map(item => path.relative(root, path.join(item.parentPath, item.name)).replaceAll('\\', '/'))
      .filter(file => !file.split('/').some(part => part === 'obj' || part === 'bin')))];
const hashes = () => Object.fromEntries(sources.map(file => [file, sha(readFileSync(path.join(root, file)))]));
const frozen = hashes();
const records = [];
const docker = args => execFileSync('docker', args, { windowsHide: true, encoding: 'utf8', timeout: 10000, maxBuffer: 65536 });
const existingContainers = docker(['ps', '--format', '{{.ID}} {{.Names}}']).trim().split('\n').filter(Boolean);
const parserStarted = id => docker(['top', id, '-eo', 'pid,args']).split(/\r?\n/).some(line => /^\s*\d+\s+\/app\/parser\s*$/.test(line));
// 无真实凭据的宿主金丝雀，用于证明没有把父进程环境传播给隔离执行器。
process.env.FULLNET_PDF_SECRET_CANARY = 'synthetic-not-a-credential';

async function check(name, imageId, input, expected, options = {}) {
  const result = await runPdfSandbox({ imageId, input, ...options });
  records.push({ name, inputSha256: sha(input), inputBytes: input.length, ...result, output: undefined });
  assert.equal(result.code, expected, `${name}: ${JSON.stringify({ ...result, output: undefined })}`);
  assert.equal(result.policyVerified, true, name);
  assert.equal(result.stopped, true, name);
  assert.equal(result.cleaned, true, name);
  assert.ok(result.durationMs < (options.timeoutMs ?? 30000) + 12000, name);
  assert.equal(docker(['ps', '-a', '--filter', `id=${result.containerId}`, '--format', '{{.ID}}']).trim(), '', name);
  if (expected !== 'ok') assert.equal(result.output, undefined, name);
  console.log(`PASS ${name}: ${result.code}, exit=${result.exitCode}, ${result.durationMs} ms`);
  return result;
}

try {
  const collisionId = randomUUID();
  const collisionCancel = new AbortController();
  const original = runPdfSandbox({ imageId: images.probe.imageId, input: Buffer.from('hang\n'), runId: collisionId, signal: collisionCancel.signal });
  try {
    let originalId;
    for (let attempt = 0; attempt < 100; attempt++) {
      originalId = docker(['ps', '--filter', `label=fullnet.ai.pdf.run=${collisionId}`, '--format', '{{.ID}}']).trim();
      if (originalId && parserStarted(originalId)) break;
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.ok(originalId);
    await check('duplicate-correlation-is-independent', images.probe.imageId, Buffer.from('isolation\n'), 'ok', { runId: collisionId });
    assert.equal(JSON.parse(docker(['inspect', originalId]))[0].State.Running, true);
    collisionCancel.abort();
    const cancelled = await original;
    assert.equal(cancelled.code, 'fn_ai_pdf_sandbox_cancelled');
    assert.equal(cancelled.cleaned, true);
  } finally { collisionCancel.abort(); await original; }
  for (const compressed of [false, true]) {
    const input = createPdf({ compressed });
    const result = await check(`Chinese-pages-${compressed ? 'compressed' : 'plain'}`, images.parser.imageId, input, 'ok');
    const json = JSON.parse(result.output.toString('utf8'));
    assert.equal(json.protocolVersion, 1);
    assert.equal(json.parserVersion, 'pdfpig-0.1.16-content-order-v1');
    assert.equal(json.contentHash, sha(input));
    assert.deepEqual(json.pages, [1, 2].map(pageNumber => ({ pageNumber, text: '合成制度 FN-PDF-042：三个工作日。' })));
  }
  await check('page-limit', images.parser.imageId, createPdf({ pages: 129 }), 'fn_ai_pdf_page_limit');
  const exact = await check('128-pages', images.parser.imageId, createPdf({ pages: 128 }), 'ok');
  assert.equal(JSON.parse(exact.output.toString('utf8')).pages.length, 128);
  await check('image-only-scan', images.parser.imageId, createPdf({ imageOnly: true }), 'fn_ai_pdf_empty_page');
  await check('blank-page', images.parser.imageId, createPdf({ text: '' }), 'fn_ai_pdf_empty_page');
  await check('bad-signature', images.parser.imageId, Buffer.from('ordinary text'), 'fn_ai_pdf_bad_signature');
  await check('corrupt-pdf', images.parser.imageId, Buffer.from('%PDF-1.7\nprivate source\n%%EOF'), 'fn_ai_pdf_parse_failed');
  await check('unknown-filter', images.parser.imageId, createPdf({ compressed: true, filter: 'LZWDecode' }), 'fn_ai_pdf_unsupported_filter');
  await check('unsupported-predictor', images.parser.imageId, createPdf({ compressed: true, predictor: 12 }), 'fn_ai_pdf_unsupported_predictor');
  await check('indirect-predictor', images.parser.imageId, createPdf({ compressed: true, predictor: 12, indirectParameters: true }), 'fn_ai_pdf_unsupported_predictor');
  await check('unfinished-deflate-with-valid-checksum', images.parser.imageId, createPdf({ compressed: true, incomplete: true }), 'fn_ai_pdf_corrupt_stream');
  await check('decompression-bomb', images.parser.imageId, createPdf({ pages: 1, compressed: true, decodedBytes: 33 * 1024 * 1024 }), 'fn_ai_pdf_decode_limit');
  await check('shared-stream-budget', images.parser.imageId, createPdf({ compressed: true, decodedBytes: 20 * 1024 * 1024 }), 'fn_ai_pdf_decode_limit');
  await check('embedded-actions', images.parser.imageId, createPdf({ actions: true }), 'ok');
  const isolated = await check('real-isolation', images.probe.imageId, Buffer.from('isolation\n'), 'ok');
  const policy = JSON.parse(isolated.output.toString('utf8'));
  for (const key of ['native', 'networkBlocked', 'loopbackOnly', 'readOnly', 'nonRoot', 'noPrivileges', 'zeroCapabilities', 'noCredentials']) assert.equal(policy[key], true, key);
  assert.equal(policy.memoryMax, '536870912');
  assert.equal(policy.swapMax, '0');
  assert.equal(policy.cpuMax, '200000 100000');
  assert.equal(policy.pidsMax, '32');
  records.at(-1).observedPolicy = policy;
  const timeout = await check('timeout-kill', images.probe.imageId, Buffer.from('hang\n'), 'fn_ai_pdf_sandbox_timeout', { timeoutMs: 3000 });
  assert.ok(timeout.outputBytes >= 8);
  assert.equal(timeout.exitCode, 137);
  const runId = randomUUID();
  const cancel = new AbortController();
  const work = check('cancellation-kill', images.probe.imageId, Buffer.from('hang\n'), 'fn_ai_pdf_sandbox_cancelled', { runId, signal: cancel.signal });
  // 等待真实容器运行和探针开始，再取消，避免只证明启动前取消。
  let running = false;
  for (let attempt = 0; attempt < 100; attempt++) {
    const id = docker(['ps', '--filter', `label=fullnet.ai.pdf.run=${runId}`, '--format', '{{.ID}}']).trim();
    if (id && parserStarted(id)) { running = true; break; }
    await new Promise(resolve => setTimeout(resolve, 100));
  }
  assert.equal(running, true);
  cancel.abort();
  const cancelled = await work;
  assert.ok(cancelled.outputBytes >= 8);
  assert.equal(cancelled.exitCode, 137);
  const oom = await check('actual-cgroup-OOM', images.probe.imageId, Buffer.from('memory\n'), 'fn_ai_pdf_sandbox_memory_limit');
  assert.equal(oom.oomKilled, true);
  assert.equal(oom.exitCode, 137);
  await check('stdout-flood', images.probe.imageId, Buffer.from('stdout\n'), 'fn_ai_pdf_sandbox_output_limit');
  await check('stderr-flood', images.probe.imageId, Buffer.from('stderr\n'), 'fn_ai_pdf_sandbox_error_limit');
  // 杀掉协调进程后仍由容器内 watchdog 强杀解析器，证明硬时限不依赖父进程存活。
  const orphanId = randomUUID();
  const coordinator = spawn(process.execPath, ['--input-type=module', '-e',
    'import { runPdfSandbox } from "./scripts/ai/pdf-sandbox.mjs"; await runPdfSandbox({ imageId: process.argv[1], runId: process.argv[2], input: Buffer.from("hang\\n"), timeoutMs: 5000 });',
    images.probe.imageId, orphanId], { cwd: root, windowsHide: true, stdio: 'ignore' });
  let orphan;
  try {
    for (let attempt = 0; attempt < 100; attempt++) {
      const id = docker(['ps', '--filter', `label=fullnet.ai.pdf.run=${orphanId}`, '--format', '{{.ID}}']).trim();
      if (id && parserStarted(id)) { orphan = JSON.parse(docker(['inspect', id]))[0]; break; }
      await new Promise(resolve => setTimeout(resolve, 50));
    }
    assert.ok(orphan, '真实探针开始后再杀掉协调进程');
    coordinator.kill('SIGKILL');
    const killedAt = performance.now();
    let state;
    for (let attempt = 0; attempt < 100; attempt++) {
      state = JSON.parse(docker(['inspect', orphan.Id]))[0].State;
      if (!state.Running) break;
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.equal(state.Running, false);
    assert.equal(state.ExitCode, 137);
    assert.equal(state.OOMKilled, false);
    assert.ok(performance.now() - killedAt < 8000);
    records.push({ name: 'watchdog-survives-coordinator-death', containerId: orphan.Id, stopped: true, exitCode: state.ExitCode, policyVerified: true });
    console.log('PASS watchdog survives coordinator death: exit=137');
  } finally {
    coordinator.kill('SIGKILL');
    if (orphan) {
      const owned = JSON.parse(docker(['inspect', orphan.Id]))[0];
      assert.equal(owned.Config.Labels['fullnet.ai.pdf.run'], orphanId);
      assert.equal(owned.Id, orphan.Id);
      docker(['rm', '-f', orphan.Id]);
      if (records.at(-1).name === 'watchdog-survives-coordinator-death') records.at(-1).cleaned = true;
    }
  }
  assert.deepEqual(hashes(), frozen, '验收期间源码不能变化');
  assert.equal(docker(['ps', '-a', '--filter', 'label=fullnet.ai.pdf.run', '--format', '{{.ID}}']).trim(), '');
  const remaining = docker(['ps', '--format', '{{.ID}} {{.Names}}']).trim().split('\n');
  for (const container of existingContainers) assert.ok(remaining.includes(container), container);
  writeFileSync(path.join(output, 'acceptance.json'), JSON.stringify({ status: 'passed', runtime: 'linux-x64 NativeAOT',
    baselineHead: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, windowsHide: true, encoding: 'utf8' }).trim(),
    images, sourceSha256: frozen, records, resourcesRemoved: true, completedAtUtc: new Date().toISOString() }, null, 2) + '\n');
} catch (error) {
  writeFileSync(path.join(output, 'acceptance.json'), JSON.stringify({ status: 'failed', images, sourceSha256: frozen, records,
    error: error.message, completedAtUtc: new Date().toISOString() }, null, 2) + '\n');
  throw error;
} finally { delete process.env.FULLNET_PDF_SECRET_CANARY; }
