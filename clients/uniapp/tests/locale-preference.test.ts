import { describe, expect, it } from 'vitest';

import type { HttpClient, HttpRequestOptions } from '../src/api/http';
import { getCurrentProfile, saveLocalePreference } from '../src/api/locale-preference';

function createHttpClient(response: unknown): { readonly http: HttpClient; readonly calls: unknown[] } {
  const calls: unknown[] = [];
  return {
    http: {
      async request<T>(options: HttpRequestOptions) {
        calls.push(options);
        return response as T;
      }
    },
    calls
  };
}

describe('account locale preference port', () => {
  it('reads the locale snapshot from the full current-user response', async () => {
    const { http, calls } = createHttpClient({
      id: '6ca85abd-9191-4246-84aa-ec3a0fc603e1',
      username: 'admin',
      permissions: ['identity.profile.read'],
      preferredLocale: 'en-US',
      profileVersion: 7
    });

    await expect(getCurrentProfile(http)).resolves.toEqual({ preferredLocale: 'en-US', profileVersion: 7 });
    expect(calls).toEqual([{ path: '/api/v1/me', method: 'GET' }]);
  });

  it('maps the port request to the backend locale wire contract and guards its response', async () => {
    const { http, calls } = createHttpClient({ preferredLocale: 'en-US', profileVersion: 8 });

    await expect(saveLocalePreference(http, { preferredLocale: 'en-US', profileVersion: 7 }))
      .resolves.toEqual({ preferredLocale: 'en-US', profileVersion: 8 });
    expect(calls).toEqual([{
      path: '/api/v1/me/locale',
      method: 'PUT',
      data: { locale: 'en-US', profileVersion: 7 }
    }]);
  });

  it.each([
    ['an access token', { preferredLocale: 'en-US', profileVersion: 7, accessToken: 'secret' }],
    ['a PascalCase access token', { preferredLocale: 'en-US', profileVersion: 7, AccessToken: 'secret' }],
    ['a snake-case access token', { preferredLocale: 'en-US', profileVersion: 7, access_token: 'secret' }],
    ['a hyphenated access token', { preferredLocale: 'en-US', profileVersion: 7, 'access-token': 'secret' }],
    ['a refresh token', { preferredLocale: 'en-US', profileVersion: 7, refreshToken: 'secret' }],
    ['a PascalCase refresh token', { preferredLocale: 'en-US', profileVersion: 7, RefreshToken: 'secret' }],
    ['a snake-case refresh token', { preferredLocale: 'en-US', profileVersion: 7, refresh_token: 'secret' }],
    ['a hyphenated refresh token', { preferredLocale: 'en-US', profileVersion: 7, 'refresh-token': 'secret' }],
    ['an alias locale', { preferredLocale: 'en', profileVersion: 7 }],
    ['a missing locale', { profileVersion: 7 }],
    ['a missing version', { preferredLocale: 'en-US' }],
    ['a zero version', { preferredLocale: 'en-US', profileVersion: 0 }],
    ['a non-integer version', { preferredLocale: 'en-US', profileVersion: 7.5 }],
    ['an unsafe version', { preferredLocale: 'en-US', profileVersion: Number.MAX_SAFE_INTEGER + 1 }]
  ])('rejects a response containing %s as a whole snapshot', async (_description, response) => {
    const { http } = createHttpClient(response);

    await expect(getCurrentProfile(http)).rejects.toThrow('Current profile locale response is invalid.');
    await expect(saveLocalePreference(http, { preferredLocale: 'en-US', profileVersion: 7 }))
      .rejects.toThrow('Current profile locale response is invalid.');
  });
});
