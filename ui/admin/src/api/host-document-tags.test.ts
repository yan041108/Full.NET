import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { createHostDocumentTag, listHostDocumentTags } from './host-document-tags';

vi.mock('./http', () => ({ request: vi.fn() }));
const requestMock = vi.mocked(request);

const sample = {
  id: '01912345-6789-7abc-8def-0123456789ab',
  name: 'release',
  createdAtUtc: '2026-08-02T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

describe('Vue Host 文档标签 API', () => {
  beforeEach(() => requestMock.mockReset());

  it('校验标签列表', async () => {
    requestMock.mockResolvedValueOnce([sample]);
    const items = await listHostDocumentTags();
    expect(items).toHaveLength(1);
  });

  it('创建标签', async () => {
    requestMock.mockResolvedValueOnce(sample);
    await createHostDocumentTag('release');
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/document/host/tags',
      expect.objectContaining({ method: 'POST' })
    );
  });
});
