import {
  isCurrentUserResponse,
  isTokenResponse,
  type CurrentUserResponse,
  type TokenResponse
} from '@fullnet/client-contracts';

import type { ConfigurableHttpClient } from '../../api/http';

export type H5IdentitySessionState = 'initializing' | 'authenticated' | 'anonymous';

export interface H5IdentitySessionSnapshot {
  readonly state: H5IdentitySessionState;
  readonly currentUser?: CurrentUserResponse;
}

export interface H5IdentitySessionController {
  login(username: string, password: string): Promise<void>;
  restore(): Promise<boolean>;
  logout(): Promise<void>;
  can(permission: string): boolean;
  readAccessToken(): string | undefined;
  snapshot(): H5IdentitySessionSnapshot;
  subscribe(listener: (snapshot: H5IdentitySessionSnapshot) => void): () => void;
  dispose(): void;
}

export interface H5IdentitySessionOptions {
  readonly http: ConfigurableHttpClient;
  readonly readCsrfHeaders: () => Readonly<Record<string, string>>;
}

/**
 * 创建 H5 专用身份会话。访问令牌只存在闭包内，刷新令牌仍由浏览器 HttpOnly Cookie 承载。
 */
export function createH5IdentitySession(
  options: H5IdentitySessionOptions
): H5IdentitySessionController {
  const { http, readCsrfHeaders } = options;
  let state: H5IdentitySessionState = 'initializing';
  let token: TokenResponse | undefined;
  let currentUser: CurrentUserResponse | undefined;
  let generation = 0;
  let restoreInFlight: Promise<boolean> | undefined;
  const listeners = new Set<(snapshot: H5IdentitySessionSnapshot) => void>();

  http.configureAuthentication({
    getAccessToken: () => token?.accessToken,
    refresh: refreshAccessToken
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

  function restore(): Promise<boolean> {
    if (restoreInFlight !== undefined) {
      return restoreInFlight;
    }

    const pending = performRestore().finally(() => {
      if (restoreInFlight === pending) {
        restoreInFlight = undefined;
      }
    });
    restoreInFlight = pending;
    return pending;
  }

  async function performRestore(): Promise<boolean> {
    const operationGeneration = generation;
    state = 'initializing';
    notify();
    if (!await refreshAccessToken(operationGeneration)) {
      return false;
    }

    try {
      currentUser = await loadCurrentUser(operationGeneration);
      if (operationGeneration !== generation) {
        return false;
      }

      state = 'authenticated';
      notify();
      return true;
    } catch {
      if (operationGeneration === generation) {
        clearLocal();
      }
      return false;
    }
  }

  async function refreshAccessToken(operationGeneration = generation): Promise<boolean> {
    try {
      const value = await http.request<unknown>({
        path: '/api/v1/auth/refresh',
        method: 'POST',
        headers: readCsrfHeaders(),
        retryUnauthorized: false
      });
      if (!isTokenResponse(value) || operationGeneration !== generation) {
        if (operationGeneration === generation) {
          clearLocal();
        }
        return false;
      }

      token = value;
      return true;
    } catch {
      if (operationGeneration === generation) {
        clearLocal();
      }
      return false;
    }
  }

  async function loadCurrentUser(operationGeneration: number): Promise<CurrentUserResponse> {
    const value = await http.request<unknown>({ path: '/api/v1/me' });
    if (!isCurrentUserResponse(value) || operationGeneration !== generation) {
      throw new TypeError('当前用户响应不符合 CurrentUserResponse 契约。');
    }
    return value;
  }

  async function logout(): Promise<void> {
    generation += 1;
    clearLocal();
    await http.request<void>({
      path: '/api/v1/auth/logout',
      method: 'POST',
      headers: readCsrfHeaders(),
      retryUnauthorized: false
    });
  }

  function can(permission: string): boolean {
    return state === 'authenticated'
      && currentUser?.permissions.includes(permission) === true;
  }

  function readAccessToken(): string | undefined {
    return token?.accessToken;
  }

  function snapshot(): H5IdentitySessionSnapshot {
    return currentUser === undefined
      ? { state }
      : { state, currentUser };
  }

  function subscribe(listener: (snapshot: H5IdentitySessionSnapshot) => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  }

  function notify(): void {
    const nextSnapshot = snapshot();
    for (const listener of listeners) {
      listener(nextSnapshot);
    }
  }

  function clearLocal(): void {
    token = undefined;
    currentUser = undefined;
    state = 'anonymous';
    notify();
  }

  function dispose(): void {
    generation += 1;
    restoreInFlight = undefined;
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
