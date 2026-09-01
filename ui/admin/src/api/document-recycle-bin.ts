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

/** 分页查询文档回收站列表，并对响应页做失败关闭校验。 */
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

/** 恢复回收站条目；请求与响应都必须通过运行时契约校验。 */
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

/** 永久清除回收站条目。 */
export async function purgeRecycleBinItem(
  id: string,
  signal?: AbortSignal
): Promise<boolean> {
  return documentHostPurgeRecycleBinItem(http, { id }, signal);
}

/** 导出回收站分页、条目与恢复请求模型，供列表页和恢复确认流程复用同一数据结构。 */
export type {
  HostRecycleBinItemResponse,
  HostRecycleBinPage,
  RestoreHostRecycleBinItemRequest
};
