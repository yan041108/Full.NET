import {
  documentHostListRecycleBinItems,
  documentHostPurgeRecycleBinItem,
  documentHostRestoreRecycleBinItem,
  isHostRecycleBinItemResponse,
  isHostRecycleBinPage,
  isRestoreHostRecycleBinItemRequest,
  type HostRecycleBinItemResponse,
  type HostRecycleBinPage,
  type RestoreHostRecycleBinItemRequest
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listRecycleBinItems(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<HostRecycleBinPage> {
  const value = await documentHostListRecycleBinItems(
    http,
    { page, pageSize },
    signal
  );
  if (!isHostRecycleBinPage(value)) {
    throw new Error('client.invalid_recycle_bin_page');
  }
  return value;
}

export async function restoreRecycleBinItem(
  id: string,
  req: RestoreHostRecycleBinItemRequest,
  signal?: AbortSignal
): Promise<HostRecycleBinItemResponse> {
  if (!isRestoreHostRecycleBinItemRequest(req)) {
    throw new Error('client.invalid_restore_recycle_bin_request');
  }
  const value = await documentHostRestoreRecycleBinItem(
    http,
    { id, body: req },
    signal
  );
  if (!isHostRecycleBinItemResponse(value)) {
    throw new Error('client.invalid_recycle_bin_item');
  }
  return value;
}

export async function purgeRecycleBinItem(
  id: string,
  signal?: AbortSignal
): Promise<boolean> {
  return documentHostPurgeRecycleBinItem(http, { id }, signal);
}

export type {
  HostRecycleBinItemResponse,
  HostRecycleBinPage,
  RestoreHostRecycleBinItemRequest
};
