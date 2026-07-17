import { computed, ref } from 'vue';
import { defineStore } from 'pinia';
import {
  isCurrentUserResponse,
  isTokenResponse,
  type CurrentUserResponse,
  type TokenResponse
} from '@fullnet/client-contracts';
import { configureAuthentication, request } from '../api/http';

export type SessionState = 'initializing' | 'authenticated' | 'anonymous';

export const useSessionStore = defineStore('identity-session', () => {
  const state = ref<SessionState>('initializing');
  const currentUser = ref<CurrentUserResponse>();
  let token: TokenResponse | undefined;

  configureAuthentication({
    getAccessToken: () => token?.accessToken,
    refresh: refreshAccessToken
  });

  const isAuthenticated = computed(() => state.value === 'authenticated');

  function acceptToken(value: TokenResponse): void {
    token = value;
  }

  async function login(username: string, password: string): Promise<void> {
    const value = await request<unknown>('/api/v1/auth/login', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ username, password })
    }, undefined, { retryUnauthorized: false });
    if (!isTokenResponse(value)) {
      throw new TypeError('登录响应不符合 TokenResponse 契约。');
    }

    acceptToken(value);
    try {
      await loadCurrentUser();
      state.value = 'authenticated';
    } catch (error: unknown) {
      clear();
      throw error;
    }
  }

  async function restore(): Promise<void> {
    state.value = 'initializing';
    if (!await refreshAccessToken()) {
      clear();
      return;
    }

    try {
      await loadCurrentUser();
      state.value = 'authenticated';
    } catch {
      clear();
    }
  }

  async function refreshAccessToken(): Promise<boolean> {
    try {
      const value = await request<unknown>('/api/v1/auth/refresh', {
        method: 'POST',
        headers: csrfHeaders()
      }, undefined, { retryUnauthorized: false });
      if (!isTokenResponse(value)) {
        clear();
        return false;
      }

      acceptToken(value);
      return true;
    } catch {
      clear();
      return false;
    }
  }

  async function logout(): Promise<void> {
    try {
      await request<void>('/api/v1/auth/logout', {
        method: 'POST',
        headers: csrfHeaders()
      }, undefined, { retryUnauthorized: false });
    } catch {
      // 本地状态清理不能依赖网络成功，服务端会话仍由过期和重用检测兜底。
    } finally {
      clear();
    }
  }

  async function loadCurrentUser(): Promise<void> {
    const value = await request<unknown>('/api/v1/me');
    if (!isCurrentUserResponse(value)) {
      throw new TypeError('当前用户响应不符合契约。');
    }

    currentUser.value = value;
  }

  function clear(): void {
    token = undefined;
    currentUser.value = undefined;
    state.value = 'anonymous';
  }

  return {
    state,
    currentUser,
    isAuthenticated,
    login,
    restore,
    logout
  };
});

function csrfHeaders(): HeadersInit {
  const value = document.cookie
    .split(';')
    .map(part => part.trim())
    .find(part => part.startsWith('fullnet-csrf='))
    ?.slice('fullnet-csrf='.length);
  return value ? { 'X-CSRF-Token': decodeURIComponent(value) } : {};
}
