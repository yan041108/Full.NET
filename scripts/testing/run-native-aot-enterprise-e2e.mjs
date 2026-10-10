#!/usr/bin/env node
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { withTestRun, testRunEnvironment } from './test-run-context.mjs';
import { prepareTestBuild } from './test-build-reuse.mjs';
import { summarizeTrxOutcomes } from './summarize-trx-outcomes.mjs';

/** 双角色专项必须实际执行全部用例；缺少产物、跳过或非成功结果不能验收。 */
export function assertEnterpriseNativeResult(xml, minimum) {
  const summary = summarizeTrxOutcomes(xml);
  assert.ok(summary.outcomes.Passed >= minimum, '企业 Native 实际通过数不足');
  for (const [outcome, count] of Object.entries(summary.outcomes)) {
    if (outcome !== 'Passed') assert.equal(count, 0, `企业 Native 存在 ${outcome}`);
  }
  return summary;
}

async function main() {
  const cwd = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
  const matrix = JSON.parse(readFileSync(path.join(cwd, 'eng/testing/test-matrix.json'), 'utf8'));
  const suite = matrix.nativeAotEnterpriseIntegration;
  const assembly = matrix.integration.assembly;
  const results = 'artifacts/native-aot/enterprise/test-results';
  const trx = 'Full.NET.IntegrationTests-native-enterprise.trx';
  const run = (args, env = testRunEnvironment(), capture = false) => new Promise((resolve, reject) => {
    const child = spawn('dotnet', args, { cwd, env, windowsHide: true, stdio: ['ignore', capture ? 'pipe' : 'inherit', 'inherit'] });
    let output = '';
    if (capture) child.stdout.on('data', chunk => { output += chunk; });
    child.once('error', reject);
    child.once('close', (code, signal) => code === 0 && !signal ? resolve(output) :
      reject(new Error(`企业 Native 命令退出：${code ?? signal}`)));
  });
  await withTestRun({ cwd, heavy: process.platform === 'linux' }, async () => {
    const args = ['build', suite.project, '-c', 'Release', '--nologo'];
    await prepareTestBuild({ cwd, project: suite.project, assembly, args, mode: 'reuse', build: env => run(args, env) });
    const discovery = JSON.parse(await run([assembly, '--list-tests', 'json', '--no-ansi', '--filter', suite.filter], undefined, true));
    assert.ok(discovery.tests?.length >= suite.minimum, '企业 Native 发现数不足');
    if (process.platform !== 'linux') {
      console.log(`企业 Native 非 Linux 仅发现 ${discovery.tests.length} 项，未执行真实原生业务。`);
      return;
    }
    await run([assembly, '--filter', suite.filter, '--minimum-expected-tests', String(suite.minimum),
      '--no-ansi', '--progress', 'off', '--timeout', suite.timeout, '--report-trx',
      '--report-trx-filename', trx, '--results-directory', results]);
    const summary = assertEnterpriseNativeResult(readFileSync(path.join(cwd, results, trx), 'utf8'), suite.minimum);
    console.log(`企业 Native 实际通过 ${summary.outcomes.Passed} 项，零失败/跳过。`);
  });
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
}
