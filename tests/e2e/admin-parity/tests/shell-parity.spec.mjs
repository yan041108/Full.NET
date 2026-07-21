import { expect, test } from '@playwright/test';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

test.beforeEach(async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
});

test('动态导航和可信租户范围在两套管理端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  await page.goto('/');

  await expect(page).toHaveTitle(/Full\.NET/);
  const navigation = page.getByRole('navigation', { name: '主导航' });
  await expect(navigation).toBeVisible();
  await expect(navigation.getByRole('link', { name: /工作台/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /租户上下文/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /用户管理/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /超级管理员/ })).toBeVisible();
  await expect(page.getByRole('button', { name: '检查会话' })).toBeVisible();
  await expect(page.getByText('Full.NET Host', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('活跃租户', { exact: true })).toBeVisible();
  await expect(page.locator(`[data-client-kind="${clientKind}"]`)).toBeVisible();

  await navigation.getByRole('link', { name: /租户上下文/ }).click();
  await expect(page.getByRole('heading', { name: '租户上下文' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Acme Corporation' })).toBeVisible();
});

test('超级管理员列表、审计与密码重认证授予在两端保持一致', async ({ page }) => {
  await mockAuthenticatedSession(page);
  const grants = [];
  await page.route('**/api/v1/identity/super-administrators/audits?*', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify([{
      id: 'audit-1', targetUserId: 'target-1', actorUserId: 'e2e-user-id',
      eventType: 'identity.super_administrator.granted',
      resultCode: 'identity.super_administrator.granted', succeeded: true,
      occurredAtUtc: '2026-07-18T00:00:00Z'
    }])
  }));
  await page.route('**/api/v1/identity/super-administrators/', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify([{
      userId: 'e2e-user-id', username: 'admin',
      displayName: '系统管理员', isActive: true
    }])
  }));
  await page.route('**/api/v1/identity/super-administrators/grant', async route => {
    grants.push(route.request().postDataJSON());
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ targetUserId: 'target-1', changed: true })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /超级管理员/ }).click();
  await expect(page.getByRole('heading', { name: '超级管理员', exact: true })).toBeVisible();
  await expect(page.getByText('系统管理员', { exact: true }).first()).toBeVisible();
  await expect(page.getByText('identity.super_administrator.granted', { exact: true })).toBeVisible();
  await page.getByLabel('Host 账号', { exact: true }).fill('target-admin');
  await page.getByLabel('当前密码', { exact: true }).fill('FullNet!2026Secure');
  await page.getByRole('button', { name: '确认授予' }).click();
  await expect.poll(() => grants).toEqual([{
    username: 'target-admin',
    currentPassword: 'FullNet!2026Secure'
  }]);
  await expect(page.getByLabel('当前密码', { exact: true })).toHaveValue('');
});

test('用户列表、创建与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const userId = 'e2e-host-user-id';
  const state = { hasUser: false, disabled: false };
  const listBody = () => {
    if (!state.hasUser) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: userId,
        username: 'parity-user',
        displayName: '对等用户',
        isActive: !state.disabled,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: state.disabled ? '2026-07-21T01:00:00Z' : null,
        version: state.disabled ? 2 : 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/identity/users?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
  }));
  await page.route('**/api/v1/identity/users', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasUser = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: userId,
        username: 'parity-user',
        displayName: '对等用户',
        isActive: true,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      })
    });
  });
  await page.route(`**/api/v1/identity/users/${userId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: userId,
        username: 'parity-user',
        displayName: '对等用户',
        isActive: false,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: '2026-07-21T01:00:00Z',
        version: 2
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /用户管理/ }).click();
  await expect(page.getByRole('heading', { name: '用户管理', exact: true })).toBeVisible();
  await expect(page.getByText('尚无 Host 用户', { exact: true })).toBeVisible();

  await page.getByLabel('用户名', { exact: true }).fill('parity-user');
  await page.getByLabel('显示名称', { exact: true }).fill('对等用户');
  await page.getByLabel('初始密码', { exact: true }).fill('FullNet!2026Secure');
  await page.getByRole('button', { name: '创建用户' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      username: 'parity-user',
      displayName: '对等用户',
      password: 'FullNet!2026Secure'
    }
  }]);
  await expect(page.getByText('对等用户', { exact: true }).first()).toBeVisible();

  await page.getByRole('article').getByRole('button', { name: '禁用' }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '禁用', exact: true })
      .evaluate(button => button.click());
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  await expect.poll(() => operations.some(operation => operation.type === 'disable')).toBe(true);
  await expect(page.getByText('已禁用', { exact: true })).toBeVisible();
});

test('进入租户、刷新恢复并返回 Host 的闭环等价', async ({ page }) => {
  const state = await mockAuthenticatedSession(page, { mutableContext: true });
  await page.goto('/');
  await page.getByRole('link', { name: /租户上下文/ }).click();

  await page.getByRole('button', { name: '进入租户' }).click();
  await expect.poll(() => state.tenantId).toBe(tenantId);
  await expect(page.getByText('Acme Corporation', { exact: true }).first())
    .toBeVisible();

  await page.reload();
  await expect(page.getByText('Acme Corporation', { exact: true }).first())
    .toBeVisible();
  await page.getByRole('link', { name: /租户上下文/ }).click();
  await page.getByRole('button', { name: '返回 Host' }).click();

  await expect.poll(() => state.tenantId).toBeNull();
  await expect(page.getByText('Full.NET Host', { exact: true }).first())
    .toBeVisible();
});

test('403 状态页在两套管理端保持相同关键语义', async ({ page }) => {
  await mockAuthenticatedSession(page);
  await page.goto('/#/403');

  await expect(page.getByText('403', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: '没有访问权限' })).toBeVisible();
});

test('ProblemDetails 错误码和 TraceId 在两套管理端一致呈现', async ({ page }) => {
  await mockAuthenticatedSession(page, { probeDenied: true });
  await page.goto('/');
  await page.getByTestId('load-current-user').click();

  await expect(page.getByTestId('error-code'))
    .toHaveText('authorization.permission_denied');
  await expect(page.getByTestId('trace-id')).toHaveText('trace-admin-parity');
});

test('未知组件键拒绝整个授权快照', async ({ page }) => {
  await mockAuthenticatedSession(page, { unknownComponent: true });
  await page.goto('/');

  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeHidden();
});

test('刷新失败后可登录、进入动态控制台并安全退出', async ({ page }) => {
  await page.route('**/api/v1/auth/refresh', route => route.fulfill({
    status: 401,
    contentType: 'application/problem+json',
    body: JSON.stringify({
      status: 401,
      code: 'identity.refresh_missing',
      title: '刷新会话不存在'
    })
  }));
  await page.route('**/api/v1/auth/login', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(tokenResponse())
  }));
  await page.route('**/api/v1/auth/logout', route => route.fulfill({ status: 204 }));
  await page.route('**/api/v1/me', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(currentUserResponse())
  }));
  await mockSnapshotEndpoints(page);

  await page.goto('/');
  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
  await page.getByLabel('账号', { exact: true }).fill('admin');
  await page.getByLabel('密码', { exact: true }).fill('FullNet!2026Secure');
  await page.getByRole('button', { name: '进入控制台' }).click();

  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible();
  await expect(page.getByRole('link', { name: /租户上下文/ })).toBeVisible();
  await expect(page.getByText('系统管理员', { exact: true }).first()).toBeVisible();
  await page.getByRole('button', { name: '退出登录' }).click();
  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
});

async function mockAuthenticatedSession(page, options = {}) {
  const state = { tenantId: null };
  let meCalls = 0;
  await page.route('**/api/v1/auth/refresh', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(tokenResponse(
      state.tenantId ? 'tenant-refresh-token' : 'host-refresh-token'
    ))
  }));
  await page.route('**/api/v1/me', route => {
    meCalls += 1;
    if (options.probeDenied && meCalls > 1) {
      return route.fulfill({
        status: 403,
        contentType: 'application/problem+json',
        body: JSON.stringify({
          status: 403,
          code: 'authorization.permission_denied',
          title: '没有访问权限',
          traceId: 'trace-admin-parity'
        })
      });
    }

    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(currentUserResponse(state.tenantId))
    });
  });
  await mockSnapshotEndpoints(page, options);

  if (options.mutableContext) {
    await page.route('**/api/v1/tenancy/context', async route => {
      const body = route.request().postDataJSON();
      state.tenantId = body.tenantId;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(contextTokenResponse(state.tenantId))
      });
    });
  }

  return state;
}

async function mockSnapshotEndpoints(page, options = {}) {
  await page.route('**/api/v1/navigation', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(navigationResponse(options.unknownComponent))
  }));
  await page.route('**/api/v1/tenancy/available', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(availableTenants())
  }));
}

function tokenResponse(accessToken = 'e2e-access-token') {
  return {
    accessToken,
    tokenType: 'Bearer',
    expiresAtUtc: '2026-07-17T04:00:00Z'
  };
}

function contextTokenResponse(activeTenantId) {
  if (!activeTenantId) {
    return {
      ...tokenResponse('host-context-token'),
      context: {
        tenantId: null,
        identifier: 'host',
        name: 'Full.NET Host',
        scope: 'host'
      }
    };
  }

  return {
    ...tokenResponse('tenant-context-token'),
    context: {
      tenantId: activeTenantId,
      identifier: 'acme',
      name: 'Acme Corporation',
      scope: `tenant:${activeTenantId.replaceAll('-', '')}`
    }
  };
}

function currentUserResponse(activeTenantId = null) {
  return {
    id: 'e2e-user-id',
    username: 'admin',
    displayName: '系统管理员',
    tenantId: activeTenantId,
    actorScope: 'host',
    isSuperAdministrator: true,
    scope: activeTenantId
      ? `tenant:${activeTenantId.replaceAll('-', '')}`
      : 'host',
    permissions: [
      'identity.navigation.read',
      'identity.super_administrators.manage',
      'identity.super_administrators.read',
      'identity.users.read',
      'identity.users.write',
      'platform.dashboard.read',
      'tenancy.tenants.read',
      'tenancy.tenants.switch'
    ],
    sessionId: 'e2e-session-id',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
}

function navigationResponse(unknownComponent = false) {
  return [
    {
      id: 'overview', parentId: null, routeName: 'overview', path: '/',
      componentKey: unknownComponent ? 'remote-script' : 'overview',
      title: '工作台', caption: '平台运行概览', icon: 'dashboard', order: 10,
      requiredPermission: 'platform.dashboard.read', children: []
    },
    {
      id: 'tenant-context', parentId: null, routeName: 'tenant-context',
      path: '/tenant-context', componentKey: 'tenant-context',
      title: '租户上下文', caption: '进入租户或返回 Host', icon: 'building',
      order: 20, requiredPermission: 'tenancy.tenants.read', children: []
    },
    {
      id: 'users', parentId: null, routeName: 'users', path: '/identity/users',
      componentKey: 'users', title: '用户管理', caption: 'Host 作用域账号',
      icon: 'users', order: 35, requiredPermission: 'identity.users.read', children: []
    },
    {
      id: 'super-administrators', parentId: null,
      routeName: 'super-administrators', path: '/identity/super-administrators',
      componentKey: 'super-administrators', title: '超级管理员',
      caption: '受保护的 Host 最高权限账号', icon: 'shield', order: 40,
      requiredPermission: 'identity.super_administrators.read', children: []
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
