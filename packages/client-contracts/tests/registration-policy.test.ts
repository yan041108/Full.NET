import { describe, expect, it } from 'vitest';
import { isRegistrationPolicy } from '../src/registration-ways';

const policy = { id: '01900000-0000-7000-8000-000000000001', updatedAtUtc: '2026-11-04T00:00:00Z', version: 7 };

describe('注册策略三态响应', () => {
  it.each([0, 1, 2])('接受服务端模式 %s', registrationMode => {
    expect(isRegistrationPolicy({ ...policy, registrationMode, isPublicRegistrationEnabled: registrationMode === 2 })).toBe(true);
  });
  it.each([undefined, null, -1, 3, '1'])('拒绝缺失或未知模式 %s', registrationMode => {
    expect(isRegistrationPolicy({ ...policy, registrationMode, isPublicRegistrationEnabled: false })).toBe(false);
  });
  it('拒绝模式与公开注册标记互相矛盾', () => {
    expect(isRegistrationPolicy({ ...policy, registrationMode: 1, isPublicRegistrationEnabled: true })).toBe(false);
    expect(isRegistrationPolicy({ ...policy, registrationMode: 2, isPublicRegistrationEnabled: false })).toBe(false);
  });
});

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
