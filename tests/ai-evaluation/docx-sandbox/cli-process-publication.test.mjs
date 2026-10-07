import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import test from 'node:test';

const cli = new URL('../../../scripts/ai/docx-sandbox-cli.mjs', import.meta.url).href;
const source = String.raw`
const {publishDocxSandboxProcess}=await import(process.argv[1]);
const mode=process.argv[2], controller=new AbortController();
const bytes=mode==='ok'?Buffer.from('{"protocolVersion":1}'):mode==='empty'?Buffer.alloc(0):Buffer.alloc(1048576);
let timer;
if(mode==='cancel') timer=setTimeout(()=>controller.abort(),200);
const code=await publishDocxSandboxProcess(bytes,{timeoutMs:mode==='cancel'?2000:500,signal:controller.signal});
clearTimeout(timer);
process.stderr.write(code+'\n'); process.exitCode=code==='ok'?0:2;
`;

function invoke(mode, readOutput = false) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, ['--input-type=module', '-e', source, cli, mode], { windowsHide: true });
    const output = [], errors = []; const started = performance.now();
    const timer = setTimeout(() => { child.kill('SIGKILL'); reject(new Error('publication controller did not exit')); }, 5000);
    if (readOutput) child.stdout.on('data', bytes => output.push(bytes));
    child.stderr.on('data', bytes => errors.push(bytes));
    child.once('exit', () => { if (!readOutput) child.stdout.resume(); });
    child.on('error', error => { clearTimeout(timer); reject(error); });
    child.once('close', code => {
      clearTimeout(timer);
      resolve({ code, output: Buffer.concat(output), error: Buffer.concat(errors).toString('utf8'), durationMs: performance.now() - started });
    });
  });
}

test('publication controller terminates unread output on Windows and Unix', async () => {
  const result = await invoke('timeout');
  assert.equal(result.code, 2); assert.equal(result.error, 'fn_ai_docx_sandbox_timeout\n');
  assert.ok(result.durationMs < 4000);
});

test('publication controller cancels and waits for its owned writer to exit', async () => {
  const result = await invoke('cancel');
  assert.equal(result.code, 2); assert.equal(result.error, 'fn_ai_docx_sandbox_cancelled\n');
  assert.ok(result.durationMs < 4000);
});

test('publication controller accepts complete output and empty stderr from the writer', async () => {
  const result = await invoke('ok', true);
  assert.equal(result.code, 0); assert.equal(result.error, 'ok\n');
  assert.equal(result.output.toString('utf8'), '{"protocolVersion":1}');
});

test('publication controller rejects empty output without exposing diagnostics', async () => {
  const result = await invoke('empty', true);
  assert.equal(result.code, 2); assert.equal(result.error, 'fn_ai_docx_sandbox_transport_failed\n');
  assert.equal(result.output.length, 0);
});
