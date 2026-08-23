import {
  isSerialNumberPreviewResponse,
  isSerialNumberRulePage,
  isSerialNumberRuleResponse,
  serialNumbersCreateRule,
  serialNumbersDisableRule,
  serialNumbersEnableRule,
  serialNumbersListRules,
  serialNumbersPreviewSerialNumber,
  serialNumbersUpdateRule,
  type ChangeSerialNumberRuleStatusRequest,
  type CreateSerialNumberRuleRequest,
  type PreviewSerialNumberRequest,
  type SerialNumberPreviewResponse,
  type SerialNumberRulePage,
  type SerialNumberRuleResponse,
  type UpdateSerialNumberRuleRequest
} from '@fullnet/client-contracts';
import { http } from './http';

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

export async function listSerialNumberRules(
  params: ListSerialNumberRulesParams | number = {},
  pageSize = 20,
  signal?: AbortSignal
): Promise<SerialNumberRulePage> {
  const normalized: ListSerialNumberRulesParams =
    typeof params === 'number'
      ? { page: params, pageSize }
      : params;
  const name = normalized.name?.trim();
  const key = normalized.key?.trim();
  const value = await serialNumbersListRules(
    http,
    {
      page: normalized.page ?? 1,
      pageSize: normalized.pageSize ?? 20,
      ...(name ? { name } : {}),
      ...(key ? { key } : {}),
      ...(normalized.isEnabled !== undefined
        ? { isEnabled: normalized.isEnabled }
        : {}),
      ...(normalized.sortBy ? { sortBy: normalized.sortBy } : {}),
      ...(normalized.sortDirection
        ? { sortDirection: normalized.sortDirection }
        : {})
    },
    signal
  );
  if (!isSerialNumberRulePage(value)) {
    throw new Error('client.invalid_serial_number_rule_page');
  }

  return value;
}

export async function createSerialNumberRule(
  input: CreateSerialNumberRuleRequest,
  signal?: AbortSignal
): Promise<SerialNumberRuleResponse> {
  const value = await serialNumbersCreateRule(http, { body: input }, signal);
  return readRule(value);
}

export async function updateSerialNumberRule(
  ruleId: string,
  input: UpdateSerialNumberRuleRequest,
  signal?: AbortSignal
): Promise<SerialNumberRuleResponse> {
  const value = await serialNumbersUpdateRule(
    http,
    { ruleId, body: input },
    signal
  );
  return readRule(value);
}

export async function enableSerialNumberRule(
  ruleId: string,
  input: ChangeSerialNumberRuleStatusRequest,
  signal?: AbortSignal
): Promise<SerialNumberRuleResponse> {
  const value = await serialNumbersEnableRule(
    http,
    { ruleId, body: input },
    signal
  );
  return readRule(value);
}

export async function disableSerialNumberRule(
  ruleId: string,
  input: ChangeSerialNumberRuleStatusRequest,
  signal?: AbortSignal
): Promise<SerialNumberRuleResponse> {
  const value = await serialNumbersDisableRule(
    http,
    { ruleId, body: input },
    signal
  );
  return readRule(value);
}

export async function previewSerialNumber(
  input: PreviewSerialNumberRequest,
  signal?: AbortSignal
): Promise<SerialNumberPreviewResponse> {
  const value = await serialNumbersPreviewSerialNumber(
    http,
    { body: input },
    signal
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

export type {
  ChangeSerialNumberRuleStatusRequest,
  CreateSerialNumberRuleRequest,
  PreviewSerialNumberRequest,
  SerialNumberPreviewResponse,
  SerialNumberRulePage,
  SerialNumberRuleResponse,
  UpdateSerialNumberRuleRequest
};
