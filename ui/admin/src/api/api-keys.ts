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

/** 分页查询 Host API Key 列表，并对筛选项做 trim 规范化。 */
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

/** 创建 Host API Key，并校验返回的一次性明文密钥结果。 */
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

/** 禁用 Host API Key。 */
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

/** 轮换 Host API Key，并返回新的单次可见明文密钥。 */
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

/** 导出 Host API Key 列表、详情与一次性明文结果模型，供列表页、创建弹窗与轮换流程共享同一契约。 */
export type {
  CreateHostApiKeyRequest,
  CreateHostApiKeyResult,
  HostApiKey,
  HostApiKeyPage
};
