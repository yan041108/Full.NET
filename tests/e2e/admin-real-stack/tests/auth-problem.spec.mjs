import { expect, test } from '@playwright/test';

test.use({ storageState: { cookies: [], origins: [] } });

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('登录失败展示稳定 ProblemDetails 错误码', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('账号', { exact: true }).fill('admin');
  await page.getByLabel('密码', { exact: true }).fill('wrong-password');
  await page.getByRole('button', { name: '进入控制台' }).click();

  await expect(page.getByRole('alert')).toContainText('identity.invalid_credentials');
});
