export type HostDocumentPermissionType = 1 | 2 | 3 | 4 | 5;

export const HOST_DOCUMENT_PERMISSION_TYPES = {
  View: 1 as HostDocumentPermissionType,
  Download: 2 as HostDocumentPermissionType,
  Edit: 3 as HostDocumentPermissionType,
  Delete: 4 as HostDocumentPermissionType,
  Share: 5 as HostDocumentPermissionType,
} as const;

export type HostDocumentPermissionObjectType = 1 | 2 | 3;

export const HOST_DOCUMENT_PERMISSION_OBJECT_TYPES = {
  User: 1 as HostDocumentPermissionObjectType,
  OrganizationUnit: 2 as HostDocumentPermissionObjectType,
  Role: 3 as HostDocumentPermissionObjectType,
} as const;

export interface HostDocumentPermissionEntryRequest {
  permissionType: number;
  objectType: number;
  objectId: string;
}

export interface SetHostDocumentPermissionsRequest {
  documentId: string;
  entries: HostDocumentPermissionEntryRequest[];
}

export interface HostDocumentPermissionResponse {
  id: string;
  documentId: string;
  permissionType: number;
  objectType: number;
  objectId: string;
  objectName: string | null;
  createdAtUtc: string;
  createdByUserId: string;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

export function isHostDocumentPermissionType(value: unknown): value is HostDocumentPermissionType {
  return typeof value === 'number' && value >= 1 && value <= 5;
}

export function isHostDocumentPermissionObjectType(value: unknown): value is HostDocumentPermissionObjectType {
  return typeof value === 'number' && value >= 1 && value <= 3;
}

export function isHostDocumentPermissionEntryRequest(value: unknown): value is HostDocumentPermissionEntryRequest {
  return isRecord(value)
    && Number.isInteger(value.permissionType)
    && Number.isInteger(value.objectType)
    && isGuid(value.objectId);
}

export function isSetHostDocumentPermissionsRequest(value: unknown): value is SetHostDocumentPermissionsRequest {
  return isRecord(value)
    && isGuid(value.documentId)
    && Array.isArray(value.entries)
    && value.entries.every(isHostDocumentPermissionEntryRequest);
}

export function isHostDocumentPermissionResponse(value: unknown): value is HostDocumentPermissionResponse {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.documentId)
    && Number.isInteger(value.permissionType)
    && Number.isInteger(value.objectType)
    && isGuid(value.objectId)
    && isNullableString(value.objectName)
    && typeof value.createdAtUtc === 'string'
    && isGuid(value.createdByUserId);
}

export function isHostDocumentPermissionResponseList(value: unknown): value is HostDocumentPermissionResponse[] {
  return Array.isArray(value) && value.every(isHostDocumentPermissionResponse);
}
