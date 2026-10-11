#!/usr/bin/env node
/** 企业 Native 专项统一复用消费者构建，真实运行仍须具备对应原生产物。 */
import path from 'node:path';
import {pathToFileURL} from 'node:url';
import {runNativeSuite} from './native-suite-runner.mjs';
import assert from 'node:assert/strict';
import {summarizeTrxOutcomes} from './summarize-trx-outcomes.mjs';

/** 双角色专项必须实际执行全部用例；缺少产物、跳过或非成功结果不能验收。 */
export function assertEnterpriseNativeResult(xml, minimum) {
  const summary = summarizeTrxOutcomes(xml);
  assert.ok(summary.outcomes.Passed >= minimum, '企业 Native 实际通过数不足');
  for (const [outcome, count] of Object.entries(summary.outcomes)) {
    if (outcome !== 'Passed') assert.equal(count, 0, `企业 Native 存在 ${outcome}`);
  }
  return summary;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  runNativeSuite({
    suite: 'nativeAotEnterpriseIntegration',
    results: 'artifacts/native-aot/enterprise/test-results',
    trx: 'Full.NET.IntegrationTests-native-enterprise.trx',
    label: '企业 Native',
    discoveryOnlyOnNonLinux: true,
  }).catch(error => {console.error(error.message); process.exitCode = 1;});
}
