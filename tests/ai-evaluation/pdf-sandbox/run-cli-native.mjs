import assert from 'node:assert/strict';
import { execFileSync, spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { PassThrough } from 'node:stream';
import { fileURLToPath } from 'node:url';
import { executePdfSandboxCli } from '../../../scripts/ai/pdf-sandbox-cli.mjs';
import { runPdfSandbox } from '../../../scripts/ai/pdf-sandbox.mjs';
import { createPdf } from './fixtures.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const output = path.join(root, 'artifacts/ai-evaluation/r05e3');
const previous = path.join(root, 'artifacts/ai-evaluation/r05e2');
const images = JSON.parse(readFileSync(path.join(previous, 'images.json'), 'utf8'));
const evidence = JSON.parse(readFileSync(path.join(previous, 'acceptance.json'), 'utf8'));
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const docker = args => execFileSync('docker', args, { windowsHide: true, encoding: 'utf8', timeout: 10000, maxBuffer: 65536 });
const files = ['scripts/ai/pdf-sandbox-cli.mjs', 'scripts/ai/pdf-sandbox-publish.mjs', 'scripts/ai/pdf-sandbox.mjs',
  'tests/ai-evaluation/pdf-sandbox/fixtures.mjs', 'tests/ai-evaluation/pdf-sandbox/run-cli-native.mjs',
  ...Object.keys(evidence.sourceSha256).filter(file => file.startsWith('src/Tools/Full.NET.Ai.PdfParser/') || file === 'Directory.Packages.props')];
const hashes = () => Object.fromEntries(files.map(file => [file, sha(readFileSync(path.join(root, file)))]));
const frozen = hashes();
const records = [];
const existing = docker(['ps', '--format', '{{.ID}} {{.Names}}']).trim().split(/\r?\n/).filter(Boolean);
const label = 'fullnet.ai.pdf.run';
const args = (imageId, input, runId, timeoutMs = 30000) => ['--image-id', imageId, '--content-hash', sha(input),
  '--size-bytes', String(input.length), '--run-id', runId, '--timeout-ms', String(timeoutMs)];
const delay = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));
mkdirSync(output, { recursive: true });

// 复用源码和二进制均未变化的已发布工具；本次重新执行 CLI，不用历史运行结果替代新传输证据。
assert.equal(evidence.status, 'passed');
for (const file of files.filter(file => file.startsWith('src/Tools/') || file === 'Directory.Packages.props'))
  assert.equal(frozen[file], evidence.sourceSha256[file], `native source changed: ${file}`);
for (const role of ['parser', 'probe']) {
  assert.equal(docker(['image', 'inspect', images[role].imageId, '--format', '{{.Id}}']).trim(), images[role].imageId);
  assert.equal(sha(readFileSync(path.join(previous, role, role === 'parser' ? 'Full.NET.Ai.PdfParser' : 'Full.NET.PdfSandbox.Probe'))), images[role].artifactSha256);
}

function invoke(arguments_, input, { readOutput = true } = {}) {
  const child = spawn(process.execPath, [path.join(root, 'scripts/ai/pdf-sandbox-cli.mjs'), ...arguments_], {
    cwd: root, windowsHide: true, env: { ...process.env, FULLNET_PDF_SECRET_CANARY: 'synthetic-not-a-credential' }
  });
  const completed = new Promise((resolve, reject) => {
    const chunks = [], errors = []; const started = performance.now(); let processExitMs;
    const timer = setTimeout(() => { child.kill('SIGKILL'); reject(new Error(`CLI close unconfirmed (exitCode=${child.exitCode})`)); }, 60000);
    if (readOutput) child.stdout.on('data', bytes => chunks.push(bytes));
    child.stderr.on('data', bytes => errors.push(bytes));
    child.stdin.on('error', () => {});
    child.once('exit', () => {
      processExitMs = Math.ceil(performance.now() - started);
      // 背压保持到真实进程退出；其后排空父进程缓冲，才能观测 child close，不将未读管道误报为活进程。
      if (!readOutput) child.stdout.resume();
    });
    child.on('error', error => { clearTimeout(timer); reject(error); });
    child.on('close', (exitCode, signal) => {
      clearTimeout(timer);
      resolve({ exitCode, signal, output: Buffer.concat(chunks), errors: Buffer.concat(errors).toString('utf8'),
        processExitMs, durationMs: Math.ceil(performance.now() - started) });
    });
  });
  child.stdin.end(input);
  return { child, completed };
}

function removed(runId) {
  assert.equal(docker(['ps', '-a', '--filter', `label=${label}=${runId}`, '--format', '{{.ID}}']).trim(), '');
}

async function running(runId) {
  for (let attempt = 0; attempt < 100; attempt++) {
    const id = docker(['ps', '--filter', `label=${label}=${runId}`, '--format', '{{.ID}}']).trim();
    if (id && docker(['top', id, '-eo', 'pid,args']).split(/\r?\n/).some(line => /^\s*\d+\s+\/app\/parser\s*$/.test(line))) {
      const container = JSON.parse(docker(['inspect', id]))[0];
      assert.equal(container.Config.Labels[label], runId);
      assert.match(container.Config.Labels['fullnet.ai.pdf.owner'], /^[0-9a-f-]{36}$/);
      return container;
    }
    await delay(50);
  }
  throw new Error('Native parser did not start');
}

async function check(name, imageId, input, expected, timeoutMs = 30000) {
  const runId = randomUUID();
  const result = await invoke(args(imageId, input, runId, timeoutMs), input).completed;
  removed(runId);
  if (expected === 'ok') { assert.equal(result.exitCode, 0); assert.equal(result.errors, ''); assert.ok(result.output.length > 0); }
  else { assert.equal(result.exitCode, 2); assert.equal(result.errors, `${expected}\n`); assert.equal(result.output.length, 0); }
  records.push({ name, runId, inputSha256: sha(input), inputBytes: input.length, exitCode: result.exitCode,
    code: expected, outputBytes: result.output.length, resourcesRemoved: true, durationMs: result.durationMs });
  console.log(`PASS ${name}: ${expected}`);
  return result.output;
}

try {
  for (const compressed of [false, true]) {
    const input = createPdf({ compressed });
    const bytes = await check(`CLI-Chinese-pages-${compressed ? 'compressed' : 'plain'}`, images.parser.imageId, input, 'ok');
    const result = JSON.parse(bytes.toString('utf8'));
    assert.equal(result.protocolVersion, 1); assert.equal(result.parserVersion, 'pdfpig-0.1.16-content-order-v1');
    assert.equal(result.contentHash, sha(input));
    assert.deepEqual(result.pages, [1, 2].map(pageNumber => ({ pageNumber, text: '合成制度 FN-PDF-042：三个工作日。' })));
  }
  await check('CLI-image-only-scan', images.parser.imageId, createPdf({ imageOnly: true }), 'fn_ai_pdf_empty_page');
  await check('CLI-corrupt-PDF', images.parser.imageId, Buffer.from('%PDF-1.7\nprivate source\n%%EOF'), 'fn_ai_pdf_parse_failed');
  const isolated = JSON.parse((await check('CLI-real-isolation', images.probe.imageId, Buffer.from('isolation\n'), 'ok')).toString('utf8'));
  for (const key of ['native', 'networkBlocked', 'loopbackOnly', 'readOnly', 'nonRoot', 'noPrivileges', 'zeroCapabilities', 'noCredentials'])
    assert.equal(isolated[key], true);
  assert.equal(isolated.memoryMax, '536870912'); assert.equal(isolated.swapMax, '0');
  assert.equal(isolated.cpuMax, '200000 100000'); assert.equal(isolated.pidsMax, '32');
  records.at(-1).observedPolicy = isolated;
  await check('CLI-timeout-kill', images.probe.imageId, Buffer.from('hang\n'), 'fn_ai_pdf_sandbox_timeout', 3000);
  await check('CLI-stdout-flood', images.probe.imageId, Buffer.from('stdout\n'), 'fn_ai_pdf_sandbox_output_limit');
  await check('CLI-stderr-flood', images.probe.imageId, Buffer.from('stderr\n'), 'fn_ai_pdf_sandbox_error_limit');

  // 使用真实完整 PDF 结果填满父进程未读取的管道，先观察容器已清理，再确认交付阶段仍按预算退出。
  const blockedRun = randomUUID(), largeInput = createPdf({ text: 'A'.repeat(8192), pages: 32, compressed: true });
  const blocked = invoke(args(images.parser.imageId, largeInput, blockedRun, 20000), largeInput, { readOutput: false });
  let publishing = false;
  for (let attempt = 0; attempt < 150; attempt++) {
    if (blocked.child.stdout.readableLength > 0
        && docker(['ps', '-a', '--filter', `label=${label}=${blockedRun}`, '--format', '{{.ID}}']).trim() === '') {
      publishing = true; break;
    }
    if (blocked.child.exitCode !== null) break;
    await delay(100);
  }
  const unread = await blocked.completed;
  assert.equal(publishing, true, '真实结果已进入 stdout 且容器清理完成');
  assert.equal(unread.exitCode, 2); assert.equal(unread.errors, 'fn_ai_pdf_sandbox_timeout\n');
  assert.ok(unread.durationMs < 25000); removed(blockedRun);
  records.push({ name: 'CLI-unread-stdout-deadline', runId: blockedRun, inputSha256: sha(largeInput), inputBytes: largeInput.length,
    publicationObservedAfterCleanup: true, code: 'fn_ai_pdf_sandbox_timeout', exitCode: unread.exitCode,
    processExitMs: unread.processExitMs, durationMs: unread.durationMs, resourcesRemoved: true });
  console.log('PASS CLI unread stdout: publication stopped by remaining deadline');

  // Windows 的子进程 kill 不执行信号处理器；在同一真实传输函数中注入取消，观察容器先停止清理。
  const runId = randomUUID(), input = Buffer.from('hang\n'), controller = new AbortController();
  const stream = new PassThrough(); stream.end(input);
  let observed;
  const cancelled = await executePdfSandboxCli(args(images.probe.imageId, input, runId), stream, {
    signal: controller.signal, runSandbox: async options => {
      const work = runPdfSandbox(options);
      try { observed = await running(runId); controller.abort(); return await work; }
      finally { controller.abort(); await work; }
    }
  });
  assert.equal(cancelled.code, 'fn_ai_pdf_sandbox_cancelled'); assert.equal(cancelled.output, undefined); removed(runId);
  records.push({ name: 'transport-running-cancellation', runId, containerId: observed.Id, code: cancelled.code, resourcesRemoved: true });
  console.log('PASS transport running cancellation: stopped and removed');

  const orphanRun = randomUUID();
  const coordinator = invoke(args(images.probe.imageId, input, orphanRun, 5000), input);
  let orphan;
  try {
    orphan = await running(orphanRun);
    coordinator.child.kill('SIGKILL');
    await coordinator.completed;
    let state;
    for (let attempt = 0; attempt < 100; attempt++) {
      state = JSON.parse(docker(['inspect', orphan.Id]))[0].State;
      if (!state.Running) break;
      await delay(100);
    }
    assert.equal(state.Running, false); assert.equal(state.ExitCode, 137); assert.equal(state.OOMKilled, false);
    records.push({ name: 'CLI-death-watchdog', runId: orphanRun, containerId: orphan.Id, stopped: true, exitCode: state.ExitCode });
  } finally {
    coordinator.child.kill('SIGKILL'); await coordinator.completed;
    if (orphan) {
      // 只清理本次捕获且归属未变化的容器；watchdog 停止不等于控制器完成了资源清理。
      const owned = JSON.parse(docker(['inspect', orphan.Id]))[0];
      assert.equal(owned.Id, orphan.Id); assert.equal(owned.Config.Labels[label], orphanRun);
      assert.equal(owned.Config.Labels['fullnet.ai.pdf.owner'], orphan.Config.Labels['fullnet.ai.pdf.owner']);
      assert.equal(owned.Name, orphan.Name);
      docker(['rm', '-f', orphan.Id]); removed(orphanRun);
      if (records.at(-1)?.name === 'CLI-death-watchdog') records.at(-1).harnessRemoved = true;
    }
  }
  console.log('PASS CLI death: independent watchdog terminated parser; harness removed owned resource');
  assert.deepEqual(hashes(), frozen);
  const remaining = docker(['ps', '--format', '{{.ID}} {{.Names}}']).trim().split(/\r?\n/);
  for (const container of existing) assert.ok(remaining.includes(container), container);
  writeFileSync(path.join(output, 'acceptance.json'), JSON.stringify({ status: 'passed',
    baselineHead: execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8', windowsHide: true }).trim(),
    platform: process.platform, nodeVersion: process.version, images, reusedNativeBuild: true, sourceSha256: frozen,
    records, existingContainersPreserved: true, completedAtUtc: new Date().toISOString() }, null, 2) + '\n');
} catch (error) {
  writeFileSync(path.join(output, 'acceptance.json'), JSON.stringify({ status: 'failed', images, sourceSha256: frozen,
    records, error: error.message, completedAtUtc: new Date().toISOString() }, null, 2) + '\n');
  throw error;
}
