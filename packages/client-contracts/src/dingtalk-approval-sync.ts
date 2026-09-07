export interface DingTalkApprovalSyncResponse {
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
}

export interface PagedDingTalkApprovalSyncResponse {
  items: DingTalkApprovalSyncResponse[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateDingTalkApprovalSyncRequest {
  workflowInstanceId: string;
  originatorUserId: string;
  deptId: number;
  title: string;
  summary?: string | null;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function isDingTalkApprovalSyncResponse(
  value: unknown
): value is DingTalkApprovalSyncResponse {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.workflowInstanceId)
    && typeof value.idempotencyKey === 'string'
    && (value.dingTalkProcessInstanceId === null || typeof value.dingTalkProcessInstanceId === 'string')
    && typeof value.processCode === 'string'
    && typeof value.originatorUserId === 'string'
    && Number.isInteger(value.deptId)
    && typeof value.title === 'string'
    && (value.summary === null || typeof value.summary === 'string')
    && typeof value.statusKey === 'string'
    && (value.externalStatusKey === null || typeof value.externalStatusKey === 'string')
    && (value.externalResultKey === null || typeof value.externalResultKey === 'string')
    && (value.lastErrorCode === null || typeof value.lastErrorCode === 'string')
    && (value.lastSyncedAtUtc === null || typeof value.lastSyncedAtUtc === 'string')
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string');
}

export function isPagedDingTalkApprovalSyncResponse(
  value: unknown
): value is PagedDingTalkApprovalSyncResponse {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isDingTalkApprovalSyncResponse)
    && Number.isInteger(value.page)
    && Number.isInteger(value.pageSize)
    && Number.isInteger(value.total);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
