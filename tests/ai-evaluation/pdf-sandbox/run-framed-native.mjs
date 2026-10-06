import assert from 'node:assert/strict';
import { execFileSync, spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import { createPdf } from './fixtures.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const [parserImage, probeImage, evidenceDirectory] = process.argv.slice(2);
assert.match(parserImage ?? '', /^sha256:[a-f0-9]{64}$/); assert.match(probeImage ?? '', /^sha256:[a-f0-9]{64}$/);
assert.ok(evidenceDirectory, '显式指定证据目录');
const docker = args => execFileSync('docker', args, { windowsHide: true, encoding: 'utf8', timeout: 10000, maxBuffer: 65536 });
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const files = ['scripts/ai/pdf-sandbox-cli.mjs', 'scripts/ai/pdf-sandbox-publish.mjs', 'scripts/ai/pdf-sandbox.mjs',
  'tests/ai-evaluation/pdf-sandbox/fixtures.mjs', 'tests/ai-evaluation/pdf-sandbox/run-framed-native.mjs'];
const hashes = () => Object.fromEntries(files.map(file => [file, sha(readFileSync(path.join(root, file)))]));
const frozen = hashes(), records = [], runs = [];
const owned = runId => docker(['ps', '-a', '--filter', `label=fullnet.ai.pdf.run=${runId}`, '--format', '{{.ID}}']).trim();
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
mkdirSync(evidenceDirectory, { recursive: true });

function invoke(image, input, { readOutput = true, timeoutMs = 20000 } = {}) {
  const runId = randomUUID();
  const child = spawn(process.execPath, [path.join(root, 'scripts/ai/pdf-sandbox-cli.mjs'), '--image-id', image,
    '--content-hash', sha(input), '--size-bytes', String(input.length), '--run-id', runId,
    '--timeout-ms', String(timeoutMs), '--input-mode', 'framed-v1'], { cwd: root, windowsHide: true });
  const complete = new Promise((resolve, reject) => {
    let length = 0; const chunks = [], errors = [], started = performance.now();
    const timer = setTimeout(() => { child.kill('SIGKILL'); reject(Error('隔离 CLI 未在清理时限内退出')); }, 60000);
    if (readOutput) child.stdout.on('data', bytes => {
      length += bytes.length; if (length > 13 * 1024 * 1024) { child.kill('SIGKILL'); reject(Error('输出越界')); }
      else chunks.push(bytes);
    });
    child.stderr.on('data', bytes => { if (errors.reduce((n, b) => n + b.length, 0) > 4096) child.kill('SIGKILL'); else errors.push(bytes); });
    child.stdin.on('error', () => {});
    child.once('exit', () => { if (!readOutput) child.stdout.resume(); });
    child.once('error', error => { clearTimeout(timer); reject(error); });
    child.once('close', code => { clearTimeout(timer); resolve({ code, output: Buffer.concat(chunks),
      error: Buffer.concat(errors).toString('utf8'), durationMs: Math.ceil(performance.now() - started) }); });
  });
  // 只写权威长度，不发送 EOF；关闭留作跨平台的取消通知。
  child.stdin.write(input); const run = { child, runId, complete }; runs.push(run); return run;
}

async function waitFor(run, predicate) {
  for (let attempt = 0; attempt < 160; attempt++) {
    if (predicate()) return;
    assert.equal(run.child.exitCode, null, '取消场景必须先观察目标阶段'); await delay(75);
  }
  assert.fail('未观察到取消目标阶段');
}

async function accept(name, run, expected) {
  const result = await run.complete;
  assert.equal(result.code, expected === 'ok' ? 0 : 2); assert.equal(result.error, expected === 'ok' ? '' : `${expected}\n`);
  assert.equal(owned(run.runId), '', '仅核对本次 UUID 归属的资源');
  if (expected !== 'ok') assert.equal(result.output.length, 0);
  records.push({ name, runId: run.runId, code: expected, durationMs: result.durationMs, resourcesRemoved: true });
  console.log(`PASS ${name}: ${expected}`); return result;
}

try {
  for (const compressed of [false, true]) {
    const input = createPdf({ compressed }); const result = await accept(`framed-PDF-${compressed}`, invoke(parserImage, input), 'ok');
    const wire = JSON.parse(result.output.toString('utf8'));
    assert.equal(wire.contentHash, sha(input)); assert.equal(wire.parserVersion, 'pdfpig-0.1.16-content-order-v1');
    assert.deepEqual(wire.pages, [1, 2].map(pageNumber => ({ pageNumber, text: '合成制度 FN-PDF-042：三个工作日。' })));
  }
  for (const mode of ['eof', 'extra']) {
    const run = invoke(probeImage, Buffer.from('hang\n'));
    await waitFor(run, () => {
      const id = docker(['ps', '--filter', `label=fullnet.ai.pdf.run=${run.runId}`, '--format', '{{.ID}}']).trim();
      return id && docker(['top', id, '-eo', 'pid,args']).includes('/app/parser');
    });
    if (mode === 'eof') run.child.stdin.end(); else run.child.stdin.write(Buffer.from('X'));
    await accept(`framed-running-${mode}`, run, 'fn_ai_pdf_sandbox_cancelled');
  }
  const publishing = invoke(parserImage, createPdf({ text: 'A'.repeat(8192), pages: 32, compressed: true }), { readOutput: false });
  await waitFor(publishing, () => publishing.child.stdout.readableLength > 0 && owned(publishing.runId) === '');
  publishing.child.stdin.end(); await accept('framed-publication-eof', publishing, 'fn_ai_pdf_sandbox_cancelled');
  await accept('framed-running-timeout', invoke(probeImage, Buffer.from('hang\n'), { timeoutMs: 3000 }), 'fn_ai_pdf_sandbox_timeout');
  assert.deepEqual(hashes(), frozen);
  writeFileSync(path.join(evidenceDirectory, 'framed-native.json'), JSON.stringify({ status: 'passed', parserImage, probeImage,
    sourceSha256: frozen, records, completedAtUtc: new Date().toISOString() }, null, 2));
} catch (error) {
  writeFileSync(path.join(evidenceDirectory, 'framed-native.json'), JSON.stringify({ status: 'failed', records, error: error.message }, null, 2));
  throw error;
} finally {
  // 夹具断言失败也必须通知已启动的控制器取消，并排空管道等待其归属清理，不能直接退出遗留容器。
  for (const run of runs) {
    if (run.child.exitCode === null) { run.child.stdin.end(); run.child.stdout.resume(); }
    await run.complete;
    assert.equal(owned(run.runId), '', '夹具退出时本次资源清理必须完成');
  }
}
