export interface HostDashboardActivity {
  actionKey: string;
  httpMethod: string;
  requestPath: string;
  succeeded: boolean;
  occurredAtUtc: string;
}

export interface HostDashboardSummary {
  activeTenantCount: number;
  onlineSessionCount: number;
  todayRequestCount: number;
  todayErrorRate: number;
  recentActivities: HostDashboardActivity[];
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

export function isHostDashboardSummary(
  value: unknown
): value is HostDashboardSummary {
  return isRecord(value)
    && Number.isInteger(value.activeTenantCount)
    && Number.isInteger(value.onlineSessionCount)
    && Number.isInteger(value.todayRequestCount)
    && typeof value.todayErrorRate === 'number'
    && Array.isArray(value.recentActivities)
    && value.recentActivities.every(isHostDashboardActivity);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
