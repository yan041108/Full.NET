import { request } from './http';

export interface DataApprovalRequestResponse {
  id: string;
  scenarioKey: string;
  targetEntityId: string;
  statusKey: string;
  beforeSnapshotJson: string | null;
  afterSnapshotJson: string;
  workflowInstanceId: string | null;
  workflowRevision: number | null;
  workflowDefinitionVersionId: string;
  submittedByUserId: string;
  submittedAtUtc: string;
  resolvedAtUtc: string | null;
  version: number;
}

export interface DataApprovalRequestPage {
  items: DataApprovalRequestResponse[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateDataApprovalRequestBody {
  scenarioKey: string;
  targetEntityId: string;
  proposedChangeJson: string;
  workflowDefinitionKey: string;
  idempotencyKey: string;
}

export interface CancelDataApprovalRequestBody {
  idempotencyKey: string;
}

export interface ListDataApprovalRequestsParams {
  page?: number;
  pageSize?: number;
  scenarioKey?: string;
  statusKey?: string;
}

function isDataApprovalRequestResponse(value: unknown): value is DataApprovalRequestResponse {
  if (!value || typeof value !== 'object') return false;
  const record = value as Record<string, unknown>;
  return typeof record.id === 'string' &&
    typeof record.scenarioKey === 'string' &&
    typeof record.statusKey === 'string';
}

function isDataApprovalRequestPage(value: unknown): value is DataApprovalRequestPage {
  if (!value || typeof value !== 'object') return false;
  const record = value as Record<string, unknown>;
  return Array.isArray(record.items) &&
    typeof record.page === 'number' &&
    typeof record.pageSize === 'number' &&
    typeof record.total === 'number';
}

export async function listDataApprovalRequests(
  params: ListDataApprovalRequestsParams = {},
  signal?: AbortSignal
): Promise<DataApprovalRequestPage> {
  const search = new URLSearchParams();
  if (params.page) search.set('page', String(params.page));
  if (params.pageSize) search.set('pageSize', String(params.pageSize));
  if (params.scenarioKey) search.set('scenarioKey', params.scenarioKey);
  if (params.statusKey) search.set('statusKey', params.statusKey);
  const query = search.toString();
  const response = await request<DataApprovalRequestPage>(
    `/api/v1/data-approvals/requests${query ? `?${query}` : ''}`,
    { method: 'GET', signal }
  );
  if (!isDataApprovalRequestPage(response)) {
    throw new Error('data_approvals.response.invalid');
  }
  return response;
}

export async function getDataApprovalRequest(
  requestId: string,
  signal?: AbortSignal
): Promise<DataApprovalRequestResponse> {
  const response = await request<DataApprovalRequestResponse>(
    `/api/v1/data-approvals/requests/${requestId}`,
    { method: 'GET', signal }
  );
  if (!isDataApprovalRequestResponse(response)) {
    throw new Error('data_approvals.response.invalid');
  }
  return response;
}

export async function createDataApprovalRequest(
  body: CreateDataApprovalRequestBody,
  signal?: AbortSignal
): Promise<DataApprovalRequestResponse> {
  const response = await request<DataApprovalRequestResponse>(
    '/api/v1/data-approvals/requests',
    { method: 'POST', body, signal }
  );
  if (!isDataApprovalRequestResponse(response)) {
    throw new Error('data_approvals.response.invalid');
  }
  return response;
}

export async function cancelDataApprovalRequest(
  requestId: string,
  body: CancelDataApprovalRequestBody,
  signal?: AbortSignal
): Promise<DataApprovalRequestResponse> {
  const response = await request<DataApprovalRequestResponse>(
    `/api/v1/data-approvals/requests/${requestId}/cancel`,
    { method: 'POST', body, signal }
  );
  if (!isDataApprovalRequestResponse(response)) {
    throw new Error('data_approvals.response.invalid');
  }
  return response;
}
