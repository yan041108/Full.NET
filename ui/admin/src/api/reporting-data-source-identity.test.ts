import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { createReportingDataSource, deleteReportingDataSource, disableReportingDataSource, getReportingDataSource, listReportingDataSources, updateReportingDataSource } from './reporting-data-sources';
import { dataSource as source, dataSourceRow as row } from '../test/reporting-data-source-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ request: vi.fn() }));
const other = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const body = () => ({ name: source.name, providerKey: source.providerKey, serverHost: source.serverHost, port: source.port,
  databaseName: source.databaseName, username: source.username, password: 'fixture-only', trustServerCertificate: false, isEnabled: true });
beforeEach(() => vi.resetAllMocks());
describe('报表数据源客户端响应归属', () => {
  it.each([
    { page: 2 }, { pageSize: 10 }, { total: -1 }, { total: 0.5 }, { items: [row, { ...row, id: row.id.toUpperCase() }] },
    { items: [{ ...row, id: '00000000-0000-0000-0000-000000000000' }] }, { items: [{ ...row, version: 0 }] },
    { items: [{ ...row, serverHost: source.serverHost }] }, { items: [{ ...row, password: 'fixture-only' }] }
  ])('拒绝错页、重复身份、无效修订和列表凭据泄露 %j', async change => {
    vi.mocked(request).mockResolvedValue({ items: [row], page: 1, pageSize: 20, total: 1, ...change });
    await expect(listReportingDataSources()).rejects.toThrow(/^client.invalid_reporting_data_source_page$/u);
  });
  it('列表按服务端上限归一化，兼容定义目录的每页 200 请求', async () => {
    vi.mocked(request).mockResolvedValue({ items: [row], page: 1, pageSize: 100, total: 1 });
    await expect(listReportingDataSources({ pageSize: 200 })).resolves.toMatchObject({ pageSize: 100 });
    expect(vi.mocked(request).mock.calls[0]![0]).toContain('pageSize=100');
  });
  it('租户和启用过滤必须与响应一致，名称保留服务端 LIKE 语义', async () => {
    vi.mocked(request).mockResolvedValue({ items: [row], page: 1, pageSize: 20, total: 1 });
    await expect(listReportingDataSources({ tenantId: other })).rejects.toThrow(); await expect(listReportingDataSources({ isEnabled: false })).rejects.toThrow();
    await expect(listReportingDataSources({ nameContains: '%' })).resolves.toMatchObject({ items: [row] });
  });
  it('在途修改查询不改变响应核对基准', async () => {
    const pending = deferred<unknown>(); vi.mocked(request).mockReturnValueOnce(pending.promise); const query = { page: 1, pageSize: 20, isEnabled: true };
    const action = listReportingDataSources(query); query.page = 2; query.isEnabled = false;
    pending.resolve({ items: [row], page: 1, pageSize: 20, total: 1 }); await expect(action).resolves.toMatchObject({ page: 1 });
  });
  it('详情接受 UUID 大小写等价，拒绝其他身份', async () => {
    vi.mocked(request).mockResolvedValue(source); await expect(getReportingDataSource(source.id.toUpperCase())).resolves.toMatchObject({ id: source.id });
    await expect(getReportingDataSource(other)).rejects.toThrow(/^client.invalid_reporting_data_source$/u);
  });
  it.each([{ id: other }, { version: 1 }, { name: '另一个名称' }, { isEnabled: false }, { username: 'other-user' }])('更新拒绝错身份、旧修订或错误回显 %j', async change => {
    vi.mocked(request).mockResolvedValue({ ...source, version: 2, ...change }); await expect(updateReportingDataSource(source.id, { ...body(), version: 1 })).rejects.toThrow();
  });
  it('更新复制提交输入，不用调用方后改的值核对结果', async () => {
    const pending = deferred<unknown>(); vi.mocked(request).mockReturnValueOnce(pending.promise); const input = { ...body(), version: 1 };
    const action = updateReportingDataSource(source.id, input); input.name = 'changed'; input.password = 'changed-fixture';
    pending.resolve({ ...source, version: 2 }); await expect(action).resolves.toMatchObject({ version: 2 });
    expect(JSON.parse(vi.mocked(request).mock.calls[0]![1]!.body as string)).toEqual({ ...body(), version: 1 });
  });
  it.each([{ isEnabled: true }, { id: other }, { version: 0 }])('停用不接受仍启用或错误身份修订 %j', async change => {
    vi.mocked(request).mockResolvedValue({ ...source, isEnabled: false, version: 2, ...change }); await expect(disableReportingDataSource(source.id)).rejects.toThrow();
  });
  it('创建拒绝错误作用域及密码泄露', async () => {
    vi.mocked(request).mockResolvedValueOnce({ ...source, tenantId: other }).mockResolvedValueOnce({ ...source, password: 'fixture-only' });
    await expect(createReportingDataSource(body())).rejects.toThrow(); await expect(createReportingDataSource(body())).rejects.toThrow();
  });
  it('删除区分明确 false 与无法确认的畸形响应', async () => {
    vi.mocked(request).mockResolvedValueOnce(false).mockResolvedValueOnce({ deleted: true });
    await expect(deleteReportingDataSource(source.id)).resolves.toBe(false); await expect(deleteReportingDataSource(source.id)).rejects.toThrow(/^client.invalid_reporting_data_source_delete_result$/u);
  });
});
