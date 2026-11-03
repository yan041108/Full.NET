import { afterEach, describe, expect, it, vi } from 'vitest';
import { recoverPassword, requestEmailChallenge } from './public-auth';
import { useAdminI18n } from '../i18n/adminI18n';

describe('公开账号挑战请求语言', () => {
  afterEach(() => vi.unstubAllGlobals());
  it.each(['zh-CN', 'en-US'] as const)('跟随当前 %s 语言且不附加认证请求头', async locale => {
    useAdminI18n().setLocale(locale);
    const body = { challengeId: '01900000-0000-7000-8000-000000000001', expiresAtUtc: '2030-01-01T00:00:00Z' };
    const fetcher = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify(body), { status: 200 })));
    vi.stubGlobal('fetch', fetcher);
    await requestEmailChallenge('member@example.test', 1);
    await recoverPassword('member@example.test');
    expect(fetcher).toHaveBeenCalledTimes(2);
    for (const [, options] of fetcher.mock.calls) {
      expect(options.headers).toEqual({ 'Content-Type': 'application/json', 'Accept-Language': locale });
      expect(options.credentials).toBeUndefined();
    }
  });
});
