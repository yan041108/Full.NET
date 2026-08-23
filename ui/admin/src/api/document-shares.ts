import {
  documentHostCreateDocumentShare,
  documentHostListDocumentShares,
  documentHostUpdateDocumentShareStatus,
  documentPublicAccessDocumentShare,
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
import { http } from './http';

export async function listDocumentShares(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostDocumentSharePage> {
  const value = await documentHostListDocumentShares(
    http,
    { page, pageSize },
    signal
  );
  if (!isHostDocumentSharePage(value)) {
    throw new Error('client.invalid_document_share_page');
  }
  return value;
}

export async function createDocumentShare(
  req: CreateHostDocumentShareRequest,
  signal?: AbortSignal
): Promise<HostDocumentShareResponse> {
  if (!isCreateHostDocumentShareRequest(req)) {
    throw new Error('client.invalid_create_document_share_request');
  }
  const value = await documentHostCreateDocumentShare(http, { body: req }, signal);
  if (!isHostDocumentShareResponse(value)) {
    throw new Error('client.invalid_document_share');
  }
  return value;
}

export async function updateDocumentShareStatus(
  id: string,
  req: UpdateHostDocumentShareStatusRequest,
  signal?: AbortSignal
): Promise<HostDocumentShareResponse> {
  if (!isUpdateHostDocumentShareStatusRequest(req)) {
    throw new Error('client.invalid_update_document_share_status_request');
  }
  const value = await documentHostUpdateDocumentShareStatus(
    http,
    { id, body: req },
    signal
  );
  if (!isHostDocumentShareResponse(value)) {
    throw new Error('client.invalid_document_share');
  }
  return value;
}

export async function accessDocumentShareByCode(
  shareCode: string,
  req: AccessHostDocumentShareRequest = {},
  signal?: AbortSignal
): Promise<HostDocumentShareAccessResponse> {
  if (!isAccessHostDocumentShareRequest(req)) {
    throw new Error('client.invalid_access_document_share_request');
  }
  const value = await documentPublicAccessDocumentShare(
    http,
    { shareCode, body: req },
    signal
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
