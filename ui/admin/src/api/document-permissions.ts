import {
  documentHostListDocumentPermissions,
  documentHostSetDocumentPermissions,
  isHostDocumentPermissionResponseList,
  isSetHostDocumentPermissionsRequest,
  type HostDocumentPermissionResponse,
  type SetHostDocumentPermissionsRequest
} from '@fullnet/client-contracts';
import { http } from './http';

export async function getDocumentPermissionsByDocument(
  documentId: string,
  signal?: AbortSignal
): Promise<HostDocumentPermissionResponse[]> {
  const value = await documentHostListDocumentPermissions(
    http,
    { documentId },
    signal
  );
  if (!isHostDocumentPermissionResponseList(value)) {
    throw new Error('client.invalid_document_permission_list');
  }
  return value;
}

export async function setDocumentPermissions(
  req: SetHostDocumentPermissionsRequest,
  signal?: AbortSignal
): Promise<HostDocumentPermissionResponse[]> {
  if (!isSetHostDocumentPermissionsRequest(req)) {
    throw new Error('client.invalid_set_document_permissions_request');
  }
  const value = await documentHostSetDocumentPermissions(
    http,
    { body: req },
    signal
  );
  if (!isHostDocumentPermissionResponseList(value)) {
    throw new Error('client.invalid_document_permission_list');
  }
  return value;
}

export type { HostDocumentPermissionResponse, SetHostDocumentPermissionsRequest };
