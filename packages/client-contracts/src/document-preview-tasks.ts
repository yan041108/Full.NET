export interface CreateHostDocumentPreviewTaskRequest {
  documentItemId: string;
  versionId?: string | null;
}

export interface HostDocumentPreviewTaskResponse {
  id: string;
  documentItemId: string;
  documentTitle: string;
  versionId: string | null;
  sourceFileId: string;
  outputFileId: string | null;
  statusKey: string;
  providerKey: string;
  errorCode: string | null;
  requestedByUserId: string;
  createdAtUtc: string;
  startedAtUtc: string | null;
  completedAtUtc: string | null;
  version: number;
}

export interface HostDocumentPreviewTaskPage {
  items: HostDocumentPreviewTaskResponse[];
  page: number;
  pageSize: number;
  total: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const officeMimeTypes = new Set([
  'application/msword',
  'application/vnd.ms-excel',
  'application/vnd.ms-powerpoint',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  'application/vnd.openxmlformats-officedocument.presentationml.presentation',
]);

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNullableGuid(value: unknown): value is string | null {
  return value === null || isGuid(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

/** 判断 MIME 是否属于可提交 Office 预览转换的白名单。 */
export function isOfficePreviewMimeType(contentType: string | null | undefined): boolean {
  if (!contentType) {
    return false;
  }

  const normalized = contentType.split(';', 1)[0]?.trim().toLowerCase() ?? '';
  return officeMimeTypes.has(normalized);
}

export function isCreateHostDocumentPreviewTaskRequest(
  value: unknown
): value is CreateHostDocumentPreviewTaskRequest {
  return isRecord(value)
    && isGuid(value.documentItemId)
    && (value.versionId === undefined || isNullableGuid(value.versionId));
}

export function isHostDocumentPreviewTaskResponse(
  value: unknown
): value is HostDocumentPreviewTaskResponse {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.documentItemId)
    && typeof value.documentTitle === 'string'
    && isNullableGuid(value.versionId)
    && isGuid(value.sourceFileId)
    && isNullableGuid(value.outputFileId)
    && typeof value.statusKey === 'string'
    && typeof value.providerKey === 'string'
    && isNullableString(value.errorCode)
    && isGuid(value.requestedByUserId)
    && typeof value.createdAtUtc === 'string'
    && isNullableString(value.startedAtUtc)
    && isNullableString(value.completedAtUtc)
    && Number.isInteger(value.version);
}

export function isHostDocumentPreviewTaskPage(value: unknown): value is HostDocumentPreviewTaskPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostDocumentPreviewTaskResponse)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}
