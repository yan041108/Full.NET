import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(repositoryRoot, 'contracts/openapi/data-approvals-v1.json');
const contractsSourcePath = path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.DataApproval/Contracts/DataApprovalContracts.cs');
const endpointSourcePath = path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.DataApproval/Features/ManageRequests/Endpoint.cs');

test('DataApproval OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'data-approvals-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/data-approvals\/requests"\)/u);
  assert.match(endpointSource, /\.WithTags\("DataApprovalRequests"\)/u);
  assert.match(endpointSource, /\.WithName\("dataApprovalsListRequests"\)/u);
  assert.match(endpointSource, /\.WithName\("dataApprovalsCreateRequest"\)/u);
  assert.match(endpointSource, /\.WithName\("dataApprovalsGetRequest"\)/u);
  assert.match(endpointSource, /\.WithName\("dataApprovalsCancelRequest"\)/u);
  assert.match(contractsSource, /record DataApprovalRequestResponse/u);
  assert.match(contractsSource, /serial_numbers\.host_rule\.update/u);
});
