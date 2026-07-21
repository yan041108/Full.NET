import { expect } from '@playwright/test';

const username = process.env.FULLNET_E2E_USERNAME ?? 'admin';
const password = process.env.FULLNET_E2E_PASSWORD ?? 'FullNet!2026Secure';
const viewerUsername = process.env.FULLNET_E2E_VIEWER_USERNAME ?? 'e2e-viewer';
const viewerPassword = process.env.FULLNET_E2E_VIEWER_PASSWORD ?? password;

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
}

/** 登录 Development 受限查看者并等待动态导航就绪。 */
export async function loginAsHostViewer(page, baseUrl = '/') {
  await page.goto(baseUrl);
  await expect(page.getByRole('heading', { name: '管理员登录' })).toBeVisible();
  await page.getByLabel('账号', { exact: true }).fill(viewerUsername);
  await page.getByLabel('密码', { exact: true }).fill(viewerPassword);
  await page.getByRole('button', { name: '进入控制台' }).click();
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({
    timeout: 15_000
  });
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
  const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
  const origin = adminOrigin(clientKind);
  const response = await request.post(`${apiBaseUrl}/api/v1/auth/login`, {
    data: {
      username: viewerUsername,
      password: viewerPassword
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
