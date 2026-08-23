import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/notifications-inbox-messages-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Contracts/InboxMessageContracts.cs'
);
const myEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Features/ManageMyInboxMessages/Endpoint.cs'
);
const hostEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Features/SendHostInboxMessages/Endpoint.cs'
);

test('站内信 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const myEndpointSource = await readFile(myEndpointSourcePath, 'utf8');
  const hostEndpointSource = await readFile(hostEndpointSourcePath, 'utf8');

  assert.equal(contract.id, 'notifications-inbox-messages-v1');
  assert.match(contractsSource, /record InboxMessageResponse/u);
  assert.match(contractsSource, /record SendHostInboxMessageRequest/u);
  assert.match(
    myEndpointSource,
    /MapGroup\("\/api\/v1\/notifications\/my-inbox-messages"\)/u
  );
  assert.match(
    hostEndpointSource,
    /MapGroup\("\/api\/v1\/notifications\/host-inbox-messages"\)/u
  );
  assert.match(myEndpointSource, /\.WithTags\("NotificationsMyInboxMessages"\)/u);
  assert.match(hostEndpointSource, /\.WithTags\("NotificationsHostInboxMessages"\)/u);
  assert.match(myEndpointSource, /\.WithName\("notificationsListMyInboxMessages"\)/u);
  assert.match(myEndpointSource, /\.WithName\("notificationsGetMyInboxUnreadCount"\)/u);
  assert.match(myEndpointSource, /\.WithName\("notificationsMarkMyInboxMessageRead"\)/u);
  assert.match(myEndpointSource, /\.WithName\("notificationsMarkAllMyInboxMessagesRead"\)/u);
  assert.match(hostEndpointSource, /\.WithName\("notificationsSendHostInboxMessage"\)/u);
});
