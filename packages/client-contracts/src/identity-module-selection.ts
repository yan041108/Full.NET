export interface IdentityModuleSelectionIssue {
  code: string;
  message: string;
  moduleKey: string | null;
  relatedModuleKey: string | null;
}

export interface IdentityModuleSelectionModuleState {
  moduleKey: string;
  isEnabled: boolean;
  dependencies: string[];
  missingDependencies: string[];
}

export interface IdentityModuleSelectionAnalysis {
  isValid: boolean;
  sourceKind: 'preset' | 'explicit' | string;
  preset: string | null;
  enabledModuleKeys: string[];
  officialModuleKeys: string[];
  issues: IdentityModuleSelectionIssue[];
  modules: IdentityModuleSelectionModuleState[];
  deploymentNotice: string;
}

export interface IdentityModuleSelectionValidateRequest {
  preset?: string | null;
  enabled?: string[] | null;
}

export function isIdentityModuleSelectionIssue(
  value: unknown
): value is IdentityModuleSelectionIssue {
  return isRecord(value)
    && isNonEmptyString(value.code)
    && typeof value.message === 'string'
    && (value.moduleKey === null || typeof value.moduleKey === 'string')
    && (value.relatedModuleKey === null || typeof value.relatedModuleKey === 'string');
}

export function isIdentityModuleSelectionModuleState(
  value: unknown
): value is IdentityModuleSelectionModuleState {
  return isRecord(value)
    && isNonEmptyString(value.moduleKey)
    && typeof value.isEnabled === 'boolean'
    && Array.isArray(value.dependencies)
    && value.dependencies.every(item => typeof item === 'string')
    && Array.isArray(value.missingDependencies)
    && value.missingDependencies.every(item => typeof item === 'string');
}

export function isIdentityModuleSelectionAnalysis(
  value: unknown
): value is IdentityModuleSelectionAnalysis {
  return isRecord(value)
    && typeof value.isValid === 'boolean'
    && isNonEmptyString(value.sourceKind)
    && (value.preset === null || typeof value.preset === 'string')
    && Array.isArray(value.enabledModuleKeys)
    && value.enabledModuleKeys.every(isNonEmptyString)
    && Array.isArray(value.officialModuleKeys)
    && value.officialModuleKeys.every(isNonEmptyString)
    && Array.isArray(value.issues)
    && value.issues.every(isIdentityModuleSelectionIssue)
    && Array.isArray(value.modules)
    && value.modules.every(isIdentityModuleSelectionModuleState)
    && typeof value.deploymentNotice === 'string';
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
