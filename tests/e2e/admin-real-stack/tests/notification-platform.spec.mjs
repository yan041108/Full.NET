import { expect, test } from '@playwright/test';
import {
  clickMainNavLink,
  loginAsHostAdmin
} from './support/real-stack-auth.mjs';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('Host 管理员可打开通知控制面；空目录、FanOut 明示且不回显密钥', async ({ page }, testInfo) => {
  test.skip(
    testInfo.project.metadata.clientKind === 'layui',
    '通知平台控制面仅验证 Vue 管理端'
  );

  await loginAsHostAdmin(page);

  await clickMainNavLink(page, /渠道配置/, '通知');
  const profilesView = page.locator('.notification-profiles-view');
  await expect(profilesView.getByRole('heading', { name: '渠道配置', exact: true })).toBeVisible();
  await expect(page.getByTestId('notification-profiles-empty-catalog')).toBeVisible();
  await expect(page.getByTestId('notification-profiles-create')).toHaveCount(0);
  await expect(profilesView).not.toContainText('vault://');
  await expect(profilesView).not.toContainText('apiToken');

  await clickMainNavLink(page, /通知模板/, '通知');
  const templatesView = page.locator('.notification-templates-view');
  await expect(templatesView.getByRole('heading', { name: '通知模板', exact: true })).toBeVisible();
  await expect(page.getByTestId('notification-templates-create')).toBeVisible();
  await expect(page.getByTestId('notification-templates-channel')).toContainText('inbox');

  await clickMainNavLink(page, /场景绑定/, '通知');
  const bindingsView = page.locator('.notification-bindings-view');
  await expect(bindingsView.getByRole('heading', { name: '场景绑定', exact: true })).toBeVisible();
  await page.getByTestId('notification-bindings-mode').click();
  await page.getByRole('option', { name: /扇出/ }).click();
  await expect(page.getByTestId('notification-bindings-fanout')).toContainText('不会隐式多发');
  await expect(page.getByTestId('notification-bindings-create')).toBeDisabled();

  await clickMainNavLink(page, /投递运维/, '通知');
  const deliveriesView = page.locator('.notification-deliveries-view');
  await expect(deliveriesView.getByRole('heading', { name: '投递运维', exact: true })).toBeVisible();
  await expect(deliveriesView).toContainText('还没有投递记录');
  await expect(page.getByTestId('notification-deliveries-retry')).toHaveCount(0);

  await clickMainNavLink(page, /通知偏好/, '通知');
  await expect(page.getByTestId('notification-preferences-unavailable')).toBeVisible();
  await expect(page.getByTestId('notification-preferences-save')).toHaveCount(0);
  await expect(page.getByTestId('notification-preferences-unavailable')).toContainText('首个真实 Provider');
});
