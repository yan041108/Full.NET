import assert from 'node:assert/strict';
import test from 'node:test';
import { validateVueApiCallSiteCoverage } from '../../scripts/openapi/validate-vue-api-call-site-coverage.mjs';

test('Vue 调用点覆盖门禁通过', async () => {
  const violations = await validateVueApiCallSiteCoverage();
  assert.deepEqual(violations, []);
});
