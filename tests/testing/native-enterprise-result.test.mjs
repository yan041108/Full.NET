import assert from 'node:assert/strict';
import test from 'node:test';
import { assertEnterpriseNativeResult } from '../../scripts/testing/run-native-aot-enterprise-e2e.mjs';

const results = (...outcomes) => `<TestRun><Results>${outcomes.map((outcome, index) =>
  `<UnitTestResult testId="case-${index}" testName="provider-${index}" outcome="${outcome}"/>`).join('')}</Results></TestRun>`;

test('双库全部实际通过可以关闭企业 Native 专项', () => {
  assert.equal(assertEnterpriseNativeResult(results('Passed', 'Passed'), 2).outcomes.Passed, 2);
});

test('发现但零执行及只执行一个数据库不能关闭专项', () => {
  assert.throws(() => assertEnterpriseNativeResult(results(), 2), /实际通过数不足/u);
  assert.throws(() => assertEnterpriseNativeResult(results('Passed'), 2), /实际通过数不足/u);
});

test('达到通过门槛也不能隐藏跳过、失败或未知结果', () => {
  for (const outcome of ['Inconclusive', 'Skipped', 'Failed', 'NotExecuted', 'new-outcome']) {
    assert.throws(() => assertEnterpriseNativeResult(results('Passed', 'Passed', outcome), 2), /企业 Native 存在/u);
  }
});
