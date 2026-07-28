export interface AuditingAccessLog {
  id: string;
  occurredAtUtc: string;
  httpMethod: string;
  requestPath: string;
  statusCode: number;
  durationMs: number;
  userId: string | null;
  tenantId: string | null;
  traceId: string | null;
  clientIpFingerprint: string | null;
  isAuthenticated: boolean;
}

export interface AuditingAccessLogPage {
  items: AuditingAccessLog[];
  page: number;
  pageSize: number;
  total: number;
}

export interface AuditingAccessLogCursorPage {
  items: AuditingAccessLog[];
  nextCursor: string | null;
  hasMore: boolean;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isAuditingAccessLog(value: unknown): value is AuditingAccessLog {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.occurredAtUtc === 'string'
    && typeof value.httpMethod === 'string'
    && typeof value.requestPath === 'string'
    && Number.isInteger(value.statusCode)
    && Number.isInteger(value.durationMs)
    && (value.userId === null || isGuid(value.userId))
    && (value.tenantId === null || isGuid(value.tenantId))
    && (value.traceId === null || typeof value.traceId === 'string')
    && (value.clientIpFingerprint === null || typeof value.clientIpFingerprint === 'string')
    && typeof value.isAuthenticated === 'boolean';
}

export function isAuditingAccessLogPage(value: unknown): value is AuditingAccessLogPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isAuditingAccessLog)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

export function isAuditingAccessLogCursorPage(
  value: unknown
): value is AuditingAccessLogCursorPage {
  if (!isRecord(value)
    || !Array.isArray(value.items)
    || !value.items.every(isAuditingAccessLog)
    || typeof value.hasMore !== 'boolean') {
    return false;
  }

  return value.hasMore
    ? typeof value.nextCursor === 'string' && value.nextCursor.length > 0
    : value.nextCursor === null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}
