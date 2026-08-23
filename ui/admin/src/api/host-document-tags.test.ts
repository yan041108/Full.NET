import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { createDocumentTag, listDocumentTags } from './host-document-tags';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const tag = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  name: 'Guides',
  code: null,
  icon: null,
  color: null,
  description: null,
  useCount: 0,
  createdAtUtc: '2026-08-02T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('document-tags api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists tags with runtime guards', async () => {
    requestMock.mockResolvedValueOnce([tag]);

    const tags = await listDocumentTags();
    expect(tags).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/document/host/tags',
      { method: 'GET' },
      undefined
    );
  });

  it('creates tags with runtime guards', async () => {
    requestMock.mockResolvedValueOnce(tag);

    const created = await createDocumentTag('Guides', null, null, null, null);
    expect(created.name).toBe('Guides');
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/document/host/tags',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({
          name: 'Guides',
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
