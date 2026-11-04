import { describe, expect, it } from 'vitest';
import {
  isBeginTotpEnrollmentResponse,
  isTotpEnrollmentStatus
} from '../src/totp-enrollment.js';

describe('TOTP enrollment contracts', () => {
  it('rejects enabled status without enrollment', () => {
    expect(isTotpEnrollmentStatus({ isEnrolled: false, isEnabled: true })).toBe(false);
  });
  it.each([
    'javascript:alert(1)',
    'https://example.test/secret?secret=ABCDEF',
    'otpauth://hotp/Full.NET:admin?secret=ABCDEF',
    'otpauth://totp/Full.NET:admin?secret=DIFFERENT',
    'otpauth://totp/Full.NET:admin?secret=ABCDEF&secret=OTHER',
    'otpauth://totp/Full.NET:admin?secret=ABCDEF&digits=8'
  ])('rejects unsupported or mismatched provisioning URI %s', otpAuthUri => {
    expect(isBeginTotpEnrollmentResponse({ sharedSecretBase32: 'ABCDEF', otpAuthUri })).toBe(false);
  });
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
