export interface HostDocumentTagResponse {
  id: string;
  name: string;
  code: string | null;
  icon: string | null;
  color: string | null;
  description: string | null;
  useCount: number;
  isHot: boolean;
  isRecommended: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface CreateHostDocumentTagRequest {
  name: string;
  code?: string | null;
  icon?: string | null;
  color?: string | null;
  description?: string | null;
  isHot?: boolean;
  isRecommended?: boolean;
}

export interface UpdateHostDocumentTagRequest {
  name: string;
  code?: string | null;
  icon?: string | null;
  color?: string | null;
  description?: string | null;
  version: number;
  isHot?: boolean;
  isRecommended?: boolean;
}

export interface DeleteHostDocumentTagRequest {
  version: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string';
}

function isOptionalBoolean(value: unknown): boolean {
  return value === undefined || typeof value === 'boolean';
}

function readBooleanFlag(value: unknown, fallback = false): boolean {
  return typeof value === 'boolean' ? value : fallback;
}

export function isHostDocumentTagResponse(value: unknown): value is HostDocumentTagResponse {
  return isRecord(value)
    && isGuid(value.id)
    && isNonEmptyString(value.name)
    && isNullableString(value.code)
    && isNullableString(value.icon)
    && isNullableString(value.color)
    && isNullableString(value.description)
    && Number.isInteger(value.useCount)
    && isOptionalBoolean(value.isHot)
    && isOptionalBoolean(value.isRecommended)
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isHostDocumentTagResponseList(value: unknown): value is HostDocumentTagResponse[] {
  return Array.isArray(value) && value.every(isHostDocumentTagResponse);
}

export function isCreateHostDocumentTagRequest(value: unknown): value is CreateHostDocumentTagRequest {
  return isRecord(value)
    && isNonEmptyString(value.name)
    && (value.code === undefined || isNullableString(value.code))
    && (value.icon === undefined || isNullableString(value.icon))
    && (value.color === undefined || isNullableString(value.color))
    && (value.description === undefined || isNullableString(value.description))
    && isOptionalBoolean(value.isHot)
    && isOptionalBoolean(value.isRecommended);
}

export function isUpdateHostDocumentTagRequest(value: unknown): value is UpdateHostDocumentTagRequest {
  return isRecord(value)
    && isNonEmptyString(value.name)
    && (value.code === undefined || isNullableString(value.code))
    && (value.icon === undefined || isNullableString(value.icon))
    && (value.color === undefined || isNullableString(value.color))
    && (value.description === undefined || isNullableString(value.description))
    && Number.isInteger(value.version)
    && isOptionalBoolean(value.isHot)
    && isOptionalBoolean(value.isRecommended);
}

/** 将 API 标签响应规范化为带默认布尔运营标记的结构。 */
export function normalizeHostDocumentTagResponse(
  value: HostDocumentTagResponse
): HostDocumentTagResponse {
  return {
    ...value,
    isHot: readBooleanFlag(value.isHot),
    isRecommended: readBooleanFlag(value.isRecommended)
  };
}

export function isDeleteHostDocumentTagRequest(value: unknown): value is DeleteHostDocumentTagRequest {
  return isRecord(value)
    && Number.isInteger(value.version);
}
