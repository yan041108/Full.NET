import { expect, test } from '@playwright/test';
import { loginAsHostAdmin } from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可访问 enterprise-requests CRUD 页', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await loginAsHostAdmin(page);
  await page.goto('/enterprise-requests');
  if (clientKind === 'vue') {
    await expect(page).toHaveURL(/\/enterprise-requests/);
    await expect(page.locator('.generated-crud-view, .enterprise-requests-view').first()).toBeVisible({ timeout: 15000 });
  } else {
    await expect(page).toHaveURL(/\/enterprise-requests/);
  }
});
