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

export async function listSerialNumberRules(
  page = 1,
  pageSize = 20
): Promise<SerialNumberRulePage> {
  const value = await request<unknown>(
    `${rulesPath}?page=${page}&pageSize=${pageSize}`
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