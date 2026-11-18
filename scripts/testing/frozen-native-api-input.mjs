import assert from 'node:assert/strict';

// 只允许不参与原生生产构建的变更；未知输入默认要求重新发布。
const consumerOnlyPaths = new Set([
  'security/client-audit-policy.json',
  '.github/workflows/native-notifications-focused.yml',
  'scripts/testing/frozen-native-api-input.mjs'
]);

/** 核对同仓库 main 发布的身份与摘要；业务或构建输入改变时拒绝复用原生包。 */
export function validateFrozenNativeApiInput({repository, run, artifact, jobs, changedPaths}) {
  assert.equal(run.repository?.full_name, repository, '原生包必须来自当前仓库');
  assert.equal(run.head_repository?.full_name, repository, '原生包不能来自 Fork');
  assert.equal(run.head_branch, 'main', '原生包必须来自 main');
  assert.equal(run.path, '.github/workflows/api-native-aot-linux.yml', '原生包发布入口不符');
  assert.equal(run.status, 'completed', '原生包来源作业尚未结束');
  assert.match(run.head_sha, /^[a-f0-9]{40}$/u, '来源提交无效');
  assert.ok(jobs.some(job => job.name === 'linux-native-aot' && job.steps?.some(step =>
    step.name === 'Publish linux-x64 Native AOT artifact' && step.conclusion === 'success')),
  '缺少成功的原生 API 发布回执');
  assert.equal(artifact.name, 'api-native-aot-linux-manifest', '原生包名称不符');
  assert.equal(artifact.expired, false, '原生包已过期');
  assert.equal(artifact.workflow_run?.id, run.id, '原生包来源作业不符');
  assert.equal(artifact.workflow_run?.head_sha, run.head_sha, '原生包来源提交不符');
  assert.ok(Number.isSafeInteger(artifact.id) && artifact.id > 0, '原生包 ID 无效');
  assert.match(artifact.digest, /^sha256:[a-f0-9]{64}$/u, '原生包缺少可靠摘要');
  assert.ok(Array.isArray(changedPaths) && changedPaths.every(file => typeof file === 'string' &&
    !file.split('/').includes('..') && (file.startsWith('tests/') || file.startsWith('docs/') || consumerOnlyPaths.has(file))),
  '生产源码或构建输入改变，必须重新发布原生 API');
  return {artifactId: artifact.id, digest: artifact.digest.slice(7), sourceSha: run.head_sha};
}
