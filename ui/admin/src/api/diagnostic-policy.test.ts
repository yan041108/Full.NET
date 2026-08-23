import { describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getDiagnosticPolicy } from './diagnostic-policy';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

describe('diagnostic-policy api', () => {
  it('loads current policy', async () => {
    requestMock.mockResolvedValueOnce({
      version: 0,
      pressureState: 'Normal',
      isDefault: true,
      loadedAtUtc: new Date().toISOString(),
      activeRules: [],
      configEntryVersion: 0
    });

    const policy = await getDiagnosticPolicy();
    expect(policy.isDefault).toBe(true);
    expect(policy.pressureState).toBe('Normal');
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/settings/diagnostic-policy',
      { method: 'GET' },
      undefined
    );
  });
});
