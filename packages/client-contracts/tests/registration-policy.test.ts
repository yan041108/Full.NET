import { describe, expect, it } from 'vitest';
import { isRegistrationPolicy } from '../src/registration-ways.js';

const policy = { id: '018f5f40-0000-7000-8000-000000000001', registrationMode: 1, isPublicRegistrationEnabled: false, updatedAtUtc: '2026-10-07T00:00:00Z', version: 1 };

describe('registration policy boundary', () => {
  it.each([0, 1, 2])('accepts mode %s', registrationMode => {
    expect(isRegistrationPolicy({ ...policy, registrationMode, isPublicRegistrationEnabled: registrationMode === 2 })).toBe(true);
  });
  it.each([undefined, null, -1, 3, 255, '0', false])('rejects unknown mode %s', registrationMode => {
    expect(isRegistrationPolicy({ ...policy, registrationMode })).toBe(false);
  });
  it.each([0, 1, 2])('rejects contradictory public flag for mode %s', registrationMode => {
    expect(isRegistrationPolicy({ ...policy, registrationMode, isPublicRegistrationEnabled: registrationMode !== 2 })).toBe(false);
  });
  it.each([0, -1, 1.5])('rejects invalid version %s', version => {
    expect(isRegistrationPolicy({ ...policy, version })).toBe(false);
  });
  it('rejects malformed timestamp', () => {
    expect(isRegistrationPolicy({ ...policy, updatedAtUtc: 'invalid' })).toBe(false);
  });
});
