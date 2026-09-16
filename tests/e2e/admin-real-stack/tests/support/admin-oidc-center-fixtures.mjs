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