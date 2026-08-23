import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  accessDocumentShareByCode,
  createDocumentShare,
  listDocumentShares,
  updateDocumentShareStatus
} from './document-shares';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const share = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  documentId: '01912345-6789-7abc-8def-0123456789ac',
  shareCode: 'SHARE-ABC123',
  createdAtUtc: '2026-08-02T00:00:00Z',
  expireTime: '2026-08-09T00:00:00Z',
  maxAccessCount: 10,
  accessCount: 0,
  isEnabled: true,
  version: 1,
  hasPassword: true
};

const access = {
  shareId: share.id,
  documentId: share.documentId,
  shareCode: share.shareCode,
  title: 'Spec',
  fileName: 'spec.pdf',
  mimeType: 'application/pdf',
  fileSizeBytes: 1024,
  hasPassword: true,
  accessCountRemaining: 9
};

describe('document-shares api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists document shares', async () => {
    requestMock.mockResolvedValueOnce({
      items: [share],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listDocumentShares(1, 20)).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/document/host/shares?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
  });

  it('creates, updates and accesses document shares', async () => {
    requestMock
      .mockResolvedValueOnce(share)
      .mockResolvedValueOnce({ ...share, isEnabled: false, version: 2 })
      .mockResolvedValueOnce(access);

    await expect(
      createDocumentShare({
        documentId: share.documentId,
        validDays: 7,
        password: 'Share@2026',
        maxAccessCount: 10
      })
    ).resolves.toMatchObject({ shareCode: share.shareCode });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      '/api/v1/document/host/shares',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          documentId: share.documentId,
          validDays: 7,
          password: 'Share@2026',
          maxAccessCount: 10
        })
      }),
      undefined
    );

    await expect(updateDocumentShareStatus(share.id, { isEnabled: false, version: 1 }))
      .resolves.toMatchObject({ isEnabled: false, version: 2 });
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/document/host/shares/${share.id}/status`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ isEnabled: false, version: 1 })
      }),
      undefined
    );

    await expect(accessDocumentShareByCode(share.shareCode, { password: 'Share@2026' }))
      .resolves.toMatchObject({ title: 'Spec' });
    expect(requestMock).toHaveBeenNthCalledWith(
      3,
      `/api/v1/document/public/shares/${share.shareCode}/access`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ password: 'Share@2026' })
      }),
      undefined
    );
  });
});
