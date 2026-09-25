import { expect } from '@playwright/test';
import { adminOrigin, loginHostAdminAccessToken } from './real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const itemsPath = `${apiBaseUrl}/api/v1/document/host/items`;
const recycleBinPath = `${apiBaseUrl}/api/v1/document/host/recycle-bin`;
const sharesPath = `${apiBaseUrl}/api/v1/document/host/shares`;
const permissionsPath = `${apiBaseUrl}/api/v1/document/host/permissions`;
const statisticsPath = `${apiBaseUrl}/api/v1/document/host/statistics`;
const versionRetentionPath = `${apiBaseUrl}/api/v1/document/host/version-retention`;
const accessLogsPath = `${apiBaseUrl}/api/v1/document/host/access-logs`;
const previewTasksPath = `${apiBaseUrl}/api/v1/document/host/preview-tasks`;
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

/** 经 multipart 上传 Host 文档新版本。 */
export async function uploadHostDocumentVersionViaApi(
  request,
  clientKind,
  itemId,
  content,
  fileName = 'version.txt'
) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(`${itemsPath}/${itemId}/versions/upload`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind)
    },
    multipart: {
      file: {
        name: fileName,
        mimeType: 'text/plain',
        buffer: Buffer.from(content)
      }
    }
  });
  expect(response.ok()).toBeTruthy();
  return response.json();
}

/** 列出 Host 文档全部版本。 */
export async function listHostDocumentVersionsViaApi(request, clientKind, itemId) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.get(`${itemsPath}/${itemId}/versions`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind)
    }
  });
  expect(response.ok()).toBeTruthy();
  return response.json();
}

/** 删除非当前的历史版本（需 item 乐观锁 Version）。 */
export async function deleteHostDocumentVersionViaApi(
  request,
  clientKind,
  itemId,
  versionId,
  itemVersion
) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  return request.post(`${itemsPath}/${itemId}/versions/${versionId}/delete`, {
    data: { version: itemVersion },
    headers: authHeaders(clientKind, accessToken)
  });
}

/** 读取 Host 文档版本保留策略有效值。 */
export async function getHostDocumentVersionRetentionViaApi(request, clientKind) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  return request.get(versionRetentionPath, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind)
    }
  });
}

/** 将当前版本指针回滚到既有历史版本。 */
export async function rollbackHostDocumentVersionViaApi(
  request,
  clientKind,
  itemId,
  versionId,
  itemVersion
) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const response = await request.post(
    `${itemsPath}/${itemId}/versions/${versionId}/rollback`,
    {
      data: { version: itemVersion },
      headers: authHeaders(clientKind, accessToken)
    }
  );
  return response;
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

/** 上传 Office docx 版本（用于预览转换任务入队）。 */
export async function uploadHostDocumentOfficeVersionViaApi(
  request,
  clientKind,
  itemId,
  content = 'office-e2e',
  fileName = 'sample.docx'
) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  return request.post(`${itemsPath}/${itemId}/versions/upload`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind)
    },
    multipart: {
      file: {
        name: fileName,
        mimeType:
          'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        buffer: Buffer.from(content)
      }
    }
  });
}

/** 创建 Host 文档 Office 预览转换任务。 */
export async function createHostDocumentPreviewTaskViaApi(
  request,
  clientKind,
  documentItemId,
  versionId = null
) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  return request.post(previewTasksPath, {
    data: { documentItemId, versionId },
    headers: authHeaders(clientKind, accessToken)
  });
}

/** 分页查询 Host 文档预览转换任务。 */
export async function listHostDocumentPreviewTasksViaApi(
  request,
  clientKind,
  documentItemId = null
) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const params = new URLSearchParams({ page: '1', pageSize: '20' });
  if (documentItemId) {
    params.set('documentItemId', documentItemId);
  }
  return request.get(`${previewTasksPath}?${params.toString()}`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind)
    }
  });
}

/** 分页查询 Host 文档访问日志。 */
export async function listHostDocumentAccessLogsViaApi(
  request,
  clientKind,
  query = {}
) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  const params = new URLSearchParams({ page: '1', pageSize: '20' });
  if (query.documentItemId) {
    params.set('documentItemId', query.documentItemId);
  }
  if (query.accessTypeKey) {
    params.set('accessTypeKey', query.accessTypeKey);
  }
  return request.get(`${accessLogsPath}?${params.toString()}`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind)
    }
  });
}

/** 触发 Host 文档预览（可能 422，仍会记录访问日志）。 */
export async function previewHostDocumentItemViaApi(request, clientKind, itemId) {
  const accessToken = await loginHostAdminAccessToken(request, clientKind);
  return request.get(`${itemsPath}/${itemId}/preview`, {
    headers: {
      Authorization: `Bearer ${accessToken}`,
      Origin: adminOrigin(clientKind)
    }
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
