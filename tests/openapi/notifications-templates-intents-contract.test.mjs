import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/notifications-templates-intents-v1.json'
);
const templateContractsPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Contracts/NotificationTemplateContracts.cs'
);
const intentContractsPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Contracts/NotificationIntentContracts.cs'
);
const templateEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Features/ManageTemplates/Endpoint.cs'
);
const intentEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Features/CreateNotificationIntents/Endpoint.cs'
);

test('模板与意图 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const templateContracts = await readFile(templateContractsPath, 'utf8');
  const intentContracts = await readFile(intentContractsPath, 'utf8');
  const templateEndpoint = await readFile(templateEndpointPath, 'utf8');
  const intentEndpoint = await readFile(intentEndpointPath, 'utf8');

  assert.equal(contract.id, 'notifications-templates-intents-v1');
  assert.match(templateContracts, /record CreateNotificationTemplateRequest/u);
  assert.match(templateContracts, /record NotificationTemplateResponse/u);
  assert.match(intentContracts, /record CreateNotificationIntentRequest/u);
  assert.match(intentContracts, /record NotificationIntentResponse/u);
  assert.match(templateEndpoint, /MapGroup\("\/api\/v1\/notifications\/templates"\)/u);
  assert.match(templateEndpoint, /\.WithTags\("NotificationsTemplates"\)/u);
  assert.match(templateEndpoint, /\.WithName\("notificationsListTemplates"\)/u);
  assert.match(templateEndpoint, /\.WithName\("notificationsGetTemplate"\)/u);
  assert.match(templateEndpoint, /\.WithName\("notificationsCreateTemplate"\)/u);
  assert.match(templateEndpoint, /\.WithName\("notificationsUpdateTemplate"\)/u);
  assert.match(templateEndpoint, /\.WithName\("notificationsPublishTemplate"\)/u);
  assert.match(intentEndpoint, /MapGroup\("\/api\/v1\/notifications\/intents"\)/u);
  assert.match(intentEndpoint, /\.WithTags\("NotificationsIntents"\)/u);
  assert.match(intentEndpoint, /\.WithName\("notificationsCreateIntent"\)/u);
  assert.match(intentEndpoint, /\.WithName\("notificationsGetIntent"\)/u);
});
