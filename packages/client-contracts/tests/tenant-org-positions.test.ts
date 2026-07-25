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

  it('导航白名单发布 org-positions', () => {
    const catalog = createAdminNavigationCatalog();
    expect(catalog.localNavigationFor('org-positions')).toEqual({
      componentKey: 'org-positions',
      routeName: 'org-positions',
      path: '/organization/positions'
    });
  });
});
