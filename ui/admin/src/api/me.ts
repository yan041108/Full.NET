import {
  identityGetCurrentUser,
  isCurrentUserResponse,
  type CurrentUserResponse
} from '@fullnet/client-contracts';
import { http } from './http';

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

export type { CurrentUserResponse };
