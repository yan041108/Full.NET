import {
  queryAuditingAccessLogTrend,
  queryAuditingDomainChangeDiffs,
  queryAuditingExceptionLogTrend,
  queryAuditingOperationLogTrend,
  type AuditingDomainChangeDiffQueryResult,
  type AuditingLogTrend,
  type AuditingLogTrendQuery
} from '@fullnet/client-contracts';
import { http } from './http';

export type AuditLogTrendKind = 'access' | 'operation' | 'exception';

export async function queryAuditLogTrend(
  kind: AuditLogTrendKind,
  query: AuditingLogTrendQuery,
  signal?: AbortSignal
): Promise<AuditingLogTrend> {
  const options = signal ? { signal } : undefined;
  switch (kind) {
    case 'access':
      return queryAuditingAccessLogTrend(http, query, options);
    case 'operation':
      return queryAuditingOperationLogTrend(http, query, options);
    case 'exception':
      return queryAuditingExceptionLogTrend(http, query, options);
    default:
      throw new Error('client.invalid_audit_log_trend_kind');
  }
}

export async function queryDomainChangeDiffs(
  traceId: string,
  signal?: AbortSignal
): Promise<AuditingDomainChangeDiffQueryResult> {
  return queryAuditingDomainChangeDiffs(http, traceId, signal ? { signal } : undefined);
}

export function createDefaultTrendQuery(now = new Date()): AuditingLogTrendQuery {
  const toUtc = now.toISOString();
  const fromUtc = new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString();
  return { fromUtc, toUtc };
}
