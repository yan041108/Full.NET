import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/auditing-operation-logs-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Auditing/Contracts/OperationLogContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Auditing/Features/QueryHostOperationLogs/Endpoint.cs'
);
const clientManifestPath = path.join(
  repositoryRoot,
  'contracts/openapi/client-generation-manifest-v1.json'
);
const clientSnapshotPath = path.join(
  repositoryRoot,
  'contracts/openapi/fullnet-client-v1.openapi.json'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('受限操作详情进入生成客户端且保留独立授权响应', async () => {
  const manifest = JSON.parse(await readFile(clientManifestPath, 'utf8'));
  const snapshot = JSON.parse(await readFile(clientSnapshotPath, 'utf8'));
  const operationId = 'auditingGetHostOperationLogDetails';
  assert.ok(manifest.entries.some(entry =>
    entry.operationId === operationId
    && entry.apiModule === 'ui/admin/src/api/operation-logs.ts'
    && entry.status === 'generated'));

  const operation = snapshot.paths['/api/v1/auditing/operation-logs/{operationLogId}/details']?.get;
  assert.equal(operation?.operationId, operationId);
  assert.ok(operation.responses['200']);
  assert.ok(operation.responses['403']);
  assert.ok(operation.responses['404']);
});

test('Host 操作日志 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'auditing-operation-logs-v1');
  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/auditing\//u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.ok([
        'auditing.operations.read',
        'auditing.operations.details.read'
      ].includes(operation.permission));
      for (const permission of operation.additionalPermissions ?? []) {
        assert.equal(permission, 'auditing.operations.read');
      }
    }
  }
});

test('Host 操作日志 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record OperationLogResponse/u);
  assert.match(contractsSource, /record OperationLogDetailsResponse/u);
  assert.match(contractsSource, /auditing\.operations\.read/u);
  assert.match(contractsSource, /auditing\.operations\.details\.read/u);
  assert.match(
    endpointSource,
    /MapGroup\("\/api\/v1\/auditing\/operation-logs"\)/u
  );
  assert.match(endpointSource, /WithTags\("AuditingHostOperationLogs"\)/u);
  assert.match(endpointSource, /WithName\("auditingListHostOperationLogs"\)/u);

  const relativeRoutes = new Map([
    ['/api/v1/auditing/operation-logs', new Map([['GET', 'MapGet("/",']])],
    ['/api/v1/auditing/operation-logs/{operationLogId}', new Map([
      ['GET', 'MapGet("/{operationLogId:guid}",']
    ])],
    ['/api/v1/auditing/operation-logs/{operationLogId}/details', new Map([
      ['GET', 'MapGet("/{operationLogId:guid}/details",']
    ])]
  ]);

  for (const entry of contract.paths) {
    const routes = relativeRoutes.get(entry.path);
    assert.ok(routes, `未登记的路由组：${entry.path}`);
    for (const operation of entry.operations) {
      const marker = routes.get(operation.method);
      assert.ok(marker, `${entry.path} 缺少 ${operation.method}`);
      assert.match(
        endpointSource,
        new RegExp(marker.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&'), 'u')
      );
    }
  }

  for (const [schemaName, schema] of Object.entries(contract.schemas)) {
    for (const property of schema.properties) {
      const pascal = property.charAt(0).toUpperCase() + property.slice(1);
      assert.match(
        contractsSource,
        new RegExp(`${pascal}`, 'u'),
        `${schemaName}.${property} 未在 C# 契约中找到`
      );
    }
  }
});
