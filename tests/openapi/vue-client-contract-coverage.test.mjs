import assert from 'node:assert/strict';
import test from 'node:test';
import {
  collectProductionApiModules,
  validateVueClientContractCoverage
} from '../../scripts/openapi/validate-vue-client-contract-coverage.mjs';

test('Vue 生产 API 模块与 manifest 一一对应', async () => {
  const modules = await collectProductionApiModules();
  assert.equal(modules.length, 54);
  assert.ok(modules.every((modulePath) => modulePath.startsWith('ui/admin/src/api/')));
  assert.ok(!modules.includes('ui/admin/src/api/http.ts'));
});

test('Vue/OpenAPI/共享 TypeScript 契约覆盖门禁通过', async () => {
  const violations = await validateVueClientContractCoverage();
  assert.deepEqual(violations, []);
});
