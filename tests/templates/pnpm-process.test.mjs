import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import test from 'node:test';

const helper = new URL('./support/pnpm-process.mjs', import.meta.url).href;
const argumentSets = [
  ['install', '--filter', '@fullnet/admin...', '--frozen-lockfile', '--ignore-scripts'],
  ['--filter', '@fullnet/admin', 'build'],
  ['install', '--frozen-lockfile'],
  ['--filter', '@fullnet/client-contracts', 'build'],
];

// 子 Node 将弃用警告转为失败；真正经过本机命令解析器，不能由调用形状替身掩盖。
function invoke(args, options = {}) {
  return spawnSync(process.execPath, ['--throw-deprecation', '--input-type=module', '-e',
    `import { runPnpm } from ${JSON.stringify(helper)};
const result = runPnpm(${JSON.stringify(args)}, { encoding: 'utf8', timeout: 30_000 });
if (result.error) throw result.error;
process.stdout.write(result.stdout ?? '');
process.stderr.write(result.stderr ?? '');
process.exitCode = result.status ?? 1;`],
  { encoding: 'utf8', timeout: 40_000, windowsHide: true, ...options });
}

test('template pnpm launch runs the installed CLI without deprecation warnings', () => {
  const result = invoke(['--version']);
  assert.equal(result.error, undefined);
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout.trim(), /^\d+\.\d+\.\d+$/u);
  assert.equal(result.stderr.includes('DEP0190'), false);
});

for (const args of argumentSets) {
  test('template pnpm launch preserves arguments in a workspace with spaces: ' + args.join(' '), () => {
    const root = mkdtempSync(join(tmpdir(), 'fullnet pnpm arguments '));
    try {
      const capture = join(root, 'capture.mjs');
      writeFileSync(capture, "process.stdout.write(JSON.stringify({ args: process.argv.slice(2), cwd: process.cwd() }));\n");
      if (process.platform === 'win32') {
        writeFileSync(join(root, 'pnpm.cmd'), `@echo off\r\n@"${process.execPath}" "${capture}" %*\r\n`);
      } else {
        const shim = join(root, 'pnpm');
        writeFileSync(shim, `#!/bin/sh\nexec "${process.execPath}" "${capture}" "$@"\n`);
        chmodSync(shim, 0o755);
      }
      const environment = { ...process.env };
      const pathKey = Object.keys(environment).find(key => key.toLowerCase() === 'path') ?? 'PATH';
      environment[pathKey] = root + (process.platform === 'win32' ? ';' : ':') + (environment[pathKey] ?? '');
      const result = invoke(args, { cwd: root, env: environment });
      assert.equal(result.error, undefined);
      assert.equal(result.status, 0, result.stderr);
      assert.deepEqual(JSON.parse(result.stdout), { args, cwd: root });
      assert.equal(result.stderr.includes('DEP0190'), false);
    } finally {
      // 清理范围固定为本用例新建的临时目录。
      rmSync(root, { recursive: true, force: true });
    }
  });
}

test('template pnpm launch retains a failed command exit code', () => {
  const result = invoke(['--filter', '@fullnet/nonexistent-template-probe', '--fail-if-no-match', 'build']);
  assert.equal(result.error, undefined);
  assert.equal(result.status, 1, result.stderr);
  assert.match(result.stdout + result.stderr, /No projects matched/u);
  assert.equal(result.stderr.includes('DEP0190'), false);
});

if (process.platform === 'win32') {
  test('template pnpm launch rejects shell syntax before starting a command', async () => {
    const { runPnpm } = await import(helper);
    for (const value of ['a b', 'a&b', 'a|b', '%PATH%', '!PATH!', 'a"b', 'a\nb', '', 'a>b', 'a<b', '(a)', '^a']) {
      assert.throws(() => runPnpm(['--filter', value]), /fixed pnpm argument/u);
    }
  });
}
