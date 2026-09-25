import { expect, test } from '@playwright/test';
import { randomUUID } from 'node:crypto';
import { clickMainNavLink, loginAsHostAdmin } from './support/real-stack-auth.mjs';
import {
  buildK3CloudConnectionBody,
  createK3CloudConnectionConfigViaApi,
  createK3CloudDocumentSyncViaApi,
  expectK3CloudConnectionListItemMasked,
  getK3CloudDocumentSyncViaApi,
  k3cloudSalSaleOrderDocumentTypeKey,
  listK3CloudConnectionConfigsViaApi,
  listK3CloudDocumentSyncsViaApi,
  testK3CloudConnectionConfigViaApi
} from './support/k3cloud-real-stack.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function requireVue(clientKind) {
  test.skip(clientKind === 'layui', 'K3Cloud 仅 Vue 交付线');
}

test('API：连接脱敏、ValidateUser 测试入口与固定单据同步契约（清单 79）', async ({ request }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);

  const { response: connectionsResponse } = await listK3CloudConnectionConfigsViaApi(
    request,
    clientKind
  );
  expect(connectionsResponse.ok()).toBeTruthy();
  for (const item of await connectionsResponse.json()) {
    expectK3CloudConnectionListItemMasked(item);
  }

  const suffix = randomUUID().slice(0, 8);
  const { response: createConnection } = await createK3CloudConnectionConfigViaApi(
    request,
    clientKind,
    buildK3CloudConnectionBody(suffix)
  );
  expect(createConnection.status()).toBe(201);
  const connection = await createConnection.json();
  expect(connection.hasPassword).toBe(true);
  expect(connection).not.toHaveProperty('password');

  const { response: testMissing } = await testK3CloudConnectionConfigViaApi(
    request,
    clientKind,
    randomUUID()
  );
  expect(testMissing.status()).toBe(404);

  const { response: syncsList } = await listK3CloudDocumentSyncsViaApi(request, clientKind);
  expect(syncsList.ok()).toBeTruthy();
  expect(Array.isArray((await syncsList.json()).items)).toBeTruthy();

  const businessKey = `e2e-so-${suffix}`;
  const { response: unsupportedType } = await createK3CloudDocumentSyncViaApi(request, clientKind, {
    connectionConfigId: connection.id,
    documentTypeKey: 'k3cloud.unknown',
    businessKey,
    payloadJson: '{}'
  });
  expect(unsupportedType.status()).toBe(422);

  const { response: invalidPayload } = await createK3CloudDocumentSyncViaApi(request, clientKind, {
    connectionConfigId: connection.id,
    documentTypeKey: k3cloudSalSaleOrderDocumentTypeKey,
    businessKey: `${businessKey}-bad`,
    payloadJson: '{not-json'
  });
  expect(invalidPayload.status()).toBe(422);

  const { response: disabledConnection } = await createK3CloudDocumentSyncViaApi(request, clientKind, {
    connectionConfigId: connection.id,
    documentTypeKey: k3cloudSalSaleOrderDocumentTypeKey,
    businessKey,
    payloadJson: '{"Model":{}}'
  });
  expect(disabledConnection.status()).toBe(422);

  const { response: syncMissing } = await getK3CloudDocumentSyncViaApi(
    request,
    clientKind,
    randomUUID()
  );
  expect(syncMissing.status()).toBe(404);
});

test('UI：K3Cloud 连接与单据同步状态页（清单 79）', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  requireVue(clientKind);
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /K3Cloud 连接/);
  await expect(page.locator('.k3cloud-connection-configs-view')).toBeVisible({ timeout: 20_000 });
  await expect(page.getByTestId('k3cloud-connection-create')).toBeVisible();

  await clickMainNavLink(page, /K3Cloud 同步/);
  await expect(page.locator('.k3cloud-document-syncs-view')).toBeVisible({ timeout: 20_000 });
  await expect(page.getByTestId('k3cloud-document-sync-create')).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
