import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { recoverPassword, requestEmailChallenge } from './public-auth';
const id = '01936c8a-7b3e-7c5d-9f2a-1b2c3d4e5f60';
describe('公开验证码响应运行时校验', () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(() => vi.unstubAllGlobals());
  it.each([null, {}, { challengeId: 'invalid', expiresAtUtc: '2099-01-01T00:00:00Z' }, { challengeId: id, expiresAtUtc: 'invalid' }])('rejects invalid recovery response %j', async payload => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(payload), { status: 200 })));
    await expect(recoverPassword('user@example.test')).rejects.toThrow('client.invalid_account_challenge_response');
  });
  it('rejects malformed registration challenge data', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ challengeId: 'invalid' }), { status: 200 })));
    await expect(requestEmailChallenge('user@example.test', 1)).rejects.toThrow('client.invalid_account_challenge_response');
  });

  it.each(['recovery', 'registration'])('accepts only the guarded %s result fields', async kind => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ challengeId: id, expiresAtUtc: '2099-01-01T00:00:00Z', unexpected: 'not-an-input' }), { status: 200 })));
    const result = kind === 'recovery' ? await recoverPassword('user@example.test') : await requestEmailChallenge('user@example.test', 1);
    expect(result).toEqual({ challengeId: id, expiresAtUtc: '2099-01-01T00:00:00Z' });
  });
});
