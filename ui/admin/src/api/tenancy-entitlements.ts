import type { EntitlementEnforcementPhase } from '@fullnet/client-contracts';
import { request } from './http';
import { isRecord, isInteger, readResponse } from '@fullnet/client-contracts';

export async function getEntitlementEnforcementPhase(): Promise<EntitlementEnforcementPhase> {
  const value = await request<unknown>('/api/v1/tenancy/settings/entitlement-enforcement');
  return readResponse(value, (v): v is EntitlementEnforcementPhase => isRecord(v) && typeof v.phase === 'string' && isInteger(v.version), 'client.invalid_entitlement_enforcement');
}

export async function updateEntitlementEnforcementPhase(
  phase: string,
  version: number
): Promise<EntitlementEnforcementPhase> {
  const value = await request<unknown>('/api/v1/tenancy/settings/entitlement-enforcement', {
    method: 'PUT',
    body: JSON.stringify({ phase, version })
  });
  return readResponse(value, (v): v is EntitlementEnforcementPhase => isRecord(v) && typeof v.phase === 'string' && isInteger(v.version), 'client.invalid_entitlement_enforcement');
}
