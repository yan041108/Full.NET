import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createDocumentItem,
  openDocumentBlob,
  safeDocumentPreviewBlob,
  downloadDocumentContent,
  listDocumentItems,
  previewDocumentContent,
  updateDocumentItem,
  uploadDocumentVersion
} from './host-document-items';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);
const requestBlobMock = vi.mocked(http.requestBlob);

const item = {
  visibilityMode: 'host' as const,
  id: '01912345-6789-7abc-8def-0123456789ab',
  documentNo: 'DOC-000001',
  title: 'Spec',
  description: 'integration',
  categoryId: null,
  categoryName: null,
  categoryColor: null,
  documentType: 1,
  sizeKb: 0,
  thumbnail: null,
  status: 1,
  accessCount: 0,
  sort: 0,
  lastAccessTime: null,
  currentVersion: {
    id: '01912345-6789-7abc-8def-0123456789ac',
    versionNumber: 1,
    fileId: '01912345-6789-7abc-8def-0123456789ad',
    contentHash: 'a'.repeat(64),
    sizeBytes: 12,
    changeDescription: null,
    createdAtUtc: '2026-08-02T00:00:00Z',
    uploadedByUserId: '01912345-6789-7abc-8def-0123456789ae'
  },
  tags: [],
  createdAtUtc: '2026-08-02T00:00:00Z',
  createdByUserId: '01912345-6789-7abc-8def-0123456789af',
  updatedAtUtc: null,
  updatedByUserId: null,
  deletedAtUtc: null,
  deletedByUserId: null,
  version: 2
};

describe('host-document-items api', () => {
  beforeEach(() => {
    requestMock.mockReset();
    requestBlobMock.mockReset();
  });

  it('lists document items', async () => {
    requestMock.mockResolvedValueOnce({
      items: [item],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listDocumentItems(1, 20)).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/document/host/items?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
  });

  it('creates and uploads document versions', async () => {
    requestMock.mockResolvedValueOnce(item);

    await expect(createDocumentItem('Spec', 'integration'))
      .resolves.toMatchObject({ title: 'Spec' });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/document/host/items',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          title: 'Spec',
          description: 'integration',
          categoryId: null,
          documentType: 1,
          sort: 0,
          status: 0,
          tagIds: null,
          thumbnail: null
        })
      }),
      undefined
    );

    requestMock.mockResolvedValueOnce({ ...item, version: 3 });
    const file = new File(['hello'], 'hello.txt', { type: 'text/plain' });
    await expect(uploadDocumentVersion(item.id, file))
      .resolves.toMatchObject({ version: 3 });
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/document/host/items/${item.id}/versions/upload`,
      expect.objectContaining({ method: 'POST' }),
      undefined
    );
  });

  it('downloads and previews document content as blobs', async () => {
    const blob = new Blob(['content'], { type: 'application/pdf' });
    requestBlobMock.mockResolvedValueOnce(blob).mockResolvedValueOnce(blob);

    await expect(downloadDocumentContent(item.id)).resolves.toBe(blob);
    expect(requestBlobMock).toHaveBeenNthCalledWith(
      1,
      expect.stringMatching(new RegExp(`/api/v1/document/host/items/${item.id}/content\\?requestKey=[0-9a-f-]+$`)),
      {
        method: 'GET',
        headers: { accept: 'application/octet-stream' }
      },
      undefined
    );

    await expect(previewDocumentContent(item.id)).resolves.toBe(blob);
    expect(requestBlobMock).toHaveBeenNthCalledWith(
      2,
      expect.stringMatching(new RegExp(`/api/v1/document/host/items/${item.id}/preview\\?requestKey=[0-9a-f-]+$`)),
      {
        method: 'GET',
        headers: { accept: 'application/octet-stream' }
      },
      undefined
    );
  });

  it('标题编辑提交完整元数据并保留排序、缩略图和标签', async () => {
    requestMock.mockResolvedValueOnce(item);
    await updateDocumentItem({ ...item, visibilityMode: 'host' as const, sort: 7, thumbnail: '/cover.png',
      tags: [{ tagId: item.id, tagName: 'Important' }] }, 'Renamed', null);
    const init = requestMock.mock.calls[0]![1]!;
    expect(JSON.parse(String(init.body))).toEqual({ title: 'Renamed', description: null,
      categoryId: null, thumbnail: '/cover.png', sort: 7, tagIds: [item.id], version: item.version });
  });

  it('同一文件上传重试保留请求键', async () => {
    requestMock.mockResolvedValue(item);
    const file = new File(['same'], 'same.txt', { type: 'text/plain' });
    await uploadDocumentVersion(item.id, file);
    await uploadDocumentVersion(item.id, file);
    const first = requestMock.mock.calls[0]![1]!.body as FormData;
    const second = requestMock.mock.calls[1]![1]!.body as FormData;
    expect(first.get('requestKey')).toEqual(expect.any(String));
    expect(second.get('requestKey')).toBe(first.get('requestKey'));
  });
  it('分页组合条件包含零值状态，并将清空值与未修改值区分', async () => {
    requestMock.mockResolvedValueOnce({ items: [], page: 3, pageSize: 10, total: 100 });
    await listDocumentItems(3, 10, { keyword: '第101条', categoryId: item.id, status: 0, documentType: 2, tagId: item.id });
    const url = new URL(String(requestMock.mock.calls[0]![0]), 'http://localhost');
    expect(Object.fromEntries(url.searchParams)).toMatchObject({ page: '3', pageSize: '10', keyword: '第101条', status: '0', documentType: '2', categoryId: item.id, tagId: item.id });
    requestMock.mockResolvedValueOnce(item);
    await updateDocumentItem({ ...item, categoryId: item.id, thumbnail: '/cover.png' }, 'Spec', null,
      { categoryId: null, thumbnail: null, tagIds: [], sort: 0 });
    expect(JSON.parse(String(requestMock.mock.calls[1]![1]!.body))).toMatchObject({ categoryId: null, thumbnail: null, tagIds: [], sort: 0 });
  });

  it.each(['text/html', 'image/svg+xml', 'application/xhtml+xml'])('活动格式 %s 按原始字节纯文本展示', mime => {
    const source = new Blob(['<script>localStorage.clear()</script>'], { type: mime });
    expect(safeDocumentPreviewBlob(source)).toMatchObject({ type: 'text/plain', size: source.size });
  });

  it('安全预览与普通下载不依赖 noopener 窗口句柄，延迟回收 URL', () => {
    vi.useFakeTimers();
    const create = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:document');
    const revoke = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const clicked: HTMLAnchorElement[] = [];
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) { clicked.push(this); });
    try {
      openDocumentBlob(new Blob(['pdf'], { type: 'application/pdf' }));
      openDocumentBlob(new Blob(['zip'], { type: 'application/zip' }));
      expect(clicked[0]).toMatchObject({ target: '_blank', rel: 'noopener noreferrer', download: '' });
      expect(clicked[1]!.download).toBe('document');
      expect(revoke).not.toHaveBeenCalled();
      vi.advanceTimersByTime(60_000);
      expect(revoke).toHaveBeenCalledTimes(2);
    } finally { create.mockRestore(); revoke.mockRestore(); click.mockRestore(); vi.useRealTimers(); }
  });

});
