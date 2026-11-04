import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http, request as rawRequest } from './http';
import { listReportingTenantVersionGrants, setReportingTenantVersionGrant, listReportingDefinitionVersions } from './reporting-definitions';

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
describe('报表发布历史分页适配器', () => {
  const historyRequest = vi.mocked(rawRequest);
  const row = (versionNumber: number) => ({ id: '019bc2b1-2a40-7cc3-8992-' + (1000 + versionNumber).toString(16).padStart(12, '0'), definitionId: definition,
    versionNumber, dataSourceId: tenant, queryPortKey: 'metadata.schemas', changeNote: null, publishedByUserId: tenant, publishedAtUtc: '2026-11-04T00:00:00Z' });
  beforeEach(() => vi.resetAllMocks());
  it('只读取目标定义的最小倒序分页，原样传递取消信号', async () => {
    const signal = new AbortController().signal, result = { items: [row(1)], total: 21, page: 2, pageSize: 20 };
    historyRequest.mockResolvedValue(result); await expect(listReportingDefinitionVersions(definition, signal, 2, 20)).resolves.toEqual(result);
    expect(historyRequest).toHaveBeenCalledWith(`/api/v1/reporting/definitions/${definition}/versions?page=2&pageSize=20`, { method: 'GET' }, signal);
  });
  it.each([
    { items: [], total: 1, page: 1, pageSize: 20 },
    { items: [row(1), row(2)], total: 2, page: 1, pageSize: 20 },
    { items: [row(2), row(1)], total: 3, page: 1, pageSize: 20 },
    { items: [{ ...row(1), definitionId: tenant }], total: 1, page: 1, pageSize: 20 },
    { items: [{ ...row(1), id: 'bad' }], total: 1, page: 1, pageSize: 20 },
    { items: [{ ...row(1), publishedByUserId: '00000000-0000-0000-0000-000000000000' }], total: 1, page: 1, pageSize: 20 },
    { items: [{ ...row(1), layoutConfigJson: '{}' }], total: 1, page: 1, pageSize: 20 },
    { items: [{ ...row(1), parameterSchema: [] }], total: 1, page: 1, pageSize: 20 },
    { items: [row(1)], total: 1, page: 2, pageSize: 20 }
  ])('拒绝不一致、错误目标或混入正文的历史页：%j', async value => {
    historyRequest.mockResolvedValue(value); await expect(listReportingDefinitionVersions(definition)).rejects.toThrow();
  });
  it.each([[0, 20], [1, 0], [1, 101], [Number.MAX_SAFE_INTEGER, 100]])('非法分页 %s/%s 不发请求', async (page, pageSize) => {
    await expect(listReportingDefinitionVersions(definition, undefined, page, pageSize)).rejects.toThrow(); expect(historyRequest).not.toHaveBeenCalled();
  });
});
