import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import {
  getDocumentPermissionsByDocument,
  setDocumentPermissions
} from './document-permissions';

vi.mock('./http', () => ({
  http: {
    request: vi.fn(),
    requestBlob: vi.fn()
  }
}));
const requestMock = vi.mocked(http.request);

const documentId = '01912345-6789-7abc-8def-0123456789ab';
const userId = '01912345-6789-7abc-8def-0123456789ac';
const permission = {
  id: '01912345-6789-7abc-8def-0123456789ad',
  documentId,
  userId,
  permissionLevel: 'read',
  createdAtUtc: '2026-08-02T00:00:00Z'
};

describe('document-permissions api', () => {
  beforeEach(() => requestMock.mockReset());

  it('lists permissions by document', async () => {
    requestMock.mockResolvedValueOnce([permission]);

    const permissions = await getDocumentPermissionsByDocument(documentId);
    expect(permissions).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith(
      `/api/v1/document/host/permissions/by-document/${documentId}`,
      { method: 'GET' },
      undefined
    );
  });

  it('sets document permissions with request validation', async () => {
    requestMock.mockResolvedValueOnce([permission]);
    const req = {
      documentId,
      permissions: [{ userId, permissionLevel: 'read' }]
    };

    const permissions = await setDocumentPermissions(req);
    expect(permissions).toHaveLength(1);
    expect(requestMock).toHaveBeenCalledWith(
      '/api/v1/document/host/permissions',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify(req)
      }),
      undefined
    );
  });
});
