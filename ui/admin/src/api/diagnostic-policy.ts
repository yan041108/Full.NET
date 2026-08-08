import {
  isDiagnosticPolicy,
  type DiagnosticPolicy,
  type DiagnosticPolicyRule
} from '@fullnet/client-contracts';
import { request } from './http';

export type { DiagnosticPolicy, DiagnosticPolicyRule };

export async function getDiagnosticPolicy(): Promise<DiagnosticPolicy> {
  const value = await request<unknown>('/api/v1/settings/diagnostic-policy');
  if (!isDiagnosticPolicy(value)) {
    throw new Error('client.invalid_diagnostic_policy');
  }
  return value;
}

export async function updateDiagnosticPolicy(
  pressureState: string,
  rules: DiagnosticPolicyRule[],
  configEntryVersion: number
): Promise<DiagnosticPolicy> {
  const value = await request<unknown>('/api/v1/settings/diagnostic-policy', {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ pressureState, rules, configEntryVersion })
  });
  if (!isDiagnosticPolicy(value)) {
    throw new Error('client.invalid_diagnostic_policy');
  }
  return value;
}

export async function restoreDiagnosticPolicy(
  configEntryVersion: number
): Promise<DiagnosticPolicy> {
  const value = await request<unknown>('/api/v1/settings/diagnostic-policy/restore', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ configEntryVersion })
  });
  if (!isDiagnosticPolicy(value)) {
    throw new Error('client.invalid_diagnostic_policy');
  }
  return value;
}
