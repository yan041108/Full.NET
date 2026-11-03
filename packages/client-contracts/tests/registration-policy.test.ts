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
