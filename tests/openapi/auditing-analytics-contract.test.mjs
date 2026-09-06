import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/auditing-analytics-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Auditing/Contracts/AuditLogAnalyticsContracts.cs'
);
const errorCodesSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Auditing/Contracts/AuditingErrorCodes.cs'
);
const trendEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Auditing/Features/QueryHostAuditLogTrends/Endpoint.cs'
);
const diffEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Auditing/Features/QueryDomainChangeDiffs/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('审计分析 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'auditing-analytics-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length === 4);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/auditing\//u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.ok(typeof operation.successStatus === 'number');
      if (operation.responseSchema) {
        assert.ok(contract.schemas[operation.responseSchema]);
      }
    }
  }
});

test('审计分析 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const errorCodesSource = await readFile(errorCodesSourcePath, 'utf8');
  const trendEndpointSource = await readFile(trendEndpointSourcePath, 'utf8');
  const diffEndpointSource = await readFile(diffEndpointSourcePath, 'utf8');

  assert.match(contractsSource, /record AuditLogTrendResponse/u);
  assert.match(contractsSource, /record DomainChangeDiffQueryResponse/u);
  assert.match(errorCodesSource, /TrendBucketLimitExceeded/u);
  assert.match(errorCodesSource, /DomainChangeDiffTraceIdRequired/u);
  assert.match(trendEndpointSource, /auditingQueryHostAccessLogTrends/u);
  assert.match(trendEndpointSource, /auditingQueryHostOperationLogTrends/u);
  assert.match(trendEndpointSource, /auditingQueryHostExceptionLogTrends/u);
  assert.match(diffEndpointSource, /auditingQueryDomainChangeDiffs/u);
  assert.match(diffEndpointSource, /DomainChangeDiffPermissions\.Read/u);

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
