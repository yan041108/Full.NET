import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { createHostDocumentCategory, listHostDocumentCategories } from './host-document-categories';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sample = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  parentId: null,
  name: 'General',
  sortOrder: 0,
  createdAtUtc: '2026-08-02T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('Vue Host 文档分类 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验分类列表', async () => {
    requestMock.mockResolvedValueOnce([sample]);
    const items = await listHostDocumentCategories();
    expect(items).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith('/api/v1/document/host/categories');
  });

  it('创建分类', async () => {
    requestMock.mockResolvedValueOnce(sample);
    await createHostDocumentCategory('General', null, 0);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/document/host/categories',
      expect.objectContaining({ method: 'POST' })
    );
  });
});
