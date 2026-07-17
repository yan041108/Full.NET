import { configureAuthentication, request } from './http.js';

/**
 * 创建独立的管理端会话状态机。
 * Access Token 仅保存在函数闭包中，不写入 Web Storage 或可读 Cookie。
 */
export function createIdentitySession() {
  let state = 'initializing';
  let currentUser;
  let token;
  const listeners = new Set();
  const bridge = {
    getAccessToken: () => token?.accessToken,
    refresh: refreshAccessToken
  };

  configureAuthentication(bridge);

  async function login(username, password) {
    const value = await request('/api/v1/auth/login', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ username, password })
    }, undefined, { retryUnauthorized: false });
    if (!isTokenResponse(value)) {
      throw new TypeError('登录响应不符合 TokenResponse 契约。');
    }

    token = value;
    try {
      await loadCurrentUser();
      state = 'authenticated';
      notify();
    } catch (error) {
      clear();
      throw error;
    }
  }

  async function restore() {
    state = 'initializing';
    notify();
    if (!await refreshAccessToken()) {
      return false;
    }

    try {
      await loadCurrentUser();
      state = 'authenticated';
      notify();
      return true;
    } catch {
      clear();
      return false;
    }
  }

  async function refreshAccessToken() {
    try {
      const value = await request('/api/v1/auth/refresh', {
        method: 'POST',
        headers: csrfHeaders()
      }, undefined, { retryUnauthorized: false });
      if (!isTokenResponse(value)) {
        clear();
        return false;
      }

      token = value;
      return true;
    } catch {
      clear();
      return false;
    }
  }

  async function logout() {
    try {
      await request('/api/v1/auth/logout', {
        method: 'POST',
        headers: csrfHeaders()
      }, undefined, { retryUnauthorized: false });
    } catch {
      // 本地状态清理不能依赖网络成功，服务端会话仍由过期和重用检测兜底。
    } finally {
      clear();
    }
  }

  async function loadCurrentUser() {
    const value = await request('/api/v1/me');
    if (!isCurrentUserResponse(value)) {
      throw new TypeError('当前用户响应不符合契约。');
    }

    currentUser = value;
  }

  function clear() {
    token = undefined;
    currentUser = undefined;
    state = 'anonymous';
    notify();
  }

  function snapshot() {
    return { state, currentUser };
  }

  function subscribe(listener) {
    listeners.add(listener);
    listener(snapshot());
    return () => listeners.delete(listener);
  }

  function notify() {
    const value = snapshot();
    listeners.forEach(listener => listener(value));
  }

  function dispose() {
    listeners.clear();
    token = undefined;
    configureAuthentication();
  }

  return { login, restore, logout, snapshot, subscribe, dispose };
}

export const identitySession = createIdentitySession();

function csrfHeaders() {
  const encodedValue = document.cookie
    .split(';')
    .map(part => part.trim())
    .find(part => part.startsWith('fullnet-csrf='))
    ?.slice('fullnet-csrf='.length);
  if (!encodedValue) {
    return {};
  }

  try {
    return { 'X-CSRF-Token': decodeURIComponent(encodedValue) };
  } catch {
    return {};
  }
}

function isTokenResponse(value) {
  return isRecord(value)
    && typeof value.accessToken === 'string'
    && value.accessToken.length > 0
    && value.tokenType === 'Bearer'
    && typeof value.expiresAtUtc === 'string'
    && value.expiresAtUtc.length > 0;
}

function isCurrentUserResponse(value) {
  return isRecord(value)
    && typeof value.id === 'string'
    && typeof value.username === 'string'
    && typeof value.displayName === 'string'
    && (typeof value.tenantId === 'string' || value.tenantId === null)
    && typeof value.scope === 'string'
    && Array.isArray(value.permissions)
    && value.permissions.every(permission => typeof permission === 'string')
    && typeof value.sessionId === 'string';
}

function isRecord(value) {
  return typeof value === 'object' && value !== null;
}
