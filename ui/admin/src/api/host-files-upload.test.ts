import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { batchUploadHostFiles, listHostFileReferences, uploadHostFile } from './host-files';
import { deferred, outputId } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));
const source = new File(['abc'], 'source.txt'), second = new File(['xy'], 'second.txt');
const file = { id: outputId, originalFileName: 'source.txt', contentType: 'text/plain', sizeBytes: 3, contentHash: null, folderId: null, revision: 1, createdAtUtc: '2026-10-16T00:00:00Z', createdByUserId: outputId, updatedAtUtc: null, updatedByUserId: null };
const success = { originalFileName: source.name, succeeded: true, file, errorCode: null, message: null };
const failure = { originalFileName: second.name, succeeded: false, file: null, errorCode: 'files.file_too_large', message: 'private' };
beforeEach(() => vi.resetAllMocks());
describe('文件上传逐项响应与引用页归属', () => {
  it('单项上传拒绝不是原字节长度的文件', async () => {
    vi.mocked(http.request).mockResolvedValue({ ...file, sizeBytes: 99 }); await expect(uploadHostFile(source)).rejects.toThrow(/^client.invalid_host_file_upload$/u);
  });
  it.each([
    { succeededCount: 1, results: [success] },
    { succeededCount: 2, results: [success, failure] },
    { succeededCount: 1, results: [failure, success] },
    { succeededCount: 1, results: [{ ...success, file: null }, failure] },
    { succeededCount: 1, results: [success, { ...failure, file }] },
    { succeededCount: 1, results: [{ ...success, file: { ...file, sizeBytes: 99 } }, failure] }
  ])('批量上传拒绝缺项、错序、计数或成功载荷错误 %j', async response => {
    vi.mocked(http.request).mockResolvedValue(response); await expect(batchUploadHostFiles([source, second])).rejects.toThrow(/^client.invalid_host_file_upload_batch$/u);
  });
  it('批量新文件标识不能重复', async () => {
    vi.mocked(http.request).mockResolvedValue({ succeededCount: 2, results: [success, { ...success, originalFileName: source.name, file: { ...file, id: outputId.toUpperCase() } }] });
    await expect(batchUploadHostFiles([source, source])).rejects.toThrow(/^client.invalid_host_file_upload_batch$/u);
  });
  it('保留同名输入的逐项顺序并冻结原文件数组和取消信号', async () => {
    const pending = deferred<Awaited<ReturnType<typeof batchUploadHostFiles>>>(); vi.mocked(http.request).mockReturnValueOnce(pending.promise); const files = [source, new File(['xy'], source.name)], signal = new AbortController().signal;
    const result = batchUploadHostFiles(files, undefined, signal); files.splice(0, 2, second); pending.resolve({ succeededCount: 1, results: [success, { ...failure, originalFileName: source.name }] });
    await expect(result).resolves.toMatchObject({ succeededCount: 1 }); expect((vi.mocked(http.request).mock.calls[0]?.[1]?.body as FormData).getAll('files')).toHaveLength(2); expect(vi.mocked(http.request).mock.calls[0]?.[2]).toBe(signal);
  });
  it.each([{ page: 1, pageSize: 20 }, { page: 2, pageSize: 10 }])('引用页须属于原分页请求 %j', async paging => {
    vi.mocked(http.request).mockResolvedValue({ ...paging, items: [], total: 0 }); await expect(listHostFileReferences(outputId, 2, 20)).rejects.toThrow(/^client.invalid_host_file_reference_page$/u);
  });
  it('引用正确空页仍保留查询与取消信号', async () => {
    vi.mocked(http.request).mockResolvedValue({ page: 2, pageSize: 20, items: [], total: 0 }); const signal = new AbortController().signal;
    await expect(listHostFileReferences(outputId, 2, 20, signal)).resolves.toMatchObject({ page: 2 }); expect(vi.mocked(http.request).mock.calls[0]?.[2]).toBe(signal);
  });
});
