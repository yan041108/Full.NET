import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { createDocumentCategory, listDocumentCategories } from './host-document-categories';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const category = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  parentId: null,
  name: 'Guides',
  code: null,
  sortOrder: 0,
  icon: null,
  color: null,
  description: null,
  createdAtUtc: '2026-08-02T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('document-categories api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists categories with runtime guards', async () => {
    requestMock.mockResolvedValueOnce([category]);

    const categories = await listDocumentCategories();
    expect(categories).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/document/host/categories',
      { method: 'GET' },
      undefined
    );
  });

  it('creates categories with runtime guards', async () => {
    requestMock.mockResolvedValueOnce(category);

    const created = await createDocumentCategory('Guides', null, 0, null, null, null, null);
    expect(created.name).toBe('Guides');
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/document/host/categories',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          name: 'Guides',
          parentId: null,
          sortOrder: 0,
          code: null,
          icon: null,
          color: null,
          description: null
        })
      }),
      undefined
    );
  });
});
