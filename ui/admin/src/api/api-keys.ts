import {
  isCreateHostApiKeyResult,
  isHostApiKey,
  isHostApiKeyPage,
  type CreateHostApiKeyResult,
  type HostApiKey,
  type HostApiKeyPage
} from '@fullnet/client-contracts';
import { request } from './http';

export interface CreateHostApiKeyRequest {
  userId: string;
  displayName: string;
  permissions: string[];
  expiresAtUtc: string | null;
}

export async function listHostApiKeys(
  page = 1,
  pageSize = 20,
  userId?: string,
  displayNameContains?: string
): Promise<HostApiKeyPage> {
  const query = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });
  if (userId?.trim()) {
    query.set('userId', userId.trim());
  }
  if (displayNameContains?.trim()) {
    query.set('displayNameContains', displayNameContains.trim());
  }

  const value = await request<unknown>(
    `/api/v1/identity/api-keys?${query.toString()}`
  );
  if (!isHostApiKeyPage(value)) {
    throw new Error('client.invalid_host_api_key_page');
  }
  return value;
}

export async function createHostApiKey(
  body: CreateHostApiKeyRequest
): Promise<CreateHostApiKeyResult> {
  const value = await request<unknown>('/api/v1/identity/api-keys', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!isCreateHostApiKeyResult(value)) {
    throw new Error('client.invalid_create_host_api_key_result');
  }
  return value;
}

export async function disableHostApiKey(id: string): Promise<HostApiKey> {
  const value = await request<unknown>(
    `/api/v1/identity/api-keys/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isHostApiKey(value)) {
    throw new Error('client.invalid_host_api_key');
  }
  return value;
}
