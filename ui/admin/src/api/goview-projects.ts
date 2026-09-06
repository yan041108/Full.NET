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
  signal?: AbortSignal
): Promise<GoViewProject[]> {
  const params = new URLSearchParams();
  if (nameContains) {
    params.set('nameContains', nameContains);
  }
  const suffix = params.size > 0 ? `?${params.toString()}` : '';
  const value = await request<unknown>(`/api/v1/goview/projects${suffix}`, { method: 'GET' }, signal);
  if (!Array.isArray(value) || !value.every(isGoViewProject)) {
    throw new Error('client.invalid_goview_project_list');
  }
  return value;
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
  const value = await request<unknown>('/api/v1/goview/projects', { method: 'POST', body }, signal);
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
    { method: 'PUT', body },
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
    { method: 'POST', body },
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
    { method: 'POST', body },
    signal
  );
  if (!isGoViewProjectPreview(value)) {
    throw new Error('client.invalid_goview_project_preview');
  }
  return value;
}
