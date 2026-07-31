import { describe, expect, it } from 'vitest';
import {
  isOrganizationUserUnit,
  isOrganizationUserUnitPage,
  isUpdateOrganizationUserUnitRequest
} from '../src/tenant-user-units';
import { isOrganizationAssignableUserPage } from '../src/tenant-org-assignable-users';

describe('租户用户-机构隶属客户端契约', () => {
  it('校验分页列表、单条隶属与写请求', () => {
    const assignment = {
      id: 'assignment-id',
      userId: 'user-id',
      username: 'admin',
      displayName: '系统管理员',
      unitId: 'unit-id',
      unitCode: 'hq',
      unitName: '总部',
      isPrimary: true,
      isActive: true,
      createdAtUtc: '2026-07-21T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    };
    expect(isOrganizationUserUnit(assignment)).toBe(true);
    expect(isOrganizationUserUnitPage({
      items: [assignment],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isOrganizationUserUnit({ id: 'assignment-id' })).toBe(false);
    expect(isUpdateOrganizationUserUnitRequest({
      isPrimary: false,
      version: 2
    })).toBe(true);
    expect(isOrganizationAssignableUserPage({
      items: [{
        id: 'user-id',
        username: 'admin',
        displayName: '系统管理员'
      }],
      page: 1,
      pageSize: 100,
      total: 1
    })).toBe(true);
    expect(isOrganizationAssignableUserPage({
      items: [],
      page: Number.NaN,
      pageSize: 100,
      total: 0
    })).toBe(false);
    expect(isOrganizationAssignableUserPage({
      items: [],
      page: 1,
      pageSize: 0,
      total: -1
    })).toBe(false);
  });
});
