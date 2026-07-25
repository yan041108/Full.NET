export interface AuditingOperationLog {
  id: string;
  occurredAtUtc: string;
  actionKey: string;
  httpMethod: string;
  requestPath: string;
  statusCode: number;
  durationMs: number;
  succeeded: boolean;
  userId: string | null;
  tenantId: string | null;
  traceId: string | null;
  clientIpFingerprint: string | null;
  permissionCode: string | null;
}

export interface AuditingOperationLogPage {
  items: AuditingOperationLog[];
  page: number;
  pageSize: number;
  total: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isAuditingOperationLog(value: unknown): value is AuditingOperationLog {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.occurredAtUtc === 'string'
    && typeof value.actionKey === 'string'
    && typeof value.httpMethod === 'string'
    && typeof value.requestPath === 'string'
    && Number.isInteger(value.statusCode)
    && Number.isInteger(value.durationMs)
    && typeof value.succeeded === 'boolean'
    && (value.userId === null || isGuid(value.userId))
    && (value.tenantId === null || isGuid(value.tenantId))
    && (value.traceId === null || typeof value.traceId === 'string')
    && (value.clientIpFingerprint === null || typeof value.clientIpFingerprint === 'string')
    && (value.permissionCode === null || typeof value.permissionCode === 'string');
}

export function isAuditingOperationLogPage(
  value: unknown
): value is AuditingOperationLogPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isAuditingOperationLog)
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
