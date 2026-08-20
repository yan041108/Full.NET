import {
  isSerialNumberPreviewResponse,
  isSerialNumberRulePage,
  isSerialNumberRuleResponse,
  type ChangeSerialNumberRuleStatusRequest,
  type CreateSerialNumberRuleRequest,
  type PreviewSerialNumberRequest,
  type SerialNumberPreviewResponse,
  type SerialNumberRulePage,
  type SerialNumberRuleResponse,
  type UpdateSerialNumberRuleRequest
} from '@fullnet/client-contracts';
import { request } from './http';

const rulesPath = '/api/v1/serial-numbers/rules';

export type SerialNumberRuleSortBy =
  | 'displayOrder'
  | 'ruleKey'
  | 'displayName'
  | 'createdAtUtc'
  | 'isEnabled';

export type SerialNumberRuleSortDirection = 'asc' | 'desc';

export interface ListSerialNumberRulesParams {
  page?: number;
  pageSize?: number;
  name?: string;
  key?: string;
  isEnabled?: boolean;
  sortBy?: SerialNumberRuleSortBy;
  sortDirection?: SerialNumberRuleSortDirection;
}

function buildListQuery(params: ListSerialNumberRulesParams): string {
  const query = new URLSearchParams();
  query.set('page', String(params.page ?? 1));
  query.set('pageSize', String(params.pageSize ?? 20));
  if (params.name?.trim()) {
    query.set('name', params.name.trim());
  }
  if (params.key?.trim()) {
    query.set('key', params.key.trim());
  }
  if (params.isEnabled !== undefined) {
    query.set('isEnabled', String(params.isEnabled));
  }
  if (params.sortBy) {
    query.set('sortBy', params.sortBy);
  }
  if (params.sortDirection) {
    query.set('sortDirection', params.sortDirection);
  }
  return query.toString();
}

export async function listSerialNumberRules(
  params: ListSerialNumberRulesParams | number = {},
  pageSize = 20
): Promise<SerialNumberRulePage> {
  // 兼容旧调用 listSerialNumberRules(page, pageSize)，新调用传对象参数。
  const normalized: ListSerialNumberRulesParams =
    typeof params === 'number'
      ? { page: params, pageSize }
      : params;
  const value = await request<unknown>(
    `${rulesPath}?${buildListQuery(normalized)}`
  );
  if (!isSerialNumberRulePage(value)) {
    throw new Error('client.invalid_serial_number_rule_page');
  }

  return value;
}

export async function createSerialNumberRule(
  input: CreateSerialNumberRuleRequest
): Promise<SerialNumberRuleResponse> {
  return readRule(await request<unknown>(rulesPath, jsonRequest('POST', input)));
}

export async function updateSerialNumberRule(
  ruleId: string,
  input: UpdateSerialNumberRuleRequest
): Promise<SerialNumberRuleResponse> {
  return readRule(await request<unknown>(
    `${rulesPath}/${encodeURIComponent(ruleId)}`,
    jsonRequest('PUT', input)
  ));
}

export async function enableSerialNumberRule(
  ruleId: string,
  input: ChangeSerialNumberRuleStatusRequest
): Promise<SerialNumberRuleResponse> {
  return readRule(await request<unknown>(
    `${rulesPath}/${encodeURIComponent(ruleId)}/enable`,
    jsonRequest('POST', input)
  ));
}

export async function disableSerialNumberRule(
  ruleId: string,
  input: ChangeSerialNumberRuleStatusRequest
): Promise<SerialNumberRuleResponse> {
  return readRule(await request<unknown>(
    `${rulesPath}/${encodeURIComponent(ruleId)}/disable`,
    jsonRequest('POST', input)
  ));
}

export async function previewSerialNumber(
  input: PreviewSerialNumberRequest
): Promise<SerialNumberPreviewResponse> {
  const value = await request<unknown>(
    `${rulesPath}/preview`,
    jsonRequest('POST', input)
  );
  if (!isSerialNumberPreviewResponse(value)) {
    throw new Error('client.invalid_serial_number_preview');
  }

  return value;
}

function readRule(value: unknown): SerialNumberRuleResponse {
  if (!isSerialNumberRuleResponse(value)) {
    throw new Error('client.invalid_serial_number_rule');
  }

  return value;
}

function jsonRequest(method: string, body: unknown): RequestInit {
  return {
    method,
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  };
}
