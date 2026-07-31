import { describe, expect, it } from 'vitest';
import { isHostUser, isHostUserPage, isHostUserRoles, isReplaceHostUserRolesRequest, isResetHostUserPasswordRequest, isUpdateHostUserRequest } from '../src/host-users';

describe('Host 用户客户端契约', () => {
  it('校验分页列表与单条用户', () => {
    const user = {
      id: 'user-id',
      username: 'operator',
      displayName: '运维账号',
      isActive: true,
      createdAtUtc: '2026-07-21T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    };
    expect(isHostUser(user)).toBe(true);
    expect(isHostUser({
      ...user,
      projectedFields: {
        effectiveFieldKeys: ['id', 'preferred_locale'],
        preferredLocale: 'zh-CN',
        failedLoginCount: null,
        lockoutEndUtc: null
      }
    })).toBe(true);
    expect(isHostUser({
      ...user,
      projectedFields: {
        effectiveFieldKeys: ['id', 'PasswordHash'],
        preferredLocale: null,
        failedLoginCount: null,
        lockoutEndUtc: null
      }
    })).toBe(false);
    expect(isHostUser({
      ...user,
      projectedFields: {
        effectiveFieldKeys: 'preferred_locale',
        preferredLocale: null,
        failedLoginCount: -1,
        lockoutEndUtc: null
      }
    })).toBe(false);
    expect(isHostUserPage({
      items: [user],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isHostUser({ id: 'user-id' })).toBe(false);
    expect(isUpdateHostUserRequest({ displayName: '新名称', version: 2 })).toBe(true);
    expect(isUpdateHostUserRequest({ displayName: '', version: 2 })).toBe(false);
    expect(isHostUserRoles({
      userId: 'user-id',
      roleIds: ['role-id'],
      version: 2
    })).toBe(true);
    expect(isReplaceHostUserRolesRequest({ roleIds: ['role-id'], version: 2 })).toBe(true);
    expect(isResetHostUserPasswordRequest({ password: 'FullNet!2026Secure' })).toBe(true);
    expect(isResetHostUserPasswordRequest({ password: '' })).toBe(false);
  });
});
