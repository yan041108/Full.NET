import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  crudTableRow,
  loginAsHostAdmin,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

function authHeaders(token, origin) {
  return {
    Authorization: `Bearer ${token}`,
    Origin: origin,
    'Content-Type': 'application/json'
  };
}

async function createAndPublishAnnouncement(request, clientKind) {
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = authHeaders(token, origin);
  const stamp = Date.now().toString(36);
  const title = `E2E 公告 ${stamp}`;

  const createResponse = await request.post(
    `${apiBaseUrl}/api/v1/notifications/host-announcements`,
    { headers, data: { title, content: `正文 ${stamp}` } }
  );
  expect(createResponse.status()).toBe(201);
  const draft = await createResponse.json();

  const publishResponse = await request.post(
    `${apiBaseUrl}/api/v1/notifications/host-announcements/${draft.id}/publish`,
    { headers, data: { version: draft.version } }
  );
  expect(publishResponse.ok()).toBeTruthy();
  const published = await publishResponse.json();
  return { token, origin, headers, title, published };
}

test('Host 用户可收件、标记已读并查询发布方统计（清单 42 API）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const { token, origin, headers, title, published } = await createAndPublishAnnouncement(
    request,
    clientKind
  );

  const unreadResponse = await request.get(
    `${apiBaseUrl}/api/v1/notifications/my-host-announcements/unread-count`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(unreadResponse.ok()).toBeTruthy();
  const unread = await unreadResponse.json();
  expect(unread.unreadCount).toBeGreaterThanOrEqual(1);

  const listResponse = await request.get(
    `${apiBaseUrl}/api/v1/notifications/my-host-announcements?page=1&pageSize=20&isRead=false`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(listResponse.ok()).toBeTruthy();
  const list = await listResponse.json();
  const row = list.items.find(item => item.id === published.id);
  expect(row).toBeTruthy();
  expect(row.isRead).toBe(false);
  expect(row.title).toBe(title);

  const readResponse = await request.post(
    `${apiBaseUrl}/api/v1/notifications/my-host-announcements/${published.id}/read`,
    { headers }
  );
  expect(readResponse.ok()).toBeTruthy();
  const detail = await readResponse.json();
  expect(detail.isRead).toBe(true);
  expect(detail.readAtUtc).toBeTruthy();

  const statsResponse = await request.get(
    `${apiBaseUrl}/api/v1/notifications/host-announcements/${published.id}/read-stats`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(statsResponse.ok()).toBeTruthy();
  const stats = await statsResponse.json();
  expect(stats.readCount).toBeGreaterThanOrEqual(1);
  expect(stats.eligibleRecipientCount).toBeGreaterThanOrEqual(stats.readCount);

  const receiptsResponse = await request.get(
    `${apiBaseUrl}/api/v1/notifications/host-announcements/${published.id}/read-receipts?page=1&pageSize=20`,
    { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
  );
  expect(receiptsResponse.ok()).toBeTruthy();
  const receipts = await receiptsResponse.json();
  expect(receipts.total).toBeGreaterThanOrEqual(1);
});

test('Host 管理员可在「我收到的公告」标记已读并查看阅读统计（清单 42 UI）', async ({
  page,
  request
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '收件与统计 UI 仅验收 Vue');
  const clientKind = testInfo.project.metadata.clientKind;
  const { title, published } = await createAndPublishAnnouncement(request, clientKind);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /我收到的公告/);
  await expect(page.getByRole('heading', { name: '我收到的公告', exact: true }))
    .toBeVisible({ timeout: 15_000 });

  const inboxRow = crudTableRow(page.locator('main'), 'vue', title);
  await expect(inboxRow).toBeVisible({ timeout: 15_000 });
  await inboxRow.getByTestId('my-host-announcements-mark-read').click();
  await expect(inboxRow.getByText('已读')).toBeVisible({ timeout: 10_000 });

  await clickMainNavLink(page, /公告管理/);
  const manageRow = crudTableRow(page.locator('main'), 'vue', title);
  await expect(manageRow).toBeVisible();
  await manageRow.getByTestId('host-announcements-read-stats').click();
  const dialog = page.getByTestId('host-announcements-read-stats-dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog.getByText('已读人数', { exact: true })).toBeVisible();
  await expect(dialog.locator('dd').first()).not.toHaveText('0');
});
