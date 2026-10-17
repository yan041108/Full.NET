import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {readFileSync} from 'node:fs';
import {unlink} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {prepareTestBuild} from './test-build-reuse.mjs';
import {withPreparedTestRun} from './prepared-test-run.mjs';
import {testRunEnvironment} from './test-run-context.mjs';
import {summarizeTrxOutcomes} from './summarize-trx-outcomes.mjs';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

function runDotnet(cwd, args, env, capture) {
  return new Promise((resolve, reject) => {
    const child = spawn('dotnet', args, {cwd, env, windowsHide: true, shell: false,
      stdio: ['ignore', capture ? 'pipe' : 'inherit', 'inherit']});
    let output = '';
    if (capture) child.stdout.on('data', chunk => { output += chunk; });
    child.once('error', reject);
    // close 等待输出管道关闭，不能在子进程收尾前释放测试资源。
    child.once('close', (code, signal) => code === 0 && !signal ? resolve(output) :
      reject(new Error(`Native 命令退出：${code ?? signal}`)));
  });
}

/** 多个专项共享同一构建身份，仍保留各自的发现门禁、运行期限和独立报告。 */
export async function runNativeSuite({suite, results, trx, label = 'Native AOT', discoveryOnlyOnNonLinux = false},
  {cwd = repositoryRoot, platform = process.platform, lockRoot, signal, timeoutMs, sdkVersion,
    runCommand = (args, env, capture) => runDotnet(cwd, args, env, capture)} = {}) {
  const matrixPath = path.join(cwd, 'eng/testing/test-matrix.json');
  const matrixText = readFileSync(matrixPath, 'utf8');
  const matrix = JSON.parse(matrixText), nativeGate = matrix[suite];
  assert.ok(nativeGate?.project && nativeGate.filter && nativeGate.timeout, 'Native 测试矩阵不完整');
  assert.ok(Number.isInteger(nativeGate.minimum) && nativeGate.minimum > 0, 'Native 最低发现数必须为正整数');
  assert.ok(matrix.integration?.assembly, 'Native 消费者程序集未登记');
  const resultPath = path.resolve(cwd, results, trx);
  const relative = path.relative(cwd, resultPath);
  assert.ok(relative && !path.isAbsolute(relative) && relative !== '..' && !relative.startsWith('..' + path.sep),
    '结果路径必须位于工作区');
  // 使用完全相同的参数拼写，使 API、Worker 和 Provider 共享消费者构建记录。
  const args = ['build', nativeGate.project, '--configuration', 'Release', '--nologo'];
  const assembly = matrix.integration.assembly;
  const prepare = mode => {
    assert.equal(readFileSync(matrixPath, 'utf8'), matrixText, '测试矩阵在准备或排队期间发生变化');
    return prepareTestBuild({cwd, project: nativeGate.project, assembly, args, mode, sdkVersion,
      build: env => runCommand(args, env, false)});
  };
  return withPreparedTestRun({cwd, heavy: platform === 'linux', disposableBuild: true,
    lockRoot, signal, timeoutMs, prepare}, async () => {
    const discovery = JSON.parse(await runCommand([assembly, '--list-tests', 'json', '--no-ansi', '--filter', nativeGate.filter],
      testRunEnvironment(), true));
    const discovered = discovery.tests ?? [];
    assert.ok(discovered.length >= nativeGate.minimum, `${label} 发现数不足`);
    const discoveredTypes = new Set(discovered.map(item => item.type?.typeName));
    for (const requiredType of nativeGate.requiredTestTypes ?? []) {
      assert.ok(discoveredTypes.has(requiredType), `${label} 缺少必需用例：${requiredType}`);
    }
    if (platform !== 'linux' && discoveryOnlyOnNonLinux) {
      console.log(`${label} 非 Linux 仅发现 ${discovered.length} 项，未执行真实原生业务。`);
      return {nativeVerified: false, discovered: discovered.length};
    }
    // 删除本专项旧报告，避免零退出但未生成新 TRX 时误读历史成功记录。
    try { await unlink(resultPath); } catch (error) { if (error.code !== 'ENOENT') throw error; }
    const policy = platform === 'linux' ? ['--minimum-expected-tests', String(nativeGate.minimum)] :
      ['--zero-tests-policy', 'allow-skipped'];
    await runCommand([assembly, '--no-ansi', '--progress', 'off', '--timeout', nativeGate.timeout,
      '--filter', nativeGate.filter, '--results-directory', path.dirname(resultPath), '--report-trx',
      '--report-trx-filename', path.basename(resultPath), ...policy], testRunEnvironment(), false);
    if (platform !== 'linux') {
      console.log(`${label} 非 Linux 发现/跳过门禁完成，未验收原生业务。`);
      return {nativeVerified: false, discovered: discovered.length};
    }
    const summary = summarizeTrxOutcomes(readFileSync(resultPath, 'utf8'));
    // 命令成功不能掩盖跳过、未知结果或不足的实际执行范围。
    for (const [outcome, count] of Object.entries(summary.outcomes)) {
      if (outcome !== 'Passed') assert.equal(count, 0, `${label} 存在 ${outcome}`);
    }
    assert.ok(summary.outcomes.Passed >= nativeGate.minimum, `${label} 实际通过数不足`);
    console.log(`${label} 实际通过 ${summary.outcomes.Passed} 项，零失败/跳过。`);
    return {nativeVerified: true, discovered: discovered.length, summary};
  });
}
