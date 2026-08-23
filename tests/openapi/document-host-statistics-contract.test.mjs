import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/document-host-statistics-v1.json');
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Features/QueryHostDocumentStatistics/Endpoint.cs'
);

test('Host 文档统计 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'document-host-statistics-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/document\/host\/statistics"\)/u);
  assert.match(endpointSource, /\.WithTags\("DocumentHostStatistics"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostGetDocumentStatistics"\)/u);
  assert.match(contractsSource, /record HostDocumentStatisticsResponse/u);
  assert.match(contractsSource, /record HostDocumentStatisticsSummaryResponse/u);
});
