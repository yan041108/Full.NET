import {
  dataApprovalsCancelRequest,
  dataApprovalsCreateRequest,
  dataApprovalsGetRequest,
  dataApprovalsListRequests,
  dataApprovalsRetryRequest,
  dataApprovalsRetryApplyRequest,
  type CancelDataApprovalRequestBody,
  type CreateDataApprovalRequestBody,
  type DataApprovalRequestResponse,
  type PagedResultOfDataApprovalRequestResponse,
  type RetryDataApprovalRequestBody
} from '@fullnet/client-contracts';
import { http } from './http';

export type DataApprovalRequestPage = PagedResultOfDataApprovalRequestResponse;

export interface ListDataApprovalRequestsParams {
  page?: number;
  pageSize?: number;
  scenarioKey?: string;
  statusKey?: string;
}

/** 分页读取当前可信作用域内的数据审批请求。 */
export function listDataApprovalRequests(
  params: ListDataApprovalRequestsParams = {},
  signal?: AbortSignal
): Promise<DataApprovalRequestPage> {
  return dataApprovalsListRequests(http, params, signal);
}

/** 读取单个数据审批请求。 */
export function getDataApprovalRequest(
  requestId: string,
  signal?: AbortSignal
): Promise<DataApprovalRequestResponse> {
  return dataApprovalsGetRequest(http, { requestId }, signal);
}

/** 创建数据审批请求。 */
export function createDataApprovalRequest(
  body: CreateDataApprovalRequestBody,
  signal?: AbortSignal
): Promise<DataApprovalRequestResponse> {
  return dataApprovalsCreateRequest(http, { body }, signal);
}

/** 取消由当前用户提交且仍可取消的数据审批请求。 */
export function cancelDataApprovalRequest(
  requestId: string,
  body: CancelDataApprovalRequestBody,
  signal?: AbortSignal
): Promise<DataApprovalRequestResponse> {
  return dataApprovalsCancelRequest(http, { requestId, body }, signal);
}

/** 人工重试 pending 请求的工作流关联。 */
export function retryDataApprovalRequest(
  requestId: string,
  body: RetryDataApprovalRequestBody,
  signal?: AbortSignal
): Promise<DataApprovalRequestResponse> {
  return dataApprovalsRetryRequest(http, { requestId, body }, signal);
}

/** 人工重试批准后业务应用。 */
export function retryDataApprovalApplyRequest(
  requestId: string,
  body: RetryDataApprovalRequestBody,
  signal?: AbortSignal
): Promise<DataApprovalRequestResponse> {
  return dataApprovalsRetryApplyRequest(http, { requestId, body }, signal);
}

export type {
  CancelDataApprovalRequestBody,
  CreateDataApprovalRequestBody,
  DataApprovalRequestResponse,
  RetryDataApprovalRequestBody
};
