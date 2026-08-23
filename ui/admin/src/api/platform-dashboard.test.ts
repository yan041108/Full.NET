import { describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getHostDashboardSummary } from './platform-dashboard';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

describe('platform-dashboard api', () => {
  it('loads host dashboard summary', async () => {
    requestMock.mockResolvedValueOnce({
      activeTenantCount: 2,
      onlineSessionCount: 1,
      todayRequestCount: 42,
      todayErrorRate: 0.05,
      recentActivities: [{
        actionKey: 'settings.config-entry.updated',
        httpMethod: 'PUT',
        requestPath: '/api/v1/settings/config-entries/1',
        succeeded: true,
        occurredAtUtc: '2026-07-25T08:00:00.000Z'
      }]
    });

    const summary = await getHostDashboardSummary();
    expect(summary.activeTenantCount).toBe(2);
    expect(summary.recentActivities).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/platform/host-dashboard-summary',
      { method: 'GET' },
      undefined
    );
  });
});
