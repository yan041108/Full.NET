// 此文件由 OpenAPI 快照确定性生成，禁止手工修改。
// 内容：OpenAPI 运行时响应守卫。

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

export function readCreateEnterpriseRequestAggregateRequest(value: unknown): CreateEnterpriseRequestAggregateRequest {
  return requireValidResponse(value, isCreateEnterpriseRequestAggregateRequest(value), 'create_enterprise_request_aggregate_request') as CreateEnterpriseRequestAggregateRequest;
}

function isCreateEnterpriseRequestAggregateRequest(value: unknown): value is CreateEnterpriseRequestAggregateRequest {
  return isRecord(value) && (Array.isArray(value["details"]) && value["details"].every(item16 => isEnterpriseRequestDetailInput(item16))) && (isCreateEnterpriseRequestRequest(value["master"]));
}

export function readCreateEnterpriseRequestDetailRequest(value: unknown): CreateEnterpriseRequestDetailRequest {
  return requireValidResponse(value, isCreateEnterpriseRequestDetailRequest(value), 'create_enterprise_request_detail_request') as CreateEnterpriseRequestDetailRequest;
}

function isCreateEnterpriseRequestDetailRequest(value: unknown): value is CreateEnterpriseRequestDetailRequest {
  return isRecord(value) && (typeof value["itemDescription"] === 'string') && (typeof value["lineAmount"] === 'string') && (typeof value["lineNumber"] === 'number' && Number.isSafeInteger(value["lineNumber"])) && (typeof value["quantity"] === 'string') && (typeof value["requestId"] === 'string' && guidPattern.test(value["requestId"])) && (typeof value["unitPrice"] === 'string');
}

export function readCreateEnterpriseRequestRequest(value: unknown): CreateEnterpriseRequestRequest {
  return requireValidResponse(value, isCreateEnterpriseRequestRequest(value), 'create_enterprise_request_request') as CreateEnterpriseRequestRequest;
}

function isCreateEnterpriseRequestRequest(value: unknown): value is CreateEnterpriseRequestRequest {
  return isRecord(value) && (typeof value["applicantUserId"] === 'string' && guidPattern.test(value["applicantUserId"])) && (typeof value["requestNumber"] === 'string') && (typeof value["status"] === 'string') && (typeof value["title"] === 'string') && (typeof value["totalAmount"] === 'string');
}

export function readDeleteEnterpriseRequestDetailRequest(value: unknown): DeleteEnterpriseRequestDetailRequest {
  return requireValidResponse(value, isDeleteEnterpriseRequestDetailRequest(value), 'delete_enterprise_request_detail_request') as DeleteEnterpriseRequestDetailRequest;
}

function isDeleteEnterpriseRequestDetailRequest(value: unknown): value is DeleteEnterpriseRequestDetailRequest {
  return isRecord(value) && (typeof value["version"] === 'string');
}

export function readDeleteEnterpriseRequestRequest(value: unknown): DeleteEnterpriseRequestRequest {
  return requireValidResponse(value, isDeleteEnterpriseRequestRequest(value), 'delete_enterprise_request_request') as DeleteEnterpriseRequestRequest;
}

function isDeleteEnterpriseRequestRequest(value: unknown): value is DeleteEnterpriseRequestRequest {
  return isRecord(value) && (typeof value["version"] === 'string');
}

export function readEnterpriseRequestAggregateResponse(value: unknown): EnterpriseRequestAggregateResponse {
  return requireValidResponse(value, isEnterpriseRequestAggregateResponse(value), 'enterprise_request_aggregate_response') as EnterpriseRequestAggregateResponse;
}

function isEnterpriseRequestAggregateResponse(value: unknown): value is EnterpriseRequestAggregateResponse {
  return isRecord(value) && (Array.isArray(value["details"]) && value["details"].every(item16 => isEnterpriseRequestDetailResponse(item16))) && (isEnterpriseRequestResponse(value["master"]));
}

export function readEnterpriseRequestDetailEdit(value: unknown): EnterpriseRequestDetailEdit {
  return requireValidResponse(value, isEnterpriseRequestDetailEdit(value), 'enterprise_request_detail_edit') as EnterpriseRequestDetailEdit;
}

function isEnterpriseRequestDetailEdit(value: unknown): value is EnterpriseRequestDetailEdit {
  return isRecord(value) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (isEnterpriseRequestDetailInput(value["input"])) && (typeof value["version"] === 'string');
}

export function readEnterpriseRequestDetailInput(value: unknown): EnterpriseRequestDetailInput {
  return requireValidResponse(value, isEnterpriseRequestDetailInput(value), 'enterprise_request_detail_input') as EnterpriseRequestDetailInput;
}

function isEnterpriseRequestDetailInput(value: unknown): value is EnterpriseRequestDetailInput {
  return isRecord(value) && (typeof value["itemDescription"] === 'string') && (typeof value["lineAmount"] === 'string') && (typeof value["lineNumber"] === 'number' && Number.isSafeInteger(value["lineNumber"])) && (typeof value["quantity"] === 'string') && (typeof value["unitPrice"] === 'string');
}

export function readEnterpriseRequestDetailRemoval(value: unknown): EnterpriseRequestDetailRemoval {
  return requireValidResponse(value, isEnterpriseRequestDetailRemoval(value), 'enterprise_request_detail_removal') as EnterpriseRequestDetailRemoval;
}

function isEnterpriseRequestDetailRemoval(value: unknown): value is EnterpriseRequestDetailRemoval {
  return isRecord(value) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["version"] === 'string');
}

export function readEnterpriseRequestDetailResponse(value: unknown): EnterpriseRequestDetailResponse {
  return requireValidResponse(value, isEnterpriseRequestDetailResponse(value), 'enterprise_request_detail_response') as EnterpriseRequestDetailResponse;
}

function isEnterpriseRequestDetailResponse(value: unknown): value is EnterpriseRequestDetailResponse {
  return isRecord(value) && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdById"] === 'string' && guidPattern.test(value["createdById"])) && ((value["deletedAtUtc"] === null) || (typeof value["deletedAtUtc"] === 'string')) && ((value["deletedById"] === null) || (typeof value["deletedById"] === 'string' && guidPattern.test(value["deletedById"]))) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isDeleted"] === 'boolean') && (typeof value["itemDescription"] === 'string') && (typeof value["lineAmount"] === 'string') && (typeof value["lineNumber"] === 'number' && Number.isSafeInteger(value["lineNumber"])) && (typeof value["organizationUnitId"] === 'string' && guidPattern.test(value["organizationUnitId"])) && (typeof value["quantity"] === 'string') && (typeof value["requestId"] === 'string' && guidPattern.test(value["requestId"])) && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (typeof value["unitPrice"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && ((value["updatedById"] === null) || (typeof value["updatedById"] === 'string' && guidPattern.test(value["updatedById"]))) && (typeof value["version"] === 'string');
}

export function readEnterpriseRequestResponse(value: unknown): EnterpriseRequestResponse {
  return requireValidResponse(value, isEnterpriseRequestResponse(value), 'enterprise_request_response') as EnterpriseRequestResponse;
}

function isEnterpriseRequestResponse(value: unknown): value is EnterpriseRequestResponse {
  return isRecord(value) && (typeof value["applicantUserId"] === 'string' && guidPattern.test(value["applicantUserId"])) && (typeof value["createdAtUtc"] === 'string') && (typeof value["createdById"] === 'string' && guidPattern.test(value["createdById"])) && ((value["deletedAtUtc"] === null) || (typeof value["deletedAtUtc"] === 'string')) && ((value["deletedById"] === null) || (typeof value["deletedById"] === 'string' && guidPattern.test(value["deletedById"]))) && (typeof value["id"] === 'string' && guidPattern.test(value["id"])) && (typeof value["isDeleted"] === 'boolean') && (typeof value["organizationUnitId"] === 'string' && guidPattern.test(value["organizationUnitId"])) && (typeof value["requestNumber"] === 'string') && (typeof value["status"] === 'string') && (typeof value["tenantId"] === 'string' && guidPattern.test(value["tenantId"])) && (typeof value["title"] === 'string') && (typeof value["totalAmount"] === 'string') && ((value["updatedAtUtc"] === null) || (typeof value["updatedAtUtc"] === 'string')) && ((value["updatedById"] === null) || (typeof value["updatedById"] === 'string' && guidPattern.test(value["updatedById"]))) && (typeof value["version"] === 'string');
}

export function readPagedResultOfEnterpriseRequestDetailResponse(value: unknown): PagedResultOfEnterpriseRequestDetailResponse {
  return requireValidResponse(value, isPagedResultOfEnterpriseRequestDetailResponse(value), 'paged_result_of_enterprise_request_detail_response') as PagedResultOfEnterpriseRequestDetailResponse;
}

function isPagedResultOfEnterpriseRequestDetailResponse(value: unknown): value is PagedResultOfEnterpriseRequestDetailResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isEnterpriseRequestDetailResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readPagedResultOfEnterpriseRequestResponse(value: unknown): PagedResultOfEnterpriseRequestResponse {
  return requireValidResponse(value, isPagedResultOfEnterpriseRequestResponse(value), 'paged_result_of_enterprise_request_response') as PagedResultOfEnterpriseRequestResponse;
}

function isPagedResultOfEnterpriseRequestResponse(value: unknown): value is PagedResultOfEnterpriseRequestResponse {
  return isRecord(value) && (Array.isArray(value["items"]) && value["items"].every(item14 => isEnterpriseRequestResponse(item14))) && (typeof value["page"] === 'number' && Number.isSafeInteger(value["page"])) && (typeof value["pageSize"] === 'number' && Number.isSafeInteger(value["pageSize"])) && (typeof value["total"] === 'number' && Number.isSafeInteger(value["total"]));
}

export function readProblemDetails(value: unknown): ProblemDetails {
  return requireValidResponse(value, isProblemDetails(value), 'problem_details') as ProblemDetails;
}

function isProblemDetails(value: unknown): value is ProblemDetails {
  return isRecord(value) && (value["detail"] === undefined || ((value["detail"] === null) || (typeof value["detail"] === 'string'))) && (value["instance"] === undefined || ((value["instance"] === null) || (typeof value["instance"] === 'string'))) && (value["status"] === undefined || ((value["status"] === null) || (typeof value["status"] === 'number' && Number.isSafeInteger(value["status"])))) && (value["title"] === undefined || ((value["title"] === null) || (typeof value["title"] === 'string'))) && (value["type"] === undefined || ((value["type"] === null) || (typeof value["type"] === 'string')));
}

export function readUpdateEnterpriseRequestAggregateRequest(value: unknown): UpdateEnterpriseRequestAggregateRequest {
  return requireValidResponse(value, isUpdateEnterpriseRequestAggregateRequest(value), 'update_enterprise_request_aggregate_request') as UpdateEnterpriseRequestAggregateRequest;
}

function isUpdateEnterpriseRequestAggregateRequest(value: unknown): value is UpdateEnterpriseRequestAggregateRequest {
  return isRecord(value) && (Array.isArray(value["added"]) && value["added"].every(item14 => isEnterpriseRequestDetailInput(item14))) && (isUpdateEnterpriseRequestRequest(value["master"])) && (Array.isArray(value["removed"]) && value["removed"].every(item16 => isEnterpriseRequestDetailRemoval(item16))) && (Array.isArray(value["updated"]) && value["updated"].every(item16 => isEnterpriseRequestDetailEdit(item16)));
}

export function readUpdateEnterpriseRequestDetailRequest(value: unknown): UpdateEnterpriseRequestDetailRequest {
  return requireValidResponse(value, isUpdateEnterpriseRequestDetailRequest(value), 'update_enterprise_request_detail_request') as UpdateEnterpriseRequestDetailRequest;
}

function isUpdateEnterpriseRequestDetailRequest(value: unknown): value is UpdateEnterpriseRequestDetailRequest {
  return isRecord(value) && (typeof value["itemDescription"] === 'string') && (typeof value["lineAmount"] === 'string') && (typeof value["lineNumber"] === 'number' && Number.isSafeInteger(value["lineNumber"])) && (typeof value["quantity"] === 'string') && (typeof value["requestId"] === 'string' && guidPattern.test(value["requestId"])) && (typeof value["unitPrice"] === 'string') && (typeof value["version"] === 'string');
}

export function readUpdateEnterpriseRequestRequest(value: unknown): UpdateEnterpriseRequestRequest {
  return requireValidResponse(value, isUpdateEnterpriseRequestRequest(value), 'update_enterprise_request_request') as UpdateEnterpriseRequestRequest;
}

function isUpdateEnterpriseRequestRequest(value: unknown): value is UpdateEnterpriseRequestRequest {
  return isRecord(value) && (typeof value["applicantUserId"] === 'string' && guidPattern.test(value["applicantUserId"])) && (typeof value["requestNumber"] === 'string') && (typeof value["status"] === 'string') && (typeof value["title"] === 'string') && (typeof value["totalAmount"] === 'string') && (typeof value["version"] === 'string');
}

// 统一失败关闭与错误码，避免每个生成读取器重复携带异常和返回分支。
function requireValidResponse(value: unknown, valid: boolean, responseKind: string): unknown {
  if (!valid) throw new Error('client.invalid_' + responseKind);
  return value;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// 服务端 Int64 可按 JSON 字符串输出；客户端仅在无精度损失时归一为既有 number 契约。
function normalizeWireInteger(value: unknown): unknown {
  if (typeof value !== 'string' || !/^-?(?:0|[1-9]\d*)$/.test(value)) return value;
  const number = Number(value);
  return Number.isSafeInteger(number) ? number : value;
}

function normalizeIntegerIntersection(value: unknown, normalizers: ReadonlyArray<(value: unknown) => unknown>): unknown {
  for (const normalize of normalizers) value = normalize(value);
  return value;
}

function normalizeIntegerUnion(value: unknown, branches: ReadonlyArray<{ matches: (value: unknown) => boolean; normalize: (value: unknown) => unknown }>): unknown {
  if (branches.some(branch => branch.matches(value))) return value;
  for (const branch of branches) {
    const candidate = branch.normalize(value);
    if (branch.matches(candidate)) return candidate;
  }
  return value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isJsonValue(value: unknown): boolean {
  if (value === null || typeof value === 'string' || typeof value === 'boolean') {
    return true;
  }
  if (typeof value === 'number') {
    return Number.isFinite(value);
  }
  if (Array.isArray(value)) {
    return value.every(isJsonValue);
  }
  return isRecord(value) && Object.values(value).every(isJsonValue);
}
