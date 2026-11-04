import { beforeEach, expect, it, vi } from 'vitest';
import { http } from './http';
import { listPrintingTemplates } from './printing-templates';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf299';
const row = { id, templateKey: 'fixture', name: '版式', formSchemaKey: 'printing.tenant_profile_card', latestPublishedVersionNumber: 1,
  isEnabled: true, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version: 4 };
const result = (items = [row]) => ({ items, page: 1, pageSize: 20, total: items.length });
beforeEach(() => vi.resetAllMocks());
it('最小目录冻结规范分页和关键词，并传递取消信号', async () => {
  vi.mocked(http.request).mockResolvedValue(result()); const signal = new AbortController().signal;
  expect((await listPrintingTemplates('  版式  ', signal)).items).toEqual([row]);
  const call = vi.mocked(http.request).mock.lastCall!, url = new URL(call[0], 'https://example.test');
  expect(url.searchParams.get('nameContains')).toBe('版式'); expect(url.searchParams.get('page')).toBe('1'); expect(url.searchParams.get('pageSize')).toBe('20'); expect(call[2]).toBe(signal);
});
it.each([{ page: 2 }, { pageSize: 50 }, { total: -1 }, { total: 1.5 }])('拒绝错误分页摘要 %j', async change => {
  vi.mocked(http.request).mockResolvedValue({ ...result(), ...change }); await expect(listPrintingTemplates()).rejects.toThrow();
});
it.each([{ id: 'bad' }, { id: '00000000-0000-0000-0000-000000000000' }, { version: 0 }, { latestPublishedVersionNumber: -1 }, { layoutHtml: '<p>不能进入摘要</p>' }])('拒绝错误身份、修订或正文泄入 %j', async change => {
  vi.mocked(http.request).mockResolvedValue(result([{ ...row, ...change }])); await expect(listPrintingTemplates()).rejects.toThrow();
});
it('重复身份与超过请求大小的行不能进入目录', async () => {
  vi.mocked(http.request).mockResolvedValue(result([row, { ...row, id: id.toUpperCase() }])); await expect(listPrintingTemplates()).rejects.toThrow();
  vi.mocked(http.request).mockResolvedValue(result(Array.from({ length: 21 }, () => row))); await expect(listPrintingTemplates()).rejects.toThrow();
});
it.each([NaN, Infinity, 1.5])('非整数页码%s不发送请求', async page => {
  await expect(listPrintingTemplates(undefined, undefined, page)).rejects.toThrow(); expect(http.request).not.toHaveBeenCalled();
});
