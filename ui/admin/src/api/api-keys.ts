import {
  identityCreateHostApiKey,
  identityDisableHostApiKey,
  identityListHostApiKeys,
  identityRotateHostApiKey,
  isCreateHostApiKeyResult,
  isHostApiKey,
  isHostApiKeyPage,
  type CreateHostApiKeyRequest,
  type CreateHostApiKeyResult,
  type HostApiKey,
  type HostApiKeyPage
} from '@fullnet/client-contracts';
import { http } from './http';

export async function listHostApiKeys(
  page = 1,
  pageSize = 20,
  userId?: string,
  displayNameContains?: string,
  signal?: AbortSignal
): Promise<HostApiKeyPage> {
  const trimmedUserId = userId?.trim();
  const trimmedDisplayName = displayNameContains?.trim();
  const value = await identityListHostApiKeys(
    http,
    {
      page,
      pageSize,
      ...(trimmedUserId ? { userId: trimmedUserId } : {}),
      ...(trimmedDisplayName ? { displayNameContains: trimmedDisplayName } : {})
    },
    signal
  );
  if (!isHostApiKeyPage(value)) {
    throw new Error('client.invalid_host_api_key_page');
  }

  return value;
}

export async function createHostApiKey(
  body: CreateHostApiKeyRequest,
  signal?: AbortSignal
): Promise<CreateHostApiKeyResult> {
  const value = await identityCreateHostApiKey(http, { body }, signal);
  // 生成守卫只要求 secret 为 string；页面契约要求一次性明文非空。
  if (!isCreateHostApiKeyResult(value)) {
    throw new Error('client.invalid_create_host_api_key_result');
  }

  return value;
}

export async function disableHostApiKey(
  id: string,
  signal?: AbortSignal
): Promise<HostApiKey> {
  const value = await identityDisableHostApiKey(http, { apiKeyId: id }, signal);
  if (!isHostApiKey(value)) {
    throw new Error('client.invalid_host_api_key');
  }

  return value;
}

export async function rotateHostApiKey(
  id: string,
  signal?: AbortSignal
): Promise<CreateHostApiKeyResult> {
  const value = await identityRotateHostApiKey(http, { apiKeyId: id }, signal);
  if (!isCreateHostApiKeyResult(value)) {
    throw new Error('client.invalid_create_host_api_key_result');
  }

  return value;
}
