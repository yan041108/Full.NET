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
  const locale = page.locator('select[name="locale"]:visible');

  await locale.selectOption('en-US');

  await expect(page.locator('html')).toHaveAttribute('lang', 'en-US');
  await expect(page).toHaveTitle('Overview · Full.NET');
  await expect(page.getByRole('navigation', { name: 'Main navigation' }))
    .toBeVisible();
  await expect(page.getByRole('heading', {
    name: 'Good morning, administrator'
  })).toBeVisible();
  if (testInfo.project.name === 'vue-admin') {
    await expect(page.locator('.admin-shell'))
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
  await expect(page.locator('select[name="locale"]:visible'))
    .toHaveValue('en-US');
  expect(server.requests.some(request =>
    request.method === 'GET' && request.locale === 'en-US'
  )).toBe(true);
  expect(server.requests.find(request => request.method === 'PUT')?.locale)
    .toBe('zh-CN');
});

test('语言偏好保存失败时保留登录、租户和原语言', async ({ page }) => {
  await mockAuthenticatedSession(page, { failLocaleSave: true });
  await page.goto('/');
  const selector = page.locator('select[name="locale"]:visible');

  await selector.selectOption('en-US');

  await expect(selector).toHaveValue('zh-CN');
  await expect(page.locator('html')).toHaveAttribute('lang', 'zh-CN');
  await expect(page.locator('[data-session-shell], .admin-shell')).toBeVisible();
  await expect(page.getByText('Full.NET Host').first()).toBeVisible();
  const alert = page.getByRole('alert').filter({
    hasText: '语言偏好保存失败，已保留原语言'
  });
  await expect(alert).toBeVisible();
  const alertBox = await alert.boundingBox();
  expect(alertBox?.width).toBeGreaterThan(100);
  expect(alertBox?.height).toBeGreaterThan(16);
});

test('切换语言不改变服务端 403 状态和稳定错误码', async ({ page }) => {
  const server = await mockAuthenticatedSession(page);
  await page.goto('/');
  await expect(page.getByRole('heading', { name: '早上好，系统管理员' }))
    .toBeVisible();

  server.failNextMe = true;
  await page.getByTestId('load-current-user').click();
  await expect(page.getByTestId('error-code')).toHaveText('authorization.denied');

  await page.locator('select[name="locale"]:visible').selectOption('en-US');
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

  await page.getByRole('link', { name: /租户上下文/ }).click();

  await expect(page.getByRole('heading', { name: '租户上下文' }))
    .toBeFocused();
});

test('320 CSS px 下不产生页面级水平溢出', async ({ page }) => {
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
  await expect(page.locator('select[name="locale"]:visible')).toBeVisible();
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
        server.profileVersion
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
  return server;
}

function tokenResponse() {
  return {
    accessToken: 'e2e-access-token',
    tokenType: 'Bearer',
    expiresAtUtc: '2026-07-17T04:00:00Z'
  };
}

function currentUserResponse(preferredLocale = 'zh-CN', profileVersion = 1) {
  return {
    id: 'e2e-user-id',
    username: 'admin',
    displayName: '系统管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: true,
    permissions: [
      'identity.navigation.read',
      'platform.dashboard.read',
      'tenancy.tenants.read',
      'tenancy.tenants.switch'
    ],
    sessionId: 'e2e-session-id',
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

function availableTenants() {
  return [{
    id: tenantId,
    identifier: 'acme',
    name: 'Acme Corporation',
    domain: 'acme.localhost'
  }];
}
