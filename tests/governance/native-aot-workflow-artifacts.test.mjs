import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const workflowUrl = new URL('../../.github/workflows/api-native-aot-linux.yml', import.meta.url);
test('API 综合 Native 门禁在执行前备齐 API 与 Worker 原生程序', async () => {
  const workflow = await readFile(workflowUrl, 'utf8');
  const api = workflow.indexOf('run: pnpm test:aot:publish:linux');
  const worker = workflow.indexOf('run: pnpm test:aot:worker:publish:linux');
  const execution = workflow.indexOf('run: pnpm test:aot:native:e2e');
  assert(api >= 0 && worker > api && execution > worker, '综合集合含 Worker 测试，不能用缺失原生程序导致的跳过通过');
});
test('综合 Native 工作流保留 Worker 发布身份与失败诊断', async () => {
  const workflow = await readFile(workflowUrl, 'utf8');
  assert(workflow.includes('artifacts/native-aot/worker/linux-x64/publish-manifest.json'));
  assert(workflow.includes('artifacts/native-aot/worker/linux-x64/publish.log'));
  assert(workflow.includes('artifacts/**/*.log') || workflow.includes('artifacts/native-aot/worker/linux-x64/test-logs/**'));
});
