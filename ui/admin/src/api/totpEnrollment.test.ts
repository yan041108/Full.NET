import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  beginTotpEnrollment,
  confirmTotpEnrollment,
  getTotpEnrollmentStatus
} from './totpEnrollment';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

describe('Vue TOTP enrollment API', () => {
  beforeEach(() => requestMock.mockReset());

  it('loads status and confirms enrollment', async () => {
    requestMock
      .mockResolvedValueOnce({ isEnrolled: false, isEnabled: false })
      .mockResolvedValueOnce({
        sharedSecretBase32: 'SECRET',
        otpAuthUri: 'otpauth://totp/Full.NET:admin?secret=SECRET'
      })
      .mockResolvedValueOnce({ isEnrolled: true, isEnabled: true });

    await expect(getTotpEnrollmentStatus()).resolves.toEqual({
      isEnrolled: false,
      isEnabled: false
    });
    await expect(beginTotpEnrollment()).resolves.toMatchObject({
      sharedSecretBase32: 'SECRET'
    });
    await expect(confirmTotpEnrollment('123456')).resolves.toEqual({
      isEnrolled: true,
      isEnabled: true
    });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/identity/me/mfa/totp',
      { method: 'GET' },
      undefined
    );
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      '/api/v1/identity/me/mfa/totp/confirm',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ totpCode: '123456' })
      }),
      undefined
    );
  });

  it('拒绝空 sharedSecretBase32', async () => {
    requestMock.mockResolvedValueOnce({
      sharedSecretBase32: '',
      otpAuthUri: 'otpauth://totp/Full.NET:admin?secret='
    });

    await expect(beginTotpEnrollment()).rejects.toThrow('client.invalid_totp_begin');
  });
});
