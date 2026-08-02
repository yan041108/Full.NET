import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { addHostDocumentVersion, createHostDocumentItem, listHostDocumentItems } from './host-document-items';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sampleItem = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  title: 'Spec',
  description: null,
  categoryId: null,
  currentVersion: null,
  createdAtUtc: '2026-08-02T00:00:00Z',
  createdByUserId: '01912345-6789-7abc-8def-0123456789ac',
  updatedAtUtc: null,
  updatedByUserId: null,
  version: 1
};

describe('Vue Host 文档 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分页列表响应', async () => {
    requestMock.mockResolvedValueOnce({
      items: [sampleItem],
      page: 1,
      pageSize: 20,
      total: 1
    });
    const page = await listHostDocumentItems();
    expect(page.items).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/document/host/items?page=1&pageSize=20'
    );
  });

  it('创建文档项', async () => {
    requestMock.mockResolvedValueOnce(sampleItem);
    await createHostDocumentItem('Spec', null);
    const [, init] = requestMock.mock.calls[0] ?? [];
    expect(init?.method).toBe('POST');
  });

  it('绑定新版本', async () => {
    requestMock.mockResolvedValueOnce(sampleItem);
    await addHostDocumentVersion(sampleItem.id, '01912345-6789-7abc-8def-0123456789ad');
    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/document/host/items/${sampleItem.id}/versions`,
      expect.objectContaining({ method: 'POST' })
    );
  });
});
