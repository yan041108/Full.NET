import { describe, expect, it } from 'vitest';
import {
  isBeginTotpEnrollmentResponse,
  isTotpEnrollmentStatus
} from '../src/totp-enrollment.js';

describe('TOTP enrollment contracts', () => {
  it('accepts status and begin payloads', () => {
    expect(isTotpEnrollmentStatus({ isEnrolled: true, isEnabled: false })).toBe(true);
    expect(isTotpEnrollmentStatus({ isEnrolled: true })).toBe(false);
    expect(isBeginTotpEnrollmentResponse({
      sharedSecretBase32: 'ABCDEF',
      otpAuthUri: 'otpauth://totp/Full.NET:admin?secret=ABCDEF'
    })).toBe(true);
    expect(isBeginTotpEnrollmentResponse({
      sharedSecretBase32: '',
      otpAuthUri: 'otpauth://totp/x'
    })).toBe(false);
  });
});
