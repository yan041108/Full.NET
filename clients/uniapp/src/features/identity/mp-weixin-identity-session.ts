import {
  isCurrentUserResponse,
  isTokenResponse,
  type CurrentUserResponse,
  type TokenResponse
} from '@fullnet/client-contracts';

import type { ConfigurableHttpClient } from '../../api/http';
import type {
  IdentitySessionController,
  IdentitySessionSnapshot,
  IdentitySessionState
} from './identity-session-types';

export interface MpWeixinIdentitySessionOptions {
  readonly http: ConfigurableHttpClient;
}

/**
 * 创建微信小程序身份会话。仅支持密码登录 + Bearer 访问令牌；刷新令牌不在小程序端恢复。
 */
export function createMpWeixinIdentitySession(
  options: MpWeixinIdentitySessionOptions
): IdentitySessionController {
  const { http } = options;
  let state: IdentitySessionState = 'anonymous';
  let token: TokenResponse | undefined;
  let currentUser: CurrentUserResponse | undefined;
  let generation = 0;
  const listeners = new Set<(snapshot: IdentitySessionSnapshot) => void>();

  http.configureAuthentication({
    getAccessToken: () => token?.accessToken,
    refresh: async () => false
  });

  async function login(username: string, password: string): Promise<void> {
    const operationGeneration = ++generation;
    try {
      const value = await http.request<unknown>({
        path: '/api/v1/auth/login',
        method: 'POST',
        data: { username, password },
        retryUnauthorized: false
      });
      if (!isTokenResponse(value)) {
        throw new TypeError('登录响应不符合 TokenResponse 契约。');
      }

      if (operationGeneration !== generation) {
        return;
      }

      token = value;
      currentUser = await loadCurrentUser(operationGeneration);
      if (operationGeneration !== generation) {
        return;
      }

      state = 'authenticated';
      notify();
    } catch (error: unknown) {
      if (operationGeneration === generation) {
        clearLocal();
      }
      throw error;
    }
  }

  async function restore(): Promise<boolean> {
    state = 'anonymous';
    notify();
    return false;
  }

  async function logout(): Promise<void> {
    generation += 1;
    clearLocal();
  }

  function can(permission: string): boolean {
    return state === 'authenticated'
      && currentUser?.permissions.includes(permission) === true;
  }

  function readAccessToken(): string | undefined {
    return token?.accessToken;
  }

  function snapshot(): IdentitySessionSnapshot {
    return currentUser === undefined
      ? { state }
      : { state, currentUser };
  }

  function subscribe(listener: (snapshot: IdentitySessionSnapshot) => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  }

  function notify(): void {
    const nextSnapshot = snapshot();
    for (const listener of listeners) {
      listener(nextSnapshot);
    }
  }

  async function loadCurrentUser(operationGeneration: number): Promise<CurrentUserResponse> {
    const value = await http.request<unknown>({ path: '/api/v1/me' });
    if (!isCurrentUserResponse(value) || operationGeneration !== generation) {
      throw new TypeError('当前用户响应不符合 CurrentUserResponse 契约。');
    }
    return value;
  }

  function clearLocal(): void {
    token = undefined;
    currentUser = undefined;
    state = 'anonymous';
    notify();
  }

  function dispose(): void {
    generation += 1;
    clearLocal();
    listeners.clear();
    http.configureAuthentication();
  }

  return {
    login,
    restore,
    logout,
    can,
    readAccessToken,
    snapshot,
    subscribe,
    dispose
  };
}
