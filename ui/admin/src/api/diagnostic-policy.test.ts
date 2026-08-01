import { describe, expect, it, vi } from 'vitest';

vi.mock('./http', () => ({
  request: vi.fn(async () => ({
    version: 0,
    pressureState: 'Normal',
    isDefault: true,
    loadedAtUtc: new Date().toISOString(),
    activeRules: [],
    configEntryVersion: 0
  }))
}));

import { getDiagnosticPolicy } from './diagnostic-policy';

describe('diagnostic-policy api', () => {
  it('loads current policy', async () => {
    const policy = await getDiagnosticPolicy();
    expect(policy.isDefault).toBe(true);
    expect(policy.pressureState).toBe('Normal');
  });
});
