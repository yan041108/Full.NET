import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));
const ids = ['11111111-1111-4111-8111-111111111111', '22222222-2222-4222-8222-222222222222'];
const report = (kind) => {
  const rows = kind === 'duplicate' ? [ids[0], ids[0]] : kind === 'foreign' ? [ids[0], '33333333-3333-4333-8333-333333333333'] : ids;
  const outcome = kind === 'skipped' ? 'NotExecuted' : kind === 'failed' ? 'Failed' : 'Passed';
  const passed = ['skipped', 'failed'].includes(kind) ? 1 : 2;
  return `<?xml version="1.0"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>${rows.map((id, i) => `<UnitTestResult testId="${id}" outcome="${i ? outcome : 'Passed'}" />`).join('')}</Results><ResultSummary outcome="${kind === 'aborted' ? 'Aborted' : 'Completed'}"><Counters total="2" executed="${kind === 'skipped' ? 1 : 2}" passed="${kind === 'counters' ? 1 : passed}" failed="${kind === 'failed' ? 1 : 0}" notExecuted="${kind === 'skipped' ? 1 : 0}" error="0" timeout="0" aborted="0" inconclusive="0" /></ResultSummary></TestRun>`;
};

// 在独立 Node 进程替换 dotnet 和 TRX 输入，验证真实入口判定，不启动构建或数据库。
const hook = `
import fs from 'node:fs';
import cp from 'node:child_process';
import { syncBuiltinESMExports } from 'node:module';
const read = fs.readFileSync;
const gate = { project: 'mock.csproj', filter: 'mock', minimum: 2, timeout: '1m', requiredTestTypes: ['NativeProbe'] };
Object.defineProperty(process, 'platform', { value: process.env.NATIVE_CASE_PLATFORM });
fs.readFileSync = function(file, ...args) {
  const name = String(file).replaceAll('\\\\', '/');
  if (name.endsWith('/eng/testing/test-matrix.json')) return JSON.stringify({ integration: { assembly: 'mock.dll' }, nativeAotIntegration: gate, workerNativeAotIntegration: gate });
  if (name.endsWith('.trx')) {
    if (process.env.NATIVE_CASE_KIND === 'missing') { const error = new Error('Missing TRX'); error.code = 'ENOENT'; throw error; }
    return process.env.NATIVE_CASE_TRX;
  }
  return read.call(this, file, ...args);
};
cp.spawnSync = function(command, args) {
  if (args.includes('--list-tests')) return { status: 0, stdout: JSON.stringify({ tests: JSON.parse(process.env.NATIVE_CASE_IDS).map(uid => ({ uid, type: { typeName: 'NativeProbe' } })) }) };
  if (args[0] !== 'build') fs.writeFileSync(process.env.NATIVE_CASE_CAPTURE, JSON.stringify(args));
  return { status: 0 };
};
syncBuiltinESMExports();
`;

for (const runner of ['run-native-aot-e2e.mjs', 'run-native-aot-worker-e2e.mjs']) {
  for (const kind of ['passed', 'skipped', 'failed', 'counters', 'duplicate', 'foreign', 'aborted', 'truncated', 'missing']) {
    test(`${runner}: dotnet 退出 0 时 ${kind} 必须按完整 TRX 判定`, t => {
      const temp = mkdtempSync(path.join(os.tmpdir(), 'fullnet-native-results-'));
      t.after(() => {
        assert.equal(path.dirname(temp), path.resolve(os.tmpdir()));
        assert(path.basename(temp).startsWith('fullnet-native-results-'));
        rmSync(temp, { recursive: true, force: true });
      });
      const preload = path.join(temp, 'hook.mjs'); writeFileSync(preload, hook);
      const child = spawnSync(process.execPath, ['--import', pathToFileURL(preload).href, path.join(root, 'scripts/testing', runner)], {
        cwd: root, encoding: 'utf8', windowsHide: true,
        env: { ...process.env, NATIVE_CASE_PLATFORM: 'linux', NATIVE_CASE_KIND: kind, NATIVE_CASE_IDS: JSON.stringify(ids), NATIVE_CASE_CAPTURE: path.join(temp, 'args.json'),
          NATIVE_CASE_TRX: kind === 'truncated' ? report('passed').replace('</TestRun>', '') : report(kind) },
      });
      assert.ifError(child.error);
      assert.equal(child.status, kind === 'passed' ? 0 : 1, child.stderr || child.stdout);
    });
  }
}

// 完整协议边界直接验证；故障输出里的 XML 形文本不得充当结果。
const { validateNativeAotRuntimeResults } = await import('../../scripts/testing/native-aot-runtime-results.mjs');
for (const [name, transform, accepted] of [
  ['正常完整报告', xml => xml, true],
  ['CDATA 中的伪造失败不属于测试结果', xml => xml.replace('</Results>', '<![CDATA[<UnitTestResult testId="fake" outcome="Failed" />]]></Results>'), true],
  ['注释中的伪造失败不属于测试结果', xml => xml.replace('</Results>', '<!-- <UnitTestResult testId="fake" outcome="Failed" /> --></Results>'), true],
  ['缺少结束标签', xml => xml.replace('</TestRun>', ''), false],
  ['结果元素未闭合', xml => xml.replace('outcome="Passed" />', 'outcome="Passed">'), false],
  ['错误关闭顺序', xml => xml.replace('</Results>', '</Other>'), false],
  ['重复属性', xml => xml.replace('total="2"', 'total="2" total="2"'), false],
  ['缺少失败计数', xml => xml.replace(' failed="0"', ''), false],
  ['未执行计数非零', xml => xml.replace('notExecuted="0"', 'notExecuted="1"'), false],
  ['存在 Inconclusive', xml => xml.replace('inconclusive="0"', 'inconclusive="1"'), false],
  ['非整数计数', xml => xml.replace('total="2"', 'total="2.0"'), false],
  ['DTD 外部实体', xml => xml.replace('<TestRun ', '<!DOCTYPE TestRun SYSTEM "https://example.invalid/x"><TestRun '), false],
  ['重复摘要', xml => xml.replace('</TestRun>', '<ResultSummary outcome="Completed"><Counters total="2" /></ResultSummary></TestRun>'), false],
]) {
  test(`TRX 完整性：${name}`, () => {
    if (accepted) assert.equal(validateNativeAotRuntimeResults(transform(report('passed')), ids).passed, 2);
    else assert.throws(() => validateNativeAotRuntimeResults(transform(report('passed')), ids));
  });
}

test('发现身份本身重复时不能以相同计数升级通过', () => {
  assert.throws(() => validateNativeAotRuntimeResults(report('passed'), [ids[0], ids[0]]));
  assert.throws(() => validateNativeAotRuntimeResults(report('passed'), []));
});

for (const runner of ['run-native-aot-e2e.mjs', 'run-native-aot-worker-e2e.mjs']) {
  test(`${runner}: 本轮 TRX 文件名隔离，Windows 仍允许原有跳过策略`, t => {
    const temp = mkdtempSync(path.join(os.tmpdir(), 'fullnet-native-results-'));
    t.after(() => {
      assert.equal(path.dirname(temp), path.resolve(os.tmpdir()));
      assert(path.basename(temp).startsWith('fullnet-native-results-'));
      rmSync(temp, { recursive: true, force: true });
    });
    const preload = path.join(temp, 'hook.mjs'); writeFileSync(preload, hook);
    const capture = path.join(temp, 'args.json');
    const invoke = platform => spawnSync(process.execPath, ['--import', pathToFileURL(preload).href, path.join(root, 'scripts/testing', runner)], {
      cwd: root, encoding: 'utf8', windowsHide: true,
      env: { ...process.env, NATIVE_CASE_PLATFORM: platform, NATIVE_CASE_KIND: 'skipped', NATIVE_CASE_IDS: JSON.stringify(ids), NATIVE_CASE_CAPTURE: capture, NATIVE_CASE_TRX: report('skipped') },
    });
    const first = invoke('win32'); assert.equal(first.status, 0, first.stderr);
    const firstArgs = JSON.parse(readFileSync(capture, 'utf8'));
    const second = invoke('win32'); assert.equal(second.status, 0, second.stderr);
    const secondArgs = JSON.parse(readFileSync(capture, 'utf8'));
    const file = args => args[args.indexOf('--report-trx-filename') + 1];
    assert.notEqual(file(firstArgs), file(secondArgs));
    assert(file(firstArgs).endsWith('.trx'));
    assert(secondArgs.includes('allow-skipped'));
  });
}

test('TRX Results 与 ResultSummary 必须为 TestRun 直属节点', () => {
  const xml = report('passed').replace('<Results>', '<Output><Results>').replace('</ResultSummary>', '</ResultSummary></Output>');
  assert.throws(() => validateNativeAotRuntimeResults(xml, ids));
});
test('未声明 XML 实体不能进入成功报告', () => {
  assert.throws(() => validateNativeAotRuntimeResults(report('passed').replace('</TestRun>', '<Output>&undeclared;</Output></TestRun>'), ids));
});
test('合法属性中的大于号不会截断标签', () => {
  assert.equal(validateNativeAotRuntimeResults(report('passed').replace('outcome="Passed"', 'testName="A > B" outcome="Passed"'), ids).passed, 2);
});

for (const [name, change, accepted] of [
  ['全部使用单引号', xml => xml.replaceAll('"', "'"), true],
  ['已声明字符实体与属性大于号', xml => xml.replace('outcome="Passed"', 'testName="A &gt; B &amp; C" outcome="Passed"'), true],
  ['非法数字字符实体', xml => xml.replace('</TestRun>', '<Output>&#0;</Output></TestRun>'), false],
  ['相邻属性缺少空白', xml => xml.replace('total="2" executed', 'total="2"executed'), false],
  ['结果节点覆盖默认命名空间', xml => xml.replace('<Results>', '<Results xmlns="">'), false],
  ['未知命名空间前缀', xml => xml.replace('<Results>', '<Results x:fake="1">'), false],
  ['根外非XML空白', xml => '\u00a0' + xml, false],
]) {
  test(`XML 协议：${name}`, () => {
    if (accepted) assert.equal(validateNativeAotRuntimeResults(change(report('passed')), ids).passed, 2);
    else assert.throws(() => validateNativeAotRuntimeResults(change(report('passed')), ids));
  });
}
