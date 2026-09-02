import { expect, test } from '@playwright/test';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('限时诊断策略导航、默认状态与恢复在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  const restores = [];
  await mockAuthenticatedSession(page);
  await page.route('**/api/v1/settings/diagnostic-policy', async route => {
    if (route.request().method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          version: 0,
          pressureState: 'Normal',
          isDefault: true,
          loadedAtUtc: '2026-08-01T00:00:00.000Z',
          activeRules: [],
          configEntryVersion: 0
        })
      });
      return;
    }
    await route.fallback();
  });
  await page.route('**/api/v1/settings/diagnostic-policy/restore', async route => {
    restores.push(route.request().postDataJSON());
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        version: 0,
        pressureState: 'Normal',
        isDefault: true,
        loadedAtUtc: '2026-08-01T00:01:00.000Z',
        activeRules: [],
        configEntryVersion: 1
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /限时诊断/ }).click();
  await expect(page.getByRole('heading', { name: '限时诊断策略', exact: true })).toBeVisible();

  const view = clientKind === 'layui'
    ? page.locator('[data-route-view="diagnostic-policy"]')
    : page.locator('.diagnostic-policy-view');
  await expect(view.getByText('安全默认', { exact: true })).toBeVisible();
  await expect(view.getByText(/Sink/)).toBeVisible();
  await view.getByRole('button', { name: /恢复安全默认/ }).click();
  await expect.poll(() => restores).toEqual([{ configEntryVersion: 0 }]);
});

async function mockAuthenticatedSession(page) {
  await page.route('**/api/v1/auth/refresh', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      accessToken: 'parity-access',
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
        'settings.diagnostic_policy.read',
        'settings.diagnostic_policy.update',
        'settings.diagnostic_policy.restore'
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
        componentKey: 'overview', title: '工作台', caption: '平台运行概览',
        icon: 'dashboard', order: 10, requiredPermission: 'platform.dashboard.read', children: []
      },
      {
        id: 'diagnostic-policy', parentId: null, routeName: 'diagnostic-policy',
        path: '/settings/diagnostic-policy', componentKey: 'diagnostic-policy',
        title: '限时诊断', caption: 'Host 限时诊断策略', icon: 'monitor',
        order: 54, requiredPermission: 'settings.diagnostic_policy.read', children: []
      }
    ])
  }));
  await page.route('**/api/v1/tenancy/available', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify([])
  }));
  await page.route('**/api/v1/platform/host-dashboard-summary', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      activeTenantCount: 1,
      onlineSessionCount: 1,
      todayRequestCount: 1,
      todayErrorRate: 0,
      recentActivities: []
    })
  }));
  await page.route('**/api/v1/notifications/my-inbox-messages/unread-count', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ unreadCount: 0 })
  }));
}
