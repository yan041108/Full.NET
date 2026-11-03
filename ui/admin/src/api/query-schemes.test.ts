import { beforeEach, describe, expect, it, vi } from 'vitest';
import { isSavedQueryConfiguration, validateSavedQueryConfiguration, type SavedQueryDefinition } from '@fullnet/client-contracts';
import { getQuerySchemeSchema, listQuerySchemes, getQueryScheme, saveQueryScheme, shareQueryScheme, deleteQueryScheme,
  getDefaultQueryScheme, setDefaultQueryScheme } from './query-schemes';
const request = vi.hoisted(() => vi.fn());
vi.mock('./http', () => ({ http: { request } }));
const id = '019a45b7-2356-7000-8000-000000000001', user = '019a45b7-2356-7000-8000-000000000002', grid = 'test.orders';
const configuration = { filters: [{ fieldKey: 'title', operatorKey: 'contains' as const, value: 'Literal_%[~' }], sortBy: 'id', descending: false,
  columns: [{ columnKey: 'title', order: 0, visible: true, width: null, fixed: null }] };
const summary = { id, ownerUserId: user, name: 'Saved', schemaVersion: 1, isShared: false, isCompatible: true, version: 1, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null };
const definition: SavedQueryDefinition = { gridKey: grid, schemaVersion: 1, readPermission: 'test.orders.read', supportsHost: false, supportsTenant: true,
  filters: [{ fieldKey: 'title', maxLength: 200 }], sortKeys: ['id', 'title'], columnKeys: ['title'] };
beforeEach(() => request.mockReset());
describe('查询方案生成客户端适配', () => {
  it('目录、详情和分页使用生成操作与取消信号', async () => {
    const signal = new AbortController().signal; request.mockResolvedValueOnce(definition).mockResolvedValueOnce({ summary, configuration })
      .mockResolvedValueOnce({ items: [summary], hasMore: true, nextCursor: 'next' });
    expect(await getQuerySchemeSchema(grid, signal)).toEqual(definition); expect(request.mock.calls[0]![2]).toBe(signal);
    await getQueryScheme(grid, id, signal); expect(request.mock.calls[1]![0]).toBe(`/api/v1/me/query-schemes/${grid}/${id}`);
    const page = await listQuerySchemes(grid, true, 'before', signal); expect(page.nextCursor).toBe('next');
    const query = new URL(request.mock.lastCall![0], 'https://localhost').searchParams; expect(query.get('shared')).toBe('true'); expect(query.get('pageSize')).toBe('20'); expect(query.get('cursor')).toBe('before');
  });
  it('创建与更新显式传递独立版本，不混入主体或租户', async () => {
    request.mockResolvedValue({ summary, configuration }); const body = { name: 'Saved', schemaVersion: 1, configuration, version: 0 };
    await saveQueryScheme(grid, undefined, body); expect(request.mock.lastCall![1].method).toBe('POST'); expect(JSON.parse(request.mock.lastCall![1].body)).toEqual(body);
    await saveQueryScheme(grid, id, { ...body, version: 1 }); expect(request.mock.lastCall![1].method).toBe('PUT');
    expect(JSON.parse(request.mock.lastCall![1].body).version).toBe(1);
  });
  it('共享、删除和个人默认各自保留明确目标及版本', async () => {
    request.mockResolvedValueOnce({ summary: { ...summary, isShared: true, version: 2 }, configuration }).mockResolvedValueOnce(true)
      .mockResolvedValueOnce({ scheme: null, isUnavailable: true, version: 4 }).mockResolvedValueOnce({ scheme: null, isUnavailable: false, version: 5 });
    await shareQueryScheme(grid, id, true, 1); expect(JSON.parse(request.mock.calls[0]![1].body)).toEqual({ isShared: true, version: 1 });
    await deleteQueryScheme(grid, id, 2); expect(JSON.parse(request.mock.calls[1]![1].body)).toEqual({ version: 2 });
    expect((await getDefaultQueryScheme(grid)).isUnavailable).toBe(true);
    await setDefaultQueryScheme(grid, null, 4); expect(JSON.parse(request.mock.calls[3]![1].body)).toEqual({ schemeId: null, version: 4 });
  });
  it('不把损坏、矛盾或过期配置当成可执行方案', async () => {
    request.mockResolvedValue({ summary, configuration: null }); await expect(getQueryScheme(grid, id)).rejects.toThrow();
    request.mockResolvedValue({ summary: { ...summary, schemaVersion: 99, isCompatible: false }, configuration: null }); expect((await getQueryScheme(grid, id)).configuration).toBeNull();
    request.mockResolvedValue({ items: [], hasMore: true, nextCursor: 'again' }); await expect(listQuerySchemes(grid)).rejects.toThrow();
    request.mockResolvedValue(false); await expect(deleteQueryScheme(grid, id, 1)).rejects.toThrow();
  });
  it('本地应用再次验证业务字段、排序和列，不能把元数据扩展为任意查询', () => {
    expect(validateSavedQueryConfiguration(definition, configuration)).toEqual(configuration);
    expect(() => validateSavedQueryConfiguration(definition, { ...configuration, sortBy: 'secret' })).toThrow();
    expect(() => validateSavedQueryConfiguration(definition, { ...configuration, filters: [{ fieldKey: 'tenantId', operatorKey: 'contains', value: 'other' }] })).toThrow();
    expect(isSavedQueryConfiguration({ ...configuration, columns: [{ ...configuration.columns[0], visible: false }] })).toBe(false);
    expect(isSavedQueryConfiguration({ ...configuration, filters: [configuration.filters[0], configuration.filters[0]] })).toBe(false);
  });
});
