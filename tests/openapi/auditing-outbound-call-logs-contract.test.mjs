import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/auditing-outbound-call-logs-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Auditing/Contracts/OutboundCallLogContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Auditing/Features/QueryHostOutboundCallLogs/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('Host 出站调用审计 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'auditing-outbound-call-logs-v1');
  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/auditing\//u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(operation.permission, /^auditing\.outbound_calls\.read$/u);
    }
  }
});

test('Host 出站调用审计 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record OutboundCallLogResponse/u);
  assert.match(contractsSource, /auditing\.outbound_calls\.read/u);
  assert.match(
    endpointSource,
    /MapGroup\("\/api\/v1\/auditing\/outbound-call-logs"\)/u
  );

  const relativeRoutes = new Map([
    ['/api/v1/auditing/outbound-call-logs', new Map([['GET', 'MapGet("/",']])],
    ['/api/v1/auditing/outbound-call-logs/{outboundCallLogId}', new Map([
      ['GET', 'MapGet("/{outboundCallLogId:guid}",']
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
