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
