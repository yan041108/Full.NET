import {
  isDingTalkApprovalSyncResponse,
  isPagedDingTalkApprovalSyncResponse,
  type CreateDingTalkApprovalSyncRequest,
  type DingTalkApprovalSyncResponse,
  type PagedDingTalkApprovalSyncResponse
} from '@fullnet/client-contracts';
import { request } from './http';

/** 分页查询钉钉审批镜像同步记录。 */
export async function listDingTalkApprovalSync(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<PagedDingTalkApprovalSyncResponse> {
  const value = await request<unknown>(
    `/api/v1/notifications/dingtalk/approval-sync?page=${page}&pageSize=${pageSize}`,
    { method: 'GET' },
    signal
  );
  if (!isPagedDingTalkApprovalSyncResponse(value)) {
    throw new Error('client.invalid_dingtalk_approval_sync');
  }
  return value;
}

/** 读取单条钉钉审批镜像同步记录。 */
export async function getDingTalkApprovalSync(
  syncId: string,
  signal?: AbortSignal
): Promise<DingTalkApprovalSyncResponse> {
  const value = await request<unknown>(
    `/api/v1/notifications/dingtalk/approval-sync/${syncId}`,
    { method: 'GET' },
    signal
  );
  if (!isDingTalkApprovalSyncResponse(value)) {
    throw new Error('client.invalid_dingtalk_approval_sync');
  }
  return value;
}

/** 为工作流实例登记钉钉审批镜像同步。 */
export async function createDingTalkApprovalSync(
  body: CreateDingTalkApprovalSyncRequest,
  signal?: AbortSignal
): Promise<DingTalkApprovalSyncResponse> {
  const value = await request<unknown>(
    '/api/v1/notifications/dingtalk/approval-sync',
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) },
    signal
  );
  if (!isDingTalkApprovalSyncResponse(value)) {
    throw new Error('client.invalid_dingtalk_approval_sync');
  }
  return value;
}

/** 对失败出站记录发起补偿重试。 */
export async function retryDingTalkApprovalSync(
  syncId: string,
  signal?: AbortSignal
): Promise<DingTalkApprovalSyncResponse> {
  const value = await request<unknown>(
    `/api/v1/notifications/dingtalk/approval-sync/${syncId}/retry`,
    { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{}' },
    signal
  );
  if (!isDingTalkApprovalSyncResponse(value)) {
    throw new Error('client.invalid_dingtalk_approval_sync');
  }
  return value;
}

export type {
  CreateDingTalkApprovalSyncRequest,
  DingTalkApprovalSyncResponse,
  PagedDingTalkApprovalSyncResponse
};
