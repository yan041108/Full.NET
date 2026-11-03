import { isGuid, isInteger, isRecord } from './response-shape.js';

export const registrationPhases = ['Requested', 'Reserved', 'Confirmed', 'Activated', 'Cancelling', 'Cancelled'] as const;
export interface TenantRegistrationProgress {
  id: string; tenantId: string; registrationWayName: string | null;
  phase: typeof registrationPhases[number]; errorCode: string | null;
  createdAtUtc: string; version: number; canContinue: boolean;
}

/** 只接受已知阶段，避免未知或损坏的进度被页面当作已成功。 */
export function isTenantRegistrationProgress(value: unknown): value is TenantRegistrationProgress {
  return isRecord(value) && isGuid(value.id) && isGuid(value.tenantId)
    && (value.registrationWayName === null || typeof value.registrationWayName === 'string')
    && (value.errorCode === null || typeof value.errorCode === 'string')
    && typeof value.phase === 'string' && registrationPhases.some(phase => phase === value.phase)
    && typeof value.createdAtUtc === 'string' && Number.isFinite(Date.parse(value.createdAtUtc))
    && isInteger(value.version) && value.version > 0 && typeof value.canContinue === 'boolean';
}
export function readTenantRegistrationProgress(value: unknown): TenantRegistrationProgress {
  if (!isTenantRegistrationProgress(value)) throw new TypeError('client.invalid_registration_progress');
  return value;
}
export function readTenantRegistrationProgressList(value: unknown): TenantRegistrationProgress[] {
  if (!Array.isArray(value) || value.length > 20 || !value.every(isTenantRegistrationProgress)
    || new Set(value.map(item => item.id)).size !== value.length) throw new TypeError('client.invalid_registration_progress_list');
  return value;
}
