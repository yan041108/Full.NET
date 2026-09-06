import { request } from './http';

export type DingTalkApprovalSyncResponse = {
  id: string;
  workflowInstanceId: string;
  idempotencyKey: string;
  dingTalkProcessInstanceId: string | null;
  processCode: string;
  originatorUserId: string;
  deptId: number;
  title: string;
  summary: string | null;
  statusKey: string;
  externalStatusKey: string | null;
  externalResultKey: string | null;
  lastErrorCode: string | null;
  lastSyncedAtUtc: string | null;
  createdAtUtc: string;
  updatedAtUtc: string | null;
};

export type PagedDingTalkApprovalSyncResponse = {
  items: DingTalkApprovalSyncResponse[];
  page: number;
  pageSize: number;
  total: number;
};

export type CreateDingTalkApprovalSyncRequest = {
  workflowInstanceId: string;
  originatorUserId: string;
  deptId: number;
  title: string;
  summary?: string | null;
};

function isSyncRecord(value: unknown): value is DingTalkApprovalSyncResponse {
  return typeof value === 'object'
    && value !== null
    && typeof (value as DingTalkApprovalSyncResponse).id === 'string';
}

function isPagedSync(value: unknown): value is PagedDingTalkApprovalSyncResponse {
  return typeof value === 'object'
    && value !== null
    && Array.isArray((value as PagedDingTalkApprovalSyncResponse).items);
}

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
  if (!isPagedSync(value)) {
    throw new Error('client.invalid_dingtalk_approval_sync');
  }
  return value;
}

export async function getDingTalkApprovalSync(
  syncId: string,
  signal?: AbortSignal
): Promise<DingTalkApprovalSyncResponse> {
  const value = await request<unknown>(
    `/api/v1/notifications/dingtalk/approval-sync/${syncId}`,
    { method: 'GET' },
    signal
  );
  if (!isSyncRecord(value)) {
    throw new Error('client.invalid_dingtalk_approval_sync');
  }
  return value;
}

export async function createDingTalkApprovalSync(
  body: CreateDingTalkApprovalSyncRequest,
  signal?: AbortSignal
): Promise<DingTalkApprovalSyncResponse> {
  const value = await request<unknown>(
    '/api/v1/notifications/dingtalk/approval-sync',
    { method: 'POST', body },
    signal
  );
  if (!isSyncRecord(value)) {
    throw new Error('client.invalid_dingtalk_approval_sync');
  }
  return value;
}

export async function retryDingTalkApprovalSync(
  syncId: string,
  signal?: AbortSignal
): Promise<DingTalkApprovalSyncResponse> {
  const value = await request<unknown>(
    `/api/v1/notifications/dingtalk/approval-sync/${syncId}/retry`,
    { method: 'POST', body: {} },
    signal
  );
  if (!isSyncRecord(value)) {
    throw new Error('client.invalid_dingtalk_approval_sync');
  }
  return value;
}
