import { describe, expect, it } from 'vitest';
import {
  isOrganizationUnit,
  isOrganizationUnitPage,
  isUpdateOrganizationUnitRequest
} from '../src/tenant-org-units';

describe('租户机构客户端契约', () => {
  it('校验分页列表、单条机构与写请求', () => {
    const unit = {
      id: 'unit-id',
      parentId: null,
      code: 'hq',
      name: '总部',
      displayOrder: 10,
      isActive: true,
      createdAtUtc: '2026-07-21T00:00:00Z',
      updatedAtUtc: null,
      version: 1
    };
    expect(isOrganizationUnit(unit)).toBe(true);
    expect(isOrganizationUnitPage({
      items: [unit],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isOrganizationUnit({ id: 'unit-id' })).toBe(false);
    expect(isUpdateOrganizationUnitRequest({
      parentId: null,
      name: '已更新机构',
      displayOrder: 10,
      version: 2
    })).toBe(true);
  });
});
