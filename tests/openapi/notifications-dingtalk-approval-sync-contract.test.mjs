import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/notifications-dingtalk-approval-sync-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Contracts/DingTalkApprovalSyncContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Features/ManageDingTalkApprovalSync/Endpoint.cs'
);

test('钉钉审批同步 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'notifications-dingtalk-approval-sync-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/notifications\/dingtalk\/approval-sync"\)/u);
  assert.match(endpointSource, /WithName\("notificationsListDingTalkApprovalSync"\)/u);
  assert.match(contractsSource, /record DingTalkApprovalSyncResponse/u);
  assert.match(contractsSource, /notifications\.dingtalk_approval_sync\.read/u);
  assert.match(contractsSource, /notifications\.dingtalk_approval_sync\.create/u);
});
