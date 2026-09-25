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

test('API：备份任务目录、运行列表与受控下载边界（清单 54）', async ({
  request
}, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const origin = adminOrigin(clientKind);
  const token = await loginHostAdminAccessToken(request, clientKind);
  const headers = {
    Authorization: `Bearer ${token}`,
    Origin: origin
  };

  const statusResponse = await request.get(
    `${apiBaseUrl}/api/v1/platform/backup-executor/status`,
    { headers }
  );
  expect(statusResponse.ok()).toBeTruthy();
  const status = await statusResponse.json();
  expect(typeof status.artifactRootPath).toBe('string');
  expect(typeof status.artifactRootExists).toBe('boolean');
  expect(typeof status.enabledTaskCount).toBe('number');

  const tasksResponse = await request.get(
    `${apiBaseUrl}/api/v1/platform/backup-executor/tasks`,
    { headers }
  );
  expect(tasksResponse.ok()).toBeTruthy();
  const tasks = await tasksResponse.json();
  expect(Array.isArray(tasks)).toBeTruthy();

  const runsResponse = await request.get(
    `${apiBaseUrl}/api/v1/platform/backup-executor/runs?page=1&pageSize=20`,
    { headers }
  );
  expect(runsResponse.ok()).toBeTruthy();
  const runsPage = await runsResponse.json();
  expect(Array.isArray(runsPage.items)).toBeTruthy();

  const missingRun = await request.get(
    `${apiBaseUrl}/api/v1/platform/backup-executor/runs/00000000-0000-4000-8000-000000000099`,
    { headers }
  );
  expect(missingRun.status()).toBe(404);

  const missingDownload = await request.get(
    `${apiBaseUrl}/api/v1/platform/backup-executor/runs/00000000-0000-4000-8000-000000000099/download`,
    { headers }
  );
  expect([404, 400]).toContain(missingDownload.status());
});

test('UI：授权备份执行器页（清单 54，Vue）', async ({ page }, testInfo) => {
  test.skip(testInfo.project.metadata.clientKind !== 'vue', '授权备份页仅 Vue 交付线');
  test.setTimeout(120_000);

  await loginAsHostAdmin(page);
  await clickMainNavLink(page, /授权备份/);

  await expect(page.getByText('授权备份执行器', { exact: true }).first()).toBeVisible({
    timeout: 20_000
  });
  await expect(page.getByText('任务目录', { exact: true })).toBeVisible();
  await expect(page.getByText('运行结果', { exact: true })).toBeVisible();
  await expect(page.getByText('不包含生产恢复操作', { exact: false })).toBeVisible();
  await expect(page.getByText('403', { exact: true })).toHaveCount(0);
});
