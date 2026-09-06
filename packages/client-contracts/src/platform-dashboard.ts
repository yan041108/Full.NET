export interface HostDashboardActivity {
  actionKey: string;
  httpMethod: string;
  requestPath: string;
  succeeded: boolean;
  occurredAtUtc: string;
}

export interface HostDashboardTrafficTrendBucket {
  bucketStartUtc: string;
  eventCount: number;
  errorCount: number;
}

export interface HostDashboardTrafficTrend {
  fromUtc: string;
  toUtc: string;
  bucketSizeMinutes: number;
  buckets: HostDashboardTrafficTrendBucket[];
}

export interface HostDashboardBusinessEntry {
  entryKey: string;
  count: number;
  routePath: string;
  requiredPermission: string;
}

export interface HostDashboardSummary {
  activeTenantCount: number | null;
  onlineSessionCount: number | null;
  todayRequestCount: number | null;
  todayErrorRate: number | null;
  recentActivities: HostDashboardActivity[] | null;
  accessTrafficTrend: HostDashboardTrafficTrend | null;
  businessEntries: HostDashboardBusinessEntry[];
}

export function isHostDashboardActivity(
  value: unknown
): value is HostDashboardActivity {
  return isRecord(value)
    && typeof value.actionKey === 'string'
    && typeof value.httpMethod === 'string'
    && typeof value.requestPath === 'string'
    && typeof value.succeeded === 'boolean'
    && typeof value.occurredAtUtc === 'string';
}

export function isHostDashboardTrafficTrendBucket(
  value: unknown
): value is HostDashboardTrafficTrendBucket {
  return isRecord(value)
    && typeof value.bucketStartUtc === 'string'
    && Number.isInteger(value.eventCount)
    && Number.isInteger(value.errorCount);
}

export function isHostDashboardTrafficTrend(
  value: unknown
): value is HostDashboardTrafficTrend {
  return isRecord(value)
    && typeof value.fromUtc === 'string'
    && typeof value.toUtc === 'string'
    && Number.isInteger(value.bucketSizeMinutes)
    && Array.isArray(value.buckets)
    && value.buckets.every(isHostDashboardTrafficTrendBucket);
}

export function isHostDashboardBusinessEntry(
  value: unknown
): value is HostDashboardBusinessEntry {
  return isRecord(value)
    && typeof value.entryKey === 'string'
    && Number.isInteger(value.count)
    && typeof value.routePath === 'string'
    && typeof value.requiredPermission === 'string';
}

export function isHostDashboardSummary(
  value: unknown
): value is HostDashboardSummary {
  return isRecord(value)
    && isNullableInteger(value.activeTenantCount)
    && isNullableInteger(value.onlineSessionCount)
    && isNullableInteger(value.todayRequestCount)
    && isNullableNumber(value.todayErrorRate)
    && (value.recentActivities === null
      || (Array.isArray(value.recentActivities)
        && value.recentActivities.every(isHostDashboardActivity)))
    && (value.accessTrafficTrend === null
      || isHostDashboardTrafficTrend(value.accessTrafficTrend))
    && Array.isArray(value.businessEntries)
    && value.businessEntries.every(isHostDashboardBusinessEntry);
}

function isNullableInteger(value: unknown): boolean {
  return value === null || Number.isInteger(value);
}

function isNullableNumber(value: unknown): boolean {
  return value === null || typeof value === 'number';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
