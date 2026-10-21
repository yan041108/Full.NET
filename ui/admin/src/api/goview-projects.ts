import { isRecord, isInteger, type PagedResult, type GoViewProjectSummary, isGoViewProjectSummary } from '@fullnet/client-contracts';
import {
  isGoViewProject,
  isGoViewProjectPreview,
  type CreateGoViewProjectRequest,
  type GoViewProject,
  type GoViewProjectPreview,
  type PreviewGoViewProjectRequest,
  type PublishGoViewProjectRequest,
  type UpdateGoViewProjectRequest
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listGoViewProjects(
  nameContains?: string,
  signal?: AbortSignal, page = 1, pageSize = 20
): Promise<PagedResult<GoViewProjectSummary>> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (nameContains) {
    params.set('nameContains', nameContains);
  }
  const suffix = params.size > 0 ? `?${params.toString()}` : '';
  const value = await request<unknown>(`/api/v1/goview/projects${suffix}`, { method: 'GET' }, signal);
  if (!isRecord(value) || !Array.isArray(value.items) || !value.items.every(isGoViewProjectSummary) || !isInteger(value.page) || !isInteger(value.pageSize) || typeof value.total !== 'number') {
    throw new Error('client.invalid_goview_project_list');
  }
  return value as unknown as PagedResult<GoViewProjectSummary>;
}

export async function getGoViewProject(projectId: string, signal?: AbortSignal): Promise<GoViewProject> {
  const value = await request<unknown>(
    `/api/v1/goview/projects/${encodeURIComponent(projectId)}`,
    { method: 'GET' },
    signal
  );
  if (!isGoViewProject(value)) {
    throw new Error('client.invalid_goview_project');
  }
  return value;
}

export async function createGoViewProject(
  body: CreateGoViewProjectRequest,
  signal?: AbortSignal
): Promise<GoViewProject> {
  const value = await request<unknown>('/api/v1/goview/projects', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }, signal);
  if (!isGoViewProject(value)) {
    throw new Error('client.invalid_goview_project');
  }
  return value;
}

export async function updateGoViewProject(
  projectId: string,
  body: UpdateGoViewProjectRequest,
  signal?: AbortSignal
): Promise<GoViewProject> {
  const value = await request<unknown>(
    `/api/v1/goview/projects/${encodeURIComponent(projectId)}`,
    { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isGoViewProject(value)) {
    throw new Error('client.invalid_goview_project');
  }
  return value;
}

export async function publishGoViewProject(
  projectId: string,
  body: PublishGoViewProjectRequest,
  signal?: AbortSignal
): Promise<void> {
  await request<unknown>(
    `/api/v1/goview/projects/${encodeURIComponent(projectId)}/publish`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
}

export async function previewGoViewProject(
  projectId: string,
  body: PreviewGoViewProjectRequest = {},
  signal?: AbortSignal
): Promise<GoViewProjectPreview> {
  const value = await request<unknown>(
    `/api/v1/goview/projects/${encodeURIComponent(projectId)}/preview`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isGoViewProjectPreview(value)) {
    throw new Error('client.invalid_goview_project_preview');
  }
  return value;
}
