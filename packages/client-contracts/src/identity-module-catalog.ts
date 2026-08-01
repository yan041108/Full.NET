export interface IdentityModuleCatalogEntry {
  moduleKey: string;
  displayName: string;
  version: string;
  dependencies: string[];
  hostProfiles: string[];
  sourceClassification: string;
  healthCapability: string;
}

const moduleKeyPattern = /^[A-Za-z][A-Za-z0-9._-]{0,126}$/;

export function isIdentityModuleCatalogEntry(
  value: unknown
): value is IdentityModuleCatalogEntry {
  return isRecord(value)
    && typeof value.moduleKey === 'string'
    && moduleKeyPattern.test(value.moduleKey)
    && isNonEmptyString(value.displayName)
    && isNonEmptyString(value.version)
    && Array.isArray(value.dependencies)
    && value.dependencies.every((item) => typeof item === 'string')
    && Array.isArray(value.hostProfiles)
    && value.hostProfiles.every((item) => typeof item === 'string')
    && isNonEmptyString(value.sourceClassification)
    && isNonEmptyString(value.healthCapability)
    && !value.moduleKey.includes('/')
    && !value.displayName.includes('\\');
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
