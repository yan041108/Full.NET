import assert from 'node:assert/strict';
import test from 'node:test';
import * as gate from '../../scripts/testing/verify-ci-job-results.mjs';

test('PR 的 build-test 汇总必须等待模块和迁移全部成功', () => {
  assert.equal(typeof gate.verifyBuildTestResults, 'function');
  assert.doesNotThrow(() => gate.verifyBuildTestResults('pull_request', 'success', 'success'));
  for (const result of ['failure', 'cancelled', 'skipped', undefined]) {
    assert.throws(() => gate.verifyBuildTestResults('pull_request', 'success', result), /迁移/);
    assert.throws(() => gate.verifyBuildTestResults('pull_request', result, 'success'), /构建与模块/);
  }
});

test('main 仍由已有完整迁移分片验收，PR 专属恢复矩阵可以跳过', () => {
  assert.equal(typeof gate.verifyBuildTestResults, 'function');
  assert.doesNotThrow(() => gate.verifyBuildTestResults('push', 'success', 'skipped'));
  assert.throws(() => gate.verifyBuildTestResults('push', 'failure', 'skipped'), /构建与模块/);
  assert.throws(() => gate.verifyBuildTestResults('unknown', 'success', 'skipped'), /事件/);
});
