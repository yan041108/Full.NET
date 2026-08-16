import { expect } from '@playwright/test';
import { adminOrigin, loginHostAdminAccessToken } from './real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const itemsPath = `${apiBaseUrl}/api/v1/document/host/items`;
const recycleBinPath = `${apiBaseUrl}/api/v1/document/host/recycle-bin`;
const sharesPath = `${apiBaseUrl}/api/v1/document/host/shares`;
const permissionsPath = `${apiBaseUrl}/api/v1/document/host/permissions`;
const statisticsPath = `${apiBaseUrl}/api/v1/document/host/statistics`;
const publicSharesPath = `${apiBaseUrl}/api/v1/document/public/shares`;

function authHeaders(clientKind, accessToken) {
  return {
    Authorization: `Bearer ${accessToken}`,
    Origin: adminOrigin(clientKind),
    'Content-Type': 'application/json'
  };
}

/** 经真实 API 创建 Host 文档项。 */
export async function createHostDocumentItemViaApi(request, clientKind, options = {}) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const title = options.title ?? `e2e-doc-${Date.now().toString(36)}`;
  const response = await request.post(itemsPath, {
    data: {
      title,
      description: options.description ?? 'real-stack document'
    },
    headers: authHeaders(clientKind, accessToken)
  });
  expect(response.status()).toBe(201);
  const body = await response.json();
  expect(typeof body.id).toBe('string');
  expect(body.title).toBe(title);
  return body;
}

/** 经真实 API 软删除 Host 文档项。 */
export async function deleteHostDocumentItemViaApi(request, clientKind, item) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(`${itemsPath}/${item.id}/delete`, {
    data: { version: item.version },
    headers: authHeaders(clientKind, accessToken)
  });
  expect(response.ok()).toBeTruthy();
  return response.json();
}

/** 经真实 API 创建无口令分享链接。 */
export async function createHostDocumentShareViaApi(request, clientKind, documentId, options = {}) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(sharesPath, {
    data: {
      documentId,
      validDays: options.validDays ?? 7,
      password: null,
      maxAccessCount: options.maxAccessCount ?? null
    },
    headers: authHeaders(clientKind, accessToken)
  });
  expect(response.status()).toBe(201);
  const body = await response.json();
  expect(typeof body.shareCode).toBe('string');
  return body;
}

/** 匿名 POST 访问分享链接。 */
export async function accessDocumentShareViaApi(request, shareCode, password = null) {
  return request.post(`${publicSharesPath}/${shareCode}/access`, {
    data: { password },
    headers: { 'Content-Type': 'application/json' }
  });
}

/** 经真实 API 读取 Host 文档统计。 */
export async function getHostDocumentStatisticsViaApi(request, clientKind, accessToken) {
  return request.get(statisticsPath, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind)
    }
  });
}

/** 经真实 API 读取文档权限列表。 */
export async function getHostDocumentPermissionsViaApi(request, clientKind, accessToken, documentId) {
  return request.get(`${permissionsPath}/by-document/${documentId}`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind)
    }
  });
}

/** 经真实 API 彻底删除回收站文档。 */
export async function purgeRecycleBinItemViaApi(request, clientKind, accessToken, documentId) {
  return request.post(`${recycleBinPath}/${documentId}/purge`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind)
    }
  });
}
