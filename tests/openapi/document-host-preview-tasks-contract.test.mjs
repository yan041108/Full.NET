import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/document-host-preview-tasks-v1.json');
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentPreviewTaskContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentPreviewTasks/Endpoint.cs'
);

test('Host 文档预览任务 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'document-host-preview-tasks-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/document\/host\/preview-tasks"\)/u);
  assert.match(endpointSource, /\.WithTags\("DocumentHostPreviewTasks"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostCreateDocumentPreviewTask"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostListDocumentPreviewTasks"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostGetDocumentPreviewTask"\)/u);
  assert.match(endpointSource, /\.WithName\("documentHostDownloadDocumentPreviewTaskContent"\)/u);
  assert.match(contractsSource, /record HostDocumentPreviewTaskResponse/u);
  assert.match(contractsSource, /document\.host_preview_tasks\.read/u);
  assert.match(contractsSource, /document\.host_preview_tasks\.create/u);
});
