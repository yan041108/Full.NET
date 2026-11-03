import assert from 'node:assert/strict';
import test from 'node:test';
import { readFile } from 'node:fs/promises';
import { validateVueClientContractCoverage } from '../../scripts/openapi/validate-vue-client-contract-coverage.mjs';
test('编排提案与审批使用生成操作和实际页面消费绑定', async () => {
  const violations = await validateVueClientContractCoverage();
  assert.deepEqual(violations.filter(value => value.includes('ai-agent-workflow') || value.includes('ai-agent-approvals')), []);
});
test('目标身份为显式 UUID，提案只读且保留认证声明', async () => {
  const document = JSON.parse(await readFile(new URL('../../contracts/openapi/fullnet-client-v1.openapi.json', import.meta.url), 'utf8'));
  const target = document.components.schemas.CreateAiAgentRunRequest.properties.targetChatSessionId;
  assert.equal(target.format, 'uuid');
  const operation = document.paths['/api/v1/ai/agent/runs/{runId}/workflow-proposal'].get;
  assert.equal(operation.operationId, 'aiGetAgentWorkflowProposal'); assert.ok(operation.security.length > 0);
  assert.equal(operation.requestBody, undefined);
});
