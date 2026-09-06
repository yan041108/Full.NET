import {
  isRegistrationPolicy,
  isRegistrationWay,
  isRegistrationWayPage,
  type CreateRegistrationWayRequest,
  type RegistrationPolicy,
  type RegistrationWay,
  type RegistrationWayListQuery,
  type RegistrationWayPage,
  type UpdateRegistrationPolicyRequest,
  type UpdateRegistrationWayRequest
} from '@fullnet/client-contracts';
import { request } from './http';

function buildListQuery(query: RegistrationWayListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.tenantId?.trim()) {
    params.set('tenantId', query.tenantId.trim());
  }
  if (query.nameContains?.trim()) {
    params.set('nameContains', query.nameContains.trim());
  }
  if (query.isEnabled !== undefined) {
    params.set('isEnabled', String(query.isEnabled));
  }
  return params.toString();
}

export async function getRegistrationPolicy(signal?: AbortSignal): Promise<RegistrationPolicy> {
  const value = await request<unknown>(
    '/api/v1/identity/registration-policy',
    { method: 'GET' },
    signal
  );
  if (!isRegistrationPolicy(value)) {
    throw new Error('client.invalid_registration_policy');
  }
  return value;
}

export async function updateRegistrationPolicy(
  body: UpdateRegistrationPolicyRequest,
  signal?: AbortSignal
): Promise<RegistrationPolicy> {
  const value = await request<unknown>(
    '/api/v1/identity/registration-policy',
    { method: 'PUT', body },
    signal
  );
  if (!isRegistrationPolicy(value)) {
    throw new Error('client.invalid_registration_policy');
  }
  return value;
}

export async function listRegistrationWays(
  query: RegistrationWayListQuery = {},
  signal?: AbortSignal
): Promise<RegistrationWayPage> {
  const value = await request<unknown>(
    `/api/v1/identity/registration-ways?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isRegistrationWayPage(value)) {
    throw new Error('client.invalid_registration_way_page');
  }
  return value;
}

export async function createRegistrationWay(
  body: CreateRegistrationWayRequest,
  signal?: AbortSignal
): Promise<RegistrationWay> {
  const value = await request<unknown>(
    '/api/v1/identity/registration-ways',
    { method: 'POST', body },
    signal
  );
  if (!isRegistrationWay(value)) {
    throw new Error('client.invalid_registration_way');
  }
  return value;
}

export async function updateRegistrationWay(
  id: string,
  body: UpdateRegistrationWayRequest,
  signal?: AbortSignal
): Promise<RegistrationWay> {
  const value = await request<unknown>(
    `/api/v1/identity/registration-ways/${encodeURIComponent(id)}`,
    { method: 'PUT', body },
    signal
  );
  if (!isRegistrationWay(value)) {
    throw new Error('client.invalid_registration_way');
  }
  return value;
}

export async function deleteRegistrationWay(id: string, signal?: AbortSignal): Promise<void> {
  await request<unknown>(
    `/api/v1/identity/registration-ways/${encodeURIComponent(id)}`,
    { method: 'DELETE' },
    signal
  );
}
