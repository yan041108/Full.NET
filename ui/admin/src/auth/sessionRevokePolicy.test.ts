import { describe, expect, it } from 'vitest';
import { shouldLogoutOnSessionRevoke } from './sessionRevokePolicy';

describe('session revoke policy', () => {
  it('logs out when revoked session matches current session', () => {
    expect(shouldLogoutOnSessionRevoke('session-a', 'session-a')).toBe(true);
  });

  it('allows repeated revoke notifications for the same session', () => {
    expect(shouldLogoutOnSessionRevoke('session-a', 'session-a')).toBe(true);
    expect(shouldLogoutOnSessionRevoke('session-a', 'session-a')).toBe(true);
  });

  it('ignores forged revoke notifications for another session', () => {
    expect(shouldLogoutOnSessionRevoke('session-a', 'session-b')).toBe(false);
  });

  it('ignores revoke notifications without sessionId', () => {
    expect(shouldLogoutOnSessionRevoke('session-a', undefined)).toBe(false);
    expect(shouldLogoutOnSessionRevoke('session-a', '')).toBe(false);
  });

  it('falls back to logout when current session is unknown', () => {
    expect(shouldLogoutOnSessionRevoke(undefined, 'session-a')).toBe(true);
  });
});