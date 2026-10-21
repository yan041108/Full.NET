import assert from 'node:assert/strict';
import test from 'node:test';
import {
  collectProductionApiModules,
  validateVueClientContractCoverage
} from '../../scripts/openapi/validate-vue-client-contract-coverage.mjs';

test('Vue 生产 API 模块与 manifest 一一对应', async () => {
  const modules = await collectProductionApiModules();
  assert.equal(modules.length, 121);
  assert.ok(modules.includes('ui/admin/src/api/log-timeline.ts'));
  assert.ok(modules.includes('ui/admin/src/api/ai-index-grants.ts'));
  assert.ok(modules.includes('ui/admin/src/api/ai-feedback-evaluation.ts'));
  assert.ok(modules.includes('ui/admin/src/api/ai-answer-feedback-review.ts'));
  assert.ok(modules.every((modulePath) => modulePath.startsWith('ui/admin/src/api/')));
  assert.ok(!modules.includes('ui/admin/src/api/http.ts'));
});

test('Vue/OpenAPI/共享 TypeScript 契约覆盖门禁通过', async () => {
  const violations = await validateVueClientContractCoverage();
  assert.deepEqual(violations, []);
});

async function sharedConsumerFixture(sharedModule) {
  const { mkdtemp, mkdir, writeFile } = await import('node:fs/promises');
  const { tmpdir } = await import('node:os');
  const path = await import('node:path');
  const root = await mkdtemp(path.join(tmpdir(), 'fullnet-generated-consumer-'));
  const files = {
    'ui/admin/src/api/knowledge.ts': "import { aiListKnowledgeBases } from '@fullnet/client-contracts'; export const list = aiListKnowledgeBases;",
    'packages/client-contracts/src/ai-knowledge.ts': '',
    'packages/client-contracts/src/other.ts': '',
    'packages/client-contracts/src/index.ts': "export * from './ai-knowledge.js'; export * from './other.js';",
    'contracts/openapi/fullnet-client-v1.openapi.json': JSON.stringify({ paths: { '/api/v1/ai/knowledge-bases': { get: { operationId: 'aiListKnowledgeBases' } } } }),
    'contracts/openapi/client-generation-manifest-v1.json': JSON.stringify({ entries: [{ apiModule: 'packages/client-contracts/src/ai-knowledge.ts', operationId: 'aiListKnowledgeBases', status: 'generated' }] }),
    'contracts/openapi/vue-client-coverage-v1.json': JSON.stringify({ schemaVersion: 1, entries: [{ apiModule: 'ui/admin/src/api/knowledge.ts', clientContractModules: [sharedModule], openApiFixture: 'contracts/openapi/fullnet-client-v1.openapi.json', routePrefixes: ['/api/v1/ai/knowledge-bases'] }] })
  };
  for (const [name, value] of Object.entries(files)) {
    await mkdir(path.dirname(path.join(root, name)), { recursive: true });
    await writeFile(path.join(root, name), value);
  }
  return root;
}

test('Vue 消费共享生成操作不转移契约所有权', async () => {
  const root = await sharedConsumerFixture('packages/client-contracts/src/ai-knowledge.ts');
  try { assert.deepEqual(await validateVueClientContractCoverage(root), []); }
  finally { await cleanupSharedConsumerFixture(root); }
});

test('未声明的共享模块不能提供操作绑定证明', async () => {
  const root = await sharedConsumerFixture('packages/client-contracts/src/other.ts');
  try { assert.ok((await validateVueClientContractCoverage(root)).some(value => value.includes('not covered'))); }
  finally { await cleanupSharedConsumerFixture(root); }
});

async function cleanupSharedConsumerFixture(root) {
  const path = await import('node:path');
  const { tmpdir } = await import('node:os');
  const { rm } = await import('node:fs/promises');
  const target = path.resolve(root);
  assert.ok(target.startsWith(path.resolve(tmpdir()) + path.sep + 'fullnet-generated-consumer-'));
  await rm(target, { recursive: true, force: true });
}
