import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const targets = [
  ['ci', 'client-build-test'],
  ['ci', 'build-and-module-test'],
  ['ci', 'affected-migration-recovery'],
  ['api-native-aot-linux', null],
  ['worker-native-aot-linux', null]
];

async function readWorkflow(name) {
  return readFile(new URL(`../../.github/workflows/${name}.yml`, import.meta.url), 'utf8');
}

function jobBlock(workflow, job) {
  return workflow.split(`\n  ${job}:`)[1]?.split(/\n  [a-z][\w-]+:/u)[0];
}

function policy(workflow, job) {
  const source = job ? jobBlock(workflow, job) : workflow.split('\njobs:')[0];
  assert.ok(source, `缺少作业：${job}`);
  const indent = job ? '    ' : '';
  const block = source.match(new RegExp(`^${indent}concurrency:\\r?\\n((?:${indent}  .+\\r?\\n?)+)`, 'm'))?.[1];
  assert.ok(block, `缺少并发边界：${job ?? 'workflow'}`);
  const group = block.match(/group: (.+)/u)?.[1];
  const cancel = block.match(/cancel-in-progress: (.+)/u)?.[1];
  assert.ok(group && cancel, '并发策略须声明分组与取消条件');
  return { group, cancel };
}

function resolvePolicy(config, github, matrix = {}) {
  // 与已有标签门禁测试相同，只求值仓库受控配置中的 Actions 布尔表达式。
  const evaluate = expression => Function('github', 'matrix', `return (${expression});`)(github, matrix);
  const group = config.group.replace(/\$\{\{\s*(.*?)\s*\}\}/gu, (_, expression) => evaluate(expression));
  const expression = config.cancel.match(/^\$\{\{\s*(.*?)\s*\}\}$/u)?.[1] ?? config.cancel;
  return { group, cancel: evaluate(expression) };
}

function context(workflow, event, run, pr) {
  return { workflow, event_name: event, run_id: run, event: { pull_request: { number: pr } } };
}

for (const [workflow, job] of targets) {
  test(`${workflow}/${job ?? 'workflow'} 只替代同一 PR 的同类旧检查`, async () => {
    const config = policy(await readWorkflow(workflow), job);
    const matrix = { group: 'migrations-legacy' };
    const current = resolvePolicy(config, context(workflow, 'pull_request', 1001, 3), matrix);
    const replacement = resolvePolicy(config, context(workflow, 'pull_request', 1002, 3), matrix);
    assert.equal(current.group, replacement.group, '同一 PR 的连续推送须使用同一分组');
    assert.equal(replacement.cancel, true);
    assert.notEqual(current.group, resolvePolicy(config, context(workflow, 'pull_request', 1003, 4), matrix).group);
    assert.notEqual(current.group, resolvePolicy(config, context('other-workflow', 'pull_request', 1004, 3), matrix).group);
    for (const event of ['push', 'workflow_dispatch']) {
      const first = resolvePolicy(config, context(workflow, event, 1001), matrix);
      const second = resolvePolicy(config, context(workflow, event, 1002), matrix);
      assert.equal(first.cancel, false, `${event} 不取消运行中的检查`);
      assert.notEqual(first.group, second.group, `${event} 不替换等待中的检查`);
      assert.notEqual(first.group, current.group, '不同事件须隔离，即使数值标识相同');
      assert.notEqual(resolvePolicy(config, context(workflow, event, 3), matrix).group, current.group);
    }
    const missingPr = resolvePolicy(config, context(workflow, 'pull_request', 1001), matrix);
    assert.notEqual(missingPr.group, resolvePolicy(config, context(workflow, 'pull_request', 1002), matrix).group);
  });
}

test('CI 作业和双迁移分片拥有独立并发分组', async () => {
  const workflow = await readWorkflow('ci');
  const github = context('ci', 'pull_request', 1001, 3);
  const groups = targets.filter(([name]) => name === 'ci').map(([, job]) =>
    resolvePolicy(policy(workflow, job), github, { group: 'migrations-legacy' }).group);
  const current = resolvePolicy(policy(workflow, 'affected-migration-recovery'), github, { group: 'migrations-current' }).group;
  groups.push(current);
  assert.equal(new Set(groups).size, groups.length, '同次运行的作业及两个迁移分片不能互相取消');
});

test('必需结果汇总不被旧运行晚到的汇总取消', async () => {
  const source = jobBlock(await readWorkflow('ci'), 'build-test');
  assert.ok(source);
  // 旧依赖取消后的清理可能晚于新运行结束，汇总不能按入组先后淘汰另一份汇总。
  assert.doesNotMatch(source, /^    concurrency:/mu);
  assert.match(source, /needs: \[build-and-module-test, affected-migration-recovery\]/u);
  assert.match(source, /run: node scripts\/testing\/verify-ci-job-results\.mjs/u);
});

test('显式独立应用验收与 main 完整集不加入可替代的 PR 检查分组', async () => {
  const workflow = await readWorkflow('ci');
  assert.doesNotMatch(workflow.split('\njobs:')[0], /^concurrency:/mu, 'CI 不得整体取消显式验收');
  for (const job of ['template-created-app-real-stack', 'foundation-sample-e2e',
    'real-stack-e2e', 'real-stack-e2e-mysql', 'real-stack-e2e-production-totp',
    'integration-matrix', 'integration-shard', 'integration-gate']) {
    const source = jobBlock(workflow, job);
    assert.ok(source, job);
    assert.doesNotMatch(source, /^    concurrency:/mu, `${job} 不加入 PR 旧检查替代策略`);
  }
});
