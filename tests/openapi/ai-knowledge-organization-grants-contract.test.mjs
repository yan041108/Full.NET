import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

test('组织授予契约冻结四个独立操作及版本字段，运行时另由真实双库验证', async () => {
  const read = path => readFile(new URL('../../' + path, import.meta.url), 'utf8');
  const fixture = JSON.parse(await read('contracts/openapi/ai-knowledge-organization-grants-v1.json'));
  const endpoint = await read('src/Modules/Full.NET.Modules.Ai/Features/ManageKnowledgeOrganizations/Endpoint.cs');
  const permissions = await Promise.all(['AiKnowledgePermissions', 'AiKnowledgeDocumentPermissions'].map(name => read('src/Modules/Full.NET.Modules.Ai.Contracts/' + name + '.cs')));
  assert.equal(fixture.paths.length, 2);
  for (const [index, path] of fixture.paths.entries()) {
    assert.equal(path.operations.length, 2);
    for (const operation of path.operations) {
      assert.ok(['GET', 'PUT'].includes(operation.method));
      assert.equal(operation.successStatus, 200);
      assert.ok(endpoint.includes('"' + operation.operationId + '"'));
      assert.ok(permissions[index].includes('"' + operation.permission + '"'));
      assert.equal(operation.responseSchema, 'AiKnowledgeOrganizationGrantsResponse');
    }
  }
  assert.deepEqual(fixture.schemas.AiKnowledgeOrganizationGrantsResponse.properties, ['resourceId', 'organizationUnitIds', 'version']);
  assert.deepEqual(fixture.schemas.SetAiKnowledgeOrganizationGrantsRequest.properties, ['organizationUnitIds', 'version']);
});
