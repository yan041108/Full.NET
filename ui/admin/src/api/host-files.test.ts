import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request, requestBlob } from './http';
import {
  deleteHostFile,
  downloadHostFileContent,
  listHostFiles,
  openHostFileBlob,
  uploadHostFile
} from './host-files';

vi.mock('./http', () => ({
  request: vi.fn(),
  requestBlob: vi.fn()
}));
const requestMock = vi.mocked(request);
const requestBlobMock = vi.mocked(requestBlob);

const sampleFile = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  originalFileName: 'parity.txt',
  contentType: 'text/plain',
  sizeBytes: 12,
  contentHash: 'a'.repeat(64),
  createdAtUtc: '2026-07-26T00:00:00Z',
  createdByUserId: '01912345-6789-7abc-8def-0123456789ac'
};

describe('Vue Host 文件 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleFile],
      page: 1,
      pageSize: 20,
      total: 1
    });
    const page = await listHostFiles();
    expect(page.items).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/files/host-files?page=1&pageSize=20'
    );
  });

  it('上传 multipart 文件', async () => {
    requestMock.mockResolvedValueOnce(sampleFile);
    const file = new File(['hello'], 'parity.txt', { type: 'text/plain' });
    const result = await uploadHostFile(file);
    expect(result.id).toBe(sampleFile.id);
    const [, init] = requestMock.mock.calls[0] ?? [];
    expect(init?.method).toBe('POST');
    expect(init?.body).toBeInstanceOf(FormData);
  });

  it('删除文件', async () => {
    requestMock.mockResolvedValueOnce(sampleFile);
    await deleteHostFile(sampleFile.id);
    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/files/host-files/${sampleFile.id}/delete`,
      { method: 'POST' }
    );
  });

  it('下载文件内容使用认证 Blob 客户端', async () => {
    const blob = new Blob(['hello'], { type: 'text/plain' });
    requestBlobMock.mockResolvedValueOnce(blob);
    await expect(downloadHostFileContent(sampleFile.id)).resolves.toBe(blob);
    expect(requestBlobMock).toHaveBeenCalledWith(
      `/api/v1/files/host-files/${sampleFile.id}/content`
    );
  });

  it('打开 Blob 时创建并回收对象 URL', () => {
    const blob = new Blob(['hello'], { type: 'text/plain' });
    const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:temp');
    const revokeUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const open = vi.spyOn(window, 'open').mockReturnValue({} as Window);

    openHostFileBlob(blob);

    expect(createUrl).toHaveBeenCalledWith(blob);
    expect(open).toHaveBeenCalledWith('blob:temp', '_blank', 'noopener,noreferrer');
    createUrl.mockRestore();
    revokeUrl.mockRestore();
    open.mockRestore();
  });
});
