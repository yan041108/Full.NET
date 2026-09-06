export interface HostDocumentAccessLogResponse {
  id: string;
  documentItemId: string;
  documentTitle: string;
  accessTypeKey: string;
  sourceKey: string;
  actorUserId: string | null;
  occurredAtUtc: string;
  clientIpFingerprint: string | null;
}

export interface HostDocumentAccessLogPage {
  items: HostDocumentAccessLogResponse[];
  page: number;
  pageSize: number;
  total: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

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

export function isHostDocumentAccessLogResponse(value: unknown): value is HostDocumentAccessLogResponse {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.documentItemId)
    && typeof value.documentTitle === 'string'
    && typeof value.accessTypeKey === 'string'
    && typeof value.sourceKey === 'string'
    && isNullableGuid(value.actorUserId)
    && typeof value.occurredAtUtc === 'string'
    && isNullableString(value.clientIpFingerprint);
}

export function isHostDocumentAccessLogPage(value: unknown): value is HostDocumentAccessLogPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isHostDocumentAccessLogResponse)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}
