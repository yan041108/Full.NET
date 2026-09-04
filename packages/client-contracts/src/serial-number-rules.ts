export type SerialNumberRuleScope = 0 | 1;
export type SerialNumberResetInterval = 0 | 1 | 2 | 3;

export interface PreviewSerialNumberRequest {
  scope: SerialNumberRuleScope;
  pattern: string;
  tenantIdentifier: string | null;
  sequenceValue: number;
  atUtc: string;
  resetInterval?: SerialNumberResetInterval;
}

export interface SerialNumberPreviewResponse {
  value: string;
  resetBucket: string;
  sequenceValue: number;
}

export interface CreateSerialNumberRuleRequest {
  ruleKey: string;
  displayName: string;
  description: string | null;
  scope: SerialNumberRuleScope;
  resetInterval: SerialNumberResetInterval;
  pattern: string;
  minimumValue: number;
  maximumValue: number;
  displayOrder: number;
  isEnabled: boolean;
}

export interface UpdateSerialNumberRuleRequest {
  displayName: string;
  description: string | null;
  scope: SerialNumberRuleScope;
  resetInterval: SerialNumberResetInterval;
  pattern: string;
  minimumValue: number;
  maximumValue: number;
  displayOrder: number;
  isEnabled: boolean;
  version: number;
}

export interface ChangeSerialNumberRuleStatusRequest {
  version: number;
}

export interface SerialNumberRuleResponse {
  id: string;
  ruleKey: string;
  displayName: string;
  description: string | null;
  scope: SerialNumberRuleScope;
  resetInterval: SerialNumberResetInterval;
  pattern: string;
  minimumValue: number;
  maximumValue: number;
  displayOrder: number;
  isEnabled: boolean;
  createdAtUtc: string;
  createdByUserId: string;
  updatedAtUtc: string | null;
  updatedByUserId: string | null;
  version: number;
}

export interface SerialNumberRulePage {
  items: SerialNumberRuleResponse[];
  page: number;
  pageSize: number;
  total: number;
}

const uuidPattern =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isUuid(value: unknown): value is string {
  return typeof value === 'string' && uuidPattern.test(value);
}

function isDateTime(value: unknown): value is string {
  return typeof value === 'string'
    && value.length > 0
    && Number.isFinite(Date.parse(value));
}

function isScope(value: unknown): value is SerialNumberRuleScope {
  return value === 0 || value === 1;
}

function isResetInterval(value: unknown): value is SerialNumberResetInterval {
  return value === 0 || value === 1 || value === 2 || value === 3;
}

function isBoundedText(
  value: unknown,
  minimum: number,
  maximum: number
): value is string {
  return typeof value === 'string'
    && value.trim().length >= minimum
    && value.length <= maximum;
}

export function isSerialNumberRuleResponse(
  value: unknown
): value is SerialNumberRuleResponse {
  if (!isRecord(value)) {
    return false;
  }

  const hasUpdateAudit = value.updatedAtUtc !== null
    && value.updatedByUserId !== null;
  const hasNoUpdateAudit = value.updatedAtUtc === null
    && value.updatedByUserId === null;
  return isUuid(value.id)
    && isBoundedText(value.ruleKey, 1, 128)
    && isBoundedText(value.displayName, 1, 128)
    && (value.description === null
      || isBoundedText(value.description, 1, 512))
    && isScope(value.scope)
    && isResetInterval(value.resetInterval)
    && isBoundedText(value.pattern, 1, 256)
    && Number.isSafeInteger(value.minimumValue)
    && Number.isSafeInteger(value.maximumValue)
    && Number.isSafeInteger(value.displayOrder)
    && typeof value.isEnabled === 'boolean'
    && isDateTime(value.createdAtUtc)
    && isUuid(value.createdByUserId)
    && (hasNoUpdateAudit || (
      hasUpdateAudit
      && isDateTime(value.updatedAtUtc)
      && isUuid(value.updatedByUserId)
    ))
    && Number.isSafeInteger(value.version)
    && (value.version as number) >= 1;
}

export function isSerialNumberRulePage(
  value: unknown
): value is SerialNumberRulePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isSerialNumberRuleResponse)
    && Number.isSafeInteger(value.page)
    && (value.page as number) >= 1
    && Number.isSafeInteger(value.pageSize)
    && (value.pageSize as number) >= 1
    && (value.pageSize as number) <= 100
    && Number.isSafeInteger(value.total)
    && (value.total as number) >= 0;
}

export function isSerialNumberPreviewResponse(
  value: unknown
): value is SerialNumberPreviewResponse {
  return isRecord(value)
    && typeof value.value === 'string'
    && value.value.length > 0
    && typeof value.resetBucket === 'string'
    && value.resetBucket.length > 0
    && Number.isSafeInteger(value.sequenceValue);
}

export function isPreviewSerialNumberRequest(
  value: unknown
): value is PreviewSerialNumberRequest {
  return isRecord(value)
    && isScope(value.scope)
    && isBoundedText(value.pattern, 1, 256)
    && (value.tenantIdentifier === null
      || isBoundedText(value.tenantIdentifier, 1, 64))
    && Number.isSafeInteger(value.sequenceValue)
    && isDateTime(value.atUtc)
    && (value.resetInterval === undefined || isResetInterval(value.resetInterval));
}