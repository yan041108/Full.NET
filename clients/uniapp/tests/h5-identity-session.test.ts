import { describe, expect, it } from 'vitest';

import type {
  AuthenticationBridge,
  ConfigurableHttpClient,
  HttpRequestOptions
} from '../src/api/http';
import { createH5IdentitySession } from '../src/features/identity/h5-identity-session';
import { readH5CsrfHeaders } from '../src/features/identity/h5-csrf';

const tokenResponse = {
  accessToken: 'access-one',
  tokenType: 'Bearer' as const,
  expiresAtUtc: '2026-08-30T12:00:00Z'
};

const currentUserResponse = {
  id: '018f0000-0000-7000-8000-000000000001',
  username: 'operator',
  displayName: 'Operator',
  tenantId: null,
  actorScope: 'host',
  scope: 'host',
  isSuperAdministrator: false,
  permissions: ['workflow.todos.read', 'workflow.todos.approve'],
  sessionId: '018f0000-0000-7000-8000-000000000002',
  preferredLocale: 'zh-CN' as const,
  profileVersion: 1
};

function createHttp(responses: readonly unknown[]): {
  readonly http: ConfigurableHttpClient;
  readonly calls: HttpRequestOptions[];
  readBridge(): AuthenticationBridge | undefined;
} {
  const calls: HttpRequestOptions[] = [];
  let index = 0;
  let bridge: AuthenticationBridge | undefined;
  return {
    calls,
    readBridge: () => bridge,
    http: {
      configureAuthentication(value) {
        bridge = value;
      },
      async request<T>(options: HttpRequestOptions): Promise<T> {
        calls.push(options);
        const response = responses[index++];
        if (response instanceof Error) {
          throw response;
        }

        return response as T;
      }
    }
  };
}

describe('H5 identity session', () => {
  it('logs in, loads the authoritative user and keeps the access token in memory', async () => {
    const harness = createHttp([tokenResponse, currentUserResponse]);
    const session = createH5IdentitySession({
      http: harness.http,
      readCsrfHeaders: () => ({ 'X-CSRF-Token': 'csrf-token' })
    });

    await session.login('operator', 'password');

    expect(harness.calls).toEqual([
      {
        path: '/api/v1/auth/login',
        method: 'POST',
        data: { username: 'operator', password: 'password' },
        retryUnauthorized: false
      },
      { path: '/api/v1/me' }
    ]);
    expect(session.snapshot()).toMatchObject({ state: 'authenticated', currentUser: currentUserResponse });
    expect(session.readAccessToken()).toBe('access-one');
    expect(session.can('workflow.todos.read')).toBe(true);
    expect(session.can('workflow.todos.reject')).toBe(false);
    expect(harness.readBridge()?.getAccessToken()).toBe('access-one');
  });

  it('restores through the CSRF-protected cookie flow and refreshes the user snapshot', async () => {
    const harness = createHttp([tokenResponse, currentUserResponse]);
    const session = createH5IdentitySession({
      http: harness.http,
      readCsrfHeaders: () => ({ 'X-CSRF-Token': 'csrf-token' })
    });

    await expect(session.restore()).resolves.toBe(true);

    expect(harness.calls).toEqual([
      {
        path: '/api/v1/auth/refresh',
        method: 'POST',
        headers: { 'X-CSRF-Token': 'csrf-token' },
        retryUnauthorized: false
      },
      { path: '/api/v1/me' }
    ]);
    expect(session.snapshot().state).toBe('authenticated');
  });

  it('uses the same refresh operation for the HTTP authentication bridge', async () => {
    const refreshedToken = { ...tokenResponse, accessToken: 'access-two' };
    const harness = createHttp([refreshedToken]);
    const session = createH5IdentitySession({
      http: harness.http,
      readCsrfHeaders: () => ({ 'X-CSRF-Token': 'csrf-token' })
    });

    await expect(harness.readBridge()?.refresh()).resolves.toBe(true);

    expect(session.readAccessToken()).toBe('access-two');
    expect(harness.calls).toEqual([{
      path: '/api/v1/auth/refresh',
      method: 'POST',
      headers: { 'X-CSRF-Token': 'csrf-token' },
      retryUnauthorized: false
    }]);
  });

  it('fails closed when login or current-user responses violate the contract', async () => {
    const malformedToken = createHttp([{ accessToken: '' }]);
    const malformedTokenSession = createH5IdentitySession({
      http: malformedToken.http,
      readCsrfHeaders: () => ({})
    });
    await expect(malformedTokenSession.login('operator', 'password')).rejects.toThrow(TypeError);
    expect(malformedTokenSession.snapshot().state).toBe('anonymous');

    const malformedUser = createHttp([tokenResponse, { id: 'incomplete' }]);
    const malformedUserSession = createH5IdentitySession({
      http: malformedUser.http,
      readCsrfHeaders: () => ({})
    });
    await expect(malformedUserSession.login('operator', 'password')).rejects.toThrow(TypeError);
    expect(malformedUserSession.readAccessToken()).toBeUndefined();
  });

  it('clears local authority before sending logout and remains anonymous on failure', async () => {
    const harness = createHttp([tokenResponse, currentUserResponse, new Error('offline')]);
    const session = createH5IdentitySession({
      http: harness.http,
      readCsrfHeaders: () => ({ 'X-CSRF-Token': 'csrf-token' })
    });
    await session.login('operator', 'password');

    const logout = session.logout();
    expect(session.snapshot().state).toBe('anonymous');
    expect(session.readAccessToken()).toBeUndefined();
    await expect(logout).rejects.toThrow('offline');
    expect(session.snapshot().state).toBe('anonymous');
  });

  it('clears the HTTP authentication bridge when disposed', () => {
    const harness = createHttp([]);
    const session = createH5IdentitySession({ http: harness.http, readCsrfHeaders: () => ({}) });

    session.dispose();

    expect(harness.readBridge()).toBeUndefined();
  });

  it('reads only the CSRF cookie and fails closed for malformed encoding', () => {
    expect(readH5CsrfHeaders('theme=dark; fullnet-csrf=csrf%20token; ignored=value'))
      .toEqual({ 'X-CSRF-Token': 'csrf token' });
    expect(readH5CsrfHeaders('fullnet-csrf=%E0%A4%A')).toEqual({});
    expect(readH5CsrfHeaders('other=value')).toEqual({});
  });
});
