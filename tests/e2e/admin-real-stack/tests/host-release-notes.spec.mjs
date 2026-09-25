import { expect, test } from '@playwright/test';
import {
  adminOrigin,
  clickMainNavLink,
  crudTableRow,
  loginAsHostAdmin,
  loginAsHostViewer,
  loginAccessTokenWithPassword,
  loginHostAdminAccessToken
} from './support/real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const viewerUsername = process.env.FULLNET_E2E_VIEWER_USERNAME ?? 'e2e-viewer';
const viewerPassword = process.env.FULLNET_E2E_VIEWER_PASSWORD
  ?? process.env.FULLNET_E2E_PASSWORD
  ?? 'FullNet!2026Secure';

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

function uniqueVersionLabel() {
  const minor = Math.floor(Date.now() / 1000) % 999;
  const patch = Date.now() % 999;
  return `99.${minor}.${patch}`;
}

async function drainOlderUnreadNotes(request, token, origin, keepId) {
  for (let attempt = 0; attempt < 12; attempt += 1) {
    const response = await request.get(
      `${apiBaseUrl}/api/v1/platform/my-release-notes/latest-unread`,
      { headers: { Authorization: `Bearer ${token}`, Origin: origin } }
    );
    if (response.status() === 204) {
      return null;
    }
    expect(response.ok()).toBeTruthy();
    const note = await response.json();
    if (note.id === keepId) {
      return note;
    }
    const readResponse = await request.post(
      `${apiBaseUrl}/api/v1/platform/my-release-notes/${note.id}/read`,
      { headers: authHeaders(token, origin) }
    );
    expect(readResponse.ok()).toBeTruthy();
  }
  return null;
}

test('Host 管理员可发布更新日志并记录用户已读（清单 37 API）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = authHeaders(token, origin);
  const versionLabel = uniqueVersionLabel();
  const stamp = Date.now().toString(36);
  const title = `E2E 更新 ${stamp}`;

  const createResponse = await request.post(
    `${apiBaseUrl}/api/v1/platform/host-release-notes`,
    {
      headers,
      data: {
        versionLabel,
        title,
        content: `清单 37 正文 ${stamp}`
      }
    }
  );
  expect(createResponse.status()).toBe(201);
  const draft = await createResponse.json();
  expect(draft.status).toBe('draft');

  const publishResponse = await request.post(
    `${apiBaseUrl}/api/v1/platform/host-release-notes/${draft.id}/publish`,
    { headers, data: { version: draft.version } }
  );
  expect(publishResponse.ok()).toBeTruthy();
  const published = await publishResponse.json();
  expect(published.status).toBe('published');

  const latestUnread = await drainOlderUnreadNotes(
    request,
    token,
    origin,
    published.id
  );
  expect(latestUnread?.id).toBe(published.id);

  const readResponse = await request.post(
    `${apiBaseUrl}/api/v1/platform/my-release-notes/${published.id}/read`,
    { headers }
  );
  expect(readResponse.ok()).toBeTruthy();
  const readOnce = await readResponse.json();
  expect(readOnce.isRead).toBe(true);
  expect(readOnce.readAtUtc).toBeTruthy();

  const readAgainResponse = await request.post(
    `${apiBaseUrl}/api/v1/platform/my-release-notes/${published.id}/read`,
    { headers }
  );
  expect(readAgainResponse.ok()).toBeTruthy();
  const readTwice = await readAgainResponse.json();
  expect(readTwice.isRead).toBe(true);

  const retractResponse = await request.post(
    `${apiBaseUrl}/api/v1/platform/host-release-notes/${published.id}/retract`,
    { headers, data: { version: published.version } }
  );
  expect(retractResponse.ok()).toBeTruthy();
});

test('Host 管理员可在管理页创建并发布更新日志（清单 37 UI）', async ({
  page
}, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '更新日志管理页仅验收 Vue');
  const stamp = Date.now().toString(36);
  const versionLabel = uniqueVersionLabel();
  const title = `E2E UI ${stamp}`;

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /更新日志管理/, '平台');
  await expect(page.getByRole('heading', { name: '更新日志管理', exact: true }))
    .toBeVisible({ timeout: 15_000 });

  await page.getByTestId('host-release-notes-action-create').click();
  await page.getByTestId('host-release-notes-version').fill(versionLabel);
  await page.getByTestId('host-release-notes-title').fill(title);
  await page.getByTestId('host-release-notes-content').fill(`UI 正文 ${stamp}`);
  await page.getByTestId('host-release-notes-editor-submit').click();

  const row = crudTableRow(page.locator('main'), 'vue', title);
  await expect(row).toBeVisible({ timeout: 15_000 });
  await row.getByTestId('host-release-notes-publish').click();
  await page.getByRole('button', { name: '确定' }).click();
  await expect(row).toContainText('已发布');

  await row.getByTestId('host-release-notes-retract').click();
  await page.getByRole('button', { name: '确定' }).click();
  await expect(row).toContainText('已撤回');
});

test('受限 Host 账号无法创建更新日志（清单 37）', async ({
  page,
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  await loginAsHostViewer(page);
  await page.goto('/#/platform/host-release-notes');
  await expect(page.getByTestId('host-release-notes-action-create')).toHaveCount(0);

  const viewerToken = await loginAccessTokenWithPassword(
    request,
    clientKind,
    viewerUsername,
    viewerPassword
  );
  const createResponse = await request.post(
    `${apiBaseUrl}/api/v1/platform/host-release-notes`,
    {
      headers: authHeaders(viewerToken, origin),
      data: {
        versionLabel: uniqueVersionLabel(),
        title: 'denied',
        content: 'denied'
      }
    }
  );
  expect(createResponse.status()).toBe(403);
  expect((await createResponse.json()).code).toBe('authorization.permission_denied');
});
