import {
  type CreateGoViewProjectRequest,
  type GoViewProject,
  type GoViewProjectPreview,
  type GoViewProjectVersion,
  type PreviewGoViewProjectRequest,
  type PublishGoViewProjectRequest,
  type UpdateGoViewProjectRequest
} from '@fullnet/client-contracts';
import { request } from './http';
import { DEFAULT_GOVIEW_CANVAS, goViewTrim, validGoViewId, validGoViewProject, validGoViewPublishedVersion, validGoViewPreview, validGoViewUpdate, validGoViewWriteVersion, validGoViewVersionNumber } from '../guards/goview-guards';

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
  if (!Array.isArray(value) || !value.every(validGoViewProject) || new Set(value.map(row => row.id.toLowerCase())).size !== value.length) {
    throw new Error('client.invalid_goview_project_list');
  }
  return value;
}

export async function getGoViewProject(projectId: string, signal?: AbortSignal): Promise<GoViewProject> {
  if (!validGoViewId(projectId)) throw new Error('client.invalid_goview_project');
  const value = await request<unknown>(
    `/api/v1/goview/projects/${encodeURIComponent(projectId)}`,
    { method: 'GET' },
    signal
  );
  if (!validGoViewProject(value) || value.id.toLowerCase() !== projectId.toLowerCase()) {
    throw new Error('client.invalid_goview_project');
  }
  return value;
}

export async function createGoViewProject(
  body: CreateGoViewProjectRequest,
  signal?: AbortSignal
): Promise<GoViewProject> {
  const payload = { ...body };
  const value = await request<unknown>('/api/v1/goview/projects', { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload) }, signal);
  if (!validGoViewProject(value) || value.version !== 1 || value.latestPublishedVersionNumber !== 0 || value.projectKey !== goViewTrim(payload.projectKey) || value.name !== goViewTrim(payload.name) || value.isEnabled !== payload.isEnabled || value.canvasJson !== (payload.canvasJson == null || !goViewTrim(payload.canvasJson) ? DEFAULT_GOVIEW_CANVAS : goViewTrim(payload.canvasJson))) {
    throw new Error('client.invalid_goview_project');
  }
  return value;
}

export async function updateGoViewProject(
  projectId: string,
  body: UpdateGoViewProjectRequest,
  signal?: AbortSignal
): Promise<GoViewProject> {
  const payload = { ...body };
  if (!validGoViewId(projectId) || !validGoViewWriteVersion(payload.version)) throw new Error('client.invalid_goview_project');
  const value = await request<unknown>(
    `/api/v1/goview/projects/${encodeURIComponent(projectId)}`,
    { method: 'PUT', headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload) },
    signal
  );
  if (!validGoViewUpdate(value, projectId, payload)) {
    throw new Error('client.invalid_goview_project');
  }
  return value;
}

export async function publishGoViewProject(
  projectId: string,
  body: PublishGoViewProjectRequest,
  signal?: AbortSignal
): Promise<GoViewProjectVersion> {
  const payload = { ...body }; if (!validGoViewId(projectId) || !validGoViewWriteVersion(payload.version)) throw new Error('client.invalid_goview_project_version');
  const value = await request<unknown>(
    `/api/v1/goview/projects/${encodeURIComponent(projectId)}/publish`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload) },
    signal
  );
  if (!validGoViewPublishedVersion(value) || value.projectId.toLowerCase() !== projectId.toLowerCase() || value.changeNote !== (payload.changeNote == null ? null : goViewTrim(payload.changeNote) || null)) throw new Error('client.invalid_goview_project_version');
  return value;
}

export async function previewGoViewProject(
  projectId: string,
  body: PreviewGoViewProjectRequest = {},
  signal?: AbortSignal
): Promise<GoViewProjectPreview> {
  const payload = { ...body }; if (!validGoViewId(projectId) || payload.versionNumber != null && !validGoViewVersionNumber(payload.versionNumber)) throw new Error('client.invalid_goview_project_preview');
  const value = await request<unknown>(
    `/api/v1/goview/projects/${encodeURIComponent(projectId)}/preview`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(payload) },
    signal
  );
  if (!validGoViewPreview(value, projectId, payload.versionNumber)) {
    throw new Error('client.invalid_goview_project_preview');
  }
  return value;
}
