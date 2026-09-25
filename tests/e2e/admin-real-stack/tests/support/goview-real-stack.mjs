import { expect } from '@playwright/test';
import { adminOrigin, loginHostAdminAccessToken } from './real-stack-auth.mjs';

const apiBaseUrl = process.env.FULLNET_E2E_API_URL ?? 'http://localhost:5149';
const projectsPath = `${apiBaseUrl}/api/v1/goview/projects`;

/** 与 `GoViewCanvasPolicy.DefaultCanvasJson` 一致。 */
export const goviewDefaultCanvasJson =
  '{"width":1920,"height":1080,"backgroundColor":"#0a1628","components":[]}';

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

export async function listGoViewProjectsViaApi(request, clientKind, accessToken = null) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.get(projectsPath, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

export async function getGoViewProjectViaApi(request, clientKind, projectId, accessToken = null) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.get(`${projectsPath}/${projectId}`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

export async function createGoViewProjectViaApi(request, clientKind, body, accessToken = null) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.post(projectsPath, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

export async function updateGoViewProjectViaApi(
  request,
  clientKind,
  projectId,
  body,
  accessToken = null
) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.put(`${projectsPath}/${projectId}`, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

export async function publishGoViewProjectViaApi(
  request,
  clientKind,
  projectId,
  body,
  accessToken = null
) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.post(`${projectsPath}/${projectId}/publish`, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

export async function previewGoViewProjectViaApi(
  request,
  clientKind,
  projectId,
  body = {},
  accessToken = null
) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.post(`${projectsPath}/${projectId}/preview`, {
    headers: authHeaders(clientKind, token),
    data: body
  });
  return { response, accessToken: token };
}

export async function listGoViewProjectVersionsViaApi(
  request,
  clientKind,
  projectId,
  accessToken = null
) {
  const token = await hostToken(request, clientKind, accessToken);
  const response = await request.get(`${projectsPath}/${projectId}/versions`, {
    headers: authHeaders(clientKind, token)
  });
  return { response, accessToken: token };
}

/** 预览响应仅含已发布快照字段，不得夹带任意查询端点。 */
export function expectGoViewPreviewReadOnlyShape(preview) {
  expect(preview).toBeTruthy();
  expect(typeof preview.projectId).toBe('string');
  expect(typeof preview.canvasJson).toBe('string');
  expect(preview).not.toHaveProperty('dataSource');
  expect(preview).not.toHaveProperty('sql');
}
