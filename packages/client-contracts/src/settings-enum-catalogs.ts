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

const catalogKeyPattern = /^[a-z][a-z0-9._-]{1,126}[a-z0-9]$/;

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

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
