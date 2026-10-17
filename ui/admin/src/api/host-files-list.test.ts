import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listHostFiles } from './host-files';
import { deferred, outputId } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));
const folderId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const file = { id: outputId, originalFileName: 'source.txt', contentType: 'text/plain', sizeBytes: 3, contentHash: null, folderId: null as string | null, revision: 1, createdAtUtc: '2026-10-16T00:00:00Z', createdByUserId: outputId, updatedAtUtc: null, updatedByUserId: null };
const page = { page: 1, pageSize: 20, items: [file], total: 1 };
beforeEach(() => vi.resetAllMocks());
describe('文件列表查询归属', () => {
  it.each([
    { ...page, page: 2 }, { ...page, pageSize: 10 }, { ...page, total: -1 },
    { ...page, items: [file, { ...file, id: outputId.toUpperCase() }] },
    { ...page, items: Array.from({ length: 21 }, (_, i) => ({ ...file, id: `01980000-0000-7000-8000-${String(i).padStart(12, '0')}` })) },
    { ...page, items: [{ ...file, folderId }] }
  ])('拒绝错误页码、重复行或根目录串线 %j', async response => {
    vi.mocked(http.request).mockResolvedValue(response); await expect(listHostFiles(1, 20, { folderId: '' })).rejects.toThrow(/^client.invalid_host_file_page_identity$/u);
  });
  it('指定目录不接受其他目录的行', async () => {
    vi.mocked(http.request).mockResolvedValue(page); await expect(listHostFiles(1, 20, { folderId })).rejects.toThrow(/^client.invalid_host_file_page_identity$/u);
  });
  it('目录按等价UUID核对，原参数修改不改变请求归属', async () => {
    const pending = deferred<typeof page>(); vi.mocked(http.request).mockReturnValueOnce(pending.promise); const options = { folderId, fileNameContains: 'source' }, signal = new AbortController().signal;
    const action = listHostFiles(1, 20, options, signal); options.folderId = outputId; options.fileNameContains = 'other'; pending.resolve({ ...page, items: [{ ...file, folderId: folderId.toUpperCase() }] });
    await expect(action).resolves.toMatchObject({ total: 1 }); expect(vi.mocked(http.request).mock.calls[0]?.[0]).toContain(`folderId=${folderId}`); expect(vi.mocked(http.request).mock.calls[0]?.[2]).toBe(signal);
  });
  it('省略目录仍支持全目录查询，空越界页可用于页码恢复', async () => {
    vi.mocked(http.request).mockResolvedValueOnce({ ...page, items: [{ ...file, folderId }] }); await expect(listHostFiles()).resolves.toMatchObject({ total: 1 });
    vi.mocked(http.request).mockResolvedValueOnce({ page: 3, pageSize: 20, items: [], total: 1 }); await expect(listHostFiles(3, 20, { folderId: '' })).resolves.toMatchObject({ page: 3, items: [] });
  });
});
