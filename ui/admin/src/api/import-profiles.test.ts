import { beforeEach, expect, it, vi } from 'vitest';
import { http } from './http';
import { validImportMapping, listImportProfiles, getImportProfile, saveImportProfile, deleteImportProfile } from './import-profiles';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const configuration = () => ({ headerColumns: ['code', 'name'], columns: [{ targetColumn: 'code', sourceHeader: '编号' }, { targetColumn: 'name', sourceHeader: '名称' }] });
const summary = () => ({ id, name: 'Profile', headerFingerprint: 'a'.repeat(64), isCompatible: true, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version: 1 });
beforeEach(() => { vi.mocked(http.request).mockReset(); });
it('字面映射校验完整目标、有界来源和重复别名', () => {
  expect(validImportMapping(configuration(), ['code', 'name'])).toBe(true);
  expect(validImportMapping({ ...configuration(), headerColumns: ['name', 'code'] }, ['code', 'name'])).toBe(false);
  expect(validImportMapping({ ...configuration(), columns: configuration().columns.map(column => ({ ...column, sourceHeader: ' same ' })) }, ['code', 'name'])).toBe(false);
  expect(validImportMapping({ ...configuration(), columns: configuration().columns.map(column => ({ ...column, sourceHeader: 'bad\nheader' })) }, ['code', 'name'])).toBe(false);
});
it('列表限制32条并拒绝重复标识、无效版本和指纹', async () => {
  for (const value of [Array.from({ length: 33 }, () => summary()), [summary(), summary()], [{ ...summary(), version: 0 }], [{ ...summary(), headerFingerprint: 'invalid' }]]) {
    vi.mocked(http.request).mockResolvedValue(value); await expect(listImportProfiles('source', 'rows')).rejects.toThrow();
  }
  vi.mocked(http.request).mockResolvedValue([summary()]); expect(await listImportProfiles('source', 'rows')).toHaveLength(1);
});
it('详情核对目标标识、结构和失效配置，所有操作携带取消信号与版本', async () => {
  const signal = new AbortController().signal; vi.mocked(http.request).mockResolvedValue({ summary: summary(), configuration: configuration() });
  await getImportProfile('source', 'rows', id, ['code', 'name'], signal);
  await saveImportProfile('source', 'rows', id, 'Updated', configuration(), 3, signal);
  const write = vi.mocked(http.request).mock.calls[1]!;
  expect(write[1]?.method).toBe('PUT'); expect(JSON.parse(String(write[1]?.body))).toMatchObject({ version: 3, name: 'Updated' }); expect(write[2]).toBe(signal);
  vi.mocked(http.request).mockResolvedValue({ summary: { ...summary(), isCompatible: false }, configuration: null });
  expect((await getImportProfile('source', 'rows', id, ['code', 'name'])).configuration).toBeNull();
  vi.mocked(http.request).mockResolvedValue({ summary: summary(), configuration: configuration() });
  await expect(getImportProfile('source', 'rows', '019bc2b1-2a40-7cc3-8992-a80de51bf331', ['code', 'name'])).rejects.toThrow();
  await expect(getImportProfile('source', 'rows', id, ['changed'])).rejects.toThrow();
  vi.mocked(http.request).mockResolvedValue(true); await deleteImportProfile('source', 'rows', id, 4, signal);
  expect(vi.mocked(http.request).mock.lastCall?.[1]?.method).toBe('DELETE');
  expect(JSON.parse(String(vi.mocked(http.request).mock.lastCall?.[1]?.body))).toEqual({ version: 4 });
});
it('权限拒绝原样返回，写入不自动重试', async () => {
  vi.mocked(http.request).mockRejectedValue({ status: 403 });
  await expect(saveImportProfile('source', 'rows', undefined, 'New', configuration(), 0)).rejects.toEqual({ status: 403 });
  expect(http.request).toHaveBeenCalledTimes(1);
});
