import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/document-host-recycle-bin-v1.json');
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Features/QueryHostRecycleBin/Endpoint.cs'
);

test('Host 文档回收站 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'document-host-recycle-bin-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/document\/host\/recycle-bin"\)/u);
  assert.match(endpointSource, /\.WithTags\("DocumentHostRecycleBin"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostListRecycleBinItems"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostRestoreRecycleBinItem"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostPurgeRecycleBinItem"\)/u);
  assert.match(contractsSource, /record RestoreHostDocumentItemRequest/u);
  assert.match(contractsSource, /record HostDocumentItemResponse/u);
  assert.ok(contract.paths.some((entry) => entry.path.endsWith('/purge')));
});
