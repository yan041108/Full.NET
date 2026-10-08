import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

test('集中独立应用验收由显式 PR 批次标记触发，main 保留完整验收', async () => {
  const workflow = await readFile(new URL('../../.github/workflows/ci.yml', import.meta.url), 'utf8');
  assert.match(workflow, /pull_request:\s*\n\s+types:.*labeled/u);
  for (const name of ['template-created-app-real-stack', 'foundation-sample-e2e']) {
    const block = workflow.split(`  ${name}:`)[1]?.split(/\n  [a-z][\w-]+:/u)[0];
    assert.ok(block, name);
    assert.match(block, /contains\(github\.event\.pull_request\.labels\.\*\.name, 'fullnet:acceptance'\)/u);
  }
  const generated = workflow.split('  template-created-app-real-stack:')[1].split(/\n  [a-z][\w-]+:/u)[0];
  assert.match(generated, /github\.ref == 'refs\/heads\/main'/u);
  assert.doesNotMatch(generated, /continue-on-error:\s*true/u);
});

test('标签事件只启动本次明确请求的集中验收，普通推送和 main 回归不变', async () => {
  const workflow = await readFile(new URL('../../.github/workflows/ci.yml', import.meta.url), 'utf8');
  const jobs = [...workflow.matchAll(/^  ([a-z][\w-]+):\r?\n([\s\S]*?)(?=^  [a-z][\w-]+:|$(?![\s\S]))/gm)]
    .filter(([, name]) => !['push', 'pull_request'].includes(name));
  const ordinary = ['client-build-test', 'build-and-module-test', 'affected-migration-recovery', 'build-test'];
  const acceptance = ['template-created-app-real-stack', 'foundation-sample-e2e'];
  const scenarios = [
    ['labeled', 'bug', [], []],
    ['labeled', 'bug', ['fullnet:acceptance', 'bug'], []],
    ['labeled', 'fullnet:acceptance', ['fullnet:acceptance'], acceptance],
    ['synchronize', undefined, [], ordinary],
    ['synchronize', undefined, ['fullnet:acceptance'], [...ordinary, ...acceptance]],
    ['opened', undefined, [], ordinary]
  ];
  function selected(github) {
    return jobs.filter(([, , block]) => {
      const expression = block.match(/^    if: (.+)$/m)?.[1]?.trim() ?? 'true';
      // 只求值本仓库工作流中的布尔条件；把 Actions 的标签投影转换成等价数组。
      const condition = expression.replaceAll('github.event.pull_request.labels.*.name', 'github.event.pull_request.labels.map(label => label.name)');
      return Function('github', 'always', 'contains', `return (${condition});`)(github, () => true, (values, value) => values.includes(value));
    }).map(([, name]) => name).sort();
  }
  for (const [action, label, labels, expected] of scenarios) {
    const github = { event_name: 'pull_request', ref: 'refs/pull/3/merge', event: { action, label: { name: label }, pull_request: { labels: labels.map(name => ({ name })) } } };
    assert.deepEqual(selected(github), [...expected].sort(), `${action}:${label ?? ''}:${labels.join(',')}`);
  }
  assert.deepEqual(selected({ event_name: 'push', ref: 'refs/heads/main', event: {} }), [
    'client-build-test', 'template-created-app-real-stack', 'real-stack-e2e', 'real-stack-e2e-mysql',
    'real-stack-e2e-production-totp', 'build-and-module-test', 'build-test', 'integration-matrix', 'integration-shard', 'integration-gate'
  ].sort());
});
