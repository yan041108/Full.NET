import { describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getHostJobHealth } from './host-job-health';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

describe('host-job-health api', () => {
  it('loads host job health', async () => {
    requestMock.mockResolvedValueOnce({
      registeredHandlers: ['ping', 'http'],
      backlog: {
        pendingCount: 2,
        oldestClaimableCreatedAtUtc: '2026-07-26T00:00:00Z',
        dueRetryCount: 0,
        oldestDueRetryAtUtc: null
      },
      workers: [{
        instanceId: '01912345-6789-7abc-8def-0123456789ab',
        hostProfile: 'api',
        startedAtUtc: '2026-07-26T00:00:00Z',
        lastHeartbeatAtUtc: '2026-07-26T00:01:00Z',
        workerVersion: '1.0.0',
        isStale: false
      }]
    });

    const health = await getHostJobHealth();
    expect(health.registeredHandlers).toContain('ping');
    expect(health.workers).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/jobs/host-health',
      { method: 'GET' },
      undefined
    );
  });
});
