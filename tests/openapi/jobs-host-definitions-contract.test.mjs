import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/jobs-host-definitions-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Jobs/Contracts/JobContracts.cs'
);
const definitionsEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobDefinitions/Endpoint.cs'
);
const executionsEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Jobs/Features/ManageHostJobExecutions/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('Host 任务 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'jobs-host-definitions-v1');

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/jobs\//u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(operation.permission, /^jobs\.(definitions|executions)\.(read|write)$/u);
      if (operation.requestSchema) {
        assert.ok(contract.schemas[operation.requestSchema]);
      }
      assert.ok(contract.schemas[operation.responseSchema]);
    }
  }
});

test('Host 任务 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const definitionsEndpoint = await readFile(definitionsEndpointPath, 'utf8');
  const executionsEndpoint = await readFile(executionsEndpointPath, 'utf8');
  const endpointSources = `${definitionsEndpoint}\n${executionsEndpoint}`;

  for (const permission of [
    'jobs.definitions.read',
    'jobs.definitions.write',
    'jobs.executions.read'
  ]) {
    assert.ok(contractsSource.includes(permission), `C# 契约缺少权限码：${permission}`);
  }
  assert.match(definitionsEndpoint, /MapGroup\("\/api\/v1\/jobs\/host-definitions"\)/u);
  assert.match(executionsEndpoint, /MapGroup\("\/api\/v1\/jobs\/host-executions"\)/u);

  const routeMarkers = new Map([
    ['GET /api/v1/jobs/host-definitions', 'MapGet("/",'],
    ['POST /api/v1/jobs/host-definitions', 'MapPost("/",'],
    ['GET /api/v1/jobs/host-definitions/{definitionId}', 'MapGet("/{definitionId:guid}",'],
    ['PUT /api/v1/jobs/host-definitions/{definitionId}', 'MapPut("/{definitionId:guid}",'],
    [
      'POST /api/v1/jobs/host-definitions/{definitionId}/disable',
      'MapPost("/{definitionId:guid}/disable",'
    ],
    [
      'POST /api/v1/jobs/host-definitions/{definitionId}/trigger',
      'MapPost("/{definitionId:guid}/trigger",'
    ],
    ['GET /api/v1/jobs/host-executions', 'MapGet("/",']
  ]);

  for (const entry of contract.paths) {
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      const marker = routeMarkers.get(key);
      assert.ok(marker, `未登记的路由操作：${key}`);
      assert.ok(endpointSources.includes(marker), `端点源码缺少：${key}`);
    }
  }

  for (const [schemaName, schema] of Object.entries(contract.schemas)) {
    if (schemaName.endsWith('Page')) {
      continue;
    }
    assert.match(contractsSource, new RegExp(`record ${schemaName}\\b`, 'u'));
    for (const property of schema.properties) {
      const pascal = property.charAt(0).toUpperCase() + property.slice(1);
      assert.match(
        contractsSource,
        new RegExp(`\\b${pascal}\\b`, 'u'),
        `${schemaName}.${property} 未在 C# 契约中找到`
      );
    }
  }
});
