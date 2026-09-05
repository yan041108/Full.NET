import {
  dataApprovalsCancelRequest,
  dataApprovalsCreateRequest,
  dataApprovalsGetRequest,
  dataApprovalsListRequests,
  type CancelDataApprovalRequestBody,
  type CreateDataApprovalRequestBody,
  type DataApprovalRequestResponse,
  type PagedResultOfDataApprovalRequestResponse
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

export type {
  CancelDataApprovalRequestBody,
  CreateDataApprovalRequestBody,
  DataApprovalRequestResponse
};
