import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
    sessionStorage.setItem('fullnet.admin.artShellSettings', JSON.stringify({
      menuLayout: 'left',
      dualMenuShowText: false
    }));
  });
  await mockAuthenticatedSession(page);
});

test('Vue 管理端可在设置中切换菜单布局', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'vue-admin', '仅验证 Vue Art 壳层');

  await page.goto('/');
  await page.getByRole('button', { name: '界面设置' }).click();
  await page.getByRole('button', { name: '顶部菜单' }).click();

  await expect(page.locator('.art-admin-shell--layout-top')).toBeVisible();
  await expect(page.locator('.art-horizontal-menu')).toBeVisible();
  await expect(page.locator('.art-admin-shell__sidebar')).toBeHidden();

  await page.getByRole('button', { name: '混合菜单' }).click();
  await expect(page.locator('.art-admin-shell--layout-top-left')).toBeVisible();
  await expect(page.locator('.art-mixed-menu')).toBeVisible();

  await page.getByRole('button', { name: '双栏菜单' }).click();
  await expect(page.locator('.art-admin-shell--layout-dual-menu')).toBeVisible();
  await expect(page.locator('.art-dual-rail')).toBeVisible();
});

test('Layui 管理端可在设置中切换菜单布局', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'layui-admin', '仅验证 Layui 壳层');

  await page.goto('/');
  await page.locator('[data-shell-settings-open]').click();
  await page.getByRole('button', { name: '顶部菜单' }).click();

  await expect(page.locator('.fn-shell--layout-top')).toBeVisible();
  await expect(page.locator('[data-horizontal-menu]')).toBeVisible();

  await page.getByRole('button', { name: '混合菜单' }).click();
  await expect(page.locator('.fn-shell--layout-top-left')).toBeVisible();
  await expect(page.locator('[data-mixed-menu]')).toBeVisible();

  await page.getByRole('button', { name: '双栏菜单' }).click();
  await expect(page.locator('.fn-shell--layout-dual-menu')).toBeVisible();
  await expect(page.locator('[data-dual-rail]')).toBeVisible();
});

test('双端设置抽屉可切换主题与语言开关', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;

  await page.goto('/');

  if (clientKind === 'vue') {
    await page.getByRole('button', { name: '界面设置' }).click();
    await page.locator('.art-settings-drawer').getByRole('button', { name: '深色' }).first().click();
    await expect(page.locator('html')).toHaveAttribute('data-art-theme', 'dark');
    await page.locator('.art-settings-basic-item').filter({ hasText: '语言切换' }).locator('.el-switch').click();
    await expect(page.getByTestId('shell-locale-trigger')).toBeHidden();
  } else {
    await page.locator('[data-shell-settings-open]').click();
    await page.locator('[data-shell-settings-body]').getByRole('button', { name: '深色' }).first().click();
    await expect(page.locator('html')).toHaveAttribute('data-art-theme', 'dark');
    await page.locator('[data-shell-settings-body]').getByText('语言切换', { exact: true }).locator('..').locator('input[type="checkbox"]').setChecked(false);
    await expect(page.locator('[data-shell-chrome="language"]')).toBeHidden();
  }
});

test('Layui 管理端多标签页与面包屑随路由更新', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'layui-admin', '仅验证 Layui 壳层');

  await page.goto('/');
  await expect(page.locator('[data-shell-chrome="breadcrumb"]')).toBeVisible();
  await expect(page.locator('[data-page-tabs]')).toBeVisible();
  await expect(page.locator('[data-page-tabs] .fn-page-tabs__item')).toHaveCount(1);

  await page.getByRole('link', { name: /租户管理/ }).click();
  await expect(page).toHaveURL(/#\/tenants$/);
  await expect(page.locator('[data-page-tabs] .fn-page-tabs__item')).toHaveCount(2);
  await expect(page.locator('[data-page-tabs] .fn-page-tabs__item.is-active')).toContainText('租户');

  await page.locator('[data-shell-settings-open]').click();
  await page.locator('[data-shell-settings-body]').getByText('面包屑', { exact: true }).locator('..').locator('input[type="checkbox"]').setChecked(false);
  await expect(page.locator('[data-shell-chrome="breadcrumb"]')).toBeHidden();
});

test('Layui 管理端移动端回退左侧菜单布局', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'layui-admin', '仅验证 Layui 壳层');

  await page.addInitScript(() => {
    sessionStorage.setItem('fullnet.admin.artShellSettings', JSON.stringify({
      menuLayout: 'top',
      dualMenuShowText: false,
      showPageTabs: true,
      showBreadcrumb: true
    }));
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/');

  await expect(page.locator('.fn-shell--layout-top')).toBeVisible();
  await expect(page.locator('[data-horizontal-menu]')).toBeHidden();
  await expect(page.locator('.fn-sidebar')).toBeVisible();
});

test('Layui 管理端顶栏折叠与刷新控件', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'layui-admin', '仅验证 Layui 壳层');

  let tenantLoads = 0;
  await page.route('**/api/v1/tenancy/tenants?page=1&pageSize=20', route => {
    tenantLoads += 1;
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /租户管理/ }).click();
  await expect.poll(() => tenantLoads).toBeGreaterThan(0);

  await page.locator('[data-shell-menu-toggle]').click();
  await expect(page.locator('.fn-sidebar--collapsed')).toBeVisible();
  await expect(page.locator('.fn-shell--menu-collapsed')).toBeVisible();

  const beforeRefresh = tenantLoads;
  await page.locator('[data-shell-refresh]').click();
  await expect.poll(() => tenantLoads).toBeGreaterThan(beforeRefresh);

  await page.locator('[data-shell-settings-open]').click();
  await page.locator('[data-shell-settings-body]').getByText('全屏按钮', { exact: true }).locator('..').locator('input[type="checkbox"]').setChecked(false);
  await expect(page.locator('[data-shell-fullscreen]')).toBeHidden();
});

test('双端全局搜索与主题切换', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;

  await page.goto('/');

  if (clientKind === 'vue') {
    await page.locator('.art-header__search').click();
    await expect(page.locator('.art-global-search')).toBeVisible();
    await page.locator('.art-global-search input').fill('租户');
    await page.locator('.art-global-search__item').first().click();
    await expect(page).toHaveURL(/\/tenants$/);
    await page.getByRole('button', { name: '切换为深色主题' }).click();
  } else {
    await page.locator('[data-shell-search-open]').click();
    await expect(page.locator('[data-shell-search]')).toBeVisible();
    await page.locator('[data-shell-search-input]').fill('租户');
    await page.locator('.fn-search-modal__item').first().click();
    await expect(page).toHaveURL(/#\/tenants$/);
    await page.locator('[data-shell-theme-toggle]').click();
  }

  await expect(page.locator('html')).toHaveAttribute('data-art-theme', 'dark');
});

test('双端通知面板可打开', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;

  await page.goto('/');

  if (clientKind === 'vue') {
    await page.getByRole('button', { name: '通知' }).click();
    await expect(page.locator('.art-notification-panel')).toBeVisible();
    await expect(page.locator('.art-notification-panel__title')).toHaveText('通知');
    await expect(page.locator('.art-notification-panel__tabs li')).toHaveCount(3);
    await page.locator('.art-notification-panel__tabs li').nth(2).click();
    await expect(page.locator('.art-notification-panel__empty')).toContainText('待办');
  } else {
    await page.locator('[data-shell-notifications-open]').click();
    await expect(page.locator('[data-shell-notifications]')).toBeVisible();
    await expect(page.locator('[data-shell-notifications-title]')).toHaveText('通知');
    await expect(page.locator('.fn-notice-panel__tab')).toHaveCount(3);
    await page.locator('.fn-notice-panel__tab').nth(2).click();
    await expect(page.locator('[data-shell-notifications-empty]')).toContainText('待办');
  }
});

test('双端聊天抽屉可打开并发送消息', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const messageText = 'E2E 测试消息';

  await page.goto('/');

  if (clientKind === 'vue') {
    await page.getByRole('button', { name: '消息' }).click();
    await expect(page.locator('.art-chat-drawer')).toBeVisible();
    await expect(page.locator('.art-chat-drawer strong')).toHaveText('Art Bot');
    await expect(page.locator('.art-chat-drawer__message')).toHaveCount(5);
    await page.locator('.art-chat-drawer textarea').fill(messageText);
    await page.getByRole('button', { name: '发送' }).click();
    await expect(page.locator('.art-chat-drawer__message')).toHaveCount(6);
    await expect(page.locator('.art-chat-drawer__message.is-me').last()).toContainText(messageText);
  } else {
    await page.locator('[data-shell-chat-open]').click();
    await expect(page.locator('[data-shell-chat]')).toBeVisible();
    await expect(page.locator('[data-shell-chat-title]')).toHaveText('Art Bot');
    await expect(page.locator('.fn-chat-drawer__message')).toHaveCount(5);
    await page.locator('[data-shell-chat-input]').fill(messageText);
    await page.locator('[data-shell-chat-send]').click();
    await expect(page.locator('.fn-chat-drawer__message')).toHaveCount(6);
    await expect(page.locator('.fn-chat-drawer__message.is-me').last()).toContainText(messageText);
  }
});

test('双端多标签页随路由累积并可关闭', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const tabItem = clientKind === 'vue'
    ? page.locator('.art-tabs__item')
    : page.locator('.fn-page-tabs__item');
  const tabClose = clientKind === 'vue'
    ? page.locator('.art-tabs__close').first()
    : page.locator('.fn-page-tabs__close').first();

  await page.goto('/');
  await expect(tabItem).toHaveCount(1);
  await page.getByRole('link', { name: /租户/ }).click();
  await expect(tabItem).toHaveCount(2);
  await tabClose.click();
  await expect(tabItem).toHaveCount(1);
});

async function mockAuthenticatedSession(page) {
  await page.route('**/api/v1/auth/refresh', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      accessToken: 'e2e-access-token',
      tokenType: 'Bearer',
      expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString()
    })
  }));
  await page.route('**/api/v1/me', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
      username: 'admin',
      displayName: '系统管理员',
      tenantId: null,
      actorScope: 'host',
      isSuperAdministrator: true,
      scope: 'host',
      permissions: [
        'identity.navigation.read',
        'platform.dashboard.read',
        'tenancy.tenants.read',
        'tenancy.tenants.switch'
      ],
      sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
      preferredLocale: 'zh-CN',
      profileVersion: 1
    })
  }));
  await page.route('**/api/v1/navigation', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify([
      {
        id: 'overview', parentId: null, routeName: 'overview', path: '/',
        componentKey: 'overview', title: '工作台', caption: '概览', icon: 'dashboard',
        order: 10, requiredPermission: 'platform.dashboard.read', children: []
      },
      {
        id: 'tenants', parentId: null, routeName: 'tenant-management', path: '/tenants',
        componentKey: 'tenants', title: '租户管理', caption: '租户', icon: 'building',
        order: 20, requiredPermission: 'tenancy.tenants.read', children: []
      }
    ])
  }));
  await page.route('**/api/v1/tenancy/available', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify([])
  }));
}
