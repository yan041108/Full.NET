export interface RegenerateMfaRecoveryCodesResponse {
  recoveryCodes: string[];
}

export interface ConsumeMfaRecoveryCodeRequest {
  recoveryCode: string;
}

export interface ConsumeMfaRecoveryCodeResponse {
  consumed: boolean;
}

export function isRegenerateMfaRecoveryCodesResponse(
  value: unknown
): value is RegenerateMfaRecoveryCodesResponse {
  return isRecord(value)
    && Array.isArray(value.recoveryCodes)
    && value.recoveryCodes.every(code => typeof code === 'string' && code.length > 0);
}

export function isConsumeMfaRecoveryCodeResponse(
  value: unknown
): value is ConsumeMfaRecoveryCodeResponse {
  return isRecord(value) && typeof value.consumed === 'boolean';
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
