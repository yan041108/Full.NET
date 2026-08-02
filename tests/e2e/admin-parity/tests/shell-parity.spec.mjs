import { expect, test } from '@playwright/test';

const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

async function expectShellHostContextVisible(page, testInfo) {
  if (testInfo.project.name === 'vue-admin') {
    await page.getByRole('button', { name: '系统管理员' }).click();
    await expect(page.getByTestId('shell-tenant-select')).toBeVisible();
    await expect(page.getByTestId('shell-tenant-select')).toContainText('Full.NET Host');
    await page.keyboard.press('Escape');
    return;
  }

  await expect(page.getByText('Full.NET Host', { exact: true }).first()).toBeVisible();
}

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
  await expect(navigation.getByRole('link', { name: /角色管理/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /菜单管理/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /超级管理员/ })).toBeVisible();
  await expect(page.getByRole('button', { name: '检查会话' })).toBeVisible();
  await expectShellHostContextVisible(page, testInfo);
  await expect(page.getByText('活跃租户', { exact: true })).toBeVisible();
  await expect(page.locator(`[data-client-kind="${clientKind}"]`)).toBeVisible();

  await navigation.getByRole('link', { name: /租户上下文/ }).click();
  await expect(page.getByRole('heading', { name: '租户上下文' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Acme Corporation' })).toBeVisible();
});

test('超级管理员列表、审计与密码重认证授予在两端保持一致', async ({ page }) => {
  await mockAuthenticatedSession(page);
  const grants = [];
  await page.route('**/api/v1/identity/me/mfa/totp**', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ isEnrolled: false, isEnabled: false })
  }));
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
  await expect(page.getByRole('heading', { name: '本账号 TOTP', exact: true })).toBeVisible();
  await page.getByLabel('Host 账号', { exact: true }).fill('target-admin');
  await page.getByLabel('当前密码', { exact: true }).fill('FullNet!2026Secure');
  await page.locator('[data-super-admin-grant-form] [name="totpCode"], .grant-strip input[maxlength="6"]').first().fill('123456');
  await page.getByRole('button', { name: '确认授予' }).click();
  await expect.poll(() => grants).toEqual([{
    username: 'target-admin',
    currentPassword: 'FullNet!2026Secure',
    totpCode: '123456'
  }]);
  await expect(page.getByLabel('当前密码', { exact: true })).toHaveValue('');
});

test('用户列表、创建与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const userId = 'e2e-host-user-id';
  const roleId = 'e2e-assignable-role-id';
  const state = { hasUser: false, disabled: false, roleIds: [] };
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
  await page.route(`**/api/v1/identity/users/${userId}/enable`, async route => {
    operations.push({ type: 'enable' });
    state.disabled = false;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: userId,
        username: 'parity-user',
        displayName: '对等用户',
        isActive: true,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: '2026-07-21T02:00:00Z',
        version: 4
      })
    });
  });
  await page.route(`**/api/v1/identity/users/${userId}/reset-password`, async route => {
    operations.push({ type: 'reset-password', body: route.request().postDataJSON() });
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: userId,
        username: 'parity-user',
        displayName: '对等用户',
        isActive: true,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: '2026-07-21T01:00:00Z',
        version: 3
      })
    });
  });
  await page.route('**/api/v1/identity/roles?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items: [{
        id: roleId,
        code: 'parity-role',
        name: '对等角色',
        isSystem: false,
        isActive: true,
        isSuperAdministrator: false,
        permissionCodes: [],
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    })
  }));
  await page.route(`**/api/v1/identity/users/${userId}/roles`, async route => {
    if (route.request().method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          userId,
          roleIds: state.roleIds,
          version: state.disabled ? 2 : 1
        })
      });
      return;
    }

    if (route.request().method() === 'PUT') {
      operations.push({ type: 'roles', body: route.request().postDataJSON() });
      state.roleIds = route.request().postDataJSON().roleIds ?? [];
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          userId,
          roleIds: state.roleIds,
          version: 2
        })
      });
      return;
    }

    await route.fallback();
  });

  await page.goto('/');
  await page.getByRole('link', { name: /用户管理/ }).click();
  await expect(page.getByRole('heading', { name: '用户管理', exact: true })).toBeVisible();
  await expect(page.getByText('尚无 Host 用户', { exact: true })).toBeVisible();

  const usersView = routeView(page, clientKind, 'users', '.users-view');
  await usersView.getByLabel('用户名', { exact: true }).fill('parity-user');
  await usersView.getByLabel('显示名称', { exact: true }).fill('对等用户');
  await usersView.getByLabel('初始密码', { exact: true }).fill('FullNet!2026Secure');
  await usersView.getByRole('button', { name: '创建用户' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      username: 'parity-user',
      displayName: '对等用户',
      password: 'FullNet!2026Secure'
    }
  }]);
  await expect(page.getByText('对等用户', { exact: true }).first()).toBeVisible();

  await page.getByRole('article').getByRole('button', { name: '角色' }).click();
  if (clientKind === 'vue') {
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByRole('checkbox').first().check();
    await dialog.getByRole('button', { name: '保存角色', exact: true }).click();
  } else {
    await page.locator('.layui-layer-content input[type="checkbox"]').first().check();
    await page.locator('.layui-layer-btn0').click();
  }
  await expect.poll(() => operations.some(operation => operation.type === 'roles')).toBe(true);

  await page.getByRole('article').getByRole('button', { name: '重置密码' }).click();
  if (clientKind === 'vue') {
    const passwordBox = page.locator('.el-message-box').last();
    await expect(passwordBox.locator('input')).toBeVisible();
    await passwordBox.locator('input').fill('FullNet!2026Rotate');
    await passwordBox.locator('input').press('Enter');
    await expect(passwordBox).toBeHidden();
  } else {
    const passwordLayer = page.locator('.layui-layer').last();
    await expect(passwordLayer.locator('.layui-layer-input')).toBeVisible();
    await passwordLayer.locator('.layui-layer-input').fill('FullNet!2026Rotate');
    await passwordLayer.locator('.layui-layer-btn0').click({ force: true });
  }
  await expect.poll(() => operations.filter(operation => operation.type === 'reset-password')).toEqual([{
    type: 'reset-password',
    body: { password: 'FullNet!2026Rotate' }
  }]);

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

  await page.getByRole('article').getByRole('button', { name: '启用' }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '启用', exact: true })
      .evaluate(button => button.click());
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  await expect.poll(() => operations.some(operation => operation.type === 'enable')).toBe(true);
  await expect(page.getByText('有效', { exact: true })).toBeVisible();
});

test('租户列表、开通与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf295';
  const state = { hasTenant: false, disabled: false };
  const listBody = () => {
    if (!state.hasTenant) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: tenantId,
        identifier: 'parity',
        name: '对等租户',
        domain: 'parity.localhost',
        isActive: !state.disabled,
        version: state.disabled ? 2 : 1,
        defaultLocale: 'zh-CN'
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/tenancy/tenants?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
  }));
  await page.route('**/api/v1/tenancy/tenant-packages?page=1&pageSize=100', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ items: [], page: 1, pageSize: 100, total: 0 })
  }));
  await page.route('**/api/v1/tenancy/tenants', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasTenant = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: tenantId,
        identifier: 'parity',
        name: '对等租户',
        domain: 'parity.localhost',
        isActive: true,
        version: 1,
        defaultLocale: 'zh-CN'
      })
    });
  });
  await page.route(`**/api/v1/tenancy/tenants/${tenantId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: tenantId,
        identifier: 'parity',
        name: '对等租户',
        domain: 'parity.localhost',
        isActive: false,
        version: 2,
        defaultLocale: 'zh-CN'
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /租户管理/ }).click();
  await expect(page.getByRole('heading', { name: '租户管理', exact: true })).toBeVisible();
  await expect(page.getByText('尚无租户', { exact: true })).toBeVisible();

  const tenantsView = routeView(page, clientKind, 'tenants', '.tenants-view');
  await tenantsView.getByLabel('租户标识', { exact: true }).fill('parity');
  await tenantsView.getByLabel('显示名称', { exact: true }).fill('对等租户');
  await tenantsView.getByLabel('访问域名', { exact: true }).fill('parity.localhost');
  await tenantsView.getByRole('button', { name: '开通租户' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      identifier: 'parity',
      name: '对等租户',
      domain: 'parity.localhost'
    }
  }]);
  await expect(page.getByText('对等租户', { exact: true }).first()).toBeVisible();

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

test('租户开通可选套餐在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf296';
  const packageId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';

  await page.route('**/api/v1/tenancy/tenant-packages?page=1&pageSize=100', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items: [{
        id: packageId,
        code: 'standard',
        name: '标准套餐',
        description: null,
        isActive: true,
        version: 1,
        assignedTenantCount: 0
      }],
      page: 1,
      pageSize: 100,
      total: 1
    })
  }));
  await page.route('**/api/v1/tenancy/tenants?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 })
  }));
  await page.route('**/api/v1/tenancy/tenants', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: tenantId,
        identifier: 'pkg-tenant',
        name: '套餐租户',
        domain: 'pkg-tenant.localhost',
        isActive: true,
        version: 1,
        defaultLocale: 'zh-CN',
        tenantPackageId: packageId,
        tenantPackageCode: 'standard',
        tenantPackageName: '标准套餐'
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /租户管理/ }).click();
  const tenantsView = routeView(page, clientKind, 'tenants', '.tenants-view');
  await tenantsView.getByLabel('租户标识', { exact: true }).fill('pkg-tenant');
  await tenantsView.getByLabel('显示名称', { exact: true }).fill('套餐租户');
  await tenantsView.getByLabel('访问域名', { exact: true }).fill('pkg-tenant.localhost');
  if (clientKind === 'vue') {
    await tenantsView.locator('.art-form-card .el-select').click();
    await page.getByRole('option', { name: '标准套餐' }).click();
  } else {
    await tenantsView.locator('select[name="tenantPackageId"]').selectOption(packageId);
  }
  await tenantsView.getByRole('button', { name: '开通租户' }).click();
  await expect.poll(() => operations).toEqual([{
    type: 'create',
    body: {
      identifier: 'pkg-tenant',
      name: '套餐租户',
      domain: 'pkg-tenant.localhost',
      tenantPackageId: packageId
    }
  }]);
});

test('租户列表内分配套餐在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const tenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
  const packageId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
  const state = { assignedPackageId: null };
  const listBody = () => JSON.stringify({
    items: [{
      id: tenantId,
      identifier: 'assign-pkg',
      name: '分配套餐租户',
      domain: 'assign-pkg.localhost',
      isActive: true,
      version: state.assignedPackageId ? 2 : 1,
      defaultLocale: 'zh-CN',
      tenantPackageId: state.assignedPackageId,
      tenantPackageCode: state.assignedPackageId ? 'standard' : null,
      tenantPackageName: state.assignedPackageId ? '标准套餐' : null
    }],
    page: 1,
    pageSize: 20,
    total: 1
  });

  await page.route('**/api/v1/tenancy/tenants?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
  }));
  await page.route('**/api/v1/tenancy/tenant-packages?page=1&pageSize=100', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items: [{
        id: packageId,
        code: 'standard',
        name: '标准套餐',
        description: null,
        isActive: true,
        version: 1,
        assignedTenantCount: 0
      }],
      page: 1,
      pageSize: 100,
      total: 1
    })
  }));
  await page.route(`**/api/v1/tenancy/tenants/${tenantId}/package`, async route => {
    const body = route.request().postDataJSON();
    operations.push({ type: 'assign', body });
    state.assignedPackageId = body.tenantPackageId;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: tenantId,
        identifier: 'assign-pkg',
        name: '分配套餐租户',
        domain: 'assign-pkg.localhost',
        isActive: true,
        version: 2,
        defaultLocale: 'zh-CN',
        tenantPackageId: packageId,
        tenantPackageCode: 'standard',
        tenantPackageName: '标准套餐'
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /租户管理/ }).click();
  const tenantsView = routeView(page, clientKind, 'tenants', '.tenants-view');
  await expect(tenantsView.getByText('分配套餐租户', { exact: true })).toBeVisible();

  if (clientKind === 'vue') {
    await tenantsView.locator('article .el-select').click();
    await page.getByRole('option', { name: '标准套餐' }).click();
  } else {
    await tenantsView.locator(`select[data-tenants-package="${tenantId}"]`).selectOption(packageId);
  }
  await expect.poll(() => operations).toEqual([{
    type: 'assign',
    body: { tenantPackageId: packageId, version: 1 }
  }]);
  await expect(tenantsView.getByText('套餐: 标准套餐', { exact: true })).toBeVisible();
});

test('套餐列表、创建与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const packageId = '019bc2b1-2a40-7cc3-8992-a80de51bf297';
  const state = { hasPackage: false, disabled: false };
  const listBody = () => {
    if (!state.hasPackage) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: packageId,
        code: 'parity',
        name: '对等套餐',
        description: '说明',
        isActive: !state.disabled,
        version: state.disabled ? 2 : 1,
        assignedTenantCount: 0
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/tenancy/tenant-packages?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
  }));
  await page.route('**/api/v1/tenancy/tenant-packages', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasPackage = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: packageId,
        code: 'parity',
        name: '对等套餐',
        description: '说明',
        isActive: true,
        version: 1,
        assignedTenantCount: 0
      })
    });
  });
  await page.route(`**/api/v1/tenancy/tenant-packages/${packageId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: packageId,
        code: 'parity',
        name: '对等套餐',
        description: '说明',
        isActive: false,
        version: 2,
        assignedTenantCount: 0
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /租户套餐/ }).click();
  await expect(page.getByRole('heading', { name: '租户套餐', exact: true })).toBeVisible();
  await expect(page.getByText('尚无套餐', { exact: true })).toBeVisible();

  const packagesView = routeView(page, clientKind, 'tenant-packages', '.tenant-packages-view');
  await packagesView.getByLabel('套餐编码', { exact: true }).fill('parity');
  await packagesView.getByLabel('显示名称', { exact: true }).fill('对等套餐');
  await packagesView.getByLabel('说明', { exact: true }).fill('说明');
  await packagesView.getByRole('button', { name: '创建套餐' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      code: 'parity',
      name: '对等套餐',
      description: '说明'
    }
  }]);
  await expect(page.getByText('对等套餐', { exact: true }).first()).toBeVisible();

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

test('套餐仍被引用时禁用失败在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const packageId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';

  await page.route('**/api/v1/tenancy/tenant-packages?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items: [{
        id: packageId,
        code: 'bound',
        name: '绑定套餐',
        description: null,
        isActive: true,
        version: 1,
        assignedTenantCount: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    })
  }));
  await page.route(`**/api/v1/tenancy/tenant-packages/${packageId}/disable`, async route => {
    await route.fulfill({
      status: 422,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        status: 422,
        code: 'tenancy.tenant_package.in_use',
        title: '仍有租户绑定该套餐，请先解除绑定后再禁用。'
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /租户套餐/ }).click();
  const packagesView = routeView(page, clientKind, 'tenant-packages', '.tenant-packages-view');
  await expect(packagesView.getByText('绑定租户: 1', { exact: true })).toBeVisible();

  await packagesView.getByRole('article').getByRole('button', { name: '禁用' }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '禁用', exact: true })
      .evaluate(button => button.click());
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  await expect(packagesView.getByText('tenancy.tenant_package.in_use', { exact: true }))
    .toBeVisible();
});

test('字典类型列表、创建与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const dictTypeId = '019bc2b1-2a40-7cc3-8992-a80de51bf29a';
  const state = { hasDictType: false, disabled: false };
  const listBody = () => {
    if (!state.hasDictType) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: dictTypeId,
        code: 'parity_status',
        name: '对等状态',
        description: '说明',
        displayOrder: 10,
        isActive: !state.disabled,
        version: state.disabled ? 2 : 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/settings/dict-types?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
  }));
  await page.route('**/api/v1/settings/dict-types', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasDictType = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: dictTypeId,
        code: 'parity_status',
        name: '对等状态',
        description: '说明',
        displayOrder: 10,
        isActive: true,
        version: 1
      })
    });
  });
  await page.route(`**/api/v1/settings/dict-types/${dictTypeId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: dictTypeId,
        code: 'parity_status',
        name: '对等状态',
        description: '说明',
        displayOrder: 10,
        isActive: false,
        version: 2
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /数据字典/ }).click();
  await expect(page.getByRole('heading', { name: '数据字典', exact: true })).toBeVisible();
  await expect(page.getByText('尚无字典类型', { exact: true })).toBeVisible();

  const dictTypesView = routeView(page, clientKind, 'dict-types', '.dict-types-view');
  await dictTypesView.getByLabel('字典编码', { exact: true }).fill('parity_status');
  await dictTypesView.getByLabel('显示名称', { exact: true }).fill('对等状态');
  await dictTypesView.getByLabel('说明', { exact: true }).fill('说明');
  await dictTypesView.getByLabel('显示顺序', { exact: true }).first().fill('10');
  await dictTypesView.getByRole('button', { name: '创建字典类型' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      code: 'parity_status',
      name: '对等状态',
      description: '说明',
      displayOrder: 10
    }
  }]);
  await expect(page.getByText('对等状态', { exact: true }).first()).toBeVisible();

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

test('字典项列表、创建与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const dictTypeId = '019bc2b1-2a40-7cc3-8992-a80de51bf29b';
  const dictItemId = '019bc2b1-2a40-7cc3-8992-a80de51bf29c';
  const state = { hasItem: false, disabled: false };
  const itemsBody = () => {
    if (!state.hasItem) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: dictItemId,
        dictTypeId,
        label: '对等项',
        value: 'parity_item',
        color: '#409eff',
        displayOrder: 5,
        isActive: !state.disabled,
        version: state.disabled ? 2 : 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/settings/dict-types?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items: [{
        id: dictTypeId,
        code: 'parity_enum',
        name: '对等枚举',
        description: null,
        displayOrder: 1,
        isActive: true,
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    })
  }));
  await page.route(`**/api/v1/settings/dict-types/${dictTypeId}/items?page=1&pageSize=20`, route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: itemsBody()
  }));
  await page.route(`**/api/v1/settings/dict-types/${dictTypeId}/items`, async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasItem = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: dictItemId,
        dictTypeId,
        label: '对等项',
        value: 'parity_item',
        color: '#409eff',
        displayOrder: 5,
        isActive: true,
        version: 1
      })
    });
  });
  await page.route(`**/api/v1/settings/dict-items/${dictItemId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: dictItemId,
        dictTypeId,
        label: '对等项',
        value: 'parity_item',
        color: '#409eff',
        displayOrder: 5,
        isActive: false,
        version: 2
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /数据字典/ }).click();
  await expect(page.getByRole('heading', { name: '数据字典', exact: true })).toBeVisible();
  await expect(page.getByText('对等枚举', { exact: true })).toBeVisible();

  const dictTypesView = routeView(page, clientKind, 'dict-types', '.dict-types-view');
  await dictTypesView.getByRole('button', { name: '字典项', exact: true }).click();
  const itemsPanel = dictTypesView.locator('[data-dict-items-panel]');
  await expect(itemsPanel).toBeVisible();
  await expect(itemsPanel.getByText('该类型尚无字典项', { exact: true })).toBeVisible();

  await itemsPanel.getByLabel('显示文本', { exact: true }).fill('对等项');
  await itemsPanel.getByLabel('机器值', { exact: true }).fill('parity_item');
  await itemsPanel.getByLabel('颜色', { exact: true }).fill('#409eff');
  await itemsPanel.getByLabel('显示顺序', { exact: true }).fill('5');
  await itemsPanel.getByRole('button', { name: '创建字典项' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      label: '对等项',
      value: 'parity_item',
      color: '#409eff',
      displayOrder: 5
    }
  }]);
  await expect(itemsPanel.getByText('对等项', { exact: true }).first()).toBeVisible();
  await expect(itemsPanel.locator('code', { hasText: 'parity_item' })).toBeVisible();

  await itemsPanel.getByRole('article').getByRole('button', { name: '禁用' }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '禁用', exact: true })
      .evaluate(button => button.click());
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  await expect.poll(() => operations.some(operation => operation.type === 'disable')).toBe(true);
  await expect(itemsPanel.getByText('已禁用', { exact: true })).toBeVisible();
});

test('租户字典类型列表、创建与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page, { initialTenantId: tenantId });
  const operations = [];
  const dictTypeId = '019bc2b1-2a40-7cc3-8992-a80de51bf29d';
  const state = { hasDictType: false, disabled: false };
  const listBody = () => {
    if (!state.hasDictType) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: dictTypeId,
        code: 'parity_tenant_status',
        name: '对等租户状态',
        description: '说明',
        displayOrder: 10,
        isActive: !state.disabled,
        version: state.disabled ? 2 : 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/settings/tenant-dict-types?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
  }));
  await page.route('**/api/v1/settings/tenant-dict-types', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasDictType = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: dictTypeId,
        code: 'parity_tenant_status',
        name: '对等租户状态',
        description: '说明',
        displayOrder: 10,
        isActive: true,
        version: 1
      })
    });
  });
  await page.route(`**/api/v1/settings/tenant-dict-types/${dictTypeId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: dictTypeId,
        code: 'parity_tenant_status',
        name: '对等租户状态',
        description: '说明',
        displayOrder: 10,
        isActive: false,
        version: 2
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /租户数据字典/ }).click();
  await expect(page.getByRole('heading', { name: '数据字典', exact: true })).toBeVisible();
  await expect(page.getByText('尚无字典类型', { exact: true })).toBeVisible();

  const dictTypesView = routeView(page, clientKind, 'tenant-dict-types', '.dict-types-view');
  await dictTypesView.getByLabel('字典编码', { exact: true }).fill('parity_tenant_status');
  await dictTypesView.getByLabel('显示名称', { exact: true }).fill('对等租户状态');
  await dictTypesView.getByLabel('说明', { exact: true }).fill('说明');
  await dictTypesView.getByLabel('显示顺序', { exact: true }).first().fill('10');
  await dictTypesView.getByRole('button', { name: '创建字典类型' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      code: 'parity_tenant_status',
      name: '对等租户状态',
      description: '说明',
      displayOrder: 10
    }
  }]);
  await expect(page.getByText('对等租户状态', { exact: true }).first()).toBeVisible();

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

test('租户字典项列表、创建与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page, { initialTenantId: tenantId });
  const operations = [];
  const dictTypeId = '019bc2b1-2a40-7cc3-8992-a80de51bf29e';
  const dictItemId = '019bc2b1-2a40-7cc3-8992-a80de51bf29f';
  const state = { hasItem: false, disabled: false };
  const itemsBody = () => {
    if (!state.hasItem) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: dictItemId,
        dictTypeId,
        label: '对等租户项',
        value: 'parity_tenant_item',
        color: '#409eff',
        displayOrder: 5,
        isActive: !state.disabled,
        version: state.disabled ? 2 : 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/settings/tenant-dict-types?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items: [{
        id: dictTypeId,
        code: 'parity_tenant_enum',
        name: '对等租户枚举',
        description: null,
        displayOrder: 1,
        isActive: true,
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    })
  }));
  await page.route(`**/api/v1/settings/tenant-dict-types/${dictTypeId}/items?page=1&pageSize=20`, route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: itemsBody()
  }));
  await page.route(`**/api/v1/settings/tenant-dict-types/${dictTypeId}/items`, async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasItem = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: dictItemId,
        dictTypeId,
        label: '对等租户项',
        value: 'parity_tenant_item',
        color: '#409eff',
        displayOrder: 5,
        isActive: true,
        version: 1
      })
    });
  });
  await page.route(`**/api/v1/settings/tenant-dict-items/${dictItemId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: dictItemId,
        dictTypeId,
        label: '对等租户项',
        value: 'parity_tenant_item',
        color: '#409eff',
        displayOrder: 5,
        isActive: false,
        version: 2
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /租户数据字典/ }).click();
  await expect(page.getByRole('heading', { name: '数据字典', exact: true })).toBeVisible();
  await expect(page.getByText('对等租户枚举', { exact: true })).toBeVisible();

  const dictTypesView = routeView(page, clientKind, 'tenant-dict-types', '.dict-types-view');
  await dictTypesView.getByRole('button', { name: '字典项', exact: true }).click();
  const itemsPanel = clientKind === 'layui'
    ? dictTypesView.locator('[data-tenant-dict-items-panel]')
    : dictTypesView.locator('[data-dict-items-panel]');
  await expect(itemsPanel).toBeVisible();
  await expect(itemsPanel.getByText('该类型尚无字典项', { exact: true })).toBeVisible();

  await itemsPanel.getByLabel('显示文本', { exact: true }).fill('对等租户项');
  await itemsPanel.getByLabel('机器值', { exact: true }).fill('parity_tenant_item');
  await itemsPanel.getByLabel('颜色', { exact: true }).fill('#409eff');
  await itemsPanel.getByLabel('显示顺序', { exact: true }).fill('5');
  await itemsPanel.getByRole('button', { name: '创建字典项' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      label: '对等租户项',
      value: 'parity_tenant_item',
      color: '#409eff',
      displayOrder: 5
    }
  }]);
  await expect(itemsPanel.getByText('对等租户项', { exact: true }).first()).toBeVisible();
  await expect(itemsPanel.locator('code', { hasText: 'parity_tenant_item' })).toBeVisible();

  await itemsPanel.getByRole('article').getByRole('button', { name: '禁用' }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '禁用', exact: true })
      .evaluate(button => button.click());
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  await expect.poll(() => operations.some(operation => operation.type === 'disable')).toBe(true);
  await expect(itemsPanel.getByText('已禁用', { exact: true })).toBeVisible();
});

test('系统配置列表、创建与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const configEntryId = '019bc2b1-2a40-7cc3-8992-a80de51bf29d';
  const state = { hasEntry: false, disabled: false };
  const listBody = () => {
    if (!state.hasEntry) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: configEntryId,
        configKey: 'parity.system.title',
        displayName: '对等标题',
        description: '说明',
        valueKind: 'string',
        value: 'Full.NET',
        displayOrder: 10,
        isActive: !state.disabled,
        version: state.disabled ? 2 : 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/settings/config-entries?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
  }));
  await page.route('**/api/v1/settings/config-entries', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasEntry = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: configEntryId,
        configKey: 'parity.system.title',
        displayName: '对等标题',
        description: '说明',
        valueKind: 'string',
        value: 'Full.NET',
        displayOrder: 10,
        isActive: true,
        version: 1
      })
    });
  });
  await page.route(`**/api/v1/settings/config-entries/${configEntryId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: configEntryId,
        configKey: 'parity.system.title',
        displayName: '对等标题',
        description: '说明',
        valueKind: 'string',
        value: 'Full.NET',
        displayOrder: 10,
        isActive: false,
        version: 2
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /系统配置/ }).click();
  await expect(page.getByRole('heading', { name: '系统配置', exact: true })).toBeVisible();
  await expect(page.getByText('尚无系统配置项', { exact: true })).toBeVisible();

  const configEntriesView = routeView(page, clientKind, 'config-entries', '.config-entries-view');
  await configEntriesView.getByLabel('配置键', { exact: true }).fill('parity.system.title');
  await configEntriesView.getByLabel('显示名称', { exact: true }).fill('对等标题');
  await configEntriesView.getByLabel('说明', { exact: true }).fill('说明');
  await configEntriesView.getByLabel('配置值', { exact: true }).fill('Full.NET');
  await configEntriesView.getByLabel('显示顺序', { exact: true }).fill('10');
  await configEntriesView.getByRole('button', { name: '创建配置项' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      configKey: 'parity.system.title',
      displayName: '对等标题',
      description: '说明',
      valueKind: 'string',
      value: 'Full.NET',
      displayOrder: 10
    }
  }]);
  await expect(page.getByText('对等标题', { exact: true }).first()).toBeVisible();

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

test('枚举常量目录列表与详情在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);

  await page.route('**/api/v1/settings/enum-catalogs', route => {
    if (route.request().method() !== 'GET') {
      return route.fallback();
    }
    const url = route.request().url();
    if (url.includes('/settings/enum-catalogs/')
      && !url.endsWith('/settings/enum-catalogs')
      && !url.includes('/settings/enum-catalogs?')) {
      return route.fallback();
    }
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{
        key: 'settings.config_value_kind',
        displayName: '配置值类型',
        description: '说明',
        memberCount: 2
      }])
    });
  });
  await page.route('**/api/v1/settings/enum-catalogs/settings.config_value_kind', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      key: 'settings.config_value_kind',
      displayName: '配置值类型',
      description: '说明',
      members: [
        { code: 'string', label: 'string', displayOrder: 0 },
        { code: 'boolean', label: 'boolean', displayOrder: 1 }
      ]
    })
  }));

  await page.goto('/');
  await page.getByRole('link', { name: /枚举常量/ }).click();
  await expect(page.getByRole('heading', { name: '枚举常量', exact: true })).toBeVisible();

  const enumCatalogsView = routeView(page, clientKind, 'enum-catalogs', '.enum-catalogs-view');
  await expect(enumCatalogsView.getByText('settings.config_value_kind', { exact: true })).toBeVisible();
  await enumCatalogsView.getByRole('button', { name: '查看', exact: true }).click();
  await expect(enumCatalogsView.locator('code', { hasText: 'string' })).toBeVisible();
  await expect(enumCatalogsView.locator('code', { hasText: 'boolean' })).toBeVisible();
});

test('访问日志列表在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);

  await page.route('**/api/v1/auditing/access-logs**', route => {
    if (route.request().method() !== 'GET') {
      return route.fallback();
    }
    // Vue/Layui 均走 cursor 端点；响应用 cursor 页契约而非旧分页字段。
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: [{
          id: '01912345-6789-7abc-8def-0123456789ab',
          occurredAtUtc: '2026-07-25T08:00:00.000Z',
          httpMethod: 'GET',
          requestPath: '/api/v1/settings/enum-catalogs',
          statusCode: 200,
          durationMs: 12,
          userId: '01912345-6789-7abc-8def-0123456789ac',
          tenantId: null,
          traceId: 'trace-1',
          clientIpFingerprint: 'abc',
          isAuthenticated: true
        }],
        nextCursor: null,
        hasMore: false
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /访问日志/ }).click();
  await expect(page.getByRole('heading', { name: '访问日志', exact: true })).toBeVisible();

  const accessLogsView = routeView(page, clientKind, 'access-logs', '.access-logs-view');
  await expect(accessLogsView.getByText('/api/v1/settings/enum-catalogs')).toBeVisible();
  await expect(accessLogsView.getByText('已认证', { exact: true })).toBeVisible();
});

test('在线用户列表与强制下线在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const sessionId = '01912345-6789-7abc-8def-0123456789af';
  const userId = '01912345-6789-7abc-8def-0123456789ac';

  await page.route('**/api/v1/identity/online-sessions**', async route => {
    if (route.request().method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [{
            id: sessionId,
            userId,
            username: 'parity-user',
            displayName: '对等用户',
            clientId: 'fullnet-admin',
            activeTenantId: null,
            createdAtUtc: '2026-07-26T00:00:00Z',
            expiresAtUtc: '2026-08-26T00:00:00Z'
          }],
          page: 1,
          pageSize: 20,
          total: 1
        })
      });
      return;
    }

    if (route.request().method() === 'POST'
      && route.request().url().includes(`/online-sessions/${sessionId}/revoke`)) {
      operations.push({ type: 'revoke' });
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: sessionId,
          userId,
          username: 'parity-user',
          displayName: '对等用户',
          clientId: 'fullnet-admin',
          activeTenantId: null,
          createdAtUtc: '2026-07-26T00:00:00Z',
          expiresAtUtc: '2026-08-26T00:00:00Z'
        })
      });
      return;
    }

    await route.fallback();
  });

  await page.goto('/');
  await page.getByRole('link', { name: /在线用户/ }).click();
  await expect(page.getByRole('heading', { name: '在线用户', exact: true })).toBeVisible();

  const onlineSessionsView = routeView(
    page,
    clientKind,
    'online-sessions',
    '.online-sessions-view'
  );
  await expect(onlineSessionsView.getByText('parity-user', { exact: true })).toBeVisible();

  await page.getByRole('article').getByRole('button', { name: '强制下线' }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '强制下线', exact: true })
      .evaluate(button => button.click());
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  await expect.poll(() => operations.some(operation => operation.type === 'revoke')).toBe(true);
});

test('API Key 创建、轮换、一次性明文与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const keyId = '01912345-6789-7abc-8def-0123456789b8';
  const rotatedKeyId = '01912345-6789-7abc-8def-0123456789b9';
  const userId = '01912345-6789-7abc-8def-0123456789ac';
  const state = { created: false, rotated: false, activeKeyId: keyId };
  const operations = [];
  const apiKey = (id, isActive, keyPrefix = 'fn_live_parity') => ({
    id,
    userId,
    username: 'parity-automation',
    displayName: '对等流水线',
    keyPrefix,
    permissions: ['platform.dashboard.read'],
    expiresAtUtc: null,
    isActive: isActive,
    lastUsedAtUtc: null,
    createdAtUtc: '2026-07-26T00:00:00Z'
  });
  const listItems = () => {
    if (!state.created) return [];
    if (!state.rotated) {
      return [apiKey(keyId, true)];
    }
    return [
      apiKey(keyId, false),
      apiKey(rotatedKeyId, state.activeKeyId === rotatedKeyId, 'fnk_rotated')
    ];
  };

  await page.route('**/api/v1/identity/api-keys**', async route => {
    const method = route.request().method();
    const url = route.request().url();
    if (method === 'GET') {
      const items = listItems();
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items,
          page: 1,
          pageSize: 20,
          total: items.length
        })
      });
      return;
    }
    if (method === 'POST' && url.endsWith('/api/v1/identity/api-keys')) {
      operations.push({ type: 'create', body: route.request().postDataJSON() });
      state.created = true;
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({ key: apiKey(keyId, true), secret: 'fn_live_once_only' })
      });
      return;
    }
    if (method === 'POST' && url.includes(`/${keyId}/rotate`)) {
      operations.push({ type: 'rotate' });
      state.rotated = true;
      state.activeKeyId = rotatedKeyId;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          key: apiKey(rotatedKeyId, true, 'fnk_rotated'),
          secret: 'fn_live_rotated_once'
        })
      });
      return;
    }
    if (method === 'POST' && url.includes(`/${rotatedKeyId}/disable`)) {
      operations.push({ type: 'disable' });
      state.activeKeyId = keyId;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(apiKey(rotatedKeyId, false, 'fnk_rotated'))
      });
      return;
    }
    await route.fallback();
  });

  await page.goto('/');
  await page.getByRole('link', { name: /API Key/i }).click();
  await expect(page.getByRole('heading', { name: 'API Key', exact: true })).toBeVisible();

  const apiKeysView = routeView(page, clientKind, 'api-keys', '.api-keys-view');
  await apiKeysView.getByLabel('用户 ID').fill(userId);
  await apiKeysView.getByLabel('显示名称').fill('对等流水线');
  await apiKeysView.getByLabel('权限代码').fill(
    'platform.dashboard.read,\nplatform.dashboard.read'
  );
  await apiKeysView.getByRole('button', { name: '创建', exact: true }).click();

  await expect(apiKeysView.getByText('fn_live_once_only', { exact: true })).toBeVisible();
  await expect.poll(() => operations.find(operation => operation.type === 'create')?.body)
    .toEqual({
      userId,
      displayName: '对等流水线',
      permissions: ['platform.dashboard.read'],
      expiresAtUtc: null
    });
  expect(await page.evaluate(secret => ({
    local: Object.values(localStorage).includes(secret),
    session: Object.values(sessionStorage).includes(secret)
  }), 'fn_live_once_only')).toEqual({ local: false, session: false });

  const rowByName = apiKeysView.getByRole('article').filter({ hasText: '对等流水线' });
  await rowByName.filter({ hasText: '有效' }).getByRole('button', { name: '轮换', exact: true }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '轮换', exact: true })
      .evaluate(button => button.click());
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  await expect(apiKeysView.getByText('fn_live_rotated_once', { exact: true })).toBeVisible();
  await expect.poll(() => operations.some(operation => operation.type === 'rotate')).toBe(true);
  expect(await page.evaluate(secret => ({
    local: Object.values(localStorage).includes(secret),
    session: Object.values(sessionStorage).includes(secret)
  }), 'fn_live_rotated_once')).toEqual({ local: false, session: false });

  await rowByName.filter({ hasText: '有效' }).getByRole('button', { name: '禁用', exact: true }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '禁用', exact: true })
      .evaluate(button => button.click());
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  await expect.poll(() => operations.some(operation => operation.type === 'disable')).toBe(true);
  await expect(rowByName.getByText('已禁用', { exact: true }).first()).toBeVisible();
});

test('Host 文件列表与上传删除在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const fileId = '01912345-6789-7abc-8def-0123456789b0';
  const state = { hasFile: false };

  const listBody = () => {
    if (!state.hasFile) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: fileId,
        originalFileName: 'parity.txt',
        contentType: 'text/plain',
        sizeBytes: 12,
        contentHash: 'a'.repeat(64),
        createdAtUtc: '2026-07-26T00:00:00Z',
        createdByUserId: '01912345-6789-7abc-8def-0123456789ac'
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/files/host-files**', async route => {
    if (route.request().method() === 'GET'
      && route.request().url().includes('page=1')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: listBody()
      });
      return;
    }

    if (route.request().method() === 'POST'
      && !route.request().url().includes('/delete')) {
      operations.push({ type: 'upload' });
      state.hasFile = true;
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({
          id: fileId,
          originalFileName: 'parity.txt',
          contentType: 'text/plain',
          sizeBytes: 12,
          contentHash: 'a'.repeat(64),
          createdAtUtc: '2026-07-26T00:00:00Z',
          createdByUserId: '01912345-6789-7abc-8def-0123456789ac'
        })
      });
      return;
    }

    if (route.request().method() === 'POST'
      && route.request().url().includes(`/host-files/${fileId}/delete`)) {
      operations.push({ type: 'delete' });
      state.hasFile = false;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: fileId,
          originalFileName: 'parity.txt',
          contentType: 'text/plain',
          sizeBytes: 12,
          contentHash: 'a'.repeat(64),
          createdAtUtc: '2026-07-26T00:00:00Z',
          createdByUserId: '01912345-6789-7abc-8def-0123456789ac'
        })
      });
      return;
    }

    await route.fallback();
  });

  await page.goto('/');
  await page.getByRole('link', { name: /文件管理/ }).click();
  await expect(page.getByRole('heading', { name: '文件管理', exact: true })).toBeVisible();
  await expect(page.getByText('尚无文件', { exact: true })).toBeVisible();

  const hostFilesView = routeView(page, clientKind, 'host-files', '.host-files-view');
  await hostFilesView.locator('input[type="file"]').setInputFiles({
    name: 'parity.txt',
    mimeType: 'text/plain',
    buffer: Buffer.from('parity-bytes')
  });
  await hostFilesView.getByRole('button', { name: '上传文件' }).click();
  await expect.poll(() => operations.some(operation => operation.type === 'upload')).toBe(true);
  await expect(hostFilesView.getByText('parity.txt', { exact: true })).toBeVisible();

  await hostFilesView.getByRole('button', { name: '删除' }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '删除', exact: true })
      .evaluate(button => button.click());
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  await expect.poll(() => operations.some(operation => operation.type === 'delete')).toBe(true);
  await expect(page.getByText('尚无文件', { exact: true })).toBeVisible();
});

test('Host 公告列表与创建发布在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const announcementId = '01912345-6789-7abc-8def-0123456789b1';
  const state = { hasItem: false, published: false };

  const listBody = () => {
    if (!state.hasItem) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: announcementId,
        title: 'parity-announcement',
        content: 'parity-content',
        status: state.published ? 'published' : 'draft',
        publishedAtUtc: state.published ? '2026-07-26T00:00:00Z' : null,
        createdAtUtc: '2026-07-26T00:00:00Z',
        updatedAtUtc: null,
        version: state.published ? 2 : 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/notifications/host-announcements**', async route => {
    if (route.request().method() === 'GET'
      && route.request().url().includes('page=1')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: listBody()
      });
      return;
    }

    if (route.request().method() === 'POST'
      && !route.request().url().includes('/publish')) {
      operations.push({ type: 'create' });
      state.hasItem = true;
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({
          id: announcementId,
          title: 'parity-announcement',
          content: 'parity-content',
          status: 'draft',
          publishedAtUtc: null,
          createdAtUtc: '2026-07-26T00:00:00Z',
          updatedAtUtc: null,
          version: 1
        })
      });
      return;
    }

    if (route.request().method() === 'POST'
      && route.request().url().includes(`/host-announcements/${announcementId}/publish`)) {
      operations.push({ type: 'publish' });
      state.published = true;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: announcementId,
          title: 'parity-announcement',
          content: 'parity-content',
          status: 'published',
          publishedAtUtc: '2026-07-26T00:00:00Z',
          createdAtUtc: '2026-07-26T00:00:00Z',
          updatedAtUtc: '2026-07-26T00:00:00Z',
          version: 2
        })
      });
      return;
    }

    await route.fallback();
  });

  await page.goto('/');
  await page.getByRole('link', { name: /公告管理/ }).click();
  await expect(page.getByRole('heading', { name: '公告管理', exact: true })).toBeVisible();
  await expect(page.getByText('尚无公告', { exact: true })).toBeVisible();

  const announcementsView = routeView(
    page,
    clientKind,
    'host-announcements',
    '.host-announcements-view'
  );
  await announcementsView.locator('input, textarea').first().fill('parity-announcement');
  await announcementsView.locator('textarea').fill('parity-content');
  await announcementsView.getByRole('button', { name: '创建公告' }).click();
  await expect.poll(() => operations.some(operation => operation.type === 'create')).toBe(true);
  await expect(announcementsView.getByText('parity-announcement', { exact: true })).toBeVisible();

  await announcementsView.getByRole('button', { name: '发布' }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '发布', exact: true })
      .evaluate(button => button.click());
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  await expect.poll(() => operations.some(operation => operation.type === 'publish')).toBe(true);
  await expect(announcementsView.getByText('已发布', { exact: true })).toBeVisible();
});

test('消息中心列表与发信在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const messageId = '01912345-6789-7abc-8def-0123456789b2';
  const state = { hasItem: false, unread: 0 };

  const listBody = () => JSON.stringify({
    items: state.hasItem
      ? [{
        id: messageId,
        title: 'parity-inbox',
        content: 'parity-inbox-content',
        status: state.unread > 0 ? 'unread' : 'read',
        readAtUtc: state.unread > 0 ? null : '2026-07-26T00:00:00Z',
        createdAtUtc: '2026-07-26T00:00:00Z',
        createdByUserId: '01912345-6789-7abc-8def-0123456789ac'
      }]
      : [],
    page: 1,
    pageSize: 20,
    total: state.hasItem ? 1 : 0
  });

  await page.route('**/api/v1/notifications/**', async route => {
    const url = route.request().url();
    if (route.request().method() === 'GET' && url.includes('/my-inbox-messages/unread-count')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ unreadCount: state.unread })
      });
      return;
    }

    if (route.request().method() === 'GET' && url.includes('/my-inbox-messages?page=1')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: listBody()
      });
      return;
    }

    if (route.request().method() === 'POST' && url.includes('/host-inbox-messages')) {
      operations.push({ type: 'send' });
      state.hasItem = true;
      state.unread = 1;
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({
          id: messageId,
          title: 'parity-inbox',
          content: 'parity-inbox-content',
          status: 'unread',
          readAtUtc: null,
          createdAtUtc: '2026-07-26T00:00:00Z',
          createdByUserId: '01912345-6789-7abc-8def-0123456789ac'
        })
      });
      return;
    }

    if (route.request().method() === 'POST' && url.includes(`/my-inbox-messages/${messageId}/read`)) {
      operations.push({ type: 'read' });
      state.unread = 0;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: messageId,
          title: 'parity-inbox',
          content: 'parity-inbox-content',
          status: 'read',
          readAtUtc: '2026-07-26T00:00:00Z',
          createdAtUtc: '2026-07-26T00:00:00Z',
          createdByUserId: '01912345-6789-7abc-8def-0123456789ac'
        })
      });
      return;
    }

    await route.fallback();
  });

  await page.goto('/');
  await page.getByRole('link', { name: /消息中心/ }).click();
  await expect(page.getByRole('heading', { name: '消息中心', exact: true })).toBeVisible();
  await expect(page.getByText('暂无站内信', { exact: true })).toBeVisible();

  const inboxView = routeView(page, clientKind, 'inbox-messages', '.inbox-messages-view');
  await inboxView.locator('input').nth(0).fill('01912345-6789-7abc-8def-0123456789ac');
  await inboxView.locator('input').nth(1).fill('parity-inbox');
  await inboxView.locator('textarea').fill('parity-inbox-content');
  await inboxView.getByRole('button', { name: '发送' }).click();
  await expect.poll(() => operations.some(operation => operation.type === 'send')).toBe(true);
  await expect(inboxView.getByText('parity-inbox', { exact: true })).toBeVisible();

  await inboxView.getByRole('button', { name: '标记已读' }).click();
  await expect.poll(() => operations.some(operation => operation.type === 'read')).toBe(true);
  await expect(inboxView.getByText('已读', { exact: true })).toBeVisible();
});

test('任务调度列表在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const definitionId = '01912345-6789-7abc-8def-0123456789af';
  const operations = [];
  const state = { hasItem: false, triggered: false };

  const listBody = () => JSON.stringify({
    items: state.hasItem ? [{
      id: definitionId,
      jobKey: 'jobs.ping',
      displayName: 'parity-job',
      description: 'parity-description',
      isEnabled: true,
      createdAtUtc: '2026-07-26T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    }] : [],
    page: 1,
    pageSize: 20,
    total: state.hasItem ? 1 : 0
  });

  await page.route('**/api/v1/jobs/host-definitions**', async route => {
    if (route.request().method() === 'GET' && route.request().url().includes('page=1')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: listBody()
      });
      return;
    }

    if (route.request().method() === 'POST' && !route.request().url().includes('/trigger')) {
      operations.push({ type: 'create' });
      state.hasItem = true;
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({
          id: definitionId,
          jobKey: 'jobs.ping',
          displayName: 'parity-job',
          description: 'parity-description',
          isEnabled: true,
          createdAtUtc: '2026-07-26T00:00:00Z',
          updatedAtUtc: null,
          version: 1
        })
      });
      return;
    }

    if (route.request().method() === 'POST'
      && route.request().url().includes(`/host-definitions/${definitionId}/trigger`)) {
      operations.push({ type: 'trigger' });
      state.triggered = true;
      await route.fulfill({
        status: 201,
        contentType: 'application/json',
        body: JSON.stringify({
          id: '01912345-6789-7abc-8def-0123456789b0',
          jobDefinitionId: definitionId,
          status: 'succeeded',
          triggerKind: 'manual',
          errorMessage: null,
          startedAtUtc: '2026-07-26T00:00:01Z',
          finishedAtUtc: '2026-07-26T00:00:02Z',
          attemptCount: 1,
          createdAtUtc: '2026-07-26T00:00:00Z'
        })
      });
      return;
    }

    await route.fallback();
  });

  await page.goto('/');
  await page.getByRole('link', { name: /任务调度/ }).click();
  await expect(page.getByRole('heading', { name: '任务调度', exact: true })).toBeVisible();
  await expect(page.getByText('尚无任务定义', { exact: true })).toBeVisible();

  const jobsView = routeView(page, clientKind, 'host-jobs', '.host-jobs-view');
  await jobsView.getByLabel('显示名称', { exact: true }).fill('parity-job');
  await jobsView.getByRole('button', { name: '创建任务' }).click();
  await expect.poll(() => operations.some(operation => operation.type === 'create')).toBe(true);
  await expect(jobsView.getByText('parity-job', { exact: true })).toBeVisible();

  await jobsView.getByRole('button', { name: '立即执行' }).click();
  await expect.poll(() => operations.some(operation => operation.type === 'trigger')).toBe(true);
});

test('操作日志列表在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);

  await page.route('**/api/v1/auditing/operation-logs**', route => {
    if (route.request().method() !== 'GET') {
      return route.fallback();
    }
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: [{
          id: '01912345-6789-7abc-8def-0123456789ad',
          occurredAtUtc: '2026-07-25T08:05:00.000Z',
          actionKey: 'POST /api/v1/settings/config-entries',
          httpMethod: 'POST',
          requestPath: '/api/v1/settings/config-entries',
          statusCode: 201,
          durationMs: 18,
          succeeded: true,
          userId: '01912345-6789-7abc-8def-0123456789ac',
          tenantId: null,
          traceId: 'trace-2',
          clientIpFingerprint: 'def',
          permissionCode: 'settings.config.write'
        }],
        page: 1,
        pageSize: 20,
        total: 1
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /操作日志/ }).click();
  await expect(page.getByRole('heading', { name: '操作日志', exact: true })).toBeVisible();

  const operationLogsView = routeView(page, clientKind, 'operation-logs', '.operation-logs-view');
  await expect(operationLogsView.getByText('POST /api/v1/settings/config-entries')).toBeVisible();
  await expect(operationLogsView.getByText('成功', { exact: true })).toBeVisible();
});

test('异常日志列表在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);

  await page.route('**/api/v1/auditing/exception-logs**', route => {
    if (route.request().method() !== 'GET') {
      return route.fallback();
    }
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: [{
          id: '01912345-6789-7abc-8def-0123456789ae',
          occurredAtUtc: '2026-07-25T08:10:00.000Z',
          exceptionType: 'System.InvalidOperationException',
          message: 'Unhandled application exception.',
          stackTrace: null,
          httpMethod: 'POST',
          requestPath: '/api/v1/auditing/exception-probes',
          userId: '01912345-6789-7abc-8def-0123456789ac',
          tenantId: null,
          traceId: 'trace-3',
          clientIpFingerprint: 'ghi'
        }],
        page: 1,
        pageSize: 20,
        total: 1
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /异常日志/ }).click();
  await expect(page.getByRole('heading', { name: '异常日志', exact: true })).toBeVisible();

  const exceptionLogsView = routeView(page, clientKind, 'exception-logs', '.exception-logs-view');
  await expect(exceptionLogsView.getByText('System.InvalidOperationException')).toBeVisible();
  await expect(exceptionLogsView.getByText('Unhandled application exception.', { exact: false })).toBeVisible();
});

test('角色列表、创建与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const roleId = 'e2e-host-role-id';
  const state = { hasRole: false, disabled: false, dataScopeKind: 'identity.data_scope.all' };
  const listBody = () => {
    if (!state.hasRole) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: roleId,
        code: 'parity-role',
        name: '对等角色',
        isSystem: false,
        isActive: !state.disabled,
        isSuperAdministrator: false,
        permissionCodes: ['identity.users.read'],
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: state.disabled ? '2026-07-21T01:00:00Z' : null,
        version: state.disabled ? 2 : 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/identity/roles?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
  }));
  await page.route('**/api/v1/identity/roles', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasRole = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: roleId,
        code: 'parity-role',
        name: '对等角色',
        isSystem: false,
        isActive: true,
        isSuperAdministrator: false,
        permissionCodes: [],
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      })
    });
  });
  await page.route(`**/api/v1/identity/roles/${roleId}/data-scope`, async route => {
    if (route.request().method() === 'GET') {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          roleId,
          dataScopeKind: state.dataScopeKind,
          unitIds: [],
          version: 1
        })
      });
      return;
    }

    if (route.request().method() === 'PUT') {
      operations.push({ type: 'data-scope', body: route.request().postDataJSON() });
      state.dataScopeKind = route.request().postDataJSON().dataScopeKind;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          roleId,
          dataScopeKind: state.dataScopeKind,
          unitIds: [],
          version: 2
        })
      });
      return;
    }

    await route.fallback();
  });
  await page.route(`**/api/v1/identity/roles/${roleId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: roleId,
        code: 'parity-role',
        name: '对等角色',
        isSystem: false,
        isActive: false,
        isSuperAdministrator: false,
        permissionCodes: ['identity.users.read'],
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: '2026-07-21T01:00:00Z',
        version: 2
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /角色管理/ }).click();
  await expect(page.getByRole('heading', { name: '角色管理', exact: true })).toBeVisible();
  await expect(page.getByText('尚无 Host 角色', { exact: true })).toBeVisible();

  const rolesView = routeView(page, clientKind, 'roles', '.roles-view');
  await rolesView.getByLabel('角色编码', { exact: true }).fill('parity-role');
  await rolesView.getByLabel('显示名称', { exact: true }).fill('对等角色');
  await rolesView.getByRole('button', { name: '创建角色' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      code: 'parity-role',
      name: '对等角色'
    }
  }]);
  await expect(page.getByText('对等角色', { exact: true }).first()).toBeVisible();

  await page.getByRole('article').getByRole('button', { name: '数据范围' }).click();
  if (clientKind === 'vue') {
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.locator('.el-select').click();
    await page.locator('.el-select-dropdown__item').filter({ hasText: '本人' }).click();
    await dialog.getByRole('button', { name: '保存数据范围', exact: true }).click();
  } else {
    await page.locator('.layui-layer-content select').selectOption('identity.data_scope.self');
    await page.locator('.layui-layer-btn0').click();
  }
  await expect.poll(() => operations.some(operation => operation.type === 'data-scope')).toBe(true);

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

test('菜单列表、创建与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page);
  const operations = [];
  const menuId = 'e2e-host-menu-id';
  const state = { hasMenu: false, disabled: false };
  const listBody = () => {
    if (!state.hasMenu) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: menuId,
        parentId: null,
        routeName: 'parity-menu',
        path: '/',
        componentKey: 'overview',
        title: '对等菜单',
        caption: 'Parity menu',
        icon: 'grid',
        displayOrder: 50,
        requiredPermission: 'platform.dashboard.read',
        isSystem: false,
        isActive: !state.disabled,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/identity/menus?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
  }));
  await page.route('**/api/v1/identity/menus', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasMenu = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: menuId,
        parentId: null,
        routeName: 'parity-menu',
        path: '/',
        componentKey: 'overview',
        title: '对等菜单',
        caption: '对等菜单',
        icon: 'grid',
        displayOrder: 50,
        requiredPermission: 'platform.dashboard.read',
        isSystem: false,
        isActive: true,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      })
    });
  });
  await page.route(`**/api/v1/identity/menus/${menuId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: menuId,
        parentId: null,
        routeName: 'parity-menu',
        path: '/',
        componentKey: 'overview',
        title: '对等菜单',
        caption: '对等菜单',
        icon: 'grid',
        displayOrder: 50,
        requiredPermission: 'platform.dashboard.read',
        isSystem: false,
        isActive: false,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: '2026-07-21T01:00:00Z',
        version: 2
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /菜单管理/ }).click();
  await expect(page.getByRole('heading', { name: '菜单管理', exact: true })).toBeVisible();
  await expect(page.getByText('尚无 Host 菜单', { exact: true })).toBeVisible();

  const menusView = routeView(page, clientKind, 'menus', '.menus-view');
  await menusView.getByLabel('路由名', { exact: true }).fill('parity-menu');
  await menusView.getByLabel('显示标题', { exact: true }).fill('对等菜单');
  await menusView.getByRole('button', { name: '创建菜单' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: expect.objectContaining({
      routeName: 'parity-menu',
      title: '对等菜单',
      componentKey: 'overview',
      path: '/'
    })
  }]);
  await expect(page.getByText('对等菜单', { exact: true }).first()).toBeVisible();

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

test('机构列表、创建与禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page, { initialTenantId: tenantId });
  const operations = [];
  const unitId = 'e2e-tenant-unit-id';
  const state = { hasUnit: false, disabled: false };
  const listBody = () => {
    if (!state.hasUnit) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: unitId,
        parentId: null,
        code: 'parity-unit',
        name: '对等机构',
        displayOrder: 10,
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

  await page.route('**/api/v1/organization/units?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
  }));
  await page.route('**/api/v1/organization/units', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasUnit = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: unitId,
        parentId: null,
        code: 'parity-unit',
        name: '对等机构',
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      })
    });
  });
  await page.route(`**/api/v1/organization/units/${unitId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: unitId,
        parentId: null,
        code: 'parity-unit',
        name: '对等机构',
        displayOrder: 10,
        isActive: false,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: '2026-07-21T01:00:00Z',
        version: 2
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /机构管理/ }).click();
  await expect(page.getByRole('heading', { name: '机构管理', exact: true })).toBeVisible();
  await expect(page.getByText('尚无租户机构', { exact: true })).toBeVisible();

  const orgUnitsView = routeView(page, clientKind, 'org-units', '.org-units-view');
  await orgUnitsView.getByLabel('机构编码', { exact: true }).fill('parity-unit');
  await orgUnitsView.getByLabel('显示名称', { exact: true }).fill('对等机构');
  await orgUnitsView.getByRole('button', { name: '创建机构' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      parentId: null,
      code: 'parity-unit',
      name: '对等机构',
      displayOrder: 10
    }
  }]);
  await expect(page.getByText('对等机构', { exact: true }).first()).toBeVisible();

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

test('职位创建、机构与职级绑定及禁用在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page, { initialTenantId: tenantId });
  const operations = [];
  const positionId = 'e2e-tenant-position-id';
  const unitId = 'e2e-tenant-unit-id';
  const positionLevelId = 'e2e-tenant-position-level-id';
  const state = {
    hasPosition: false,
    disabled: false,
    unitId: null,
    positionLevelId: null,
    version: 1
  };
  const listBody = () => {
    if (!state.hasPosition) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: positionId,
        code: 'parity-pos',
        name: '对等职位',
        unitId: state.unitId,
        unitCode: state.unitId ? 'parity-unit' : null,
        unitName: state.unitId ? '对等机构' : null,
        positionLevelId: state.positionLevelId,
        positionLevelCode: state.positionLevelId ? 'senior' : null,
        positionLevelName: state.positionLevelId ? '高级' : null,
        displayOrder: 10,
        isActive: !state.disabled,
        createdAtUtc: '2026-07-25T00:00:00Z',
        updatedAtUtc: state.disabled ? '2026-07-25T01:00:00Z' : null,
        version: state.version
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route('**/api/v1/organization/positions?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
    })
  );
  await page.route('**/api/v1/organization/units?page=1&pageSize=100', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items: [{
        id: unitId,
        parentId: null,
        code: 'parity-unit',
        name: '对等机构',
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-07-25T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      }],
      page: 1,
      pageSize: 100,
      total: 1
    })
  }));
  await page.route(
    '**/api/v1/organization/position-levels?page=1&pageSize=100',
    route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: [{
          id: positionLevelId,
          code: 'senior',
          name: '高级',
          displayOrder: 10,
          isActive: true,
          createdAtUtc: '2026-07-25T00:00:00Z',
          updatedAtUtc: null,
          version: 1
        }],
        page: 1,
        pageSize: 100,
        total: 1
      })
    })
  );
  await page.route('**/api/v1/organization/positions', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasPosition = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: positionId,
        code: 'parity-pos',
        name: '对等职位',
        unitId: null,
        unitCode: null,
        unitName: null,
        positionLevelId: null,
        positionLevelCode: null,
        positionLevelName: null,
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-07-25T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      })
    });
  });
  await page.route(`**/api/v1/organization/positions/${positionId}/unit`, async route => {
    const body = route.request().postDataJSON();
    operations.push({ type: 'assign-unit', body });
    state.unitId = body.unitId;
    state.version += 1;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: positionId,
        code: 'parity-pos',
        name: '对等职位',
        unitId: state.unitId,
        unitCode: state.unitId ? 'parity-unit' : null,
        unitName: state.unitId ? '对等机构' : null,
        positionLevelId: state.positionLevelId,
        positionLevelCode: state.positionLevelId ? 'senior' : null,
        positionLevelName: state.positionLevelId ? '高级' : null,
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-07-25T00:00:00Z',
        updatedAtUtc: '2026-07-25T00:30:00Z',
        version: state.version
      })
    });
  });
  await page.route(
    `**/api/v1/organization/positions/${positionId}/position-level`,
    async route => {
      const body = route.request().postDataJSON();
      operations.push({ type: 'assign-position-level', body });
      state.positionLevelId = body.positionLevelId;
      state.version += 1;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: positionId,
          code: 'parity-pos',
          name: '对等职位',
          unitId: state.unitId,
          unitCode: state.unitId ? 'parity-unit' : null,
          unitName: state.unitId ? '对等机构' : null,
          positionLevelId: state.positionLevelId,
          positionLevelCode: state.positionLevelId ? 'senior' : null,
          positionLevelName: state.positionLevelId ? '高级' : null,
          displayOrder: 10,
          isActive: true,
          createdAtUtc: '2026-07-25T00:00:00Z',
          updatedAtUtc: '2026-07-25T00:45:00Z',
          version: state.version
        })
      });
    }
  );
  await page.route(`**/api/v1/organization/positions/${positionId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    state.version += 1;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: positionId,
        code: 'parity-pos',
        name: '对等职位',
        unitId: state.unitId,
        unitCode: state.unitId ? 'parity-unit' : null,
        unitName: state.unitId ? '对等机构' : null,
        positionLevelId: state.positionLevelId,
        positionLevelCode: state.positionLevelId ? 'senior' : null,
        positionLevelName: state.positionLevelId ? '高级' : null,
        displayOrder: 10,
        isActive: false,
        createdAtUtc: '2026-07-25T00:00:00Z',
        updatedAtUtc: '2026-07-25T01:00:00Z',
        version: state.version
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /职位管理/ }).click();
  await expect(page.getByRole('heading', { name: '职位管理', exact: true })).toBeVisible();
  await expect(page.getByText('尚无租户职位', { exact: true })).toBeVisible();

  const orgPositionsView = routeView(page, clientKind, 'org-positions', '.org-positions-view');
  await orgPositionsView.getByLabel('职位编码', { exact: true }).fill('parity-pos');
  await orgPositionsView.getByLabel('显示名称', { exact: true }).fill('对等职位');
  await orgPositionsView.getByRole('button', { name: '创建职位' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      code: 'parity-pos',
      name: '对等职位',
      displayOrder: 10
    }
  }]);
  await expect(page.getByText('对等职位', { exact: true }).first()).toBeVisible();

  if (clientKind === 'vue') {
    await orgPositionsView.locator('.el-select').first().click();
    await page.getByRole('option', { name: /对等机构/ }).click();
  } else {
    await orgPositionsView.getByLabel('所属机构', { exact: true }).selectOption(unitId);
  }
  await expect.poll(() => operations.some(operation => (
    operation.type === 'assign-unit'
      && operation.body.unitId === unitId
      && operation.body.version === 1
  ))).toBe(true);
  await expect(page.getByText('对等机构', { exact: true }).first()).toBeVisible();

  if (clientKind === 'vue') {
    await orgPositionsView.locator('.el-select').nth(1).click();
    await page.getByRole('option', { name: /高级/ }).click();
  } else {
    await orgPositionsView.getByLabel('所属职级', { exact: true })
      .selectOption(positionLevelId);
  }
  await expect.poll(() => operations.some(operation => (
    operation.type === 'assign-position-level'
      && operation.body.positionLevelId === positionLevelId
      && operation.body.version === 2
  ))).toBe(true);
  await expect(page.getByText('高级', { exact: true }).first()).toBeVisible();

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

test('职级列表与创建在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page, { initialTenantId: tenantId });
  const operations = [];
  let hasLevel = false;
  const level = {
    id: 'e2e-tenant-position-level-id',
    code: 'senior',
    name: '高级',
    displayOrder: 10,
    isActive: true,
    createdAtUtc: '2026-07-29T00:00:00Z',
    updatedAtUtc: null,
    version: 1
  };

  await page.route(
    '**/api/v1/organization/position-levels?page=1&pageSize=20',
    route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: hasLevel ? [level] : [],
        page: 1,
        pageSize: 20,
        total: hasLevel ? 1 : 0
      })
    })
  );
  await page.route('**/api/v1/organization/position-levels', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push(route.request().postDataJSON());
    hasLevel = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify(level)
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /职级管理/ }).click();
  await expect(page.getByRole('heading', { name: '职级管理', exact: true })).toBeVisible();
  await expect(page.getByText('尚无租户职级', { exact: true })).toBeVisible();

  const view = routeView(
    page,
    clientKind,
    'org-position-levels',
    '.org-position-levels-view'
  );
  await view.getByLabel('职级编码', { exact: true }).fill('senior');
  await view.getByLabel('显示名称', { exact: true }).fill('高级');
  await view.getByRole('button', { name: '创建职级' }).click();
  await expect.poll(() => operations).toEqual([{
    code: 'senior',
    name: '高级',
    displayOrder: 10
  }]);
  await expect(page.getByText('高级', { exact: true }).first()).toBeVisible();
});

test('用户机构隶属列表、分配与取消在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page, { initialTenantId: tenantId });
  const operations = [];
  const assignmentId = 'e2e-tenant-user-unit-id';
  const state = { hasAssignment: false, disabled: false };
  const listBody = () => {
    if (!state.hasAssignment) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: assignmentId,
        userId: 'e2e-user-id',
        username: 'admin',
        displayName: '系统管理员',
        unitId: 'e2e-tenant-unit-id',
        unitCode: 'parity-unit',
        unitName: '对等机构',
        isPrimary: false,
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

  await page.route(
    '**/api/v1/organization/user-units/assignable-users?page=*&pageSize=100',
    route => {
      const requestedPage = Number(
        new URL(route.request().url()).searchParams.get('page') ?? '1'
      );
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: requestedPage === 1
            ? [{
                id: 'e2e-user-id',
                username: 'admin',
                displayName: '系统管理员'
              }]
            : [{
                id: 'e2e-user-page-2-id',
                username: 'operator',
                displayName: '分页操作员'
              }],
          page: requestedPage,
          pageSize: 100,
          total: 101
        })
      });
    }
  );
  await page.route('**/api/v1/organization/units?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items: [{
        id: 'e2e-tenant-unit-id',
        parentId: null,
        code: 'parity-unit',
        name: '对等机构',
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    })
  }));
  await page.route('**/api/v1/organization/user-units?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
  }));
  await page.route('**/api/v1/organization/user-units', async route => {
    if (route.request().method() !== 'POST') {
      await route.fallback();
      return;
    }
    operations.push({ type: 'create', body: route.request().postDataJSON() });
    state.hasAssignment = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: assignmentId,
        userId: 'e2e-user-id',
        username: 'admin',
        displayName: '系统管理员',
        unitId: 'e2e-tenant-unit-id',
        unitCode: 'parity-unit',
        unitName: '对等机构',
        isPrimary: false,
        isActive: true,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      })
    });
  });
  await page.route(`**/api/v1/organization/user-units/${assignmentId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: assignmentId,
        userId: 'e2e-user-id',
        username: 'admin',
        displayName: '系统管理员',
        unitId: 'e2e-tenant-unit-id',
        unitCode: 'parity-unit',
        unitName: '对等机构',
        isPrimary: false,
        isActive: false,
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: '2026-07-21T01:00:00Z',
        version: 2
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /用户机构隶属/ }).click();
  await expect(page.getByRole('heading', { name: '用户机构隶属', exact: true })).toBeVisible();
  await expect(page.getByText('尚无用户机构隶属', { exact: true })).toBeVisible();

  const orgUserUnitsView = routeView(
    page,
    clientKind,
    'org-user-units',
    '.org-user-units-view'
  );
  if (clientKind === 'vue') {
    await orgUserUnitsView.getByTestId('org-user-units-load-more-users').click();
    await orgUserUnitsView.locator('.el-select').first().click();
    await expect(page.getByRole('option', { name: /分页操作员/ })).toBeVisible();
    await page.getByRole('option', { name: /系统管理员/ }).click();
    await orgUserUnitsView.locator('.el-select').nth(1).click();
    await page.getByRole('option', { name: /对等机构/ }).click();
  } else {
    await orgUserUnitsView.locator('[data-org-user-units-load-more-users]').click();
    await expect(
      orgUserUnitsView.locator('[data-org-user-units-user] option[value="e2e-user-page-2-id"]')
    ).toHaveText(/分页操作员/);
    await orgUserUnitsView.locator('[data-org-user-units-user]').selectOption('e2e-user-id');
    await orgUserUnitsView.locator('[data-org-user-units-unit]').selectOption('e2e-tenant-unit-id');
  }
  await orgUserUnitsView.getByRole('button', { name: '创建隶属' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      userId: 'e2e-user-id',
      unitId: 'e2e-tenant-unit-id',
      isPrimary: false
    }
  }]);
  await expect(page.getByText('系统管理员', { exact: true }).first()).toBeVisible();

  await page.getByRole('article').getByRole('button', { name: '取消隶属' }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '取消隶属', exact: true })
      .evaluate(button => button.click());
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  await expect.poll(() => operations.some(operation => operation.type === 'disable')).toBe(true);
  await expect(page.getByText('已取消', { exact: true })).toBeVisible();
});

test('用户职位隶属列表、分配与取消在两端保持一致', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
  await mockAuthenticatedSession(page, { initialTenantId: tenantId });
  const operations = [];
  const assignmentId = 'e2e-tenant-user-position-id';
  const state = { hasAssignment: false, disabled: false };
  const listBody = () => {
    if (!state.hasAssignment) {
      return JSON.stringify({ items: [], page: 1, pageSize: 20, total: 0 });
    }

    return JSON.stringify({
      items: [{
        id: assignmentId,
        userId: 'e2e-user-id',
        username: 'admin',
        displayName: '系统管理员',
        positionId: 'e2e-tenant-position-id',
        positionCode: 'parity-pos',
        positionName: '对等职位',
        isPrimary: false,
        isActive: !state.disabled,
        createdAtUtc: '2026-07-25T00:00:00Z',
        updatedAtUtc: state.disabled ? '2026-07-25T01:00:00Z' : null,
        version: state.disabled ? 2 : 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
  };

  await page.route(
    '**/api/v1/organization/user-positions/assignable-users?page=*&pageSize=100',
    route => {
      const requestedPage = Number(
        new URL(route.request().url()).searchParams.get('page') ?? '1'
      );
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: requestedPage === 1
            ? [{
                id: 'e2e-user-id',
                username: 'admin',
                displayName: '系统管理员'
              }]
            : [{
                id: 'e2e-user-page-2-id',
                username: 'operator',
                displayName: '分页操作员'
              }],
          page: requestedPage,
          pageSize: 100,
          total: 101
        })
      });
    }
  );
  await page.route('**/api/v1/organization/positions?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items: [{
        id: 'e2e-tenant-position-id',
        code: 'parity-pos',
        name: '对等职位',
        unitId: null,
        unitCode: null,
        unitName: null,
        positionLevelId: null,
        positionLevelCode: null,
        positionLevelName: null,
        displayOrder: 10,
        isActive: true,
        createdAtUtc: '2026-07-25T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      }],
      page: 1,
      pageSize: 20,
      total: 1
    })
  }));
  await page.route('**/api/v1/organization/user-positions?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: listBody()
  }));
  await page.route('**/api/v1/organization/user-positions', async route => {
    if (route.request().method() !== 'POST') {
      await route.continue();
      return;
    }

    const body = JSON.parse(route.request().postData() ?? '{}');
    operations.push({ type: 'create', body });
    state.hasAssignment = true;
    await route.fulfill({
      status: 201,
      contentType: 'application/json',
      body: JSON.stringify({
        id: assignmentId,
        userId: body.userId,
        username: 'admin',
        displayName: '系统管理员',
        positionId: body.positionId,
        positionCode: 'parity-pos',
        positionName: '对等职位',
        isPrimary: body.isPrimary,
        isActive: true,
        createdAtUtc: '2026-07-25T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      })
    });
  });
  await page.route(`**/api/v1/organization/user-positions/${assignmentId}/disable`, async route => {
    operations.push({ type: 'disable' });
    state.disabled = true;
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        id: assignmentId,
        userId: 'e2e-user-id',
        username: 'admin',
        displayName: '系统管理员',
        positionId: 'e2e-tenant-position-id',
        positionCode: 'parity-pos',
        positionName: '对等职位',
        isPrimary: false,
        isActive: false,
        createdAtUtc: '2026-07-25T00:00:00Z',
        updatedAtUtc: '2026-07-25T01:00:00Z',
        version: 2
      })
    });
  });

  await page.goto('/');
  await page.getByRole('link', { name: /用户职位隶属/ }).click();
  await expect(page.getByRole('heading', { name: '用户职位隶属', exact: true })).toBeVisible();
  await expect(page.getByText('尚无用户职位隶属', { exact: true })).toBeVisible();

  const orgUserPositionsView = routeView(
    page,
    clientKind,
    'org-user-positions',
    '.org-user-positions-view'
  );
  if (clientKind === 'vue') {
    await orgUserPositionsView.getByTestId('org-user-positions-load-more-users').click();
    await orgUserPositionsView.locator('.el-select').first().click();
    await expect(page.getByRole('option', { name: /分页操作员/ })).toBeVisible();
    await page.getByRole('option', { name: /系统管理员/ }).click();
    await orgUserPositionsView.locator('.el-select').nth(1).click();
    await page.getByRole('option', { name: /对等职位/ }).click();
  } else {
    await orgUserPositionsView.locator('[data-org-user-positions-load-more-users]').click();
    await expect(
      orgUserPositionsView.locator(
        '[data-org-user-positions-user] option[value="e2e-user-page-2-id"]'
      )
    ).toHaveText(/分页操作员/);
    await orgUserPositionsView.locator('[data-org-user-positions-user]').selectOption('e2e-user-id');
    await orgUserPositionsView.locator('[data-org-user-positions-position]')
      .selectOption('e2e-tenant-position-id');
  }
  await orgUserPositionsView.getByRole('button', { name: '创建隶属' }).click();
  await expect.poll(() => operations.filter(operation => operation.type === 'create')).toEqual([{
    type: 'create',
    body: {
      userId: 'e2e-user-id',
      positionId: 'e2e-tenant-position-id',
      isPrimary: false
    }
  }]);
  await expect(page.getByText('系统管理员', { exact: true }).first()).toBeVisible();

  await page.getByRole('article').getByRole('button', { name: '取消隶属' }).click();
  if (clientKind === 'vue') {
    await expect(page.getByRole('dialog')).toBeVisible();
    await page.getByRole('dialog').getByRole('button', { name: '取消隶属', exact: true })
      .evaluate(button => button.click());
  } else {
    await page.locator('.layui-layer-btn0').click();
  }
  await expect.poll(() => operations.some(operation => operation.type === 'disable')).toBe(true);
  await expect(page.getByText('已取消', { exact: true })).toBeVisible();
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

test('刷新失败后可登录、进入动态控制台并安全退出', async ({ page }, testInfo) => {
  const clientKind = testInfo.project.metadata.clientKind;
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
  if (clientKind === 'vue') {
    await expect(page.getByRole('button', { name: '系统管理员' })).toBeVisible();
    await page.getByRole('button', { name: '系统管理员' }).click();
    await page.getByRole('button', { name: '退出登录' }).click();
  } else {
    await expect(page.locator('[data-current-user]')).toHaveText('系统管理员');
    await page.locator('[data-session-logout]').click();
  }
  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
});

async function mockAuthenticatedSession(page, options = {}) {
  const state = { tenantId: options.initialTenantId ?? null };
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
  await page.route('**/api/v1/platform/host-dashboard-summary', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      activeTenantCount: 128,
      onlineSessionCount: 2406,
      todayRequestCount: 86200,
      todayErrorRate: 0.0008,
      recentActivities: [{
        actionKey: 'GET /api/v1/me',
        httpMethod: 'GET',
        requestPath: '/api/v1/me',
        succeeded: true,
        occurredAtUtc: '2026-07-26T00:00:00Z'
      }]
    })
  }));
  await mockSnapshotEndpoints(page, {
    ...options,
    activeTenantId: state.tenantId
  });

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
    body: JSON.stringify(navigationResponse(
      options.unknownComponent,
      options.activeTenantId
    ))
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
      'identity.menus.read',
      'identity.menus.create',
      'identity.menus.update',
      'identity.menus.disable',
      'identity.roles.read',
      'identity.roles.create',
      'identity.roles.update',
      'identity.roles.assign_permissions',
      'identity.roles.disable',
      'identity.roles.assign_data_scope',
      'identity.super_administrators.manage',
      'identity.super_administrators.read',
      'identity.users.read',
      'identity.users.write',
      'identity.sessions.read',
      'identity.sessions.revoke',
      'identity.api_keys.read',
      'identity.api_keys.create',
      'identity.api_keys.disable',
      'identity.api_keys.rotate',
      'platform.dashboard.read',
      'tenancy.tenants.read',
      'tenancy.host_tenants.read',
      'tenancy.tenants.switch',
      'tenancy.tenants.create',
      'tenancy.tenants.update',
      'tenancy.tenants.disable',
      'tenancy.tenants.assign_package',
      'tenancy.tenant_packages.read',
      'tenancy.tenant_packages.create',
      'tenancy.tenant_packages.update',
      'tenancy.tenant_packages.disable',
      'settings.dict_types.read',
      'settings.dict_types.write',
      'settings.config.read',
      'settings.config.write',
      'settings.enums.read',
      'files.files.read',
      'files.files.write',
      'notifications.announcements.read',
      'notifications.announcements.write',
      'notifications.inbox.read',
      'notifications.inbox.write',
      'jobs.definitions.read',
      'jobs.definitions.write',
      'jobs.executions.read',
      'auditing.access.read',
      'auditing.operations.read',
      'auditing.exceptions.read',
      ...(activeTenantId
        ? [
            'organization.units.read',
            'organization.units.create',
            'organization.units.update',
            'organization.units.disable',
            'organization.user_units.read',
            'organization.user_units.write',
            'organization.positions.read',
            'organization.positions.create',
            'organization.positions.update',
            'organization.positions.disable',
            'organization.positions.assign_unit',
            'organization.positions.assign_position_level',
            'organization.position_levels.read',
            'organization.position_levels.write',
            'organization.user_positions.read',
            'organization.user_positions.write',
            'settings.tenant_dict_types.read',
            'settings.tenant_dict_types.write'
          ]
        : [])
    ],
    sessionId: 'e2e-session-id',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
}

function navigationResponse(unknownComponent = false, activeTenantId = null) {
  const nodes = [
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
      id: 'tenant-management', parentId: null, routeName: 'tenant-management',
      path: '/tenants', componentKey: 'tenants',
      title: '租户管理', caption: 'Host 作用域租户目录', icon: 'grid',
      order: 22, requiredPermission: 'tenancy.host_tenants.read', children: []
    },
    {
      id: 'tenant-packages', parentId: null, routeName: 'tenant-packages',
      path: '/tenant-packages', componentKey: 'tenant-packages',
      title: '租户套餐', caption: 'Host 作用域套餐目录', icon: 'collection',
      order: 23, requiredPermission: 'tenancy.tenant_packages.read', children: []
    },
    {
      id: 'users', parentId: null, routeName: 'users', path: '/identity/users',
      componentKey: 'users', title: '用户管理', caption: 'Host 作用域账号',
      icon: 'users', order: 35, requiredPermission: 'identity.users.read', children: []
    },
    {
      id: 'online-sessions', parentId: null, routeName: 'online-sessions',
      path: '/identity/online-sessions', componentKey: 'online-sessions',
      title: '在线用户', caption: 'Host 在线会话与强制下线',
      icon: 'monitor', order: 35, requiredPermission: 'identity.sessions.read', children: []
    },
    {
      id: 'api-keys', parentId: null, routeName: 'api-keys',
      path: '/identity/api-keys', componentKey: 'api-keys',
      title: 'API Key', caption: 'Host API Key 与自动化访问',
      icon: 'key', order: 36, requiredPermission: 'identity.api_keys.read', children: []
    },
    {
      id: 'roles', parentId: null, routeName: 'roles', path: '/identity/roles',
      componentKey: 'roles', title: '角色管理', caption: 'Host 作用域角色与权限',
      icon: 'team', order: 36, requiredPermission: 'identity.roles.read', children: []
    },
    {
      id: 'menus', parentId: null, routeName: 'menus', path: '/identity/menus',
      componentKey: 'menus', title: '菜单管理', caption: 'Host 作用域导航配置',
      icon: 'menu', order: 37, requiredPermission: 'identity.menus.read', children: []
    },
    {
      id: 'super-administrators', parentId: null,
      routeName: 'super-administrators', path: '/identity/super-administrators',
      componentKey: 'super-administrators', title: '超级管理员',
      caption: '受保护的 Host 最高权限账号', icon: 'shield', order: 40,
      requiredPermission: 'identity.super_administrators.read', children: []
    },
    {
      id: 'dict-types', parentId: null, routeName: 'dict-types',
      path: '/settings/dict-types', componentKey: 'dict-types',
      title: '数据字典', caption: 'Host 作用域字典目录', icon: 'collection',
      order: 50, requiredPermission: 'settings.dict_types.read', children: []
    },
    {
      id: 'config-entries', parentId: null, routeName: 'config-entries',
      path: '/settings/config-entries', componentKey: 'config-entries',
      title: '系统配置', caption: 'Host 作用域配置目录', icon: 'setting',
      order: 51, requiredPermission: 'settings.config.read', children: []
    },
    {
      id: 'enum-catalogs', parentId: null, routeName: 'enum-catalogs',
      path: '/settings/enum-catalogs', componentKey: 'enum-catalogs',
      title: '枚举常量', caption: 'Host 作用域元数据目录', icon: 'list',
      order: 52, requiredPermission: 'settings.enums.read', children: []
    },
    {
      id: 'host-files', parentId: null, routeName: 'host-files',
      path: '/files/host-files', componentKey: 'host-files',
      title: '文件管理', caption: 'Host 作用域文件目录',
      icon: 'folder', order: 70, requiredPermission: 'files.files.read', children: []
    },
    {
      id: 'host-announcements', parentId: null, routeName: 'host-announcements',
      path: '/notifications/host-announcements', componentKey: 'host-announcements',
      title: '公告管理', caption: 'Host 作用域公告目录',
      icon: 'bell', order: 55, requiredPermission: 'notifications.announcements.read', children: []
    },
    {
      id: 'inbox-messages', parentId: null, routeName: 'inbox-messages',
      path: '/notifications/inbox-messages', componentKey: 'inbox-messages',
      title: '消息中心', caption: '个人站内信收件箱',
      icon: 'message', order: 56, requiredPermission: 'notifications.inbox.read', children: []
    },
    {
      id: 'host-jobs', parentId: null, routeName: 'host-jobs',
      path: '/jobs/host-definitions', componentKey: 'host-jobs',
      title: '任务调度', caption: 'Host 作用域任务定义与执行',
      icon: 'timer', order: 57, requiredPermission: 'jobs.definitions.read', children: []
    },
    {
      id: 'access-logs', parentId: null, routeName: 'access-logs',
      path: '/auditing/access-logs', componentKey: 'access-logs',
      title: '访问日志', caption: 'Host 作用域 HTTP 访问审计', icon: 'document',
      order: 60, requiredPermission: 'auditing.access.read', children: []
    },
    {
      id: 'operation-logs', parentId: null, routeName: 'operation-logs',
      path: '/auditing/operation-logs', componentKey: 'operation-logs',
      title: '操作日志', caption: 'Host 作用域写操作审计', icon: 'edit',
      order: 61, requiredPermission: 'auditing.operations.read', children: []
    },
    {
      id: 'exception-logs', parentId: null, routeName: 'exception-logs',
      path: '/auditing/exception-logs', componentKey: 'exception-logs',
      title: '异常日志', caption: 'Host 作用域未处理异常审计', icon: 'warning',
      order: 62, requiredPermission: 'auditing.exceptions.read', children: []
    }
  ];

  if (activeTenantId) {
    nodes.push({
      id: 'org-units', parentId: null, routeName: 'org-units',
      path: '/organization/units', componentKey: 'org-units',
      title: '机构管理', caption: '租户作用域组织单元', icon: 'office-building',
      order: 45, requiredPermission: 'organization.units.read', children: []
    });
    nodes.push({
      id: 'org-user-units', parentId: null, routeName: 'org-user-units',
      path: '/organization/user-units', componentKey: 'org-user-units',
      title: '用户机构隶属', caption: '租户作用域 Host 用户与机构关系', icon: 'user',
      order: 46, requiredPermission: 'organization.user_units.read', children: []
    });
    nodes.push({
      id: 'org-positions', parentId: null, routeName: 'org-positions',
      path: '/organization/positions', componentKey: 'org-positions',
      title: '职位管理', caption: '租户作用域职位目录', icon: 'postcard',
      order: 47, requiredPermission: 'organization.positions.read', children: []
    });
    nodes.push({
      id: 'org-position-levels', parentId: null, routeName: 'org-position-levels',
      path: '/organization/position-levels', componentKey: 'org-position-levels',
      title: '职级管理', caption: '租户作用域职级目录', icon: 'medal',
      order: 48, requiredPermission: 'organization.position_levels.read', children: []
    });
    nodes.push({
      id: 'org-user-positions', parentId: null, routeName: 'org-user-positions',
      path: '/organization/user-positions', componentKey: 'org-user-positions',
      title: '用户职位隶属', caption: '租户作用域 Host 用户与职位关系', icon: 'user',
      order: 49, requiredPermission: 'organization.user_positions.read', children: []
    });
    nodes.push({
      id: 'tenant-dict-types', parentId: null, routeName: 'tenant-dict-types',
      path: '/settings/tenant-dict-types', componentKey: 'tenant-dict-types',
      title: '租户数据字典', caption: '租户作用域字典目录', icon: 'collection',
      order: 50, requiredPermission: 'settings.tenant_dict_types.read', children: []
    });
  }

  return nodes;
}

function availableTenants() {
  return [{
    id: tenantId,
    identifier: 'acme',
    name: 'Acme Corporation',
    domain: 'acme.localhost'
  }];
}

function routeView(page, clientKind, layuiViewKey, vueSelector) {
  return clientKind === 'layui'
    ? page.locator(`[data-route-view="${layuiViewKey}"]`)
    : page.locator(vueSelector);
}
