import { describe, expect, it } from 'vitest';
import {
  readHostUserResponse,
  readPagedResultOfHostUserResponse
} from '../src/index';
import { isHostUser, isHostUserPage, isHostUserRoles, isReplaceHostUserRolesRequest, isResetHostUserPasswordRequest, isUpdateHostUserRequest } from '../src/host-users';

describe('Host 用户客户端契约', () => {
  it('校验分页列表与单条用户', () => {
    // 中文注释：accountType 与 C# 端 AccountType 枚举对齐，常见取值包括 normal_user / super_administrator / tenant_administrator
    const user = {
      id: 'user-id',
      username: 'operator',
      displayName: '运维账号',
      accountType: 'normal_user',
      isActive: true,
      createdAtUtc: '2026-07-21T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    };
    expect(isHostUser(user)).toBe(true);
    expect(isHostUser({
      ...user,
      projectedFields: {
        effectiveFieldKeys: ['id', 'preferred_locale', 'phone_number'],
        preferredLocale: 'zh-CN',
        failedLoginCount: null,
        lockoutEndUtc: null
      },
      // 中文注释：HostUserProfile.emergencyContactRelation 与 C# 端档案字段一致，取值为字典项字符串或 null
      profile: {
        nickname: null,
        phoneNumber: '13800000000',
        email: null,
        employeeNumber: null,
        gender: null,
        joinDateUtc: null,
        sortOrder: null,
        idCardType: null,
        idCardNumber: null,
        birthDate: null,
        ethnicity: null,
        address: null,
        graduatedSchool: null,
        educationLevel: null,
        politicalStatus: null,
        officePhone: null,
        emergencyContact: null,
        emergencyContactRelation: 'parent',
        emergencyContactPhone: null,
        emergencyContactAddress: null,
        remark: null,
        version: 3
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

  it('负向测试：字段类型或取值不匹配时返回 false', () => {
    const baseUser = {
      id: 'user-id',
      username: 'operator',
      displayName: '运维账号',
      accountType: 'normal_user',
      isActive: true,
      createdAtUtc: '2026-07-21T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    };

    // 中文注释：accountType 为空字符串（要求非空字符串），违背 HostUser.accountType:string&length>0 契约
    expect(isHostUser({
      ...baseUser,
      accountType: ''
    })).toBe(false);

    // 中文注释：accountType 传数字而非字符串，违背 HostUser.accountType:string 契约
    expect(isHostUser({
      ...baseUser,
      accountType: 123 as unknown as string
    })).toBe(false);

    const baseProfile = {
      nickname: null,
      phoneNumber: null,
      email: null,
      employeeNumber: null,
      gender: null,
      joinDateUtc: null,
      sortOrder: null,
      idCardType: null,
      idCardNumber: null,
      birthDate: null,
      ethnicity: null,
      address: null,
      graduatedSchool: null,
      educationLevel: null,
      politicalStatus: null,
      officePhone: null,
      emergencyContact: null,
      emergencyContactRelation: null,
      emergencyContactPhone: null,
      emergencyContactAddress: null,
      remark: null,
      version: 1
    };

    // 中文注释：emergencyContactRelation 传数字（应为 string|null），违背 HostUserProfile 契约
    expect(isHostUser({
      ...baseUser,
      profile: {
        ...baseProfile,
        emergencyContactRelation: 999 as unknown as string
      }
    })).toBe(false);
  });

  it('生成守卫拒绝缺失字段、错误 primitive 与数组坏项', () => {
    const user = {
      id: '01912345-6789-7abc-8def-0123456789ab',
      username: 'operator',
      displayName: '运维账号',
      accountType: 'normal_user',
      isActive: true,
      createdAtUtc: '2026-07-21T00:00:00Z',
      updatedAtUtc: null,
      version: 1,
      profile: null
    };

    expect(readHostUserResponse(user)).toEqual(user);
    expect(() => readHostUserResponse({ ...user, displayName: undefined }))
      .toThrow('client.invalid_host_user_response');
    expect(() => readHostUserResponse({ ...user, version: 'one' }))
      .toThrow('client.invalid_host_user_response');
    expect(() => readPagedResultOfHostUserResponse({
      items: [{ ...user, isActive: 'yes' }],
      page: 1,
      pageSize: 20,
      total: 1
    })).toThrow('client.invalid_paged_result_of_host_user_response');
  });
});
