import { beforeEach, expect, it, vi } from 'vitest';
import { listReportingPublishedDefinitions, listReportingDefinitionTenantGrants, setReportingDefinitionTenantGrant } from './reporting-publications';
const request = vi.hoisted(() => vi.fn());
vi.mock('./http', () => ({ http: { request } }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf296', tenant = '019bc2b1-2a40-7cc3-8992-a80de51bf297';
const published = { id, definitionKey: 'visible', name: 'Published', versionNumber: 2, parameterSchema: [] };
const grant = { id, definitionId: id, tenantId: tenant, isEnabled: true, version: 3, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null };
const page = (items: unknown[]) => ({ items, page: 1, pageSize: 20, total: items.length });
beforeEach(() => vi.resetAllMocks());
it('最小发布目录使用生成操作并转发取消信号', async () => {
  const signal = new AbortController().signal; request.mockResolvedValue(page([published]));
  expect((await listReportingPublishedDefinitions('Published', signal)).items).toEqual([published]);
  expect(request.mock.calls[0]![0]).toContain('/api/v1/reporting/published-definitions?'); expect(request.mock.calls[0]![2]).toBe(signal);
});
it('发布目录拒绝无效身份或未发布的版本', async () => {
  request.mockResolvedValue(page([{ ...published, id: 'invalid' }])); await expect(listReportingPublishedDefinitions()).rejects.toThrow();
  request.mockResolvedValue(page([{ ...published, versionNumber: 0 }])); await expect(listReportingPublishedDefinitions()).rejects.toThrow();
});
it('授权分页不接纳缺失版本的结果', async () => {
  request.mockResolvedValue(page([{ ...grant, version: undefined }])); await expect(listReportingDefinitionTenantGrants(id)).rejects.toThrow();
});
it('授权写入明确传递目标、状态、预期版本和取消令牌', async () => {
  const signal = new AbortController().signal; request.mockResolvedValue(grant);
  await setReportingDefinitionTenantGrant(id, tenant, true, 3, signal);
  expect(request.mock.calls[0]![0]).toBe(`/api/v1/reporting/definitions/${id}/tenant-grants/${tenant}`);
  expect(JSON.parse(request.mock.calls[0]![1].body)).toEqual({ isEnabled: true, expectedVersion: 3 }); expect(request.mock.calls[0]![2]).toBe(signal);
});
