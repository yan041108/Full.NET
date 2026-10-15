import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { batchDeleteHostFiles, deleteHostFile } from './host-files';
import { deferred, outputId } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));
const other = '01980000-0000-7000-8000-000000000003';
const file = { id: outputId, originalFileName: 'file.txt', contentType: 'text/plain', sizeBytes: 1, contentHash: null, folderId: null, revision: 1, createdAtUtc: '2026-10-16T00:00:00Z', createdByUserId: outputId, updatedAtUtc: null, updatedByUserId: null };
const success = { fileId: outputId, succeeded: true, errorCode: null, message: null };
const failure = { fileId: other, succeeded: false, errorCode: 'files.file.in_use', message: '拒绝' };
beforeEach(() => vi.resetAllMocks());
describe('文件删除响应归属', () => {
  it('单项删除拒绝其他文件身份', async () => {
    vi.mocked(http.request).mockResolvedValue({ ...file, id: other }); await expect(deleteHostFile(outputId)).rejects.toThrow(/^client.invalid_host_file_identity$/u);
  });
  it('单项删除接受UUID大小写等价并传递信号', async () => {
    vi.mocked(http.request).mockResolvedValue({ ...file, id: outputId.toUpperCase() }); const signal = new AbortController().signal;
    await expect(deleteHostFile(outputId, signal)).resolves.toMatchObject({ id: outputId.toUpperCase() }); expect(vi.mocked(http.request).mock.calls[0]?.[2]).toBe(signal);
  });
  it.each([
    { succeededCount: 2, results: [success, failure] },
    { succeededCount: 1, results: [success] },
    { succeededCount: 2, results: [success, success] },
    { succeededCount: 1, results: [success, { ...failure, fileId: '01980000-0000-7000-8000-000000000004' }] }
  ])('批量删除拒绝错配身份、缺失结果或错误成功计数 %j', async result => {
    vi.mocked(http.request).mockResolvedValue(result); await expect(batchDeleteHostFiles([outputId, other])).rejects.toThrow(/^client.invalid_host_file_delete_batch$/u);
  });
  it('合法乱序和大小写返回部分成功，冻结原调用数组', async () => {
    const pending = deferred<Awaited<ReturnType<typeof batchDeleteHostFiles>>>(); vi.mocked(http.request).mockReturnValueOnce(pending.promise);
    const ids = [outputId, other]; const signal = new AbortController().signal; const result = batchDeleteHostFiles(ids, signal); ids.splice(0, 2, other);
    pending.resolve({ succeededCount: 1, results: [failure, { ...success, fileId: outputId.toUpperCase() }] }); await expect(result).resolves.toMatchObject({ succeededCount: 1 });
    expect(JSON.parse(vi.mocked(http.request).mock.calls[0]?.[1]?.body as string)).toEqual({ fileIds: [outputId, other] }); expect(vi.mocked(http.request).mock.calls[0]?.[2]).toBe(signal);
  });
});
