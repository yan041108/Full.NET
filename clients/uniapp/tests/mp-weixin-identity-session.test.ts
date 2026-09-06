import { describe, expect, it } from 'vitest';

import type {
  ConfigurableHttpClient,
  HttpRequestOptions
} from '../src/api/http';
import { createMpWeixinIdentitySession } from '../src/features/identity/mp-weixin-identity-session';

const tokenResponse = {
  accessToken: 'access-mp',
  tokenType: 'Bearer' as const,
  expiresAtUtc: '2026-09-07T12:00:00Z'
};

const currentUserResponse = {
  id: '018f0000-0000-7000-8000-000000000001',
  username: 'operator',
  displayName: 'Operator',
  tenantId: null,
  actorScope: 'host',
  scope: 'host',
  isSuperAdministrator: false,
  permissions: ['workflow.todos.read', 'notifications.inbox.read'],
  sessionId: '018f0000-0000-7000-8000-000000000002',
  preferredLocale: 'zh-CN' as const,
  profileVersion: 1,
  passwordChangeRequired: false
};

function createHttp(responses: readonly unknown[]): {
  readonly http: ConfigurableHttpClient;
  readonly calls: HttpRequestOptions[];
} {
  const calls: HttpRequestOptions[] = [];
  let index = 0;
  return {
    calls,
    http: {
      configureAuthentication() {},
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

describe('mp-weixin identity session', () => {
  it('logs in with password and keeps the access token in memory only', async () => {
    const { http, calls } = createHttp([tokenResponse, currentUserResponse]);
    const session = createMpWeixinIdentitySession({ http });

    await session.login('operator', 'secret');

    expect(session.snapshot().state).toBe('authenticated');
    expect(session.can('notifications.inbox.read')).toBe(true);
    expect(session.readAccessToken()).toBe('access-mp');
    expect(calls[0]?.path).toBe('/api/v1/auth/login');
    expect(calls[1]?.path).toBe('/api/v1/me');
  });

  it('does not restore sessions from refresh cookies on mini programs', async () => {
    const { http } = createHttp([]);
    const session = createMpWeixinIdentitySession({ http });

    expect(await session.restore()).toBe(false);
    expect(session.snapshot().state).toBe('anonymous');
  });
});
