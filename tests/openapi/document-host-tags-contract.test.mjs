import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/document-host-tags-v1.json');
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentTagContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentTags/Endpoint.cs'
);

test('Host 文档标签 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'document-host-tags-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/document\/host\/tags"\)/u);
  assert.match(endpointSource, /\.WithTags\("DocumentHostTags"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostListTags"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostCreateTag"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostUpdateTag"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostDeleteTag"\)/u);
  assert.match(contractsSource, /record HostDocumentTagResponse/u);
  assert.match(contractsSource, /record CreateHostDocumentTagRequest/u);
  assert.match(contractsSource, /record UpdateHostDocumentTagRequest/u);
  assert.match(contractsSource, /record DeleteHostDocumentTagRequest/u);
  assert.ok(contract.paths.some((entry) => entry.path.endsWith('/delete')));
});
