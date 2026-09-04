import {
  isSerialNumberPreviewResponse,
  isSerialNumberRulePage,
  isSerialNumberRuleResponse,
  serialNumbersCreateRule,
  serialNumbersDisableRule,
  serialNumbersEnableRule,
  serialNumbersPreviewSerialNumber,
  serialNumbersUpdateRule,
  type ChangeSerialNumberRuleStatusRequest,
  type CreateSerialNumberRuleRequest,
  type PreviewSerialNumberRequest,
  type SerialNumberPreviewResponse,
  type SerialNumberResetInterval,
  type SerialNumberRulePage,
  type SerialNumberRuleResponse,
  type SerialNumberRuleScope,
  type UpdateSerialNumberRuleRequest
} from '@fullnet/client-contracts';
import { http, request } from './http';

/** 流水号规则列表支持的排序字段，保持与服务端查询契约一致。 */
export type SerialNumberRuleSortBy =
  | 'displayOrder'
  | 'ruleKey'
  | 'displayName'
  | 'createdAtUtc'
  | 'isEnabled';

export type SerialNumberRuleSortDirection = 'asc' | 'desc';

/** 流水号规则列表查询参数；字符串筛选项会在发送前做 trim 规范化。 */
export interface ListSerialNumberRulesParams {
  page?: number;
  pageSize?: number;
  name?: string;
  key?: string;
  isEnabled?: boolean;
  scope?: SerialNumberRuleScope;
  resetInterval?: SerialNumberResetInterval;
  sortBy?: SerialNumberRuleSortBy;
  sortDirection?: SerialNumberRuleSortDirection;
}

function buildListQuery(params: ListSerialNumberRulesParams): string {
  const query = new URLSearchParams();
  query.set('page', String(params.page ?? 1));
  query.set('pageSize', String(params.pageSize ?? 20));
  const name = params.name?.trim();
  const key = params.key?.trim();
  if (name) {
    query.set('name', name);
  }
  if (key) {
    query.set('key', key);
  }
  if (params.isEnabled !== undefined) {
    query.set('isEnabled', String(params.isEnabled));
  }
  if (params.scope !== undefined) {
    query.set('scope', String(params.scope));
  }
  if (params.resetInterval !== undefined) {
    query.set('resetInterval', String(params.resetInterval));
  }
  if (params.sortBy) {
    query.set('sortBy', params.sortBy);
  }
  if (params.sortDirection) {
    query.set('sortDirection', params.sortDirection);
  }
  return query.toString();
}

/** 查询流水号规则列表，并对响应页做运行时校验，避免脏载荷进入页面。 */
export async function listSerialNumberRules(
  params: ListSerialNumberRulesParams | number = {},
  pageSize = 20,
  signal?: AbortSignal
): Promise<SerialNumberRulePage> {
  const normalized: ListSerialNumberRulesParams =
    typeof params === 'number'
      ? { page: params, pageSize }
      : params;
  const value = await request<unknown>(
    `/api/v1/serial-numbers/rules?${buildListQuery(normalized)}`,
    { method: 'GET' },
    signal
  );
  if (!isSerialNumberRulePage(value)) {
    throw new Error('client.invalid_serial_number_rule_page');
  }

  return value;
}

/** 创建流水号规则。 */
export async function createSerialNumberRule(
  input: CreateSerialNumberRuleRequest,
  signal?: AbortSignal
): Promise<SerialNumberRuleResponse> {
  const value = await serialNumbersCreateRule(http, { body: input }, signal);
  return readRule(value);
}

/** 更新流水号规则。 */
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

/** 启用流水号规则。 */
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

/** 停用流水号规则。 */
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

/** 预览下一条流水号样式，并对结果做运行时校验。 */
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

/** 校验单条规则响应结构，避免调用方重复编写相同的失败关闭逻辑。 */
function readRule(value: unknown): SerialNumberRuleResponse {
  if (!isSerialNumberRuleResponse(value)) {
    throw new Error('client.invalid_serial_number_rule');
  }

  return value;
}

/** 导出流水号规则查询、写入与预览模型，供规则页列表、编辑器与预览对话框共享同一契约。 */
export type {
  ChangeSerialNumberRuleStatusRequest,
  CreateSerialNumberRuleRequest,
  PreviewSerialNumberRequest,
  SerialNumberPreviewResponse,
  SerialNumberRulePage,
  SerialNumberRuleResponse,
  UpdateSerialNumberRuleRequest
};

