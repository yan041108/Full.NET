import { describe, expect, it } from 'vitest';
import {
  isHostDashboardActivity,
  isHostDashboardBusinessEntry,
  isHostDashboardSummary,
  isHostDashboardTrafficTrend
} from '../src/platform-dashboard';

describe('platform-dashboard contracts', () => {
  const activity = {
    actionKey: 'GET /api/v1/me',
    httpMethod: 'GET',
    requestPath: '/api/v1/me',
    succeeded: true,
    occurredAtUtc: '2026-07-26T00:00:00Z'
  };

  it('accepts valid dashboard payloads', () => {
    expect(isHostDashboardActivity(activity)).toBe(true);
    expect(isHostDashboardTrafficTrend({
      fromUtc: '2026-07-26T00:00:00Z',
      toUtc: '2026-07-26T12:00:00Z',
      bucketSizeMinutes: 60,
      buckets: [{
        bucketStartUtc: '2026-07-26T00:00:00Z',
        eventCount: 12,
        errorCount: 1
      }]
    })).toBe(true);
    expect(isHostDashboardBusinessEntry({
      entryKey: 'workflow.pending_todos',
      count: 3,
      routePath: '/workflow/todos',
      requiredPermission: 'workflow.todos.read'
    })).toBe(true);
    expect(isHostDashboardSummary({
      activeTenantCount: 3,
      onlineSessionCount: 12,
      todayRequestCount: 420,
      todayErrorRate: 0.01,
      recentActivities: [activity],
      accessTrafficTrend: {
        fromUtc: '2026-07-26T00:00:00Z',
        toUtc: '2026-07-26T12:00:00Z',
        bucketSizeMinutes: 60,
        buckets: []
      },
      businessEntries: []
    })).toBe(true);
  });

  it('accepts permission-redacted nullable metric fragments', () => {
    expect(isHostDashboardSummary({
      activeTenantCount: null,
      onlineSessionCount: null,
      todayRequestCount: 3,
      todayErrorRate: null,
      recentActivities: null,
      accessTrafficTrend: null,
      businessEntries: []
    })).toBe(true);
  });

  it('rejects invalid error rates', () => {
    expect(isHostDashboardSummary({
      activeTenantCount: 3,
      onlineSessionCount: 12,
      todayRequestCount: 420,
      todayErrorRate: 'bad',
      recentActivities: [],
      accessTrafficTrend: null,
      businessEntries: []
    })).toBe(false);
  });
});
