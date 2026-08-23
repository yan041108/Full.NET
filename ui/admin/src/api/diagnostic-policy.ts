import {
  isDiagnosticPolicy,
  settingsGetHostDiagnosticPolicy,
  settingsRestoreHostDiagnosticPolicy,
  settingsUpdateHostDiagnosticPolicy,
  type DiagnosticPolicy,
  type DiagnosticPolicyRule
} from '@fullnet/client-contracts';
import { http } from './http';

export type { DiagnosticPolicy, DiagnosticPolicyRule };

export async function getDiagnosticPolicy(
  signal?: AbortSignal
): Promise<DiagnosticPolicy> {
  const value = await settingsGetHostDiagnosticPolicy(http, {}, signal);
  if (!isDiagnosticPolicy(value)) {
    throw new Error('client.invalid_diagnostic_policy');
  }

  return value;
}

export async function updateDiagnosticPolicy(
  pressureState: string,
  rules: DiagnosticPolicyRule[],
  configEntryVersion: number,
  signal?: AbortSignal
): Promise<DiagnosticPolicy> {
  const value = await settingsUpdateHostDiagnosticPolicy(
    http,
    {
      body: {
        pressureState,
        rules,
        configEntryVersion
      }
    },
    signal
  );
  if (!isDiagnosticPolicy(value)) {
    throw new Error('client.invalid_diagnostic_policy');
  }

  return value;
}

export async function restoreDiagnosticPolicy(
  configEntryVersion: number,
  signal?: AbortSignal
): Promise<DiagnosticPolicy> {
  const value = await settingsRestoreHostDiagnosticPolicy(
    http,
    { body: { configEntryVersion } },
    signal
  );
  if (!isDiagnosticPolicy(value)) {
    throw new Error('client.invalid_diagnostic_policy');
  }

  return value;
}
