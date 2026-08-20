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
