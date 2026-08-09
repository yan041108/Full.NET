import {
  isHostRecycleBinItemResponse,
  isHostRecycleBinPage,
  isRestoreHostRecycleBinItemRequest,
  type HostRecycleBinItemResponse,
  type HostRecycleBinPage,
  type RestoreHostRecycleBinItemRequest
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listRecycleBinItems(
  page = 1,
  pageSize = 20
): Promise<HostRecycleBinPage> {
  const value = await request<unknown>(
    `/api/v1/document/host/recycle-bin?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostRecycleBinPage(value)) {
    throw new Error('client.invalid_recycle_bin_page');
  }
  return value;
}

export async function restoreRecycleBinItem(
  id: string,
  req: RestoreHostRecycleBinItemRequest
): Promise<HostRecycleBinItemResponse> {
  if (!isRestoreHostRecycleBinItemRequest(req)) {
    throw new Error('client.invalid_restore_recycle_bin_request');
  }
  const value = await request<unknown>(
    `/api/v1/document/host/recycle-bin/${encodeURIComponent(id)}/restore`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(req)
    }
  );
  if (!isHostRecycleBinItemResponse(value)) {
    throw new Error('client.invalid_recycle_bin_item');
  }
  return value;
}

export async function purgeRecycleBinItem(id: string): Promise<boolean> {
  const value = await request<unknown>(
    `/api/v1/document/host/recycle-bin/${encodeURIComponent(id)}/purge`,
    {
      method: 'POST'
    }
  );
  return value === true;
}
