import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listReportingTenantVersionGrants, setReportingTenantVersionGrant } from './reporting-definitions';

vi.mock('./http', () => ({ http: { request: vi.fn() }, request: vi.fn() }));
const request = vi.mocked(http.request);
const definition = '019bc2b1-2a40-7cc3-8992-a80de51bf291';
const tenant = '019bc2b1-2a40-7cc3-8992-a80de51bf292';
const root = `/api/v1/reporting/definitions/${definition}/versions/2/tenant-grants`;
describe('报表精确版本授权适配器', () => {
  beforeEach(() => vi.clearAllMocks());
  it('传递分页、精确版本和取消信号', async () => {
    const signal = new AbortController().signal;
    request.mockResolvedValue({ items: [tenant], page: 3, pageSize: 20, total: '41' });
    await expect(listReportingTenantVersionGrants(definition, 2, 3, 20, signal)).resolves.toEqual({ items: [tenant], page: 3, pageSize: 20, total: 41 });
    expect(request).toHaveBeenCalledWith(root + '?page=3&pageSize=20', { method: 'GET' }, signal);
  });
  it.each([{ items: ['bad-id'], page: 1, pageSize: 20, total: 1 }, { items: [], page: 0, pageSize: 20, total: 0 }])('拒绝损坏授权分页：%j', async value => {
    request.mockResolvedValue(value);
    await expect(listReportingTenantVersionGrants(definition, 2)).rejects.toThrow();
  });
  it('授权与撤销保持相同的租户和发布版本', async () => {
    const signal = new AbortController().signal; request.mockResolvedValue(true);
    await setReportingTenantVersionGrant(definition, 2, tenant, true, signal);
    await setReportingTenantVersionGrant(definition, 2, tenant, false, signal);
    expect(request).toHaveBeenNthCalledWith(1, root + '/' + tenant, { method: 'PUT' }, signal);
    expect(request).toHaveBeenNthCalledWith(2, root + '/' + tenant, { method: 'DELETE' }, signal);
  });
});
