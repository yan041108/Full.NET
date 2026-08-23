import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/document-host-shares-v1.json');
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentShares/Endpoint.cs'
);

test('Host 文档分享 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'document-host-shares-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/document\/host\/shares"\)/u);
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/document\/public\/shares"\)/u);
  assert.match(endpointSource, /\.WithTags\("DocumentHostShares"\)/u);
  assert.match(endpointSource, /\.WithTags\("DocumentPublicShares"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostListDocumentShares"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostCreateDocumentShare"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostUpdateDocumentShareStatus"\)/u);
  assert.match(endpointSource, /\.WithName\("documentPublicAccessDocumentShare"\)/u);
  assert.match(contractsSource, /record CreateHostDocumentShareRequest/u);
  assert.match(contractsSource, /record HostDocumentShareAccessResponse/u);
  assert.ok(contract.paths.some((entry) => entry.path.includes('/public/shares/')));
});
