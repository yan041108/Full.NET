// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 低层 HttpClient Operation。

import type { HttpClient, RequestOptions } from "@fullnet/client-contracts";
import type {
  CreateEnterpriseRequestAggregateRequest,
  CreateEnterpriseRequestDetailRequest,
  CreateEnterpriseRequestRequest,
  DeleteEnterpriseRequestDetailRequest,
  DeleteEnterpriseRequestRequest,
  EnterpriseRequestAggregateResponse,
  EnterpriseRequestDetailEdit,
  EnterpriseRequestDetailInput,
  EnterpriseRequestDetailRemoval,
  EnterpriseRequestDetailResponse,
  EnterpriseRequestResponse,
  PagedResultOfEnterpriseRequestDetailResponse,
  PagedResultOfEnterpriseRequestResponse,
  ProblemDetails,
  UpdateEnterpriseRequestAggregateRequest,
  UpdateEnterpriseRequestDetailRequest,
  UpdateEnterpriseRequestRequest
} from './models.generated.js';
import {
  readEnterpriseRequestAggregateResponse,
  readEnterpriseRequestResponse,
  readPagedResultOfEnterpriseRequestResponse
} from './guards.generated.js';

export type GeneratedJsonOperation<T> = (
  http: HttpClient,
  parameters: Readonly<Record<string, unknown>>,
  signal?: AbortSignal,
  options?: RequestOptions
) => Promise<T>;

export interface EnterpriseRequestCreateEnterpriseRequestParameters {
  readonly "X-FullNet-Organization-Unit-Id": string;
  readonly body: CreateEnterpriseRequestRequest;
}

export async function enterpriseRequestCreateEnterpriseRequest(
  http: HttpClient,
  parameters: EnterpriseRequestCreateEnterpriseRequestParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<EnterpriseRequestResponse> {
  const path = `/api/v1/enterprise_request/enterprise-requests`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const headers = new Headers(init.headers);
  if (parameters["X-FullNet-Organization-Unit-Id"] !== undefined) headers.set("X-FullNet-Organization-Unit-Id", String(parameters["X-FullNet-Organization-Unit-Id"]));
  init.headers = headers;
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readEnterpriseRequestResponse(value);
}

export interface EnterpriseRequestCreateEnterpriseRequestAggregateParameters {
  readonly "X-FullNet-Organization-Unit-Id": string;
  readonly body: CreateEnterpriseRequestAggregateRequest;
}

export async function enterpriseRequestCreateEnterpriseRequestAggregate(
  http: HttpClient,
  parameters: EnterpriseRequestCreateEnterpriseRequestAggregateParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<EnterpriseRequestAggregateResponse> {
  const path = `/api/v1/enterprise_request/enterprise-requests/aggregate`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const headers = new Headers(init.headers);
  if (parameters["X-FullNet-Organization-Unit-Id"] !== undefined) headers.set("X-FullNet-Organization-Unit-Id", String(parameters["X-FullNet-Organization-Unit-Id"]));
  init.headers = headers;
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readEnterpriseRequestAggregateResponse(value);
}

export interface EnterpriseRequestDeleteEnterpriseRequestParameters {
  readonly enterpriseRequestId: string;
  readonly body: DeleteEnterpriseRequestRequest;
}

export async function enterpriseRequestDeleteEnterpriseRequest(
  http: HttpClient,
  parameters: EnterpriseRequestDeleteEnterpriseRequestParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<EnterpriseRequestResponse> {
  const path = `/api/v1/enterprise_request/enterprise-requests/${encodeURIComponent(String(parameters.enterpriseRequestId))}/delete`;
  const init: RequestInit = {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readEnterpriseRequestResponse(value);
}

export interface EnterpriseRequestGetEnterpriseRequestParameters {
  readonly enterpriseRequestId: string;
}

export async function enterpriseRequestGetEnterpriseRequest(
  http: HttpClient,
  parameters: EnterpriseRequestGetEnterpriseRequestParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<EnterpriseRequestResponse> {
  const path = `/api/v1/enterprise_request/enterprise-requests/${encodeURIComponent(String(parameters.enterpriseRequestId))}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readEnterpriseRequestResponse(value);
}

export interface EnterpriseRequestGetEnterpriseRequestAggregateParameters {
  readonly enterpriseRequestId: string;
}

export async function enterpriseRequestGetEnterpriseRequestAggregate(
  http: HttpClient,
  parameters: EnterpriseRequestGetEnterpriseRequestAggregateParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<EnterpriseRequestAggregateResponse> {
  const path = `/api/v1/enterprise_request/enterprise-requests/${encodeURIComponent(String(parameters.enterpriseRequestId))}/aggregate`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readEnterpriseRequestAggregateResponse(value);
}

export interface EnterpriseRequestListEnterpriseRequestsParameters {
  readonly page?: number;
  readonly pageSize?: number;
  readonly sortBy?: "id" | "requestNumber" | "title" | "status" | "totalAmount" | "applicantUserId";
  readonly descending?: boolean;
  readonly filterRequestNumber?: string;
  readonly filterTitle?: string;
  readonly filterStatus?: string;
  readonly filterTotalAmount?: string;
  readonly filterApplicantUserId?: string;
}

export async function enterpriseRequestListEnterpriseRequests(
  http: HttpClient,
  parameters: EnterpriseRequestListEnterpriseRequestsParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<PagedResultOfEnterpriseRequestResponse> {
  const query = new URLSearchParams();
  if (parameters.page !== undefined) {
    query.set('page', String(parameters.page));
  }
  if (parameters.pageSize !== undefined) {
    query.set('pageSize', String(parameters.pageSize));
  }
  if (parameters.sortBy !== undefined) {
    query.set('sortBy', String(parameters.sortBy));
  }
  if (parameters.descending !== undefined) {
    query.set('descending', String(parameters.descending));
  }
  if (parameters.filterRequestNumber !== undefined) {
    query.set('filterRequestNumber', String(parameters.filterRequestNumber));
  }
  if (parameters.filterTitle !== undefined) {
    query.set('filterTitle', String(parameters.filterTitle));
  }
  if (parameters.filterStatus !== undefined) {
    query.set('filterStatus', String(parameters.filterStatus));
  }
  if (parameters.filterTotalAmount !== undefined) {
    query.set('filterTotalAmount', String(parameters.filterTotalAmount));
  }
  if (parameters.filterApplicantUserId !== undefined) {
    query.set('filterApplicantUserId', String(parameters.filterApplicantUserId));
  }
  const path = query.size === 0 ? `/api/v1/enterprise_request/enterprise-requests` : `/api/v1/enterprise_request/enterprise-requests?${query.toString()}`;
  const init: RequestInit = { method: 'GET' };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readPagedResultOfEnterpriseRequestResponse(value);
}

export interface EnterpriseRequestUpdateEnterpriseRequestParameters {
  readonly enterpriseRequestId: string;
  readonly body: UpdateEnterpriseRequestRequest;
}

export async function enterpriseRequestUpdateEnterpriseRequest(
  http: HttpClient,
  parameters: EnterpriseRequestUpdateEnterpriseRequestParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<EnterpriseRequestResponse> {
  const path = `/api/v1/enterprise_request/enterprise-requests/${encodeURIComponent(String(parameters.enterpriseRequestId))}`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readEnterpriseRequestResponse(value);
}

export interface EnterpriseRequestUpdateEnterpriseRequestAggregateParameters {
  readonly enterpriseRequestId: string;
  readonly body: UpdateEnterpriseRequestAggregateRequest;
}

export async function enterpriseRequestUpdateEnterpriseRequestAggregate(
  http: HttpClient,
  parameters: EnterpriseRequestUpdateEnterpriseRequestAggregateParameters,
  signal?: AbortSignal,
  options?: RequestOptions
): Promise<EnterpriseRequestAggregateResponse> {
  const path = `/api/v1/enterprise_request/enterprise-requests/${encodeURIComponent(String(parameters.enterpriseRequestId))}/aggregate`;
  const init: RequestInit = {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(parameters.body)
  };
  const value = options === undefined
    ? await http.request<unknown>(path, init, signal)
    : await http.request<unknown>(path, init, signal, options);
  return readEnterpriseRequestAggregateResponse(value);
}
