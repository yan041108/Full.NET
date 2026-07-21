import { describe, expect, it } from 'vitest';
import {
  HOST_ROLE_ASSIGNABLE_PERMISSIONS,
  isHostRole,
  isHostRolePage,
  isReplaceHostRolePermissionsRequest,
  isUpdateHostRoleRequest
} from '../src/host-roles';

describe('Host 角色客户端契约', () => {
  it('校验分页列表、单条角色与写请求', () => {
    const role = {
      id: 'role-id',
      code: 'support',
      name: '支持角色',
      isSystem: false,
      isActive: true,
      isSuperAdministrator: false,
      permissionCodes: ['identity.users.read'],
      createdAtUtc: '2026-07-21T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    };
    expect(isHostRole(role)).toBe(true);
    expect(isHostRolePage({
      items: [role],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isHostRole({ id: 'role-id' })).toBe(false);
    expect(isUpdateHostRoleRequest({ name: '新名称', version: 2 })).toBe(true);
    expect(isReplaceHostRolePermissionsRequest({
      permissionCodes: ['identity.users.read'],
      version: 2
    })).toBe(true);
    expect(HOST_ROLE_ASSIGNABLE_PERMISSIONS).toContain('identity.roles.write');
  });
});
