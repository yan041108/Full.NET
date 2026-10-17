import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { executeReportingDefinition } from './reporting-executions';
import { deferred, reportResult } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ request: vi.fn() }));
const body = () => ({ versionNumber: 1, parameters: [{ parameterKey: 'filter', value: '2026' }] });
beforeEach(() => vi.resetAllMocks());
describe('报表执行请求与输出归属', () => {
  it.each([{ definitionId: '019bc2b1-2a40-7cc3-8992-a80de51bf298' }, { versionNumber: 2 }, { page: 2 }, { pageSize: 20 }, { totalRows: -1 }, { rows: Array.from({ length: 51 }, () => reportResult.rows[0]!) }, { columns: [reportResult.columns[0]!, reportResult.columns[0]!] }, { rows: [{ values: { secret: { nested: true } } }] }])('拒绝身份、分页或内容不一致的输出 %j', async change => {
    vi.mocked(request).mockResolvedValue({ ...reportResult, ...change });
    await expect(executeReportingDefinition(reportResult.definitionId, body())).rejects.toThrow(/^client.invalid_reporting_execution_page$/u);
  });
  it('UUID大小写等价，未指定版本允许服务端解析最新版本', async () => {
    vi.mocked(request).mockResolvedValue(reportResult);
    await expect(executeReportingDefinition(reportResult.definitionId.toUpperCase(), { parameters: [] })).resolves.toEqual(reportResult);
  });
  it('请求与返回校验使用冻结副本，不借用调用方在途修改', async () => {
    const pending = deferred<unknown>(); vi.mocked(request).mockReturnValueOnce(pending.promise);
    const payload = body(), reading = executeReportingDefinition(reportResult.definitionId, payload);
    payload.versionNumber = 2; payload.parameters[0]!.value = 'changed'; pending.resolve(reportResult);
    await expect(reading).resolves.toEqual(reportResult);
    expect(JSON.parse(vi.mocked(request).mock.calls[0]![1]!.body as string)).toEqual(body());
  });
  it.each([{ page: 0 }, { pageSize: 201 }, { page: 1.5 }, { versionNumber: 0 }, { parameters: [{ parameterKey: 'x', value: null }, { parameterKey: 'x', value: null }] }])('无效查询在HTTP前拒绝 %j', async change => {
    const payload = { ...body(), ...change };
    await expect(executeReportingDefinition(reportResult.definitionId, payload, 'page' in change ? change.page : 1, 'pageSize' in change ? change.pageSize : 50)).rejects.toThrow(/^client.invalid_reporting_execution_request$/u);
    expect(request).not.toHaveBeenCalled();
  });
});
