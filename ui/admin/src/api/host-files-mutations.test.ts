import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { createHostFolder, deleteHostFolder, updateHostFileMetadata, updateHostFolder } from './host-files';
import { deferred, outputId } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));
const other = '01980000-0000-7000-8000-000000000003';
const file = { id: outputId, originalFileName: 'file.txt', contentType: 'text/plain', sizeBytes: 1, contentHash: null, folderId: null, revision: 2, createdAtUtc: '2026-10-16T00:00:00Z', createdByUserId: outputId, updatedAtUtc: null, updatedByUserId: null };
const folder = { id: outputId, parentId: null, name: 'Folder', displayOrder: 0, revision: 2, createdAtUtc: file.createdAtUtc, createdByUserId: outputId, updatedAtUtc: null, updatedByUserId: null };
const metadata = () => ({ expectedRevision: 1, originalFileName: 'file.txt', folderId: null as string | null });
beforeEach(() => vi.resetAllMocks());
describe('文件元数据与目录响应归属', () => {
  it.each([{ ...file, id: other }, { ...file, revision: 1 }, { ...file, revision: 0 }])('拒绝错误元数据身份或未递增修订 %j', async response => {
    vi.mocked(http.request).mockResolvedValue(response); await expect(updateHostFileMetadata(outputId, metadata())).rejects.toThrow(/^client.invalid_host_file_mutation$/u);
  });
  it('元数据请求被冻结，允许写后读取返回更高修订及规范化文件名', async () => {
    const pending = deferred<typeof file>(); vi.mocked(http.request).mockReturnValueOnce(pending.promise); const body = metadata(), signal = new AbortController().signal;
    const action = updateHostFileMetadata(outputId, body, signal); body.expectedRevision = 99; body.folderId = other;
    pending.resolve({ ...file, id: outputId.toUpperCase(), revision: 5, originalFileName: 'normalized.txt' }); await expect(action).resolves.toMatchObject({ revision: 5 });
    expect(JSON.parse(vi.mocked(http.request).mock.calls[0]?.[1]?.body as string)).toEqual(metadata()); expect(vi.mocked(http.request).mock.calls[0]?.[2]).toBe(signal);
  });
  it.each([{ ...folder, id: other }, { ...folder, revision: 1 }])('拒绝目录更新错配身份或旧修订 %j', async response => {
    vi.mocked(http.request).mockResolvedValue(response); await expect(updateHostFolder(outputId, { expectedRevision: 1, name: 'Folder', displayOrder: 0 })).rejects.toThrow(/^client.invalid_host_folder_mutation$/u);
  });
  it('目录更新冻结原修订并接受更高权威修订', async () => {
    const pending = deferred<typeof folder>(); vi.mocked(http.request).mockReturnValueOnce(pending.promise); const body = { expectedRevision: 1, name: 'Folder', displayOrder: 0 };
    const action = updateHostFolder(outputId, body); body.expectedRevision = 9; pending.resolve({ ...folder, id: outputId.toUpperCase(), revision: 4 }); await expect(action).resolves.toMatchObject({ revision: 4 });
  });
  it.each([{ ...folder, id: other }, { ...folder, revision: 2 }])('目录删除须返回原目标与原修订 %j', async response => {
    vi.mocked(http.request).mockResolvedValue(response); await expect(deleteHostFolder(outputId, { expectedRevision: 1 })).rejects.toThrow(/^client.invalid_host_folder_mutation$/u);
  });
  it('目录删除接受原修订与等价UUID，原参数修改不影响核对', async () => {
    const pending = deferred<typeof folder>(); vi.mocked(http.request).mockReturnValueOnce(pending.promise); const body = { expectedRevision: 1 }, signal = new AbortController().signal;
    const action = deleteHostFolder(outputId, body, signal); body.expectedRevision = 9; pending.resolve({ ...folder, id: outputId.toUpperCase(), revision: 1 }); await expect(action).resolves.toMatchObject({ revision: 1 });
    expect(vi.mocked(http.request).mock.calls[0]?.[2]).toBe(signal);
  });
  it('新目录不允许返回到另一父目录', async () => {
    vi.mocked(http.request).mockResolvedValue(folder); await expect(createHostFolder({ parentId: other, name: 'Folder' })).rejects.toThrow(/^client.invalid_host_folder_parent$/u);
  });
  it('创建复制原父目录且接受UUID大小写差异', async () => {
    const pending = deferred<Awaited<ReturnType<typeof createHostFolder>>>(); vi.mocked(http.request).mockReturnValueOnce(pending.promise); const body = { parentId: outputId, name: 'Folder' }, signal = new AbortController().signal;
    const action = createHostFolder(body, signal); body.parentId = other; pending.resolve({ ...folder, id: other, parentId: outputId.toUpperCase() }); await expect(action).resolves.toMatchObject({ id: other });
    expect(JSON.parse(vi.mocked(http.request).mock.calls[0]?.[1]?.body as string)).toEqual({ parentId: outputId, name: 'Folder', displayOrder: 0 });
  });
});
