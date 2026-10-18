import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const entry = fileURLToPath(new URL('../../scripts/templates/diagnose-app.mjs', import.meta.url));
function temporaryWorkspace(t, prefix) {
  const root = mkdtempSync(join(tmpdir(), prefix));
  t.after(() => {
    // 核对自有临时目录的父目录后清理，不能删除调用方的工作区。
    assert.equal(dirname(resolve(root)), resolve(tmpdir()));
    rmSync(root, { recursive: true, force: true });
  });
  return root;
}
const sdkCases = [
  ['10.0.100', true], ['10.0.401', true], ['10.0.100-preview.1', true],
  ['10.0.401+credential-probe', true], ['10.0.99', false], ['9.0.307', false],
  ['11.0.100', false], ['10.1.100', false], ['010.0.100', false],
  ['10.00.100', false], ['10.0.0100', false], ['10.0.100.1', false],
  ['10.0.100-', false], ['10.0.100-preview..1', false], ['10.0.100+', false],
  ['10.0.2147483647', true], ['10.0.2147483648', false],
  ['10.0.100\ncredential-probe', false], ['', false], ['credential-probe', false],
];
for (const [version, expected] of sdkCases) {
  test('application SDK preflight follows the CLI baseline: ' + JSON.stringify(version), async () => {
    const { isCompatibleSdkVersion } = await import('../../scripts/templates/diagnose-app.mjs');
    assert.equal(isCompatibleSdkVersion(version), expected);
  });
}

for (const profile of ['development', 'production']) {
  for (const absentCommand of [false, true]) {
    test(`application entry reports missing SDK before CLR startup: ${profile}, absent command=${absentCommand}`, t => {
      const root = temporaryWorkspace(t, 'fullnet-diagnose-entry-');
      const sdk = Buffer.from(JSON.stringify({ sdk: { version: '99.0.100', rollForward: 'disable' } }));
      writeFileSync(join(root, 'global.json'), sdk);
      writeFileSync(join(root, 'manual.txt'), 'credential-probe');
      const env = { ...process.env };
      if (absentCommand) {
        // Windows 环境键忽略大小写；去掉所有变体以实际复现找不到 dotnet。
        for (const key of Object.keys(env)) if (key.toLowerCase() === 'path') delete env[key];
        env.PATH = root;
      }
      const result = spawnSync(process.execPath, [entry, '--profile', profile], {
        cwd: root, env, encoding: 'utf8', windowsHide: true, timeout: 60_000,
      });
      assert.equal(result.error, undefined);
      assert.equal(result.status, 1, result.stderr || result.stdout);
      assert.match(result.stdout, /DIAG_SDK_MISSING error/u);
      assert.match(result.stdout, /安装 .NET 10 SDK/u);
      assert.doesNotMatch(result.stdout + result.stderr, /credential-probe|99\.0\.100|MSBUILD|error MSB/u);
      assert.deepEqual(readdirSync(root).sort(), ['global.json', 'manual.txt']);
      assert.deepEqual(readFileSync(join(root, 'global.json')), sdk);
      assert.equal(readFileSync(join(root, 'manual.txt'), 'utf8'), 'credential-probe');
    });
  }
}

for (const args of [[], ['--profile', 'staging'], ['--profile', 'development', '--initialize']]) {
  test('application diagnosis rejects unsupported arguments before SDK startup: ' + JSON.stringify(args), t => {
    const root = temporaryWorkspace(t, 'fullnet-diagnose-arguments-');
    const result = spawnSync(process.execPath, [entry, ...args], {
      cwd: root, encoding: 'utf8', windowsHide: true, timeout: 60_000,
    });
    assert.equal(result.error, undefined);
    assert.equal(result.status, 64);
    assert.match(result.stderr, /--profile development\|production/u);
    assert.deepEqual(readdirSync(root), []);
  });
}

const processCases = [
  ['probe timeout', { error: { code: 'ETIMEDOUT' }, stdout: 'credential-probe' }, null, 1, 'code_generation.sdk.probe_timeout error'],
  ['probe buffer failure', { error: { code: 'ENOBUFS' }, stdout: 'credential-probe' }, null, 1, 'DIAG_SDK_MISSING error'],
  ['probe nonzero exit', { status: 1, stdout: 'credential-probe' }, null, 1, 'DIAG_SDK_MISSING error'],
  ['empty successful probe', { status: 0, stdout: '  ' }, null, 1, 'DIAG_SDK_MISSING error'],
  ['incompatible SDK', { status: 0, stdout: '9.0.307+credential-probe' }, null, 1, 'DIAG_SDK_INCOMPATIBLE error'],
  ['valid SDK delegates success', { status: 0, stdout: '10.0.401+credential-probe' }, { status: 0 }, 0, null],
  ['valid SDK preserves CLI rejection', { status: 0, stdout: '10.0.401' }, { status: 1 }, 1, null],
  ['CLI launch failure', { status: 0, stdout: '10.0.401' }, { error: { code: 'ENOENT' }, status: null }, 1, 'code_generation.diagnose.start_failed error'],
  ['CLI signal termination', { status: 0, stdout: '10.0.401' }, { status: null, signal: 'SIGTERM' }, 1, 'code_generation.diagnose.start_failed error'],
];
for (const [name, probe, diagnosis, expected, code] of processCases) {
  test('application entry handles process boundaries without raw output: ' + name, async () => {
    const { diagnoseApplication } = await import('../../scripts/templates/diagnose-app.mjs');
    const calls = [], messages = [];
    // 无法移除 CI 已安装的 SDK 或等待真实卡死；仅进程结果用受控边界替身。
    const run = (command, args, options) => {
      calls.push({ command, args, options });
      return calls.length === 1 ? probe : diagnosis;
    };
    const output = { log: value => messages.push(value), error: value => messages.push(value) };
    assert.equal(diagnoseApplication(['--profile', 'production'], run, output), expected);
    assert.equal(calls.length, diagnosis ? 2 : 1);
    assert.doesNotMatch(messages.join('\n'), /credential-probe|9\.0\.307/u);
    if (code) assert.ok(messages.some(value => value.includes(code)));
    else assert.deepEqual(messages, [], 'preflight must not certify an application or duplicate CLI findings');
    assert.equal(calls[0].options.cwd, process.cwd());
    if (diagnosis) {
      assert.equal(calls[1].options.cwd, process.cwd());
      assert.equal(calls[1].options.stdio, 'inherit');
      assert.deepEqual(calls[1].args.slice(-5), ['diagnose', '--workspace', process.cwd(), '--profile', 'production']);
    }
  });
}
