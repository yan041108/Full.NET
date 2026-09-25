import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('API：MQTT Broker 状态、目录与主题 ACL fail-closed（清单 56）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };

  const statusResponse = await request.get(`${apiBaseUrl}/api/v1/mqtt/status`, { headers });
  expect(statusResponse.ok()).toBeTruthy();
  const status = await statusResponse.json();
  expect(typeof status.isEnabled).toBe('boolean');
  expect(Array.isArray(status.allowedPublishTopicPrefixes)).toBeTruthy();

  const clientsResponse = await request.get(`${apiBaseUrl}/api/v1/mqtt/clients`, { headers });
  expect(clientsResponse.ok()).toBeTruthy();
  expect(Array.isArray(await clientsResponse.json())).toBeTruthy();

  const messagesResponse = await request.get(
    `${apiBaseUrl}/api/v1/mqtt/messages?page=1&pageSize=20`,
    { headers }
  );
  expect(messagesResponse.ok()).toBeTruthy();
  const messagesPage = await messagesResponse.json();
  expect(Array.isArray(messagesPage.items)).toBeTruthy();

  const forbiddenPublish = await request.post(`${apiBaseUrl}/api/v1/mqtt/messages/publish`, {
    headers,
    data: {
      topic: 'evil/unauthorized/topic',
      payload: 'ping',
      qos: 0,
      idempotencyKey: null
    }
  });
  expect([403, 409]).toContain(forbiddenPublish.status());
  if (forbiddenPublish.status() === 403) {
    const problem = await forbiddenPublish.json();
    expect(problem.code).toBe('mqtt.topic.forbidden');
  }
});

test('UI：MQTT 控制面页（清单 56，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', 'MQTT 控制面仅 Vue 交付线');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /MQTT 控制面/);

  await expect(page.getByText('MQTT 控制面', { exact: true }).first()).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByText('Kafka', { exact: false })).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
