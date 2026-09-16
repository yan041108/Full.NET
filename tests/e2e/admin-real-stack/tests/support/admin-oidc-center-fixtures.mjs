import { randomUUID } from 'node:crypto';
import { expect } from '@playwright/test';
import { adminOrigin, expandMainNavigation, loginHostAdminAccessToken } from './real-stack-auth.mjs';
import {
  CENTER_COOKIE_NAME,
  expectMeEndpointRejectsToken,
  expectRefreshTokenRejects,
  resolveApiBase
} from './identity-oidc-fixtures.mjs';

export const ADMIN_OIDC_CENTER_CLIENT_ID = 'e2e-admin-oidc-spa';
export const ADMIN_OIDC_CENTER_ORIGIN = 'http://localhost:25175';
export const E2E_AGENT_RUN_DEFINITION_KEY = 'fullnet-single-text-v1';

/** 构造 oidc-center 真实栈 API 请求的授权与 Origin 头。 */
export function buildOidcCenterApiHeaders(accessToken, adminOrigin = ADMIN_OIDC_CENTER_ORIGIN) {
  return {
    authorization: `Bearer ${accessToken}`,
    origin: adminOrigin
  };
}

export function buildOidcCenterJsonHeaders(accessToken, adminOrigin = ADMIN_OIDC_CENTER_ORIGIN) {
  return {
    ...buildOidcCenterApiHeaders(accessToken, adminOrigin),
    'content-type': 'application/json'
  };
}

/** 断言 oidc-center access token 对指定 GET API 的 HTTP 状态并返回响应。 */
export async function expectOidcApiGetStatus(request, accessToken, url, expectedStatus) {
  const response = await request.get(url, {
    headers: buildOidcCenterApiHeaders(accessToken)
  });
  expect(response.status()).toBe(expectedStatus);
  return response;
}

/** 断言 oidc-center access token 对指定 JSON POST API 的 HTTP 状态并返回响应。 */
export async function expectOidcApiPostStatus(
  request,
  accessToken,
  url,
  expectedStatus,
  data = {}
) {
  const response = await request.post(url, {
    headers: buildOidcCenterJsonHeaders(accessToken),
    data
  });
  expect(response.status()).toBe(expectedStatus);
  return response;
}

/** 通过 legacy Host 令牌创建 ping 后台任务定义，供 oidc-center 探针复用。 */
export async function createE2eHostPingJobDefinition(
  request,
  { jobKey, displayName, description, groupName = 'e2e' }
) {
  const apiBase = resolveApiBase();
  const setupToken = await loginHostAdminAccessToken(request, 'vue');
  const setupOrigin = adminOrigin('vue');
  const createResponse = await request.post(`${apiBase}/api/v1/jobs/host-definitions`, {
    headers: {
      authorization: `Bearer ${setupToken}`,
      'content-type': 'application/json',
      origin: setupOrigin
    },
    data: {
      jobKey,
      handlerKind: 'ping',
      args: null,
      displayName,
      description,
      groupName,
      allowConcurrentExecutions: false
    }
  });
  expect(createResponse.status()).toBe(201);
  return createResponse.json();
}

/** 通过 legacy Host 令牌创建 Agent 模型配置，供 oidc-center Agent Run 探针复用。 */
export async function createE2eAiAgentModelConfig(request, { displayName }) {
  const apiBase = resolveApiBase();
  const setupToken = await loginHostAdminAccessToken(request, 'vue');
  const setupOrigin = adminOrigin('vue');
  const createResponse = await request.post(`${apiBase}/api/v1/ai/model-configs`, {
    headers: {
      authorization: `Bearer ${setupToken}`,
      'content-type': 'application/json',
      origin: setupOrigin
    },
    data: {
      tenantId: null,
      name: displayName,
      providerKey: 'ollama',
      endpointBaseUrl: 'https://provider.test',
      modelId: 'model',
      apiKey: null,
      organizationId: null,
      isDefault: false,
      isEnabled: true
    }
  });
  expect(createResponse.status()).toBe(201);
  return createResponse.json();
}

/** 使用 oidc-center access token 创建排队中的 Agent Run。 */
export async function createOidcCenterQueuedAgentRun(
  request,
  accessToken,
  { modelConfigId, prompt }
) {
  const apiBase = resolveApiBase();
  const createResponse = await request.post(`${apiBase}/api/v1/ai/agent/runs`, {
    headers: buildOidcCenterJsonHeaders(accessToken),
    data: {
      clientRequestId: randomUUID(),
      definitionKey: E2E_AGENT_RUN_DEFINITION_KEY,
      modelConfigId,
      prompt,
      inputTokenLimit: 100,
      outputTokenLimit: 100
    }
  });
  expect(createResponse.status()).toBe(202);
  const body = await createResponse.json();
  expect(body.runId).toBeTruthy();
  return body;
}

/** 断言已撤销 OIDC 会话无法继续访问或新建 Agent Run。 */
export async function expectRevokedOidcCenterAgentRunAccessRejected(
  request,
  accessToken,
  { runId, modelConfigId, apiBase = resolveApiBase() }
) {
  await expectOidcApiGetStatus(
    request,
    accessToken,
    `${apiBase}/api/v1/ai/agent/runs/${runId}`,
    401
  );
  await expectOidcApiPostStatus(
    request,
    accessToken,
    `${apiBase}/api/v1/ai/agent/runs/${runId}/cancel`,
    401
  );
  await expectOidcApiPostStatus(
    request,
    accessToken,
    `${apiBase}/api/v1/ai/agent/runs/${runId}/resume`,
    401
  );
  await expectOidcApiPostStatus(
    request,
    accessToken,
    `${apiBase}/api/v1/ai/agent/runs`,
    401,
    {
      clientRequestId: randomUUID(),
      definitionKey: E2E_AGENT_RUN_DEFINITION_KEY,
      modelConfigId,
      prompt: 'oidc-center revoked token should not create agent run',
      inputTokenLimit: 100,
      outputTokenLimit: 100
    }
  );
}

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

/** §6 最小矩阵：退出／强撤后应拒绝直达的业务路由探针。 */
export const OIDC_CENTER_PROTECTED_ROUTE_PROBES = [
  { hashRoute: '/#/ai/agent-tools', headingName: 'Agent 工具' },
  { hashRoute: '/#/workflow/todos', headingName: '我的工作流待办' },
  { hashRoute: '/#/jobs/host-definitions', headingName: '任务定义' }
];

/** 未认证时直接访问受保护路由应回到 oidc-center 登录且目标页标题不渲染。 */
export async function expectProtectedRouteRedirectsToOidcLogin(
  page,
  hashRoute,
  { headingName, headingExact = true } = {}
) {
  await page.goto(hashRoute);
  await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 15_000 });
  if (headingName !== undefined) {
    await expect(page.getByRole('heading', { name: headingName, exact: headingExact })).toHaveCount(0);
  }
}

/** 批量断言 §6 受保护业务路由在会话结束后不可达。 */
export async function expectProtectedRoutesRedirectToOidcLogin(
  page,
  probes = OIDC_CENTER_PROTECTED_ROUTE_PROBES
) {
  for (const probe of probes) {
    await expectProtectedRouteRedirectsToOidcLogin(page, probe.hashRoute, {
      headingName: probe.headingName
    });
  }
}

/** 断言 oidc-center 本地 refresh 凭据与中心 Cookie 已清除。 */
export async function expectOidcCenterLocalCredentialsCleared(page, context) {
  expect(await page.evaluate(() => sessionStorage.getItem('fullnet.admin.oidc.refresh'))).toBeNull();
  const centerCookie = (await context.cookies(resolveApiBase()))
    .find(cookie => cookie.name === CENTER_COOKIE_NAME);
  expect(centerCookie).toBeUndefined();
}

/** 断言已失效 access/refresh token 无法再刷新或访问 /me。 */
export async function expectOidcCenterTokensRejected(request, { accessToken, refreshToken }) {
  await expectRefreshTokenRejects(request, {
    apiBase: resolveApiBase(),
    clientId: ADMIN_OIDC_CENTER_CLIENT_ID,
    refreshToken
  });
  await expectMeEndpointRejectsToken(request, accessToken);
}

/** 读取当前页面持久化的 oidc-center refresh 凭据。 */
export async function readOidcRefreshCredentialFromPage(page) {
  const refreshCredentialRaw = await page.evaluate(() => sessionStorage.getItem('fullnet.admin.oidc.refresh'));
  expect(refreshCredentialRaw).toBeTruthy();
  return {
    refreshCredentialRaw,
    refreshCredential: JSON.parse(refreshCredentialRaw)
  };
}

/** 写回已撤销 refresh 后刷新仍应回到登录页且 token 拒绝。 */
export async function expectStaleOidcRefreshCannotRestoreSession(
  page,
  request,
  refreshCredentialRaw,
  { accessToken, refreshToken }
) {
  await page.evaluate(credential => {
    sessionStorage.setItem('fullnet.admin.oidc.refresh', credential);
  }, refreshCredentialRaw);
  await page.reload();
  await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 30_000 });
  await expect(page.getByRole('navigation', { name: '主导航' })).toHaveCount(0);
  await expectOidcCenterTokensRejected(request, { accessToken, refreshToken });
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

/** 等待实时连接后强制下线当前 oidc-center 会话，并断言回到登录页。 */
export async function revokeCurrentOidcCenterSession(
  page,
  request,
  {
    username,
    adminOrigin = ADMIN_OIDC_CENTER_ORIGIN,
    clientId = ADMIN_OIDC_CENTER_CLIENT_ID
  }
) {
  await waitForNotificationsRealtimeConnection(page);
  const session = await findActiveOidcCenterSession(request, {
    adminOrigin,
    username,
    clientId
  });
  await revokeOnlineSessionById(request, {
    adminOrigin,
    sessionId: session.id
  });
  await expect(page.getByTestId('login-oidc-center')).toBeVisible({ timeout: 30_000 });
}