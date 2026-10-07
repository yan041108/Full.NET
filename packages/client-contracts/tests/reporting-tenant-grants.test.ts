import { describe, expect, it } from 'vitest';
import { isReportingTenantVersionGrantPage } from '../src/reporting-definitions.js';

const tenant = '019bc2b1-2a40-7cc3-8992-a80de51bf292';
const page = { items: [tenant], page: 1, pageSize: 20, total: 1 };
describe('精确版本授权分页契约', () => {
  it('接受标识分页和超出末页的空列表', () => {
    expect(isReportingTenantVersionGrantPage(page)).toBe(true);
    expect(isReportingTenantVersionGrantPage({ ...page, items: [], page: 2 })).toBe(true);
  });
  it.each([
    { items: ['bad-id'] }, { items: [{ id: tenant }] }, { items: null },
    { page: 0 }, { page: 1.5 }, { page: '1' }, { page: Number.MAX_SAFE_INTEGER + 1 },
    { pageSize: 0 }, { pageSize: 201 }, { pageSize: '20' },
    { total: 0 }, { total: -1 }, { total: 1.5 }, { total: '1' },
    { total: Number.MAX_SAFE_INTEGER + 1 }, { items: [tenant, tenant], pageSize: 1 }
  ])('拒绝损坏的分页：%j', invalid => {
    expect(isReportingTenantVersionGrantPage({ ...page, ...invalid })).toBe(false);
  });
});
