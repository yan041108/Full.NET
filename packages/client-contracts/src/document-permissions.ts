export interface HostDocumentPermissionEntry {
  userId: string;
  permissionLevel: string;
}

export interface SetHostDocumentPermissionsRequest {
  documentId: string;
  permissions: HostDocumentPermissionEntry[];
}

export interface HostDocumentPermissionResponse {
  id: string;
  documentId: string;
  userId: string;
  permissionLevel: string;
  createdAtUtc: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

export function isHostDocumentPermissionEntry(value: unknown): value is HostDocumentPermissionEntry {
  return isRecord(value)
    && isGuid(value.userId)
    && isNonEmptyString(value.permissionLevel);
}

export function isSetHostDocumentPermissionsRequest(value: unknown): value is SetHostDocumentPermissionsRequest {
  return isRecord(value)
    && isGuid(value.documentId)
    && Array.isArray(value.permissions)
    && value.permissions.every(isHostDocumentPermissionEntry);
}

export function isHostDocumentPermissionResponse(value: unknown): value is HostDocumentPermissionResponse {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.documentId)
    && isGuid(value.userId)
    && isNonEmptyString(value.permissionLevel)
    && typeof value.createdAtUtc === 'string';
}

export function isHostDocumentPermissionResponseList(value: unknown): value is HostDocumentPermissionResponse[] {
  return Array.isArray(value) && value.every(isHostDocumentPermissionResponse);
}
