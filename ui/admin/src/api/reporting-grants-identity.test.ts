import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http, request } from './http';
import { listReportingDefinitionVersions, listReportingTenantVersionGrants } from './reporting-definitions';
import { outputId } from '../test/data-output-fixtures';

vi.mock('./http', () => ({ http: { request: vi.fn() }, request: vi.fn() }));
const other = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const version = { id: other, definitionId: outputId, versionNumber: 1, dataSourceId: outputId, queryPortKey: 'fixture', parameterSchema: [], layoutConfigJson: '{}', changeNote: null, publishedByUserId: outputId, publishedAtUtc: '2026-10-16T00:00:00Z' };
beforeEach(() => vi.resetAllMocks());
describe('报表授权版本与分页归属', () => {
  it.each([
    { rows: [{ ...version, definitionId: other }] },
    { rows: [version, { ...version, id: outputId }] },
    { rows: [{ ...version, versionNumber: 0 }] },
    { rows: [{ ...version, versionNumber: -1 }] },
    { rows: [{ ...version, versionNumber: 1.5 }] },
    { rows: [version, { ...version, id: other.toUpperCase(), versionNumber: 3 }] }
  ])('拒绝其他定义、重复版本、重复身份或非法版本 %j', async ({ rows }) => {
    vi.mocked(request).mockResolvedValue(rows);
    await expect(listReportingDefinitionVersions(outputId)).rejects.toThrow(/^client.invalid_reporting_definition_version$/u);
  });
  it('允许大小写等价定义与非连续已发布版本', async () => {
    vi.mocked(request).mockResolvedValue([version, { ...version, id: outputId, definitionId: outputId.toUpperCase(), versionNumber: 3 }]);
    await expect(listReportingDefinitionVersions(outputId)).resolves.toHaveLength(2);
  });
  it.each([
    { items: [outputId], page: 2, pageSize: 20, total: 1 },
    { items: [outputId], page: 1, pageSize: 10, total: 1 },
    { items: [outputId, outputId.toUpperCase()], page: 1, pageSize: 20, total: 2 },
    { items: ['00000000-0000-0000-0000-000000000000'], page: 1, pageSize: 20, total: 1 }
  ])('拒绝错页、重复或空租户授权 %j', async response => {
    vi.mocked(http.request).mockResolvedValue(response);
    await expect(listReportingTenantVersionGrants(outputId, 1)).rejects.toThrow(/^client.invalid_reporting_tenant_grants$/u);
  });
});
