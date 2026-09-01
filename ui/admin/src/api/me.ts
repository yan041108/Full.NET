import {
  identityGetCurrentUser,
  isCurrentUserResponse,
  type CurrentUserResponse
} from '@fullnet/client-contracts';
import { http } from './http';

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

/** 导出当前用户快照模型，供会话恢复、壳层渲染与权限初始化共享同一契约。 */
export type { CurrentUserResponse };
