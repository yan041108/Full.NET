import { request } from './http';

export interface DiagnosticPolicyRule {
  scopeKind: string;
  scopeValue: string;
  successSampleRateOverride: number | null;
  bestEffortCapacityOverride: number | null;
  maxRequestPayloadBytesOverride: number | null;
  maxResponsePayloadBytesOverride: number | null;
  expiresAtUtc: string;
}

export interface DiagnosticPolicy {
  version: number;
  pressureState: string;
  isDefault: boolean;
  loadedAtUtc: string;
  activeRules: DiagnosticPolicyRule[];
  configEntryVersion: number;
}

export async function getDiagnosticPolicy(): Promise<DiagnosticPolicy> {
  return request<DiagnosticPolicy>('/api/v1/settings/diagnostic-policy');
}

export async function updateDiagnosticPolicy(
  pressureState: string,
  rules: DiagnosticPolicyRule[],
  configEntryVersion: number
): Promise<DiagnosticPolicy> {
  return request<DiagnosticPolicy>('/api/v1/settings/diagnostic-policy', {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ pressureState, rules, configEntryVersion })
  });
}

export async function restoreDiagnosticPolicy(
  configEntryVersion: number
): Promise<DiagnosticPolicy> {
  return request<DiagnosticPolicy>('/api/v1/settings/diagnostic-policy/restore', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ configEntryVersion })
  });
}
