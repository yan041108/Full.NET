import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractsDirectory = path.join(repositoryRoot, 'contracts/openapi');

const contractFiles = [
  'code-generation-previews-v1.json',
  'code-generation-runs-v1.json',
  'code-generation-templates-v1.json'
];

async function loadContract(fileName) {
  return JSON.parse(await readFile(path.join(contractsDirectory, fileName), 'utf8'));
}

test('代码生成 OpenAPI 冻结夹具声明规范路径、状态码与 schema', async () => {
  for (const fileName of contractFiles) {
    const contract = await loadContract(fileName);
    const paths = contract.paths ?? { [contract.path]: { post: ['200', String(contract.errorStatus)] } };

    assert.ok(Object.keys(paths).length > 0, `${fileName} 必须声明路径`);
    for (const [route, operations] of Object.entries(paths)) {
      assert.match(route, /^\/api\/v1\/code-generation\//u);
      for (const [method, statuses] of Object.entries(operations)) {
        assert.match(method, /^(get|post|put|delete)$/u);
        assert.ok(Array.isArray(statuses) && statuses.length > 0);
        assert.ok(statuses.every(status => /^[1-5][0-9]{2}$/u.test(status)));
      }
    }

    assert.ok(contract.schemas && Object.keys(contract.schemas).length > 0);
    for (const [schemaName, properties] of Object.entries(contract.schemas)) {
      assert.match(schemaName, /CodeGeneration/u);
      assert.ok(Array.isArray(properties) && properties.length > 0);
      assert.equal(new Set(properties).size, properties.length);
    }
  }
});

test('代码生成 OpenAPI 冻结路径与端点源码保持一致', async () => {
  const previewEndpoint = await readFile(path.join(
    repositoryRoot,
    'src/Modules/Full.NET.Modules.CodeGeneration/Features/PreviewCrudGeneration/Endpoint.cs'
  ), 'utf8');
  const runEndpoint = await readFile(path.join(
    repositoryRoot,
    'src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostRuns/Endpoint.cs'
  ), 'utf8');
  const templateEndpoint = await readFile(path.join(
    repositoryRoot,
    'src/Modules/Full.NET.Modules.CodeGeneration/Features/ManageHostTemplates/Endpoint.cs'
  ), 'utf8');

  assert.match(previewEndpoint, /\/api\/v1\/code-generation\/previews/u);
  assert.match(runEndpoint, /MapGroup\("\/api\/v1\/code-generation\/runs"\)/u);
  assert.match(runEndpoint, /MapPost\("\/preview"/u);
  assert.match(runEndpoint, /MapPost\("\/apply"/u);
  assert.match(runEndpoint, /MapGet\("\/\{runId:guid\}"/u);
  assert.match(templateEndpoint, /MapGroup\("\/api\/v1\/code-generation\/templates"\)/u);
  assert.match(templateEndpoint, /MapPut\("\/\{templateId:guid\}"/u);
  assert.match(templateEndpoint, /MapPost\("\/\{templateId:guid\}\/delete"/u);
});
