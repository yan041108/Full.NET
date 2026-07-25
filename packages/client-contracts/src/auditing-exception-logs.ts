export interface AuditingExceptionLog {
  id: string;
  occurredAtUtc: string;
  exceptionType: string;
  message: string;
  stackTrace: string | null;
  httpMethod: string | null;
  requestPath: string | null;
  userId: string | null;
  tenantId: string | null;
  traceId: string | null;
  clientIpFingerprint: string | null;
}

export interface AuditingExceptionLogPage {
  items: AuditingExceptionLog[];
  page: number;
  pageSize: number;
  total: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isAuditingExceptionLog(value: unknown): value is AuditingExceptionLog {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.occurredAtUtc === 'string'
    && typeof value.exceptionType === 'string'
    && typeof value.message === 'string'
    && (value.stackTrace === null || typeof value.stackTrace === 'string')
    && (value.httpMethod === null || typeof value.httpMethod === 'string')
    && (value.requestPath === null || typeof value.requestPath === 'string')
    && (value.userId === null || isGuid(value.userId))
    && (value.tenantId === null || isGuid(value.tenantId))
    && (value.traceId === null || typeof value.traceId === 'string')
    && (value.clientIpFingerprint === null || typeof value.clientIpFingerprint === 'string');
}

export function isAuditingExceptionLogPage(
  value: unknown
): value is AuditingExceptionLogPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isAuditingExceptionLog)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}
