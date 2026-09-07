import {
  identityGetCurrentUser,
  isCurrentUserResponse,
  type CurrentUserResponse
} from '@fullnet/client-contracts';
import {
  deleteSelfServiceAvatar,
  deleteSelfServiceSignature,
  downloadSelfServiceAvatarContent,
  downloadSelfServiceSignatureContent,
  getSelfServiceProfile as getSelfServiceProfileRequest,
  updateSelfServiceProfile as updateSelfServiceProfileRequest,
  uploadSelfServiceAvatar,
  uploadSelfServiceSignature
} from '@fullnet/client-contracts';
import { http } from './http';

/** 自助档案类型跟随手写客户端返回值；生成模型把字段数组收成可变 Array，不能直接标注。 */
type SelfServiceProfileResponse = Awaited<ReturnType<typeof getSelfServiceProfileRequest>>;
type UpdateSelfServiceProfileRequest = Parameters<typeof updateSelfServiceProfileRequest>[1];

/** 读取当前登录用户快照，并补一层手写契约校验防止生成守卫漏检。 */
export async function getCurrentUser(
  signal?: AbortSignal
): Promise<CurrentUserResponse> {
  const value = await identityGetCurrentUser(http, {}, signal);
  // 生成守卫不校验 SupportedLocale 与 profileVersion>0；页面仍要求手写契约。
  if (!isCurrentUserResponse(value)) {
    throw new Error('client.invalid_current_user');
  }

  return value;
}

/** 读取当前用户自助档案。 */
export async function getSelfServiceProfile(
  signal?: AbortSignal
): Promise<SelfServiceProfileResponse> {
  return getSelfServiceProfileRequest(http, signal);
}

/** 更新当前用户自助档案。 */
export async function updateSelfServiceProfile(
  request: UpdateSelfServiceProfileRequest,
  signal?: AbortSignal
): Promise<SelfServiceProfileResponse> {
  return updateSelfServiceProfileRequest(http, request, signal);
}

export async function uploadProfileAvatar(
  file: File,
  signal?: AbortSignal
): Promise<SelfServiceProfileResponse> {
  return uploadSelfServiceAvatar(http, file, signal);
}

export async function uploadProfileSignature(
  file: File,
  signal?: AbortSignal
): Promise<SelfServiceProfileResponse> {
  return uploadSelfServiceSignature(http, file, signal);
}

export async function removeProfileAvatar(
  signal?: AbortSignal
): Promise<SelfServiceProfileResponse> {
  return deleteSelfServiceAvatar(http, signal);
}

export async function removeProfileSignature(
  signal?: AbortSignal
): Promise<SelfServiceProfileResponse> {
  return deleteSelfServiceSignature(http, signal);
}

export async function fetchProfileAvatarBlob(signal?: AbortSignal): Promise<Blob> {
  return downloadSelfServiceAvatarContent(http, signal);
}

export async function fetchProfileSignatureBlob(signal?: AbortSignal): Promise<Blob> {
  return downloadSelfServiceSignatureContent(http, signal);
}

/** 导出当前用户快照模型，供会话恢复、壳层渲染与权限初始化共享同一契约。 */
export type { CurrentUserResponse, SelfServiceProfileResponse, UpdateSelfServiceProfileRequest };
