import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/document-host-access-logs-v1.json');
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentAccessLogContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Features/QueryHostDocumentAccessLogs/Endpoint.cs'
);

test('Host 文档访问日志 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'document-host-access-logs-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/document\/host\/access-logs"\)/u);
  assert.match(endpointSource, /\.WithTags\("DocumentHostAccessLogs"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostListDocumentAccessLogs"\)/u);
  assert.match(contractsSource, /record HostDocumentAccessLogResponse/u);
  assert.match(contractsSource, /document\.host_access_logs\.read/u);
});
