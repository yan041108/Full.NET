import { changePassword as changePasswordRequest, type TokenResponse } from '@fullnet/client-contracts';
import { http } from './http';

/** 当前用户自助改密（POST /api/v1/me/password）并轮换当前设备会话令牌。 */
export async function changePassword(
  currentPassword: string,
  newPassword: string,
  signal?: AbortSignal
): Promise<TokenResponse> {
  return changePasswordRequest(http, currentPassword, newPassword, signal);
}
