import { describe, expect, it } from 'vitest';
import {
  isOrganizationPosition,
  isOrganizationPositionPage
} from '../src/tenant-org-positions';
import { createAdminNavigationCatalog } from '../src/navigation-catalog';

const sample = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  code: 'engineer',
  name: '工程师',
  unitId: null,
  unitCode: null,
  unitName: null,
  positionLevelId: null,
  positionLevelCode: null,
  positionLevelName: null,
  displayOrder: 10,
  isActive: true,
  createdAtUtc: '2026-07-25T08:00:00.000Z',
  updatedAtUtc: null,
  version: 1
};

describe('Organization 职位契约', () => {
  it('接受合法职位分页', () => {
    expect(isOrganizationPosition(sample)).toBe(true);
    expect(isOrganizationPositionPage({
      items: [sample],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
  });

  it('拒绝畸形职位', () => {
    expect(isOrganizationPosition({ ...sample, isActive: 'yes' })).toBe(false);
  });

  it('拒绝缺失或不完整的机构绑定摘要', () => {
    const { unitId: _, ...missingUnitId } = sample;
    expect(isOrganizationPosition(missingUnitId)).toBe(false);
    expect(isOrganizationPosition({
      ...sample,
      unitId: '01912345-6789-7abc-8def-0123456789ab',
      unitCode: null,
      unitName: '研发中心'
    })).toBe(false);
  });

  it('导航白名单发布 org-positions', () => {
    const catalog = createAdminNavigationCatalog();
    expect(catalog.localNavigationFor('org-positions')).toEqual({
      componentKey: 'org-positions',
      routeName: 'org-positions',
      path: '/organization/positions'
    });
  });
});

describe('Organization position-level binding contract', () => {
  it('rejects a missing or incomplete position-level summary', () => {
    const { positionLevelId: _, ...missingPositionLevelId } = sample;
    expect(isOrganizationPosition(missingPositionLevelId)).toBe(false);
    expect(isOrganizationPosition({
      ...sample,
      positionLevelId: '01912345-6789-7abc-8def-0123456789ac',
      positionLevelCode: null,
      positionLevelName: '高级'
    })).toBe(false);
  });
});
