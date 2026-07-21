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
  await expect(navigation.getByRole('link', { name: /角色管理/ })).toBeVisible();
  await expect(navigation.getByRole('link', { name: /菜单管理/ })).toBeVisible();
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

  await page.route('**/api/v1/identity/users?page=1&pageSize=20', route => route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify({
      items: [{
        id: 'e2e-user-id',
        username: 'admin',
        displayName: '系统管理员',
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
    await orgUserUnitsView.locator('.el-select').first().click();
    await page.getByRole('option', { name: /系统管理员/ }).click();
    await orgUserUnitsView.locator('.el-select').nth(1).click();
    await page.getByRole('option', { name: /对等机构/ }).click();
  } else {
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
      'identity.menus.write',
      'identity.roles.read',
      'identity.roles.write',
      'identity.super_administrators.manage',
      'identity.super_administrators.read',
      'identity.users.read',
      'identity.users.write',
      'platform.dashboard.read',
      'tenancy.tenants.read',
      'tenancy.tenants.switch',
      ...(activeTenantId
        ? [
            'organization.units.read',
            'organization.units.write',
            'organization.user_units.read',
            'organization.user_units.write'
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
      id: 'users', parentId: null, routeName: 'users', path: '/identity/users',
      componentKey: 'users', title: '用户管理', caption: 'Host 作用域账号',
      icon: 'users', order: 35, requiredPermission: 'identity.users.read', children: []
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
