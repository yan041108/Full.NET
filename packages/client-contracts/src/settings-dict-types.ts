export interface SettingsDictType {
  id: string;
  code: string;
  name: string;
  description: string | null;
  displayOrder: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface SettingsDictTypePage {
  items: SettingsDictType[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateSettingsDictTypeRequest {
  code: string;
  name: string;
  description?: string | null;
  displayOrder: number;
}

export interface UpdateSettingsDictTypeRequest {
  name: string;
  description?: string | null;
  displayOrder: number;
  version: number;
}

export interface SettingsDictItem {
  id: string;
  dictTypeId: string;
  label: string;
  value: string;
  color: string | null;
  displayOrder: number;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  version: number;
}

export interface SettingsDictItemPage {
  items: SettingsDictItem[];
  page: number;
  pageSize: number;
  total: number;
}

export interface CreateSettingsDictItemRequest {
  label: string;
  value: string;
  color?: string | null;
  displayOrder: number;
}

export interface UpdateSettingsDictItemRequest {
  label: string;
  color?: string | null;
  displayOrder: number;
  version: number;
}

// 硬删除字典类型/字典项请求，携带乐观锁版本用于并发控制，对应 Admin.NET DeleteDict。
export interface DeleteSettingsDictTypeRequest {
  version: number;
}

export interface DeleteSettingsDictItemRequest {
  version: number;
}

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// 与服务端 HostDictTypeManagementService 的编码规则保持一致：3-64 位小写字母开头结尾。
const codePattern = /^[a-z][a-z0-9_-]{1,62}[a-z0-9]$/;

// 与服务端 HostDictItemManagementService 的机器值规则一致：2-128 位小写。
const valuePattern = /^[a-z][a-z0-9_-]{0,126}[a-z0-9]$/;

export function isSettingsDictType(value: unknown): value is SettingsDictType {
  return isRecord(value)
    && isGuid(value.id)
    && typeof value.code === 'string'
    && codePattern.test(value.code)
    && isNonEmptyString(value.name)
    && (value.description === null || typeof value.description === 'string')
    && Number.isInteger(value.displayOrder)
    && typeof value.isActive === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isSettingsDictTypePage(
  value: unknown
): value is SettingsDictTypePage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isSettingsDictType)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

export function isCreateSettingsDictTypeRequest(
  value: unknown
): value is CreateSettingsDictTypeRequest {
  return isRecord(value)
    && typeof value.code === 'string'
    && codePattern.test(value.code)
    && isNonEmptyString(value.name)
    && (value.description === undefined
      || value.description === null
      || typeof value.description === 'string')
    && Number.isInteger(value.displayOrder);
}

export function isUpdateSettingsDictTypeRequest(
  value: unknown
): value is UpdateSettingsDictTypeRequest {
  return isRecord(value)
    && isNonEmptyString(value.name)
    && (value.description === undefined
      || value.description === null
      || typeof value.description === 'string')
    && Number.isInteger(value.displayOrder)
    && typeof value.version === 'number'
    && Number.isInteger(value.version);
}

export function isSettingsDictItem(value: unknown): value is SettingsDictItem {
  return isRecord(value)
    && isGuid(value.id)
    && isGuid(value.dictTypeId)
    && isNonEmptyString(value.label)
    && typeof value.value === 'string'
    && valuePattern.test(value.value)
    && (value.color === null || typeof value.color === 'string')
    && Number.isInteger(value.displayOrder)
    && typeof value.isActive === 'boolean'
    && typeof value.createdAtUtc === 'string'
    && (value.updatedAtUtc === null || typeof value.updatedAtUtc === 'string')
    && Number.isInteger(value.version);
}

export function isSettingsDictItemPage(
  value: unknown
): value is SettingsDictItemPage {
  return isRecord(value)
    && Array.isArray(value.items)
    && value.items.every(isSettingsDictItem)
    && typeof value.page === 'number'
    && typeof value.pageSize === 'number'
    && typeof value.total === 'number';
}

export function isCreateSettingsDictItemRequest(
  value: unknown
): value is CreateSettingsDictItemRequest {
  return isRecord(value)
    && isNonEmptyString(value.label)
    && typeof value.value === 'string'
    && valuePattern.test(value.value)
    && (value.color === undefined
      || value.color === null
      || typeof value.color === 'string')
    && Number.isInteger(value.displayOrder);
}

export function isUpdateSettingsDictItemRequest(
  value: unknown
): value is UpdateSettingsDictItemRequest {
  return isRecord(value)
    && isNonEmptyString(value.label)
    && (value.color === undefined
      || value.color === null
      || typeof value.color === 'string')
    && Number.isInteger(value.displayOrder)
    && typeof value.version === 'number'
    && Number.isInteger(value.version);
}

export function isDeleteSettingsDictTypeRequest(
  value: unknown
): value is DeleteSettingsDictTypeRequest {
  return isRecord(value)
    && typeof value.version === 'number'
    && Number.isInteger(value.version);
}

export function isDeleteSettingsDictItemRequest(
  value: unknown
): value is DeleteSettingsDictItemRequest {
  return isRecord(value)
    && typeof value.version === 'number'
    && Number.isInteger(value.version);
}

function isGuid(value: unknown): value is string {
  return typeof value === 'string' && guidPattern.test(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
