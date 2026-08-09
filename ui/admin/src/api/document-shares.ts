import {
  isCreateHostDocumentShareRequest,
  isHostDocumentSharePage,
  isHostDocumentShareResponse,
  isUpdateHostDocumentShareStatusRequest,
  type CreateHostDocumentShareRequest,
  type HostDocumentSharePage,
  type HostDocumentShareResponse,
  type UpdateHostDocumentShareStatusRequest
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listDocumentShares(
  page = 1,
  pageSize = 20
): Promise<HostDocumentSharePage> {
  const value = await request<unknown>(
    `/api/v1/document/host/shares?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostDocumentSharePage(value)) {
    throw new Error('client.invalid_document_share_page');
  }
  return value;
}

export async function createDocumentShare(
  req: CreateHostDocumentShareRequest
): Promise<HostDocumentShareResponse> {
  if (!isCreateHostDocumentShareRequest(req)) {
    throw new Error('client.invalid_create_document_share_request');
  }
  const value = await request<unknown>('/api/v1/document/host/shares', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(req)
  });
  if (!isHostDocumentShareResponse(value)) {
    throw new Error('client.invalid_document_share');
  }
  return value;
}

export async function updateDocumentShareStatus(
  id: string,
  req: UpdateHostDocumentShareStatusRequest
): Promise<HostDocumentShareResponse> {
  if (!isUpdateHostDocumentShareStatusRequest(req)) {
    throw new Error('client.invalid_update_document_share_status_request');
  }
  const value = await request<unknown>(
    `/api/v1/document/host/shares/${encodeURIComponent(id)}/status`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(req)
    }
  );
  if (!isHostDocumentShareResponse(value)) {
    throw new Error('client.invalid_document_share');
  }
  return value;
}

export async function getDocumentShareByCode(
  shareCode: string
): Promise<HostDocumentShareResponse> {
  const value = await request<unknown>(
    `/api/v1/document/host/shares/by-code/${encodeURIComponent(shareCode)}`
  );
  if (!isHostDocumentShareResponse(value)) {
    throw new Error('client.invalid_document_share');
  }
  return value;
}
