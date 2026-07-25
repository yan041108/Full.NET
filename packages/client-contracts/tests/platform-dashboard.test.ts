import { describe, expect, it } from 'vitest';
import {
  isHostDashboardActivity,
  isHostDashboardSummary
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
    expect(isHostDashboardSummary({
      activeTenantCount: 3,
      onlineSessionCount: 12,
      todayRequestCount: 420,
      todayErrorRate: 0.01,
      recentActivities: [activity]
    })).toBe(true);
  });

  it('rejects invalid error rates', () => {
    expect(isHostDashboardSummary({
      activeTenantCount: 3,
      onlineSessionCount: 12,
      todayRequestCount: 420,
      todayErrorRate: 'bad',
      recentActivities: []
    })).toBe(false);
  });
});
