import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createDocumentCategory, listDocumentCategories } from './host-document-categories';

const requestMock = vi.fn();

vi.mock('./http', () => ({
  request: (...args: unknown[]) => requestMock(...args)
}));

describe('document-categories api', () => {
  beforeEach(() => {
    requestMock.mockReset();
  });

  it('lists categories with runtime guards', async () => {
    requestMock.mockResolvedValue([
      {
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
      }
    ]);

    const categories = await listDocumentCategories();
    expect(categories).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith('/api/v1/document/host/categories');
  });

  it('creates categories with runtime guards', async () => {
    requestMock.mockResolvedValue({
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
    });

    const category = await createDocumentCategory('Guides', null, 0, null, null, null, null);
    expect(category.name).toBe('Guides');
  });
});
