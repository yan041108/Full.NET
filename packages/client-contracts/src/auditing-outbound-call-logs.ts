export interface AuditingOutboundCallLog {
  id: string;
  occurredAtUtc: string;
  providerKey: string;
  operationKey: string;
  destinationHostCategory: string;
  statusCode: number;
  succeeded: boolean;
  durationMs: number;
  retryCount: number;
  traceId: string | null;
  safeErrorCode: string | null;
  tenantId: string | null;
  userId: string | null;
}

export interface AuditingOutboundCallLogPage {
  items: AuditingOutboundCallLog[];
  page: number;
  pageSize: number;
  total: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isAuditingOutboundCallLog(
  value: unknown
): value is AuditingOutboundCallLog {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.occurredAtUtc === 'string'
    && typeof value.providerKey === 'string'
    && typeof value.operationKey === 'string'
    && typeof value.destinationHostCategory === 'string'
    && Number.isInteger(value.statusCode)
    && typeof value.succeeded === 'boolean'
    && Number.isInteger(value.durationMs)
    && Number.isInteger(value.retryCount)
    && (value.traceId === null || typeof value.traceId === 'string')
    && (value.safeErrorCode === null || typeof value.safeErrorCode === 'string')
    && (value.tenantId === null || isGuid(value.tenantId))
    && (value.userId === null || isGuid(value.userId));
}

export function isAuditingOutboundCallLogPage(
  value: unknown
): value is AuditingOutboundCallLogPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isAuditingOutboundCallLog)
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
