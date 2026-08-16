import {
  isCurrentUserResponse,
  type CurrentUserResponse
} from '@fullnet/client-contracts';
import { request } from './http';

export async function getCurrentUser(): Promise<CurrentUserResponse> {
  const value = await request<unknown>('/api/v1/me');
  if (!isCurrentUserResponse(value)) {
    throw new Error('client.invalid_current_user');
  }
  return value;
}

export type { CurrentUserResponse };
