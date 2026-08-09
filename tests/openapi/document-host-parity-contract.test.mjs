import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/document-host-parity-v1.json');
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Contracts/HostDocumentContracts.cs'
);
const shareEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentShares/Endpoint.cs'
);
const permissionEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Features/ManageHostDocumentPermissions/Endpoint.cs'
);
const statisticsEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Features/QueryHostDocumentStatistics/Endpoint.cs'
);
const recycleBinEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Document/Features/QueryHostRecycleBin/Endpoint.cs'
);

test('Host 文档 parity OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  assert.equal(contract.id, 'document-host-parity-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length > 0);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/document\/(host|public)\//u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.ok(typeof operation.successStatus === 'number');
    }
  }
});

test('Host 文档 parity OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const shareEndpointSource = await readFile(shareEndpointSourcePath, 'utf8');
  const permissionEndpointSource = await readFile(permissionEndpointSourcePath, 'utf8');
  const statisticsEndpointSource = await readFile(statisticsEndpointSourcePath, 'utf8');
  const recycleBinEndpointSource = await readFile(recycleBinEndpointSourcePath, 'utf8');

  assert.match(contractsSource, /record AccessHostDocumentShareRequest/u);
  assert.match(contractsSource, /record HostDocumentShareAccessResponse/u);
  assert.match(contractsSource, /record SetHostDocumentPermissionsRequest/u);
  assert.match(contractsSource, /record HostDocumentStatisticsResponse/u);
  assert.match(shareEndpointSource, /MapGroup\("\/api\/v1\/document\/public\/shares"\)/u);
  assert.match(shareEndpointSource, /Status405MethodNotAllowed/u);
  assert.match(permissionEndpointSource, /MapGroup\("\/api\/v1\/document\/host\/permissions"\)/u);
  assert.match(statisticsEndpointSource, /MapGroup\("\/api\/v1\/document\/host\/statistics"\)/u);
  assert.match(recycleBinEndpointSource, /MapGroup\("\/api\/v1\/document\/host\/recycle-bin"\)/u);
  assert.ok(contract.paths.some((entry) => entry.path.endsWith('/access')));
});
