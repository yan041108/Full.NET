import type { HttpClient } from './http.js';

/** 关联字段均为可空；没有采集不等于空 GUID 或空字符串。 */
export interface LogCorrelation {
  traceId: string | null;
  spanId: string | null;
  parentSpanId: string | null;
  requestId: string | null;
  userId: string | null;
  tenantId: string | null;
  sessionId: string | null;
  centerSessionId: string | null;
  applicationSessionId: string | null;
  clientIpFingerprint: string | null;
}
export interface LogTimelineEntry {
  id: string;
  source: string;
  occurredAtUtc: string;
  summary: string;
  correlation: LogCorrelation;
}
export interface LogTimelineResponse {
  fromUtc: string;
  toUtc: string;
  entries: LogTimelineEntry[];
  sources: Array<{ source: string; status: string }>;
  partial: boolean;
}
export interface LogTimelineQuery {
  fromUtc: string;
  toUtc: string;
  traceId?: string;
  sessionId?: string;
  userId?: string;
  clientIpFingerprint?: string;
  limit?: number;
}
const record = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);
const date = (value: unknown): value is string => typeof value === 'string' && Number.isFinite(Date.parse(value));
const correlationKeys = ['traceId', 'spanId', 'parentSpanId', 'requestId', 'userId', 'tenantId',
  'sessionId', 'centerSessionId', 'applicationSessionId', 'clientIpFingerprint'];
const correlation = (value: unknown): boolean => record(value)
  && correlationKeys.every(key => value[key] === null || typeof value[key] === 'string');
export function isLogTimelineResponse(value: unknown): value is LogTimelineResponse {
  return record(value) && date(value.fromUtc) && date(value.toUtc) && typeof value.partial === 'boolean'
    && Array.isArray(value.entries) && value.entries.length <= 200 && value.entries.every(entry =>
      record(entry) && typeof entry.id === 'string' && typeof entry.source === 'string'
      && date(entry.occurredAtUtc) && typeof entry.summary === 'string' && correlation(entry.correlation))
    && Array.isArray(value.sources) && value.sources.every(source => record(source)
      && typeof source.source === 'string' && typeof source.status === 'string');
}
export async function queryLogTimeline(http: HttpClient, query: LogTimelineQuery, signal?: AbortSignal): Promise<LogTimelineResponse> {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) if (value !== undefined && value !== '') params.set(key, String(value));
  const result = await http.request<unknown>(`/api/v1/auditing/timeline?${params}`, { method: 'GET' }, signal);
  if (!isLogTimelineResponse(result)) throw new Error('client.invalid_log_timeline');
  return result;
}
