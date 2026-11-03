import { identityContinueMyTenantRegistration, identityListMyTenantRegistrations,
  readTenantRegistrationProgress, readTenantRegistrationProgressList } from '@fullnet/client-contracts';
import { http } from './http';

export async function listMyTenantRegistrations(signal?: AbortSignal) {
  return readTenantRegistrationProgressList(await identityListMyTenantRegistrations(http, {}, signal));
}
export async function continueMyTenantRegistration(id: string, version: number) {
  return readTenantRegistrationProgress(await identityContinueMyTenantRegistration(http,
    { id, body: { version } }, undefined, { retryUnauthorized: false }));
}
