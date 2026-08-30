import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  createDocumentItem,
  downloadDocumentContent,
  listDocumentItems,
  previewDocumentContent,
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
          status: 1,
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
      `/api/v1/document/host/items/${item.id}/content`,
      {
        method: 'GET',
        headers: { accept: 'application/octet-stream' }
      },
      undefined
    );

    await expect(previewDocumentContent(item.id)).resolves.toBe(blob);
    expect(requestBlobMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/document/host/items/${item.id}/preview`,
      {
        method: 'GET',
        headers: { accept: 'application/octet-stream' }
      },
      undefined
    );
  });
});
