import type { HttpClient, RequestOptions } from './http.js';

export interface AuditingLogTrendBucket {
  bucketStartUtc: string;
  eventCount: number;
  errorCount: number;
}

export interface AuditingLogTrend {
  fromUtc: string;
  toUtc: string;
  bucketSizeMinutes: number;
  buckets: AuditingLogTrendBucket[];
  totalCount: number;
  bucketLimitReached: boolean;
}

export interface AuditingDomainChangeDiffField {
  fieldKey: string;
  beforeValue: string | null;
  afterValue: string | null;
}

export interface AuditingDomainChangeDiffEntry {
  auditId: string;
  moduleKey: string;
  actionKey: string;
  occurredAtUtc: string;
  availability: 'available' | 'no_diff_recorded' | 'unparseable';
  fields: AuditingDomainChangeDiffField[];
}

export interface AuditingDomainChangeDiffQueryResult {
  traceId: string;
  entries: AuditingDomainChangeDiffEntry[];
}

export interface AuditingLogTrendQuery {
  fromUtc: string;
  toUtc: string;
  bucketMinutes?: number;
}

const guidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

export function isAuditingLogTrendBucket(
  value: unknown
): value is AuditingLogTrendBucket {
  return isRecord(value)
    && typeof value.bucketStartUtc === 'string'
    && Number.isInteger(value.eventCount)
    && Number.isInteger(value.errorCount);
}

export function isAuditingLogTrend(value: unknown): value is AuditingLogTrend {
  return isRecord(value)
    && typeof value.fromUtc === 'string'
    && typeof value.toUtc === 'string'
    && Number.isInteger(value.bucketSizeMinutes)
    && Array.isArray(value.buckets)
    && value.buckets.every(isAuditingLogTrendBucket)
    && Number.isInteger(value.totalCount)
    && typeof value.bucketLimitReached === 'boolean';
}

export function isAuditingDomainChangeDiffField(
  value: unknown
): value is AuditingDomainChangeDiffField {
  return isRecord(value)
    && typeof value.fieldKey === 'string'
    && (value.beforeValue === null || typeof value.beforeValue === 'string')
    && (value.afterValue === null || typeof value.afterValue === 'string');
}

export function isAuditingDomainChangeDiffEntry(
  value: unknown
): value is AuditingDomainChangeDiffEntry {
  return isRecord(value)
    && isGuid(value.auditId)
    && typeof value.moduleKey === 'string'
    && typeof value.actionKey === 'string'
    && typeof value.occurredAtUtc === 'string'
    && (value.availability === 'available'
      || value.availability === 'no_diff_recorded'
      || value.availability === 'unparseable')
    && Array.isArray(value.fields)
    && value.fields.every(isAuditingDomainChangeDiffField);
}

export function isAuditingDomainChangeDiffQueryResult(
  value: unknown
): value is AuditingDomainChangeDiffQueryResult {
  return isRecord(value)
    && typeof value.traceId === 'string'
    && Array.isArray(value.entries)
    && value.entries.every(isAuditingDomainChangeDiffEntry);
}

function buildTrendQuery(query: AuditingLogTrendQuery): string {
  const params = new URLSearchParams({
    fromUtc: query.fromUtc,
    toUtc: query.toUtc
  });
  if (query.bucketMinutes !== undefined) {
    params.set('bucketMinutes', String(query.bucketMinutes));
  }
  return params.toString();
}

export async function queryAuditingAccessLogTrend(
  http: HttpClient,
  query: AuditingLogTrendQuery,
  options?: RequestOptions
): Promise<AuditingLogTrend> {
  const value = await http.get(
    `/api/v1/auditing/access-logs/trends?${buildTrendQuery(query)}`,
    options
  );
  if (!isAuditingLogTrend(value)) {
    throw new Error('client.invalid_auditing_access_log_trend');
  }
  return value;
}

export async function queryAuditingOperationLogTrend(
  http: HttpClient,
  query: AuditingLogTrendQuery,
  options?: RequestOptions
): Promise<AuditingLogTrend> {
  const value = await http.get(
    `/api/v1/auditing/operation-logs/trends?${buildTrendQuery(query)}`,
    options
  );
  if (!isAuditingLogTrend(value)) {
    throw new Error('client.invalid_auditing_operation_log_trend');
  }
  return value;
}

export async function queryAuditingExceptionLogTrend(
  http: HttpClient,
  query: AuditingLogTrendQuery,
  options?: RequestOptions
): Promise<AuditingLogTrend> {
  const value = await http.get(
    `/api/v1/auditing/exception-logs/trends?${buildTrendQuery(query)}`,
    options
  );
  if (!isAuditingLogTrend(value)) {
    throw new Error('client.invalid_auditing_exception_log_trend');
  }
  return value;
}

export async function queryAuditingDomainChangeDiffs(
  http: HttpClient,
  traceId: string,
  options?: RequestOptions
): Promise<AuditingDomainChangeDiffQueryResult> {
  const params = new URLSearchParams({ traceId });
  const value = await http.get(
    `/api/v1/auditing/domain-change-diffs?${params.toString()}`,
    options
  );
  if (!isAuditingDomainChangeDiffQueryResult(value)) {
    throw new Error('client.invalid_auditing_domain_change_diff');
  }
  return value;
}
