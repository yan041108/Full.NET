import { expect } from '@playwright/test';

const username = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const viewerUsername = process.env.FULLNET_E2E_VIEWER_USERNAME ?? 'e2e-viewer';
const viewerPassword = process.env.FULLNET_E2E_VIEWER_PASSWORD ?? password;

/** 展开 Art 侧栏全部分组，使折叠菜单中的叶子链接进入可点击树。 */
export async function expandMainNavigation(page) {
  const navigation = page.getByRole('navigation', { name: '主导航' }).first();
  await expect(navigation).toBeVisible({ timeout: 15_000 });
  const subMenus = navigation.locator('.el-sub-menu');
  const subMenuCount = await subMenus.count();
  for (let index = 0; index < subMenuCount; index += 1) {
    const subMenu = subMenus.nth(index);
    if (!await subMenu.evaluate(element => element.classList.contains('is-opened'))) {
      await subMenu.locator(':scope > .el-sub-menu__title').click();
      await expect(subMenu).toHaveClass(/is-opened/);
    }
  }
}

/** 登录 Host 管理员并等待动态导航就绪。 */
export async function loginAsHostAdmin(page, baseUrl = '/') {
  await page.goto(baseUrl);
  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
  await page.getByLabel('账号', { exact: true }).fill(username);
  await page.getByLabel('密码', { exact: true }).fill(password);
  await page.getByRole('button', { name: '进入控制台' }).click();
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({
    timeout: 15_000
  });
  await expandMainNavigation(page);
}

/** 登录 Development 受限查看者并等待动态导航就绪。 */
export async function loginAsHostViewer(page, baseUrl = '/') {
  await page.context().clearCookies();
  await page.addInitScript(() => {
    localStorage.clear();
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
  await page.goto(baseUrl);
  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible({
    timeout: 15_000
  });
  await page.getByLabel('账号', { exact: true }).fill(viewerUsername);
  await page.getByLabel('密码', { exact: true }).fill(viewerPassword);
  await page.getByRole('button', { name: '进入控制台' }).click();
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({
    timeout: 15_000
  });
  await expandMainNavigation(page);
}

/** 双管理端均使用 hash 路由访问状态页。 */
export function statusPath(_clientKind, code) {
  return `/#/${code}`;
}

/** 返回当前 Playwright 项目对应的管理端 Origin。 */
export function adminOrigin(clientKind) {
  return clientKind === 'layui'
    ? 'http://localhost:25174'
    : 'http://localhost:25173';
}

/** 使用真实登录 API 获取 Access Token（不依赖 route mock）。 */
export async function loginAccessToken(request, clientKind) {
  return loginWithPassword(request, clientKind, viewerUsername, viewerPassword);
}

/** 使用 Host 管理员凭据获取 Access Token，供真实栈写路径准备数据。 */
export async function loginHostAdminAccessToken(request, clientKind) {
  return loginWithPassword(request, clientKind, username, password);
}

/** 将种子管理员当前站内信全部标记为已读，给未读徽标场景建立确定基线。 */
export async function markAllInboxMessagesReadViaApi(request, clientKind) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(
    `${apiBaseUrl}/api/v1/notifications/my-inbox-messages/read-all`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin
      }
    }
  );
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  expect(body.unreadCount).toBe(0);
}

/**
 * 经真实 Host 写 API 发送站内信，由独立 Worker 消费 Outbox 后推送到收件人连接。
 * @returns {Promise<{ id: string, title: string, status: string }>}
 */
export async function sendHostInboxMessageViaApi(
  request,
  clientKind,
  recipientUserId,
  options
) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(
    `${apiBaseUrl}/api/v1/notifications/host-inbox-messages`,
    {
      data: {
        recipientUserId,
        title: options.title,
        content: options.content
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin,
        'Content-Type': 'application/json'
      }
    }
  );
  expect(response.status()).toBe(201);
  const body = await response.json();
  expect(typeof body.id).toBe('string');
  expect(body.title).toBe(options.title);
  expect(body.status).toBe('unread');
  return body;
}

/**
 * 经真实 API 创建一次性 Host 用户，避免污染 e2e-viewer 等共享账号。
 * @returns {Promise<{ id: string, username: string }>}
 */
export async function createHostUserViaApi(request, clientKind, options) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(`${apiBaseUrl}/api/v1/identity/users`, {
    data: {
      username: options.username,
      displayName: options.displayName,
      password: options.password
    },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  expect(typeof body.id).toBe('string');
  expect(body.username).toBe(options.username);
  return { id: body.id, username: body.username };
}

/**
 * 经真实 API 创建带精确权限的 Host 角色。
 * @returns {Promise<{ id: string, code: string, version: number, permissionCodes: string[] }>}
 */
export async function createHostRoleWithPermissionsViaApi(request, clientKind, options) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const createResponse = await request.post(`${apiBaseUrl}/api/v1/identity/roles`, {
    data: {
      code: options.code,
      name: options.name
    },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(createResponse.status()).toBe(201);
  const role = await createResponse.json();
  const permissionsResponse = await request.put(
    `${apiBaseUrl}/api/v1/identity/roles/${role.id}/permissions`,
    {
      data: {
        permissionCodes: options.permissionCodes,
        version: role.version
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin,
        'Content-Type': 'application/json'
      }
    }
  );
  expect(permissionsResponse.ok()).toBeTruthy();
  const withPermissions = await permissionsResponse.json();
  return {
    id: withPermissions.id,
    code: withPermissions.code,
    version: withPermissions.version,
    permissionCodes: withPermissions.permissionCodes
  };
}

/**
 * 经真实 API 为 Host 用户绑定角色。
 */
export async function assignHostUserRolesViaApi(request, clientKind, userId, roleIds) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const currentResponse = await request.get(
    `${apiBaseUrl}/api/v1/identity/users/${encodeURIComponent(userId)}/roles`,
    {
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin
      }
    }
  );
  expect(currentResponse.ok()).toBeTruthy();
  const current = await currentResponse.json();
  const response = await request.put(
    `${apiBaseUrl}/api/v1/identity/users/${encodeURIComponent(userId)}/roles`,
    {
      data: {
        roleIds,
        version: current.version
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin,
        'Content-Type': 'application/json'
      }
    }
  );
  expect(response.ok()).toBeTruthy();
}

/**
 * 准备一次性 Host 受限账号：独立角色、用户与密码，避免污染共享 e2e-viewer。
 * @returns {Promise<{ username: string, password: string, userId: string, roleId: string }>}
 */
export async function provisionLimitedHostUserViaApi(request, clientKind, options) {
  const stamp = Date.now().toString(36);
  const roleCode = options.roleCode ?? `e2e-role-${stamp}`;
  const username = options.username ?? `e2e-user-${stamp}`;
  const password = options.password ?? (process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure');
  const role = await createHostRoleWithPermissionsViaApi(request, clientKind, {
    code: roleCode,
    name: options.roleName ?? 'E2E 受限角色',
    permissionCodes: options.permissionCodes
  });
  const user = await createHostUserViaApi(request, clientKind, {
    username,
    displayName: options.displayName ?? 'E2E 受限用户',
    password
  });
  await assignHostUserRolesViaApi(request, clientKind, user.id, [role.id]);
  return {
    username: user.username,
    password,
    userId: user.id,
    roleId: role.id
  };
}

/** 使用指定凭据登录 Host 管理端并等待动态导航就绪。 */
export async function loginAsHostUser(page, username, password, baseUrl = '/') {
  await page.context().clearCookies();
  await page.addInitScript(() => {
    localStorage.clear();
    localStorage.setItem('fullnet.admin.locale', 'zh-CN');
  });
  await page.goto(baseUrl);
  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible({
    timeout: 15_000
  });
  await page.getByLabel('账号', { exact: true }).fill(username);
  await page.getByLabel('密码', { exact: true }).fill(password);
  await page.getByRole('button', { name: '进入控制台' }).click();
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({
    timeout: 15_000
  });
  await expandMainNavigation(page);
}

/** 展开侧栏并打开叶子导航，等待 hash 路由就绪。 */
export async function openMainNavLink(page, linkName) {
  await expandMainNavigation(page);
  const navigation = page.getByRole('navigation', { name: '主导航' }).first();
  const link = navigation.getByRole('link', { name: linkName }).first();
  await expect(link).toBeVisible({ timeout: 15_000 });
  const targetHash = await link.getAttribute('href');
  // 使用 DOM click 规避 Art 侧栏折叠态下 Playwright 命中层叠节点的问题。
  await link.evaluate(element => element.click());
  if (targetHash?.startsWith('#')) {
    await expect(page).toHaveURL(url => url.hash === targetHash, { timeout: 15_000 });
  }
}

/**
 * 展开 Art 侧栏分组后点击叶子导航链接。
 * 分组菜单默认折叠时叶子 link 不在可点击树中。
 */
export async function clickMainNavLink(page, linkName, groupTitle) {
  if (!groupTitle) {
    await openMainNavLink(page, linkName);
    return;
  }
  await expandMainNavigation(page);
  const navigation = page.getByRole('navigation', { name: '主导航' }).first();
  const link = navigation.getByRole('link', { name: linkName });
  if ((await link.count()) === 0 || !(await link.first().isVisible().catch(() => false))) {
    if (groupTitle) {
      const groupTitleNode = navigation.locator('.el-sub-menu__title').filter({
        hasText: groupTitle
      });
      await expect(groupTitleNode.first()).toBeVisible({ timeout: 15_000 });
      await groupTitleNode.first().click();
    }
  }
  await expect(link.first()).toBeVisible({ timeout: 15_000 });
  await link.first().evaluate(element => element.click());
}

/** 使用指定凭据经真实登录 API 获取 Access Token。 */
export async function loginAccessTokenWithPassword(request, clientKind, username, password) {
  return loginWithPassword(request, clientKind, username, password);
}

/**
 * 经真实 API 创建 Host 租户套餐，供真实栈写路径准备数据。
 * @returns {Promise<{ id: string, code: string, name: string, version: number }>}
 */
export async function createTenantPackageViaApi(request, clientKind, options) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(`${apiBaseUrl}/api/v1/tenancy/tenant-packages`, {
    data: {
      code: options.code,
      name: options.name,
      description: options.description ?? null
    },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  expect(typeof body.id).toBe('string');
  expect(body.code).toBe(options.code);
  expect(body.name).toBe(options.name);
  return { id: body.id, code: body.code, name: body.name, version: body.version };
}

/**
 * 经真实 API 创建 Host 字典类型，供真实栈准备数据或断言写路径。
 * @returns {Promise<{ id: string, code: string, name: string, version: number }>}
 */
export async function createSettingsDictTypeViaApi(request, clientKind, options) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(`${apiBaseUrl}/api/v1/settings/dict-types`, {
    data: {
      code: options.code,
      name: options.name,
      description: options.description ?? null,
      displayOrder: options.displayOrder ?? 0
    },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  expect(typeof body.id).toBe('string');
  expect(body.code).toBe(options.code);
  expect(body.name).toBe(options.name);
  return { id: body.id, code: body.code, name: body.name, version: body.version };
}

/**
 * 经真实 API 在指定字典类型下创建字典项。
 * @returns {Promise<{ id: string, value: string, label: string, version: number }>}
 */
/** 获取 Host 管理员在 Development 本地租户上下文中的 Access Token。 */
export async function loginTenantAdminAccessToken(request, clientKind) {
  const hostToken = await loginHostAdminAccessToken(request, clientKind);
  return enterTenantAccessToken(request, clientKind, hostToken);
}

/**
 * 经真实 API 在租户上下文中创建字典类型。
 * @returns {Promise<{ id: string, code: string, name: string, version: number }>}
 */
export async function createSettingsTenantDictTypeViaApi(request, clientKind, options) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginTenantAdminAccessToken(request, clientKind);
  const response = await request.post(`${apiBaseUrl}/api/v1/settings/tenant-dict-types`, {
    data: {
      code: options.code,
      name: options.name,
      description: options.description ?? null,
      displayOrder: options.displayOrder ?? 0
    },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  expect(typeof body.id).toBe('string');
  expect(body.code).toBe(options.code);
  expect(body.name).toBe(options.name);
  return { id: body.id, code: body.code, name: body.name, version: body.version };
}

/**
 * 经真实 API 在租户上下文中于指定字典类型下创建字典项。
 * @returns {Promise<{ id: string, value: string, label: string, version: number }>}
 */
export async function createSettingsTenantDictItemViaApi(
  request,
  clientKind,
  dictTypeId,
  options
) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginTenantAdminAccessToken(request, clientKind);
  const response = await request.post(
    `${apiBaseUrl}/api/v1/settings/tenant-dict-types/${encodeURIComponent(dictTypeId)}/items`,
    {
      data: {
        label: options.label,
        value: options.value,
        color: options.color ?? null,
        displayOrder: options.displayOrder ?? 0
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin,
        'Content-Type': 'application/json'
      }
    }
  );
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  expect(typeof body.id).toBe('string');
  expect(body.value).toBe(options.value);
  expect(body.label).toBe(options.label);
  return { id: body.id, value: body.value, label: body.label, version: body.version };
}

export async function createSettingsDictItemViaApi(request, clientKind, dictTypeId, options) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(
    `${apiBaseUrl}/api/v1/settings/dict-types/${encodeURIComponent(dictTypeId)}/items`,
    {
      data: {
        label: options.label,
        value: options.value,
        color: options.color ?? null,
        displayOrder: options.displayOrder ?? 0
      },
      headers: {
        Authorization: `Bearer ${accessToken}`,
        Origin: origin,
        'Content-Type': 'application/json'
      }
    }
  );
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  expect(typeof body.id).toBe('string');
  expect(body.value).toBe(options.value);
  expect(body.label).toBe(options.label);
  return { id: body.id, value: body.value, label: body.label, version: body.version };
}

/**
 * 经真实 API 创建 Host 系统配置项。
 * @returns {Promise<{ id: string, configKey: string, displayName: string, version: number }>}
 */
export async function createSettingsConfigEntryViaApi(request, clientKind, options) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(`${apiBaseUrl}/api/v1/settings/config-entries`, {
    data: {
      configKey: options.configKey,
      displayName: options.displayName,
      description: options.description ?? null,
      valueKind: options.valueKind ?? 'string',
      value: options.value ?? '',
      displayOrder: options.displayOrder ?? 0
    },
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  expect(typeof body.id).toBe('string');
  expect(body.configKey).toBe(options.configKey);
  expect(body.displayName).toBe(options.displayName);
  return {
    id: body.id,
    configKey: body.configKey,
    displayName: body.displayName,
    version: body.version
  };
}

/**
 * 经真实 API 上传 Host 文件，供真实栈准备数据或断言写路径。
 * @returns {Promise<{ id: string, originalFileName: string, sizeBytes: number }>}
 */
export async function uploadHostFileViaApi(request, clientKind, options) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(`${apiBaseUrl}/api/v1/files/host-files`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin
    },
    multipart: {
      file: {
        name: options.fileName,
        mimeType: options.contentType ?? 'text/plain',
        buffer: Buffer.from(options.content ?? 'real-stack')
      }
    }
  });
  expect(response.status()).toBe(201);
  const body = await response.json();
  expect(typeof body.id).toBe('string');
  expect(body.originalFileName).toBe(options.fileName);
  return {
    id: body.id,
    originalFileName: body.originalFileName,
    sizeBytes: body.sizeBytes
  };
}

/**
 * 从 Host 用户目录查找种子管理员，供 API Key 等写路径绑定用户。
 * @returns {Promise<{ id: string, username: string }>}
 */
export async function findSeedAdminUserViaApi(request, clientKind, loginUsername = 'admin') {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.get(`${apiBaseUrl}/api/v1/identity/users?page=1&pageSize=50`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin
    }
  });
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  const user = body.items?.find(entry => entry.username === loginUsername);
  expect(user?.id).toBeTruthy();
  return { id: user.id, username: user.username };
}

/**
 * 从 Host 租户目录查找种子租户，供真实栈分配套餐等写路径使用。
 * @returns {Promise<{ id: string, identifier: string, name: string, version: number }>}
 */
export async function findSeedTenantViaApi(request, clientKind, identifier = 'local') {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.get(`${apiBaseUrl}/api/v1/tenancy/tenants?page=1&pageSize=50`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: origin
    }
  });
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  const tenant = body.items?.find(entry => entry.identifier === identifier);
  expect(tenant?.id).toBeTruthy();
  return tenant;
}

async function loginWithPassword(request, clientKind, loginUsername, loginPassword) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const response = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
    data: {
      username: loginUsername,
      password: loginPassword
    },
    headers: {
      Origin: origin,
      'Content-Type': 'application/json'
    }
  });
  expect(response.ok()).toBeTruthy();
  const body = await response.json();
  expect(typeof body.accessToken).toBe('string');
  return body.accessToken;
}

/** 将 Host 访问令牌切换为 Development 本地租户上下文。 */
export async function enterTenantAccessToken(request, clientKind, hostAccessToken) {
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const availableResponse = await request.get(`${apiBaseUrl}/api/v1/tenancy/available`, {
    headers: {
      Authorization: `Bearer ${hostAccessToken}`,
      Origin: origin
    }
  });
  expect(availableResponse.ok()).toBeTruthy();
  const tenants = await availableResponse.json();
  const tenant = tenants.find(entry => entry.identifier === 'local') ?? tenants[0];
  expect(tenant?.id).toBeTruthy();

  const enterResponse = await request.put(`${apiBaseUrl}/api/v1/tenancy/context`, {
    headers: {
      Authorization: `Bearer ${hostAccessToken}`,
      Origin: origin,
      'Content-Type': 'application/json'
    },
    data: { tenantId: tenant.id }
  });
  expect(enterResponse.ok()).toBeTruthy();
  const body = await enterResponse.json();
  expect(typeof body.accessToken).toBe('string');
  return body.accessToken;
}

/** 登录后进入 Development 种子租户，并等待侧栏上下文名可见。 */
export async function enterDevelopmentTenant(page, tenantName = 'Full.NET Local') {
  await clickMainNavLink(page, /租户上下文/);
  await expect(page.getByRole('heading', { name: '租户上下文' })).toBeVisible();
  const tenantRow = page
    .locator('.tenant-context-view')
    .locator('tr')
    .filter({ hasText: tenantName });
  await tenantRow.getByRole('button', { name: '进入租户' }).click();
  await expectVisibleCurrentContext(page, tenantName);
}

/** 断言当前上下文名称（避开 el-select/option 等 hidden 文本）。 */
export async function expectVisibleCurrentContext(page, name) {
  const vueShell = page.locator('[data-client-kind="vue"]');
  const layuiContext = page.locator('.fn-tenant > [data-current-context]');
  await expect(async () => {
    if ((await vueShell.count()) > 0) {
      await page.locator('.art-user-menu__trigger').click({ timeout: 5_000 });
      await expect(
        page.getByTestId('shell-tenant-select').getByText(name, { exact: true })
      ).toBeVisible({ timeout: 3_000 });
      await page.keyboard.press('Escape');
      return;
    }

    await expect(layuiContext).toHaveText(name, { timeout: 3_000 });
  }).toPass({ timeout: 20_000 });
}
