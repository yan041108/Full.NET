import { isGuid, isGoViewProject, isGoViewProjectPreview, type GoViewProject, type GoViewProjectPreview, type GoViewProjectVersion, type UpdateGoViewProjectRequest } from '@fullnet/client-contracts';
export const DEFAULT_GOVIEW_CANVAS = '{"width":1920,"height":1080,"backgroundColor":"#0a1628","components":[]}';
export const goViewTrim = (value: string): string => value.replace(/^\p{White_Space}+|\p{White_Space}+$/gu, '');
export const validGoViewId = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
export const validGoViewVersionNumber = (value: unknown): value is number => Number.isInteger(value) && Number(value) > 0 && Number(value) <= 2147483647;
export const validGoViewWriteVersion = (value: unknown): value is number => validGoViewVersionNumber(value) && value < 2147483647;
export function validGoViewProject(value: unknown): value is GoViewProject {
  return isGoViewProject(value) && validGoViewId(value.id) && validGoViewVersionNumber(value.version) && Number.isInteger(value.latestPublishedVersionNumber) && value.latestPublishedVersionNumber >= 0 && value.latestPublishedVersionNumber <= 2147483647 && !!goViewTrim(value.projectKey);
}
export function validGoViewPublishedVersion(value: unknown): value is GoViewProjectVersion {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false; const row = value as Record<string, unknown>;
  return validGoViewId(row.id) && validGoViewId(row.projectId) && validGoViewVersionNumber(row.versionNumber) && typeof row.canvasJson === 'string' && (row.changeNote === null || typeof row.changeNote === 'string') && validGoViewId(row.publishedByUserId) && typeof row.publishedAtUtc === 'string';
}
export function validGoViewPreview(value: unknown, id: string, version?: number | null): value is GoViewProjectPreview {
  return isGoViewProjectPreview(value) && value.projectId.toLowerCase() === id.toLowerCase() && validGoViewVersionNumber(value.versionNumber) && (version == null || value.versionNumber === version) && !!goViewTrim(value.projectKey);
}
export function validGoViewUpdate(value: unknown, id: string, body: UpdateGoViewProjectRequest): value is GoViewProject {
  return validGoViewProject(value) && value.id.toLowerCase() === id.toLowerCase() && value.version === body.version + 1 && value.name === goViewTrim(body.name) && value.canvasJson === goViewTrim(body.canvasJson) && value.isEnabled === body.isEnabled;
}
/** 保存校验对齐服务端顶层结构，显示层另行投影，不能把任意组件载荷直接用于样式。 */
export function validGoViewCanvas(text: string): boolean {
  if (!text || text.length > 2 * 1024 * 1024) return false;
  try { const value: unknown = JSON.parse(text); if (!value || typeof value !== 'object' || Array.isArray(value)) return false; const row = value as Record<string, unknown>; return validGoViewVersionNumber(row.width) && validGoViewVersionNumber(row.height) && Array.isArray(row.components); } catch { return false; }
}
