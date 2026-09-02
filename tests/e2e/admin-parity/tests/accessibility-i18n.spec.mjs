import { expect, test } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';
const wcagTags = [
  'wcag2a',
  'wcag2aa',
  'wcag21a',
  'wcag21aa',
  'wcag22a',
  'wcag22aa'
];

async function isVueAdminProject(testInfo) {
  return testInfo.project.name === 'vue-admin';
}

async function selectShellLocale(page, locale, testInfo) {
  if (await isVueAdminProject(testInfo)) {
    await page.getByTestId('shell-locale-trigger').click();
    const label = locale === 'en-US' ? 'English' : '简体中文';
    await page.getByRole('menuitem', { name: label }).click();
    return;
  }

  await page.locator('select[name="locale"]:visible').selectOption(locale);
}

async function expectShellLocale(page, locale, testInfo) {
  if (await isVueAdminProject(testInfo)) {
    await expect(page.getByTestId('shell-locale-trigger')).toHaveAttribute(
      'data-active-locale',
      locale
    );
    return;
  }

  await expect(page.locator('select[name="locale"]:visible')).toHaveValue(locale);
}

async function expectShellHostContextVisible(page, testInfo) {
  if (await isVueAdminProject(testInfo)) {
    await page.getByRole('button', { name: '系统管理员' }).click();
    await expect(page.getByTestId('shell-tenant-select')).toBeVisible();
    await expect(page.getByTestId('shell-tenant-select')).toContainText('Full.NET Host');
    await page.keyboard.press('Escape');
    return;
  }

  await expect(page.getByText('Full.NET Host', { exact: true }).first()).toBeVisible();
}

async function expectShellLocaleControlVisible(page, testInfo) {
  if (await isVueAdminProject(testInfo)) {
    await expect(page.getByTestId('shell-locale-trigger')).toBeVisible();
    return;
  }

  await expect(page.locator('select[name="locale"]:visible')).toBeVisible();
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    if (localStorage.getItem('fullnet.admin.locale') === null) {
      localStorage.setItem('fullnet.admin.locale', 'zh-CN');
    }
  });
});

test('匿名登录页通过 WCAG 2.2 A/AA 自动检查', async ({ page }) => {
  await mockAnonymousSession(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();

  await expectNoWcagViolations(page);
});

test('认证壳层、租户和状态页通过 WCAG 2.2 A/AA 自动检查', async ({ page }) => {
  await mockAuthenticatedSession(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: '早上好，系统管理员' }))
    .toBeVisible();
  await expectNoWcagViolations(page);

  const paths = [
    ['/tenant-context', '租户上下文'],
    ['/403', '没有访问权限'],
    ['/404', '页面没有找到'],
    ['/500', '服务暂时不可用']
  ];
  for (const [path, heading] of paths) {
    await page.goto(`/#${path}`);
    await expect(page.getByRole('heading', { name: heading })).toBeVisible();
    await expectNoWcagViolations(page);
  }
});

test('英文选择同步文档语义并在刷新后保持', async ({ page }, testInfo) => {
  const server = await mockAuthenticatedSession(page);
  await page.goto('/?component-locale-fixture=1');
  await selectShellLocale(page, 'en-US', testInfo);

  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US');
  await expect(page).toHaveTitle('Overview · Full.NET');
  await expect(page.getByRole('navigation', { name: 'Main navigation' }))
    .toBeVisible();
  await expect(page.getByRole('heading', {
    name: 'Good morning, administrator'
  })).toBeVisible();
  if (testInfo.project.name === 'vue-admin') {
    await expect(page.locator('.art-admin-shell'))
      .toHaveAttribute('data-component-locale', 'en');
    await expect(page.locator('[data-component-locale-fixture]'))
      .toBeVisible();
    await expect(page.locator('[data-component-locale-fixture] .btn-next'))
      .toHaveAttribute('aria-label', 'Go to next page');
    await page.locator('[data-component-locale-fixture] input').click();
    await expect(page.getByRole('button', { name: 'Previous Month' }))
      .toBeVisible();
  } else {
    const fixture = page.locator('[data-component-locale-fixture]');
    await expect(fixture).toBeVisible();
    await expect(fixture.locator('.layui-laypage-next')).toHaveText('Next');
    await fixture.locator('input').click();
    await expect(page.locator('.laydate-btns-confirm:visible')).toHaveText('Confirm');
  }

  await page.reload();

  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US');
  await expect(page).toHaveTitle('Overview · Full.NET');
  await expectShellLocale(page, 'en-US', testInfo);
  expect(server.requests.some(request =>
    request.method === 'GET' && request.locale === 'en-US'
  )).toBe(true);
  expect(server.requests.find(request => request.method === 'PUT')?.locale)
    .toBe('zh-CN');
});

test('语言偏好保存失败时保留登录、租户和原语言', async ({ page }, testInfo) => {
  await mockAuthenticatedSession(page, { failLocaleSave: true });
  await page.goto('/');

  await selectShellLocale(page, 'en-US', testInfo);

  await expectShellLocale(page, 'zh-CN', testInfo);
  await expect(page.locator('html')).toHaveAttribute('lang', 'zh-CN');
  await expect(page.locator('[data-session-shell], .art-admin-shell')).toBeVisible();
  await expectShellHostContextVisible(page, testInfo);
  const alert = page.getByRole('alert').filter({
    hasText: '语言偏好保存失败，已保留原语言'
  });
  await expect(alert).toBeVisible();
  const alertBox = await alert.boundingBox();
  expect(alertBox?.width).toBeGreaterThan(100);
  expect(alertBox?.height).toBeGreaterThan(16);
});

test('切换语言不改变服务端 403 状态和稳定错误码', async ({ page }, testInfo) => {
  const server = await mockAuthenticatedSession(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: '早上好，系统管理员' }))
    .toBeVisible();

  server.failNextMe = true;
  await page.getByTestId('load-current-user').click();
  await expect(page.getByTestId('error-code')).toHaveText('authorization.denied');

  await selectShellLocale(page, 'en-US', testInfo);
  server.failNextMe = true;
  await page.getByTestId('load-current-user').click();
  await expect(page.getByTestId('error-code')).toHaveText('authorization.denied');

  expect(server.problemResponses).toEqual([
    { status: 403, code: 'authorization.denied', locale: 'zh-CN' },
    { status: 403, code: 'authorization.denied', locale: 'en-US' }
  ]);
});

test('跳转链接和路由切换保持可见焦点', async ({ page }) => {
  await mockAuthenticatedSession(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: '早上好，系统管理员' }))
    .toBeVisible();

  await page.keyboard.press('Tab');
  const skipLink = page.getByRole('link', { name: '跳到主要内容' });
  const firstFocusedElement = await page.evaluate(() => ({
    tag: document.activeElement?.tagName,
    className: document.activeElement?.className?.toString() ?? '',
    text: document.activeElement?.textContent?.trim().slice(0, 80) ?? ''
  }));
  await expect(
    skipLink,
    `首次 Tab 实际聚焦：${JSON.stringify(firstFocusedElement)}`
  ).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page.locator('#main-content')).toBeFocused();

  await page.getByRole('menuitem', { name: /租户上下文/ }).click();

  await expect(page.getByRole('heading', { name: '租户上下文' }))
    .toBeFocused();
});

test('320 CSS px 下不产生页面级水平溢出', async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 320, height: 800 });
  await mockAuthenticatedSession(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: '早上好，系统管理员' }))
    .toBeVisible();

  const layout = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    offenders: [...document.querySelectorAll('*')]
      .filter(element => element.getBoundingClientRect().right > document.documentElement.clientWidth)
      .map(element => ({
        tag: element.tagName,
        className: element.className?.toString() ?? '',
        right: Math.round(element.getBoundingClientRect().right),
        scrollWidth: element.scrollWidth
      }))
      .slice(0, 10)
  }));
  expect(
    layout.scrollWidth,
    `页面级水平溢出元素：${JSON.stringify(layout.offenders)}`
  ).toBeLessThanOrEqual(layout.clientWidth);
  await expectShellLocaleControlVisible(page, testInfo);
  if (await isVueAdminProject(testInfo)) {
    await page.getByRole('button', { name: '系统管理员' }).click();
  }
  await expect(page.getByRole('button', { name: '退出登录' })).toBeVisible();
});

test('减弱动画偏好禁用非必要过渡', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' });
  await mockAuthenticatedSession(page);
  await page.goto('/');
  const skipLink = page.getByRole('link', { name: '跳到主要内容' });

  const duration = await skipLink.evaluate(element =>
    Number.parseFloat(getComputedStyle(element).transitionDuration)
  );
  expect(duration).toBeLessThanOrEqual(.001);
});

test('壳层全局搜索、主题与移动导航可键盘操作', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;

  await mockAuthenticatedSession(page);
  await page.goto('/');

  if (clientKind === 'vue') {
    await expect(page.getByRole('heading', { name: '早上好，系统管理员' }))
      .toBeVisible();

    await page.keyboard.press('Control+K');
    const searchDialog = page.getByRole('dialog', { name: '全局搜索' });
    await expect(searchDialog).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(searchDialog).toBeHidden();

    const themeToggle = page.getByRole('button', { name: '切换为深色主题' });
    await themeToggle.click();
    await expect(page.locator('html')).toHaveAttribute('data-art-theme', 'dark');
    await expect(page.getByRole('button', { name: '切换为浅色主题' })).toBeVisible();

    await page.setViewportSize({ width: 320, height: 800 });
    await page.reload();
    await expect(page.getByRole('heading', { name: '早上好，系统管理员' }))
      .toBeVisible();
    await page.getByRole('button', { name: '打开主导航' }).click();
    await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible();
    await page.keyboard.press('Escape');
    return;
  }

  await page.keyboard.press('Control+K');
  await expect(page.locator('[data-shell-search]')).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.locator('[data-shell-search]')).toBeHidden();

  await page.locator('[data-shell-theme-toggle]').click();
  await expect(page.locator('html')).toHaveAttribute('data-art-theme', 'dark');
});

test('Document 管理页通过 WCAG 2.2 A/AA 自动检查', async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== 'vue-admin', 'Document 页面仅在 Vue 管理端验收。');
  test.setTimeout(120_000);

  await mockDocumentAdminSession(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: '早上好，系统管理员' })).toBeVisible();

  const documentPaths = [
    ['/document/host-items', 'Host 文档库'],
    ['/document/recycle-bin', '文档回收站'],
    ['/document/shares', '文档分享'],
    ['/document/permissions', '文档权限'],
    ['/document/statistics', '文档统计']
  ];

  for (const [path, heading] of documentPaths) {
    await page.goto(`/#${path}`);
    await expect(page.getByRole('heading', { name: heading, exact: true })).toBeVisible({
      timeout: 15_000
    });
    await expectNoWcagViolations(page);
  }
});

test('双端标签页可切换并保持可访问性', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;

  await mockAuthenticatedSession(page);
  await page.goto('/');

  if (clientKind === 'vue') {
    await expect(page.getByRole('tablist', { name: '已打开页面' })).toBeVisible();
    await page.getByRole('menuitem', { name: /租户上下文/ }).click();
    await expect(page.getByRole('heading', { name: '租户上下文' })).toBeVisible();
    await expect(page.getByRole('tab', { name: /租户上下文/ })).toHaveAttribute('aria-selected', 'true');
    await page.getByRole('tab', { name: /工作台/ }).click();
    await expect(page.getByRole('heading', { name: '早上好，系统管理员' })).toBeVisible();
    return;
  }

  await expect(page.locator('[data-page-tabs]')).toBeVisible();
  await page.getByRole('link', { name: /租户上下文/ }).click();
  await expect(page.locator('[data-page-tabs] .fn-page-tabs__item.is-active')).toContainText('租户');
  await page.locator('[data-page-tabs] .fn-page-tabs__item').filter({ hasText: '工作台' }).click();
  await expect(page.locator('[data-page-tabs] .fn-page-tabs__item.is-active')).toContainText('工作台');
});

async function expectNoWcagViolations(page) {
  const result = await new AxeBuilder({ page })
    .withTags(wcagTags)
    .analyze();
  expect(
    result.violations,
    JSON.stringify(result.violations, null, 2)
  ).toEqual([]);
}

async function mockAnonymousSession(page) {
  await page.route('**/api/v1/auth/refresh', route => route.fulfill({
    status: 401,
    contentType: 'application/problem+json',
    body: JSON.stringify({
      status: 401,
      code: 'identity.refresh_missing',
      title: '刷新会话不存在'
    })
  }));
}

async function mockAuthenticatedSession(page, options = {}) {
  const server = {
    preferredLocale: options.preferredLocale ?? 'zh-CN',
    profileVersion: 1,
    failLocaleSave: options.failLocaleSave === true,
    failNextMe: false,
    requests: [],
    problemResponses: []
  };
  await page.route('**/api/v1/auth/refresh', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(tokenResponse())
  }));
  await page.route('**/api/v1/me/locale', async route => {
    const locale = route.request().headers()['accept-language'];
    server.requests.push({ method: 'PUT', locale });
    if (server.failLocaleSave) {
      await route.fulfill({
        status: 409,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          status: 409,
          code: 'identity.profile_version_conflict',
          title: locale === 'en-US' ? 'Profile version conflict' : '资料版本冲突'
        })
      });
      return;
    }

    const body = route.request().postDataJSON();
    server.preferredLocale = body.locale;
    server.profileVersion++;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        preferredLocale: server.preferredLocale,
        profileVersion: server.profileVersion
      })
    });
  });
  await page.route('**/api/v1/me', async route => {
    const locale = route.request().headers()['accept-language'];
    server.requests.push({ method: 'GET', locale });
    if (server.failNextMe) {
      server.failNextMe = false;
      const problem = {
        status: 403,
        code: 'authorization.denied',
        locale
      };
      server.problemResponses.push(problem);
      await route.fulfill({
        status: problem.status,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          status: problem.status,
          code: problem.code,
          title: locale === 'en-US' ? 'Access denied' : '没有访问权限',
          traceId: `trace-${server.problemResponses.length}`
        })
      });
      return;
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(currentUserResponse(
        server.preferredLocale,
        server.profileVersion,
        options.permissions
      ))
    });
  });
  await page.route('**/api/v1/navigation', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(navigationResponse())
  }));
  await page.route('**/api/v1/tenancy/available', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(availableTenants())
  }));
  await page.route('**/api/v1/platform/host-dashboard-summary', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      activeTenantCount: 0,
      onlineSessionCount: 0,
      todayRequestCount: 0,
      todayErrorRate: 0,
      recentActivities: []
    })
  }));
  await page.route('**/api/v1/tenancy/tenants**', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items: availableTenants(),
      page: 1,
      pageSize: 20,
      total: availableTenants().length
    })
  }));
  return server;
}

const documentAdminPermissions = [
  'identity.navigation.read',
  'platform.dashboard.read',
  'tenancy.tenants.read',
  'tenancy.tenants.switch',
  'document.host_documents.read',
  'document.host_documents.create',
  'document.host_documents.update',
  'document.host_documents.add_version',
  'document.host_documents.delete',
  'document.host_documents.restore',
  'document.host_recycle_bin.read',
  'document.host_recycle_bin.restore',
  'document.host_recycle_bin.purge',
  'document.host_permissions.read',
  'document.host_permissions.set',
  'document.host_shares.read',
  'document.host_shares.create',
  'document.host_shares.update_status',
  'document.host_statistics.read'
];

async function mockDocumentAdminSession(page) {
  const server = await mockAuthenticatedSession(page, {
    permissions: documentAdminPermissions
  });
  await page.route('**/api/v1/navigation', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(documentNavigationResponse())
  }));
  await page.route('**/api/v1/document/**', route => {
    const url = route.request().url();
    if (url.includes('/statistics')) {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          summary: { totalItems: 0, totalVersions: 0, totalSizeKb: 0, totalSizeInfo: '0 B' },
          byType: [],
          byCategory: [],
          shareCount: 0,
          todayAccessCount: 0,
          todayDownloadCount: 0,
          todayCreatedCount: 0,
          recycleBinCount: 0
        })
      });
    }

    if (url.includes('/permissions/by-document/')) {
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([])
      });
    }

    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: [],
        page: 1,
        pageSize: 20,
        total: 0
      })
    });
  });

  return server;
}

function tokenResponse() {
  return {
    accessToken: 'e2e-access-token',
    tokenType: 'Bearer',
    expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString()
  };
}

function currentUserResponse(
  preferredLocale = 'zh-CN',
  profileVersion = 1,
  permissions = null
) {
  return {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
    username: 'admin',
    displayName: '系统管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: true,
    permissions: permissions ?? [
      'identity.navigation.read',
      'platform.dashboard.read',
      'tenancy.tenants.read',
      'tenancy.tenants.switch'
    ],
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    preferredLocale,
    profileVersion
  };
}

function navigationResponse() {
  return [
    {
      id: 'overview', parentId: null, routeName: 'overview', path: '/',
      componentKey: 'overview', title: 'SERVER CONTROLLED TITLE',
      caption: 'SERVER CONTROLLED CAPTION', icon: 'dashboard', order: 10,
      requiredPermission: 'platform.dashboard.read', children: []
    },
    {
      id: 'tenant-context', parentId: null, routeName: 'tenant-context',
      path: '/tenant-context', componentKey: 'tenant-context',
      title: 'SERVER CONTROLLED TITLE', caption: 'SERVER CONTROLLED CAPTION',
      icon: 'building', order: 20,
      requiredPermission: 'tenancy.tenants.read', children: []
    }
  ];
}

function documentNavigationResponse() {
  return [
    ...navigationResponse(),
    {
      id: 'host-document-items', parentId: null, routeName: 'host-document-items',
      path: '/document/host-items', componentKey: 'host-document-items',
      title: 'Host 文档库', caption: 'Host 文档库', icon: 'document', order: 30,
      requiredPermission: 'document.host_documents.read', children: []
    },
    {
      id: 'document-recycle-bin', parentId: null, routeName: 'document-recycle-bin',
      path: '/document/recycle-bin', componentKey: 'document-recycle-bin',
      title: '文档回收站', caption: '文档回收站', icon: 'delete', order: 31,
      requiredPermission: 'document.host_recycle_bin.read', children: []
    },
    {
      id: 'document-shares', parentId: null, routeName: 'document-shares',
      path: '/document/shares', componentKey: 'document-shares',
      title: '文档分享', caption: '文档分享', icon: 'share', order: 32,
      requiredPermission: 'document.host_shares.read', children: []
    },
    {
      id: 'document-permissions', parentId: null, routeName: 'document-permissions',
      path: '/document/permissions', componentKey: 'document-permissions',
      title: '文档权限', caption: '文档权限', icon: 'lock', order: 33,
      requiredPermission: 'document.host_permissions.read', children: []
    },
    {
      id: 'document-statistics', parentId: null, routeName: 'document-statistics',
      path: '/document/statistics', componentKey: 'document-statistics',
      title: '文档统计', caption: '文档统计', icon: 'chart-bar', order: 34,
      requiredPermission: 'document.host_statistics.read', children: []
    }
  ];
}

function availableTenants() {
  return [{
    id: tenantId,
    identifier: 'acme',
    name: 'Acme Corporation',
    domain: 'acme.localhost'
  }];
}
