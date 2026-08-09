import {
  isHostDocumentPermissionResponseList,
  isSetHostDocumentPermissionsRequest,
  type HostDocumentPermissionResponse,
  type SetHostDocumentPermissionsRequest
} from '@fullnet/client-contracts';
import { request } from './http';

export async function getDocumentPermissionsByDocument(
  documentId: string
): Promise<HostDocumentPermissionResponse[]> {
  const value = await request<unknown>(
    `/api/v1/document/host/permissions/by-document/${encodeURIComponent(documentId)}`
  );
  if (!isHostDocumentPermissionResponseList(value)) {
    throw new Error('client.invalid_document_permission_list');
  }
  return value;
}

export async function setDocumentPermissions(
  req: SetHostDocumentPermissionsRequest
): Promise<HostDocumentPermissionResponse[]> {
  if (!isSetHostDocumentPermissionsRequest(req)) {
    throw new Error('client.invalid_set_document_permissions_request');
  }
  const value = await request<unknown>('/api/v1/document/host/permissions', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(req)
  });
  if (!isHostDocumentPermissionResponseList(value)) {
    throw new Error('client.invalid_document_permission_list');
  }
  return value;
}
