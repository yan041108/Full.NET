import { expect, test } from '@playwright/test';
import {
  expectVisibleCurrentContext,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test.describe('匿名登录流', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('legacy 管理端展示密码表单并隐藏身份中心入口', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
    await expect(page.getByLabel('账号', { exact: true })).toBeVisible();
    await expect(page.getByLabel('密码', { exact: true })).toBeVisible();
    await expect(page.getByTestId('login-oidc-center')).toHaveCount(0);
  });

  test('legacy 回退场景下遗留 OIDC refresh 凭据不阻断密码登录', async ({ page }) => {
    await page.addInitScript(() => {
      sessionStorage.setItem('fullnet.admin.oidc.refresh', JSON.stringify({
        refreshToken: 'stale-oidc-refresh',
        clientId: 'e2e-admin-oidc-spa'
      }));
    });
    await page.goto('/');
    await expect(page.getByTestId('login-oidc-center')).toHaveCount(0);
    await expect(page.getByLabel('账号', { exact: true })).toBeVisible();
    await loginAsHostAdmin(page);
    await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({
      timeout: 30_000
    });
    await expectVisibleCurrentContext(page, 'Full.NET Host');
  });

  test('legacy 登录后不写入 OIDC refresh 凭据', async ({ page }) => {
    await loginAsHostAdmin(page);
    expect(await page.evaluate(() => sessionStorage.getItem('fullnet.admin.oidc.refresh'))).toBeNull();
  });

  test('legacy 回退后刷新页面仍通过 Refresh Cookie 恢复会话', async ({ page }) => {
    await page.addInitScript(() => {
      sessionStorage.setItem('fullnet.admin.oidc.refresh', JSON.stringify({
        refreshToken: 'stale-oidc-refresh',
        clientId: 'e2e-admin-oidc-spa'
      }));
    });
    await page.goto('/');
    await loginAsHostAdmin(page);
    await expectVisibleCurrentContext(page, 'Full.NET Host');

    await page.reload();
    await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({
      timeout: 30_000
    });
    await expectVisibleCurrentContext(page, 'Full.NET Host');
  });

  test('真实 API 登录后展示动态导航与 Host 上下文', async ({ page }, testInfo) => {
    const clientKind = testInfo.project.metadata.clientKind;

    await loginAsHostAdmin(page);
    const navigation = page.getByRole('navigation', { name: '主导航' }).first();
    await expect(navigation).toBeVisible();
    // 工作台分组标题与首页同名；展开后断言叶子链接。
    const platformGroup = navigation.locator('.el-sub-menu__title').filter({ hasText: '工作台' });
    if (await platformGroup.count()) {
      await platformGroup.first().click();
    }
    await expect(navigation.getByRole('link', { name: /^工作台$/ })).toBeVisible();
    await expect(navigation.getByRole('link', { name: /租户上下文/ })).toBeVisible();
    await expectVisibleCurrentContext(page, 'Full.NET Host');
    await expect(page.locator(`[data-client-kind="${clientKind}"]`)).toBeVisible();
  });
});
