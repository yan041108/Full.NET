import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/notifications-profiles-bindings-v1.json'
);
const profileContractsPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Contracts/NotificationProviderProfileContracts.cs'
);
const bindingContractsPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Contracts/NotificationBindingContracts.cs'
);
const profileEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Features/ManageProviderProfiles/Endpoint.cs'
);
const bindingEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Features/ManageBindings/Endpoint.cs'
);

test('渠道配置与绑定 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const profileContracts = await readFile(profileContractsPath, 'utf8');
  const bindingContracts = await readFile(bindingContractsPath, 'utf8');
  const profileEndpoint = await readFile(profileEndpointPath, 'utf8');
  const bindingEndpoint = await readFile(bindingEndpointPath, 'utf8');

  assert.equal(contract.id, 'notifications-profiles-bindings-v1');
  assert.match(profileContracts, /record CreateNotificationProviderProfileRequest/u);
  assert.match(profileContracts, /record NotificationProviderProfileResponse/u);
  assert.match(profileContracts, /SecretStatus/u);
  assert.doesNotMatch(profileContracts, /string\? Secret\b/u);
  assert.match(bindingContracts, /record CreateNotificationBindingRequest/u);
  assert.match(bindingContracts, /record NotificationBindingResponse/u);
  assert.match(profileEndpoint, /MapGroup\("\/api\/v1\/notifications\/provider-profiles"\)/u);
  assert.match(profileEndpoint, /\.WithName\("notificationsListProviderTypes"\)/u);
  assert.match(profileEndpoint, /\.WithName\("notificationsCreateProviderProfile"\)/u);
  assert.match(bindingEndpoint, /MapGroup\("\/api\/v1\/notifications\/bindings"\)/u);
  assert.match(bindingEndpoint, /\.WithName\("notificationsPublishBinding"\)/u);
});
