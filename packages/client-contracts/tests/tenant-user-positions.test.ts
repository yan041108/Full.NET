import { describe, expect, it } from 'vitest';
import {
  isOrganizationUserPosition,
  isOrganizationUserPositionPage,
  isUpdateOrganizationUserPositionRequest
} from '../src/tenant-user-positions';
import { isOrganizationAssignableUserPage } from '../src/tenant-org-assignable-users';

describe('租户用户-职位隶属客户端契约', () => {
  it('校验分页列表、单条隶属与写请求', () => {
    const assignment = {
      id: 'assignment-id',
      userId: 'user-id',
      username: 'admin',
      displayName: '系统管理员',
      positionId: 'position-id',
      positionCode: 'engineer',
      positionName: '工程师',
      isPrimary: true,
      isActive: true,
      createdAtUtc: '2026-07-25T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    };
    expect(isOrganizationUserPosition(assignment)).toBe(true);
    expect(isOrganizationUserPositionPage({
      items: [assignment],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isOrganizationUserPosition({ id: 'assignment-id' })).toBe(false);
    expect(isUpdateOrganizationUserPositionRequest({
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
  });
});
