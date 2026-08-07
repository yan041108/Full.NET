/** 限时诊断策略单条规则。 */
export interface DiagnosticPolicyRule {
  scopeKind: string;
  scopeValue: string;
  successSampleRateOverride: number | null;
  bestEffortCapacityOverride: number | null;
  maxRequestPayloadBytesOverride: number | null;
  maxResponsePayloadBytesOverride: number | null;
  expiresAtUtc: string;
}

/** 当前生效的限时诊断策略快照。 */
export interface DiagnosticPolicy {
  version: number;
  pressureState: string;
  isDefault: boolean;
  loadedAtUtc: string;
  activeRules: DiagnosticPolicyRule[];
  configEntryVersion: number;
}

export interface UpdateDiagnosticPolicyRequest {
  pressureState: string;
  rules: DiagnosticPolicyRule[];
  configEntryVersion: number;
}

export interface RestoreDiagnosticPolicyRequest {
  configEntryVersion: number;
}

/** 校验不可信 JSON 是否为诊断策略规则。 */
export function isDiagnosticPolicyRule(value: unknown): value is DiagnosticPolicyRule {
  return isRecord(value)
    && typeof value.scopeKind === 'string'
    && typeof value.scopeValue === 'string'
    && (value.successSampleRateOverride === null || typeof value.successSampleRateOverride === 'number')
    && (value.bestEffortCapacityOverride === null || typeof value.bestEffortCapacityOverride === 'number')
    && (value.maxRequestPayloadBytesOverride === null || typeof value.maxRequestPayloadBytesOverride === 'number')
    && (value.maxResponsePayloadBytesOverride === null || typeof value.maxResponsePayloadBytesOverride === 'number')
    && typeof value.expiresAtUtc === 'string';
}

/** 校验不可信 JSON 是否为诊断策略响应。 */
export function isDiagnosticPolicy(value: unknown): value is DiagnosticPolicy {
  return isRecord(value)
    && Number.isInteger(value.version)
    && typeof value.pressureState === 'string'
    && typeof value.isDefault === 'boolean'
    && typeof value.loadedAtUtc === 'string'
    && Array.isArray(value.activeRules)
    && value.activeRules.every(isDiagnosticPolicyRule)
    && Number.isInteger(value.configEntryVersion);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}