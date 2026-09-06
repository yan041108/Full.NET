export interface SettingsEnumCatalogSummary {
  key: string;
  displayName: string;
  description: string | null;
  memberCount: number;
}

export interface SettingsEnumCatalogMember {
  code: string;
  label: string;
  displayOrder: number;
}

export interface SettingsEnumCatalogDetail {
  key: string;
  displayName: string;
  description: string | null;
  members: SettingsEnumCatalogMember[];
}

export type SettingsEnumCatalogDictGenerationAction =
  | 'create'
  | 'skip_exists'
  | 'conflict_label'
  | 'invalid_value';

export interface SettingsEnumCatalogDictGenerationItemPreview {
  value: string;
  proposedLabel: string;
  existingLabel: string | null;
  displayOrder: number;
  action: SettingsEnumCatalogDictGenerationAction;
}

export interface SettingsEnumCatalogDictGenerationUnmanagedItem {
  value: string;
  label: string;
  isActive: boolean;
}

export interface SettingsEnumCatalogDictGenerationPreview {
  catalogKey: string;
  dictTypeCode: string;
  dictTypeName: string;
  dictTypeExists: boolean;
  willCreateDictType: boolean;
  items: SettingsEnumCatalogDictGenerationItemPreview[];
  unmanagedItems: SettingsEnumCatalogDictGenerationUnmanagedItem[];
}

export interface SettingsEnumCatalogDictGenerationResult {
  catalogKey: string;
  dictTypeCode: string;
  dictTypeId: string | null;
  dictTypeCreated: boolean;
  itemsCreated: number;
  itemsSkipped: number;
  itemsConflicted: number;
  itemsInvalid: number;
  items: SettingsEnumCatalogDictGenerationItemPreview[];
}

const catalogKeyPattern = /^[a-z][a-z0-9._-]{1,126}[a-z0-9]$/;
const dictGenerationActions = new Set<SettingsEnumCatalogDictGenerationAction>([
  'create',
  'skip_exists',
  'conflict_label',
  'invalid_value'
]);

export function isSettingsEnumCatalogSummary(
  value: unknown
): value is SettingsEnumCatalogSummary {
  return isRecord(value)
    && typeof value.key === 'string'
    && catalogKeyPattern.test(value.key)
    && isNonEmptyString(value.displayName)
    && (value.description === null || typeof value.description === 'string')
    && typeof value.memberCount === 'number'
    && Number.isInteger(value.memberCount)
    && value.memberCount >= 0;
}

export function isSettingsEnumCatalogMember(
  value: unknown
): value is SettingsEnumCatalogMember {
  return isRecord(value)
    && isNonEmptyString(value.code)
    && isNonEmptyString(value.label)
    && Number.isInteger(value.displayOrder);
}

export function isSettingsEnumCatalogDetail(
  value: unknown
): value is SettingsEnumCatalogDetail {
  return isRecord(value)
    && typeof value.key === 'string'
    && catalogKeyPattern.test(value.key)
    && isNonEmptyString(value.displayName)
    && (value.description === null || typeof value.description === 'string')
    && Array.isArray(value.members)
    && value.members.every(isSettingsEnumCatalogMember);
}

export function isSettingsEnumCatalogDictGenerationItemPreview(
  value: unknown
): value is SettingsEnumCatalogDictGenerationItemPreview {
  return isRecord(value)
    && isNonEmptyString(value.value)
    && isNonEmptyString(value.proposedLabel)
    && (value.existingLabel === null || isNonEmptyString(value.existingLabel))
    && Number.isInteger(value.displayOrder)
    && typeof value.action === 'string'
    && dictGenerationActions.has(value.action as SettingsEnumCatalogDictGenerationAction);
}

export function isSettingsEnumCatalogDictGenerationUnmanagedItem(
  value: unknown
): value is SettingsEnumCatalogDictGenerationUnmanagedItem {
  return isRecord(value)
    && isNonEmptyString(value.value)
    && isNonEmptyString(value.label)
    && typeof value.isActive === 'boolean';
}

export function isSettingsEnumCatalogDictGenerationPreview(
  value: unknown
): value is SettingsEnumCatalogDictGenerationPreview {
  return isRecord(value)
    && typeof value.catalogKey === 'string'
    && catalogKeyPattern.test(value.catalogKey)
    && typeof value.dictTypeCode === 'string'
    && isNonEmptyString(value.dictTypeName)
    && typeof value.dictTypeExists === 'boolean'
    && typeof value.willCreateDictType === 'boolean'
    && Array.isArray(value.items)
    && value.items.every(isSettingsEnumCatalogDictGenerationItemPreview)
    && Array.isArray(value.unmanagedItems)
    && value.unmanagedItems.every(isSettingsEnumCatalogDictGenerationUnmanagedItem);
}

export function isSettingsEnumCatalogDictGenerationResult(
  value: unknown
): value is SettingsEnumCatalogDictGenerationResult {
  return isRecord(value)
    && typeof value.catalogKey === 'string'
    && catalogKeyPattern.test(value.catalogKey)
    && typeof value.dictTypeCode === 'string'
    && (value.dictTypeId === null || typeof value.dictTypeId === 'string')
    && typeof value.dictTypeCreated === 'boolean'
    && typeof value.itemsCreated === 'number'
    && Number.isInteger(value.itemsCreated)
    && value.itemsCreated >= 0
    && typeof value.itemsSkipped === 'number'
    && Number.isInteger(value.itemsSkipped)
    && value.itemsSkipped >= 0
    && typeof value.itemsConflicted === 'number'
    && Number.isInteger(value.itemsConflicted)
    && value.itemsConflicted >= 0
    && typeof value.itemsInvalid === 'number'
    && Number.isInteger(value.itemsInvalid)
    && value.itemsInvalid >= 0
    && Array.isArray(value.items)
    && value.items.every(isSettingsEnumCatalogDictGenerationItemPreview);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
