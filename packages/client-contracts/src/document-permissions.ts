export interface HostDocumentPermissionEntry {
  userId: string;
  actions: ('read' | 'write' | 'delete' | 'share')[];
}

export interface SetHostDocumentPermissionsRequest {
  documentId: string;
  permissions: HostDocumentPermissionEntry[];
  visibilityMode: 'host' | 'restricted';
  version: number;
}

export interface HostDocumentPermissionResponse {
  id: string;
  documentId: string;
  userId: string;
  actions: ('read' | 'write' | 'delete' | 'share')[];
  createdAtUtc: string;
}

export interface HostDocumentPermissionSnapshotResponse {
  visibilityMode: 'host' | 'restricted';
  documentId: string;
  version: number;
  permissions: HostDocumentPermissionResponse[];
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
    && Array.isArray(value.actions) && value.actions.every((action: unknown) => ['read', 'write', 'delete', 'share'].includes(String(action)));
}

export function isSetHostDocumentPermissionsRequest(value: unknown): value is SetHostDocumentPermissionsRequest {
  return isRecord(value)
    && isGuid(value.documentId)
    && Number.isSafeInteger(value.version) && (value.version as number) >= 1
    && (value.version as number) < Number.MAX_SAFE_INTEGER
    && Array.isArray(value.permissions)
    && value.permissions.every(isHostDocumentPermissionEntry)
    && ['host', 'restricted'].includes(String(value.visibilityMode)) && Number.isInteger(value.version);
}

export function isHostDocumentPermissionResponse(value: unknown): value is HostDocumentPermissionResponse {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.documentId)
    && isGuid(value.userId)
    && Array.isArray(value.actions) && value.actions.every((action: unknown) => ['read', 'write', 'delete', 'share'].includes(String(action)))
    && typeof value.createdAtUtc === 'string';
}

export function isHostDocumentPermissionResponseList(value: unknown): value is HostDocumentPermissionResponse[] {
  return Array.isArray(value) && value.every(isHostDocumentPermissionResponse);
}

export function isHostDocumentPermissionSnapshotResponse(value: unknown): value is HostDocumentPermissionSnapshotResponse {
  return isRecord(value) && isGuid(value.documentId)
    && Number.isSafeInteger(value.version) && (value.version as number) >= 1
    && (value.version as number) < Number.MAX_SAFE_INTEGER
    && ['host', 'restricted'].includes(String(value.visibilityMode))
    && isHostDocumentPermissionResponseList(value.permissions)
    && value.permissions.every(entry => entry.documentId.toLowerCase() === (value.documentId as string).toLowerCase());
}
