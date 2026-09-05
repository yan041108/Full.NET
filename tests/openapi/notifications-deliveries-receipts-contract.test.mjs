import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/notifications-deliveries-receipts-v1.json'
);
const deliveryContractsPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Contracts/NotificationDeliveryContracts.cs'
);
const deliveryEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Features/ManageDeliveries/Endpoint.cs'
);
const receiptEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Features/ReceiveProviderReceipts/Endpoint.cs'
);

test('投递与回执 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const deliveryContracts = await readFile(deliveryContractsPath, 'utf8');
  const deliveryEndpoint = await readFile(deliveryEndpointPath, 'utf8');
  const receiptEndpoint = await readFile(receiptEndpointPath, 'utf8');

  assert.equal(contract.id, 'notifications-deliveries-receipts-v1');
  assert.match(deliveryContracts, /record NotificationDeliveryResponse/u);
  assert.match(deliveryContracts, /record NotificationDeliveryReceiptResponse/u);
  assert.match(deliveryContracts, /record RetryNotificationDeliveryRequest/u);
  assert.match(deliveryContracts, /record NotificationReceiptAcceptedResponse/u);
  assert.doesNotMatch(deliveryContracts, /SecretReference/u);
  assert.match(deliveryEndpoint, /MapGroup\("\/api\/v1\/notifications\/deliveries"\)/u);
  assert.match(deliveryEndpoint, /\.WithName\("notificationsListDeliveries"\)/u);
  assert.match(deliveryEndpoint, /\.WithName\("notificationsGetDelivery"\)/u);
  assert.match(deliveryEndpoint, /\.WithName\("notificationsRetryDelivery"\)/u);
  assert.match(receiptEndpoint, /MapPost\("\/api\/v1\/notifications\/provider-receipts\/\{providerTypeKey\}"/u);
  assert.match(receiptEndpoint, /\.WithName\("notificationsReceiveProviderReceipt"\)/u);
  assert.match(receiptEndpoint, /\.AllowAnonymous\(\)/u);
});
