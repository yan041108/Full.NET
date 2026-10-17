import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listPrintingTemplateVersions, listPrintingTenantVersionGrants } from './printing-templates';
import { outputId } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn() }, request: vi.fn() }));
const other = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const version = { id: other, templateId: outputId, versionNumber: 1, layoutHtml: '<div/>', changeNote: null, publishedByUserId: outputId, publishedAtUtc: '2026-10-16T00:00:00Z' };
beforeEach(() => vi.resetAllMocks());
describe('打印授权版本与分页归属', () => {
  it.each([{ rows: [{ ...version, templateId: other }] }, { rows: [version, { ...version, id: outputId }] }, { rows: [{ ...version, versionNumber: 0 }] }])('拒绝其他模板、重复版本或非法版本 %j', async ({ rows }) => {
    vi.mocked(http.request).mockResolvedValue(rows); await expect(listPrintingTemplateVersions(outputId)).rejects.toThrow(/^client.invalid_printing_template_version$/u);
  });
  it('允许大小写等价模板与非连续已发布版本', async () => {
    vi.mocked(http.request).mockResolvedValue([version, { ...version, id: outputId, templateId: outputId.toUpperCase(), versionNumber: 3 }]); await expect(listPrintingTemplateVersions(outputId)).resolves.toHaveLength(2);
  });
  it.each([
    { items: [outputId], page: 2, pageSize: 20, total: 1 },
    { items: [outputId], page: 1, pageSize: 10, total: 1 },
    { items: [outputId, outputId.toUpperCase()], page: 1, pageSize: 20, total: 2 },
    { items: ['00000000-0000-0000-0000-000000000000'], page: 1, pageSize: 20, total: 1 },
    { items: [], page: 1, pageSize: 20, total: -1 }
  ])('拒绝错页、重复或空租户授权 %j', async response => {
    vi.mocked(http.request).mockResolvedValue(response); await expect(listPrintingTenantVersionGrants(outputId, 1)).rejects.toThrow(/^client.invalid_printing_tenant_grants_page$/u);
  });
});
