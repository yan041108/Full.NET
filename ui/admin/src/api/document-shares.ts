import {
  isAccessHostDocumentShareRequest,
  isCreateHostDocumentShareRequest,
  isHostDocumentShareAccessResponse,
  isHostDocumentSharePage,
  isHostDocumentShareResponse,
  isUpdateHostDocumentShareStatusRequest,
  type AccessHostDocumentShareRequest,
  type CreateHostDocumentShareRequest,
  type HostDocumentShareAccessResponse,
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

export async function accessDocumentShareByCode(
  shareCode: string,
  req: AccessHostDocumentShareRequest = {}
): Promise<HostDocumentShareAccessResponse> {
  if (!isAccessHostDocumentShareRequest(req)) {
    throw new Error('client.invalid_access_document_share_request');
  }
  const value = await request<unknown>(
    `/api/v1/document/public/shares/${encodeURIComponent(shareCode)}/access`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(req)
    }
  );
  if (!isHostDocumentShareAccessResponse(value)) {
    throw new Error('client.invalid_document_share_access');
  }
  return value;
}

export type {
  AccessHostDocumentShareRequest,
  CreateHostDocumentShareRequest,
  HostDocumentShareAccessResponse,
  HostDocumentSharePage,
  HostDocumentShareResponse,
  UpdateHostDocumentShareStatusRequest
};
