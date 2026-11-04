import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { validateVueClientContractCoverage } from '../../scripts/openapi/validate-vue-client-contract-coverage.mjs';
import { validateVueApiCallSiteCoverage } from '../../scripts/openapi/validate-vue-api-call-site-coverage.mjs';

test('实例改派候选契约绑定显式待办和有界页码', () => {
  const fixture = JSON.parse(fs.readFileSync('contracts/openapi/workflow-reassignment-candidates-v1.json', 'utf8'));
  const operation = fixture.paths['/api/v1/workflow/instances/{instanceId}/reassignment-candidates'].get;
  assert.equal(operation.operationId, 'workflowListReassignmentCandidates');
  assert.equal(operation.parameters.find(p => p.name === 'todoId').required, true);
  assert.equal(operation.parameters.find(p => p.name === 'pageSize').schema.maximum, 100);
  assert.deepEqual(Object.keys(operation.responses).sort(), ['200', '400', '401', '403', '404', '409']);
  const existing = JSON.parse(fs.readFileSync('contracts/openapi/fullnet-client-v1.openapi.json', 'utf8'));
  for (const key of ['WorkflowRecipientCandidatePageResponse', 'WorkflowRecipientCandidateResponse']) assert.deepEqual(fixture.components.schemas[key], existing.components.schemas[key]);
  assert.ok(fixture.components.schemas.WorkflowReassignmentCandidatePageResponse.required.includes('todoRevision'));
});

test('实际 API 模块和页面消费登记覆盖', async () => {
  const [contracts, calls] = await Promise.all([validateVueClientContractCoverage(), validateVueApiCallSiteCoverage()]);
  const relevant = value => /workflow-reassignment-candidates|WorkflowInstanceReassignDialog/.test(value);
  assert.deepEqual(contracts.filter(relevant), []); assert.deepEqual(calls.filter(relevant), []);
});
