import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/auditing-export-v1.json');
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Auditing/Contracts/AuditLogExportContracts.cs'
);
const errorCodesSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Auditing/Contracts/AuditingErrorCodes.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Auditing/Features/ExportHostAuditLogs/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('审计导出 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'auditing-export-v1');
  assert.equal(contract.paths.length, 3);
});

test('审计导出 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const errorCodesSource = await readFile(errorCodesSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');

  assert.match(contractsSource, /record AuditLogExportRequest/u);
  assert.match(errorCodesSource, /ExportRetentionBoundaryExceeded/u);
  assert.match(endpointSource, /auditingExportHostAccessLogs/u);
  assert.match(endpointSource, /auditingExportHostOperationLogs/u);
  assert.match(endpointSource, /auditingExportHostExceptionLogs/u);

  for (const property of contract.schemas.AuditLogExportRequest.properties) {
    const pascal = property.charAt(0).toUpperCase() + property.slice(1);
    assert.match(contractsSource, new RegExp(pascal, 'u'));
  }
});
