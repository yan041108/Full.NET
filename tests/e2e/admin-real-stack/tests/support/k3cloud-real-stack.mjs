import { expect } from '@playwright/test';
import { adminOrigin, loginHostAdminAccessToken } from './real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const connectionsPath = `${apiBaseUrl}/api/v1/k3cloud/connection-configs`;
const documentSyncsPath = `${apiBaseUrl}/api/v1/k3cloud/document-syncs`;

/** 首切片固定单据：`SAL_SaleOrder`。 */
export const k3cloudSalSaleOrderDocumentTypeKey = 'k3cloud.sal_sale_order';

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

export async function listK3CloudConnectionConfigsViaApi(request, clientKind, accessToken = null) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.get(connectionsPath, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

export async function createK3CloudConnectionConfigViaApi(
  request,
  clientKind,
  body,
  accessToken = null
) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.post(connectionsPath, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

export async function testK3CloudConnectionConfigViaApi(
  request,
  clientKind,
  connectionConfigId,
  accessToken = null
) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.post(`${connectionsPath}/${connectionConfigId}/test`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

export async function listK3CloudDocumentSyncsViaApi(
  request,
  clientKind,
  accessToken = null
) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.get(`${documentSyncsPath}?page=1&pageSize=20`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

export async function getK3CloudDocumentSyncViaApi(
  request,
  clientKind,
  syncId,
  accessToken = null
) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.get(`${documentSyncsPath}/${syncId}`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

export async function createK3CloudDocumentSyncViaApi(
  request,
  clientKind,
  body,
  accessToken = null
) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.post(documentSyncsPath, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

/** 列表项不得回显密码明文。 */
export function expectK3CloudConnectionListItemMasked(item) {
  expect(item).toBeTruthy();
  expect(typeof item.hasPassword).toBe('boolean');
  expect(item).not.toHaveProperty('password');
}

export function buildK3CloudConnectionBody(suffix, { isEnabled = false } = {}) {
  return {
    name: `e2e-k3-${suffix}`,
    baseUrl: 'https://k3cloud.example.com/K3Cloud/',
    acctId: '100001',
    username: 'e2e-user',
    password: 'E2e-Not-Real-K3!',
    lcid: 2052,
    isDefault: false,
    isEnabled
  };
}
