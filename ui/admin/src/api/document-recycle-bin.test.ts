import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  listRecycleBinItems,
  purgeRecycleBinItem,
  restoreRecycleBinItem
} from './document-recycle-bin';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const item = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  documentNo: 'DOC-000001',
  title: 'Deleted Spec',
  description: 'integration',
  categoryId: null,
  categoryName: null,
  categoryColor: null,
  documentType: 1,
  sizeKb: 0,
  thumbnail: null,
  status: 4,
  accessCount: 0,
  sort: 0,
  lastAccessTime: null,
  currentVersion: null,
  tags: [],
  createdAtUtc: '2026-08-02T00:00:00Z',
  createdByUserId: '01912345-6789-7abc-8def-0123456789af',
  updatedAtUtc: null,
  updatedByUserId: null,
  deletedAtUtc: '2026-08-03T00:00:00Z',
  deletedByUserId: '01912345-6789-7abc-8def-0123456789ae',
  version: 2
};

describe('document-recycle-bin api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists recycle bin items', async () => {
    requestMock.mockResolvedValueOnce({
      items: [item],
      page: 1,
      pageSize: 20,
      total: 1
    });

    await expect(listRecycleBinItems(1, 20)).resolves.toMatchObject({ total: 1 });
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/document/host/recycle-bin?page=1&pageSize=20',
      { method: 'GET' },
      undefined
    );
  });

  it('restores and purges recycle bin items', async () => {
    requestMock
      .mockResolvedValueOnce({ ...item, status: 1, deletedAtUtc: null, deletedByUserId: null, version: 3 })
      .mockResolvedValueOnce(true);

    await expect(restoreRecycleBinItem(item.id, { version: 2 }))
      .resolves.toMatchObject({ status: 1 });
    expect(requestMock).toHaveBeenNthCalledWith(
      1,
      `/api/v1/document/host/recycle-bin/${item.id}/restore`,
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ version: 2 })
      }),
      undefined
    );

    await expect(purgeRecycleBinItem(item.id)).resolves.toBe(true);
    expect(requestMock).toHaveBeenNthCalledWith(
      2,
      `/api/v1/document/host/recycle-bin/${item.id}/purge`,
      { method: 'POST' },
      undefined
    );
  });
});
