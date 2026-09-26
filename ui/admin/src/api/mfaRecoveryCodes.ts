import type { RegenerateMfaRecoveryCodesResponse } from '@fullnet/client-contracts';
import { isRegenerateMfaRecoveryCodesResponse } from '@fullnet/client-contracts';
import { request } from './http';
import { readResponse } from '@fullnet/client-contracts';

export async function regenerateMyMfaRecoveryCodes(
  signal?: AbortSignal
): Promise<RegenerateMfaRecoveryCodesResponse> {
  const value = await request<unknown>(
    '/api/v1/identity/me/mfa/recovery-codes/regenerate',
    { method: 'POST' },
    signal
  );
  return readResponse(
    value,
    isRegenerateMfaRecoveryCodesResponse,
    'client.invalid_mfa_recovery_codes');
}

export type { RegenerateMfaRecoveryCodesResponse };
