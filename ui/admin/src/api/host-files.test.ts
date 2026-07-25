import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { deleteHostFile, listHostFiles, uploadHostFile } from './host-files';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

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
});
