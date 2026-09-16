import { expect } from '@playwright/test';
import { expandMainNavigation, loginHostAdminAccessToken } from './real-stack-auth.mjs';
import { resolveApiBase } from './identity-oidc-fixtures.mjs';

export const ADMIN_OIDC_CENTER_CLIENT_ID = 'e2e-admin-oidc-spa';

export function resolveAdminOidcCenterRedirectUri(adminOrigin) {
  return `${adminOrigin}/#/identity/oidc/callback`;
}

/** 确保 Vue 管理端 oidc-center E2E 客户端存在；已存在时幂等返回。 */
export async function ensureAdminOidcCenterClient(request, adminOrigin) {
  const apiBase = resolveApiBase();
  const adminToken = await loginHostAdminAccessToken(request, 'vue');
  const redirectUri = resolveAdminOidcCenterRedirectUri(adminOrigin);
  const listResponse = await request.get(
    `${apiBase}/api/v1/identity/oidc-clients?page=1&pageSize=20&clientIdContains=${ADMIN_OIDC_CENTER_CLIENT_ID}`,
    {
      headers: {
        authorization: `Bearer ${adminToken}`,
        origin: adminOrigin
      }
    }
  );
  expect(listResponse.ok()).toBeTruthy();
  const listBody = await listResponse.json();
  const existing = (listBody.items ?? []).find(item => item.clientId === ADMIN_OIDC_CENTER_CLIENT_ID);
  if (existing !== undefined) {
    return { clientId: ADMIN_OIDC_CENTER_CLIENT_ID, redirectUri, resourceId: existing.id };
  }

  const createResponse = await request.post(`${apiBase}/api/v1/identity/oidc-clients`, {
    headers: {
      authorization: `Bearer ${adminToken}`,
      'content-type': 'application/json',
      origin: adminOrigin
    },
    data: {
      clientId: ADMIN_OIDC_CENTER_CLIENT_ID,
      displayName: 'E2E Vue admin OIDC center SPA',
      redirectUris: [redirectUri],
      postLogoutRedirectUris: [],
      scopes: ['openid', 'profile', 'offline_access'],
      isConfidential: false,
      isFirstParty: true,
      resourceAudience: null
    }
  });
  expect(createResponse.status()).toBe(201);
  const body = await createResponse.json();
  return {
    clientId: ADMIN_OIDC_CENTER_CLIENT_ID,
    redirectUri,
    resourceId: body.client.id
  };
}

export async function loginAdminViaOidcCenter(page, { username, password }) {
  await page.goto('/');
  await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });
  await page.getByTestId('login-oidc-center').click();
  await expect(page.getByRole('heading', { name: 'Identity Center' })).toBeVisible({
    timeout: 30_000
  });
  await page.getByLabel('Username').fill(username);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('navigation', { name: '主导航' })).toBeVisible({
    timeout: 30_000
  });
  await expandMainNavigation(page);
}

export async function logoutAdminShell(page) {
  await page.getByRole('button', { name: '系统管理员' }).click();
  await page.getByRole('button', { name: '退出登录' }).click();
}

/** 通过工作台探针捕获当前 OIDC access token，供真实栈 API 断言复用。 */
export async function captureOidcAccessTokenFromOverviewProbe(page) {
  await expect(page.getByTestId('load-current-user')).toBeVisible({ timeout: 15_000 });
  const meRequest = page.waitForRequest(request =>
    request.url().includes('/api/v1/me') && request.method() === 'GET'
  );
  await page.getByTestId('load-current-user').click();
  const accessToken = (await meRequest).headers().authorization?.replace(/^Bearer\s+/i, '');
  expect(accessToken).toBeTruthy();
  return accessToken;
}

/** 等待 Notifications Hub WebSocket 建立，确保实时撤销通知可送达。 */
export async function waitForNotificationsRealtimeConnection(page) {
  await page.waitForEvent('websocket', {
    predicate: socket => socket.url().includes('/hubs/notifications'),
    timeout: 30_000
  });
}

/** 查询指定用户的 oidc-center 应用会话；列表尚未同步时短暂重试。 */
export async function findActiveOidcCenterSession(
  request,
  { adminOrigin, username, clientId = ADMIN_OIDC_CENTER_CLIENT_ID }
) {
  const apiBase = resolveApiBase();
  const adminToken = await loginHostAdminAccessToken(request, 'vue');
  const deadline = Date.now() + 15_000;
  while (Date.now() < deadline) {
    const listResponse = await request.get(
      `${apiBase}/api/v1/identity/online-sessions?page=1&pageSize=50&usernameContains=${encodeURIComponent(username)}`,
      {
        headers: {
          authorization: `Bearer ${adminToken}`,
          origin: adminOrigin
        }
      }
    );
    expect(listResponse.ok()).toBeTruthy();
    const body = await listResponse.json();
    const target = (body.items ?? []).find(item => item.clientId === clientId);
    if (target !== undefined) {
      return target;
    }

    await new Promise(resolve => setTimeout(resolve, 250));
  }

  throw new Error(`未找到 clientId=${clientId} 的在线会话。`);
}

export async function revokeOnlineSessionById(request, { adminOrigin, sessionId }) {
  const apiBase = resolveApiBase();
  const adminToken = await loginHostAdminAccessToken(request, 'vue');
  const response = await request.post(
    `${apiBase}/api/v1/identity/online-sessions/${sessionId}/revoke`,
    {
      headers: {
        authorization: `Bearer ${adminToken}`,
        origin: adminOrigin
      }
    }
  );
  expect(response.ok()).toBeTruthy();
  return response.json();
}