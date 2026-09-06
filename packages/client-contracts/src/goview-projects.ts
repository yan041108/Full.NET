export interface GoViewProject {
  id: string;
  projectKey: string;
  name: string;
  canvasJson: string;
  latestPublishedVersionNumber: number;
  isEnabled: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface CreateGoViewProjectRequest {
  projectKey: string;
  name: string;
  canvasJson?: string | null;
  isEnabled: boolean;
}

export interface UpdateGoViewProjectRequest {
  name: string;
  canvasJson: string;
  isEnabled: boolean;
  version: number;
}

export interface PublishGoViewProjectRequest {
  changeNote?: string | null;
  version: number;
}

export interface GoViewProjectVersion {
  id: string;
  projectId: string;
  versionNumber: number;
  canvasJson: string;
  changeNote: string | null;
  publishedByUserId: string;
  publishedAtUtc: string;
}

export interface PreviewGoViewProjectRequest {
  versionNumber?: number | null;
}

export interface GoViewProjectPreview {
  projectId: string;
  projectKey: string;
  projectName: string;
  versionNumber: number;
  canvasJson: string;
  generatedAtUtc: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isGoViewProject(value: unknown): value is GoViewProject {
  return (
    isRecord(value) &&
    isGuid(value.id) &&
    typeof value.projectKey === 'string' &&
    typeof value.name === 'string' &&
    typeof value.canvasJson === 'string' &&
    typeof value.latestPublishedVersionNumber === 'number' &&
    typeof value.isEnabled === 'boolean' &&
    typeof value.createdAtUtc === 'string' &&
    (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string') &&
    typeof value.version === 'number'
  );
}

export function isGoViewProjectPreview(value: unknown): value is GoViewProjectPreview {
  return (
    isRecord(value) &&
    isGuid(value.projectId) &&
    typeof value.projectKey === 'string' &&
    typeof value.projectName === 'string' &&
    typeof value.versionNumber === 'number' &&
    typeof value.canvasJson === 'string' &&
    typeof value.generatedAtUtc === 'string'
  );
}
