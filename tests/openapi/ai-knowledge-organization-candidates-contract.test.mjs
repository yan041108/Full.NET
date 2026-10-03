import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

test('组织候选契约冻结独立权限与最小投影，真实双库另行验证运行时文档', async () => {
  const read = path => readFile(new URL('../../' + path, import.meta.url), 'utf8');
  const fixture = JSON.parse(await read('contracts/openapi/ai-knowledge-organization-candidates-v1.json'));
  const endpoint = await read('src/Modules/Full.NET.Modules.Ai/Features/ManageKnowledgeOrganizationCandidates/Endpoint.cs');
  const permissions = await Promise.all(['AiKnowledgePermissions', 'AiKnowledgeDocumentPermissions']
    .map(name => read('src/Modules/Full.NET.Modules.Ai.Contracts/' + name + '.cs')));
  assert.equal(fixture.paths.length, 2);
  for (const [index, path] of fixture.paths.entries()) {
    assert.equal(path.operations.length, 1);
    const operation = path.operations[0];
    assert.equal(operation.method, 'GET');
    assert.equal(operation.successStatus, 200);
    assert.ok(endpoint.includes('"' + operation.operationId + '"'));
    assert.ok(permissions[index].includes('"' + operation.permission + '"'));
    assert.ok(path.path.endsWith('/organization-candidates'));
    assert.equal(operation.responseSchema, 'AiKnowledgeOrganizationCandidateResponsePage');
  }
  assert.deepEqual(fixture.schemas.AiKnowledgeOrganizationCandidateResponse.properties, ['id', 'code', 'name']);
  assert.deepEqual(fixture.schemas.AiKnowledgeOrganizationCandidateResponsePage.properties, ['items', 'page', 'pageSize', 'total']);
});
