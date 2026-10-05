import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const entry = fileURLToPath(new URL('../../../scripts/ai/pdf-sandbox-cli.mjs', import.meta.url));
const input = Buffer.from('%PDF-1.7\n');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const args = (bytes = input) => ['--image-id', `sha256:${'a'.repeat(64)}`, '--content-hash', hash(bytes), '--size-bytes', String(bytes.length)];

function invoke(arguments_, bytes = input) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [entry, ...arguments_], { windowsHide: true });
    const output = [], errors = [];
    const timer = setTimeout(() => { child.kill(); reject(new Error('CLI did not terminate')); }, 5000);
    child.stdout.on('data', chunk => output.push(chunk));
    child.stderr.on('data', chunk => errors.push(chunk));
    child.stdin.on('error', () => {});
    child.on('error', reject);
    child.on('close', code => {
      clearTimeout(timer);
      resolve({ code, output: Buffer.concat(output), error: Buffer.concat(errors).toString('utf8') });
    });
    if (bytes !== null) child.stdin.end(bytes);
  });
}

function rejected(result, code) {
  assert.equal(result.code, 2);
  assert.equal(result.output.length, 0);
  assert.equal(result.error, `${code}\n`);
}

test('CLI rejects mutable image tags without emitting diagnostics or source bytes', async () => {
  const values = args(); values[1] = 'private/image:latest';
  rejected(await invoke(values), 'fn_ai_pdf_sandbox_invalid_input');
});

test('CLI rejects duplicate, unknown, missing and enlarged arguments', async () => {
  for (const values of [[], args().slice(0, -1), [...args(), '--image-id', `sha256:${'b'.repeat(64)}`],
    [...args(), '--path', '/private/source.pdf'], [...args(), '--timeout-ms', '30001'],
    [...args(), '--timeout-ms', '0'], [...args(), '--timeout-ms', '1e3'], [...args(), '--run-id', 'invalid-id']]) {
    rejected(await invoke(values), 'fn_ai_pdf_sandbox_invalid_input');
  }
});

test('CLI validates actual digest and length before accessing Docker', async () => {
  const digest = args(); digest[3] = 'b'.repeat(64);
  rejected(await invoke(digest), 'fn_ai_pdf_sandbox_source_mismatch');
  const size = args(); size[5] = String(input.length + 1);
  rejected(await invoke(size), 'fn_ai_pdf_sandbox_source_mismatch');
  rejected(await invoke(args(), Buffer.alloc(0)), 'fn_ai_pdf_sandbox_source_mismatch');
});

test('CLI enforces byte limit before accumulating an oversized input', async () => {
  rejected(await invoke(args(), Buffer.alloc(5 * 1024 * 1024 + 1)), 'fn_ai_pdf_sandbox_input_limit');
});

test('CLI deadline includes a stalled input stream', async () => {
  rejected(await invoke([...args(), '--timeout-ms', '100'], null), 'fn_ai_pdf_sandbox_timeout');
});
