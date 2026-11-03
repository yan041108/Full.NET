import { beforeEach, expect, it, vi } from 'vitest';
import { http } from './http';
import { executeReportingDefinition, getReportingExecutionVersion } from './reporting-executions';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299', other = '019bc2b1-2a40-7cc3-8992-a80de51bf300';
const version = () => ({ id: other, definitionId: id, versionNumber: 2, dataSourceId: other, queryPortKey: 'reporting.database_engine_version', parameterSchema: [], layoutConfigJson: '{}', changeNote: null, publishedByUserId: id, publishedAtUtc: '2026-11-03T00:00:00Z' });
const result = () => ({ definitionId: id, definitionKey: 'engine', definitionName: 'Engine', versionNumber: 2, queryPortKey: 'reporting.database_engine_version', columns: [{ columnKey: 'EngineVersion', displayName: 'Engine' }], rows: [{ values: { EngineVersion: 'controlled' } }], page: 1, pageSize: 50, hasMore: false, totalRows: 1, commandTimeoutSeconds: 30, executedAtUtc: '2026-11-03T00:00:00Z' });
beforeEach(() => { vi.mocked(http.request).mockReset(); });
it('从生成操作读取明确发布版本并传播取消', async () => {
  const signal = new AbortController().signal; vi.mocked(http.request).mockResolvedValue(version()); expect((await getReportingExecutionVersion(id, 2, signal)).versionNumber).toBe(2);
  expect(vi.mocked(http.request).mock.lastCall?.[0]).toBe(`/api/v1/reporting/definitions/${id}/versions/2`); expect(vi.mocked(http.request).mock.lastCall?.[2]).toBe(signal);
});
it('执行传递明确版本和参数，校验分页响应目标', async () => {
  const signal = new AbortController().signal; vi.mocked(http.request).mockResolvedValue(result()); await executeReportingDefinition(id, { versionNumber: 2, parameters: [] }, 1, 50, signal);
  const call = vi.mocked(http.request).mock.lastCall!; const url = new URL(String(call[0]), 'https://example.test'); expect(url.searchParams.get('page')).toBe('1'); expect(url.searchParams.get('pageSize')).toBe('50');
  expect(JSON.parse(String(call[1]?.body))).toEqual({ versionNumber: 2, parameters: [] }); expect(call[2]).toBe(signal);
});
it('错误定义或版本的发布内容失败关闭', async () => {
  for (const value of [{ ...version(), definitionId: other }, { ...version(), versionNumber: 1 }]) { vi.mocked(http.request).mockResolvedValue(value); await expect(getReportingExecutionVersion(id, 2)).rejects.toThrow(); }
});
it('错误执行定义、版本、页码、页大小及非字符串单元格不能显示', async () => {
  for (const value of [{ ...result(), definitionId: other }, { ...result(), versionNumber: 1 }, { ...result(), page: 2 }, { ...result(), pageSize: 20 }, { ...result(), rows: [{ values: { EngineVersion: { unsafe: true } } }] }]) {
    vi.mocked(http.request).mockResolvedValue(value); await expect(executeReportingDefinition(id, { versionNumber: 2, parameters: [] })).rejects.toThrow();
  }
});
