import { expect } from '@playwright/test';
import { adminOrigin, loginHostAdminAccessToken } from './real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const providerConfigsPath = `${apiBaseUrl}/api/v1/ocr/provider-configs`;
const idCardTasksPath = `${apiBaseUrl}/api/v1/ocr/id-card-tasks`;

export const ocrPaddleIdCardProviderKey = 'paddle_ocr_id_card';

function authHeaders(clientKind, accessToken) {
  return {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(clientKind),
    'Content-Type': 'application/json'
  };
}

async function hostToken(request, clientKind, accessToken) {
  return accessToken ?? (await loginHostAdminAccessToken(request, clientKind));
}

export async function getOcrProviderConfigViaApi(
  request,
  clientKind,
  providerKey,
  accessToken = null
) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.get(`${providerConfigsPath}/${encodeURIComponent(providerKey)}`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

export async function testOcrProviderConfigViaApi(
  request,
  clientKind,
  providerKey,
  accessToken = null
) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.post(
    `${providerConfigsPath}/${encodeURIComponent(providerKey)}/test`,
    { headers: authHeaders(clientKind, token) }
  );
  return { response, accessToken: token };
}

export async function listOcrIdCardTasksViaApi(request, clientKind, accessToken = null) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.get(`${idCardTasksPath}?page=1&pageSize=20`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

export async function getOcrIdCardTaskViaApi(request, clientKind, taskId, accessToken = null) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.get(`${idCardTasksPath}/${taskId}`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

export async function createOcrIdCardTaskViaApi(request, clientKind, body, accessToken = null) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.post(idCardTasksPath, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

export async function confirmOcrIdCardTaskViaApi(
  request,
  clientKind,
  taskId,
  body,
  accessToken = null
) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.post(`${idCardTasksPath}/${taskId}/confirm`, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** Provider 响应不得回显 API Key。 */
export function expectOcrProviderConfigMasked(config) {
  expect(config).toBeTruthy();
  expect(typeof config.hasApiKey).toBe('boolean');
  expect(config).not.toHaveProperty('apiKey');
}
