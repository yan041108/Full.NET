import { configureAuthentication, request } from './http.js';
import {
  isCurrentUserResponse,
  isFullNetProblemDetails,
  isNavigationTree,
  isTenantContextSummaryArray,
  isTenantContextTokenResponse,
  isTokenResponse
} from './contracts.js';
import { isSupportedNavigationTree } from './navigation.js';

const readTenantsPermission = 'tenancy.tenants.read';
const contextConflictCode = 'identity.session_context_conflict';

/**
 * 创建独立管理端会话状态机；Access Token 只保存在闭包内，不写入浏览器持久化存储。
 */
export function createIdentitySession() {
  let state = 'initializing';
  let currentUser;
  let navigation = [];
  let availableTenants = [];
  let switching = false;
  let token;
  const listeners = new Set();

  configureAuthentication({
    getAccessToken: () => token?.accessToken,
    refresh: refreshAccessToken
  });

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
      await loadAuthenticatedSnapshot();
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
      await loadAuthenticatedSnapshot();
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

  async function switchTenant(tenantId) {
    if (switching) {
      return;
    }

    switching = true;
    notify();
    try {
      try {
        await changeTenantContext(tenantId);
      } catch (error) {
        if (!isFullNetProblemDetails(error)
          || error.code !== contextConflictCode
          || !await refreshAccessToken()) {
          throw error;
        }

        // 并发冲突只使用最新刷新会话重试一次，防止循环覆盖服务端较新的上下文。
        try {
          await changeTenantContext(tenantId);
        } catch (retryError) {
          // Refresh 已替换 Token；重试失败时必须清空旧快照，避免授权范围错配。
          clear();
          throw retryError;
        }
      }
    } finally {
      switching = false;
      notify();
    }
  }

  async function changeTenantContext(tenantId) {
    const value = await request('/api/v1/tenancy/context', {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ tenantId })
    });
    if (!isTenantContextTokenResponse(value)) {
      throw new TypeError('租户上下文响应不符合契约。');
    }

    token = value;
    try {
      await loadAuthenticatedSnapshot();
      state = 'authenticated';
      notify();
    } catch (error) {
      // 服务端已切换时不得继续使用旧授权快照；清空状态可阻断错误范围的后续请求。
      clear();
      throw error;
    }
  }

  async function logout() {
    try {
      await request('/api/v1/auth/logout', {
        method: 'POST',
        headers: csrfHeaders()
      }, undefined, { retryUnauthorized: false });
    } catch {
      // 本地清理不依赖网络成功，服务端仍由会话过期与重用检测兜底。
    } finally {
      clear();
    }
  }

  async function loadAuthenticatedSnapshot() {
    const userValue = await request('/api/v1/me');
    if (!isCurrentUserResponse(userValue)) {
      throw new TypeError('当前用户响应不符合契约。');
    }

    const navigationValue = await request('/api/v1/navigation');
    if (!isNavigationTree(navigationValue)
      || !isSupportedNavigationTree(navigationValue)) {
      throw new TypeError('导航响应不符合本地组件白名单。');
    }

    let tenantValues = [];
    if (userValue.permissions.includes(readTenantsPermission)) {
      const tenantValue = await request('/api/v1/tenancy/available');
      if (!isTenantContextSummaryArray(tenantValue)) {
        throw new TypeError('可用租户响应不符合契约。');
      }

      tenantValues = tenantValue;
    }

    // 所有不可信响应都通过守卫后再原子替换，避免新旧授权数据混杂。
    currentUser = userValue;
    navigation = navigationValue;
    availableTenants = tenantValues;
  }

  function can(permission) {
    return currentUser?.permissions.includes(permission) === true;
  }

  function clear() {
    token = undefined;
    currentUser = undefined;
    navigation = [];
    availableTenants = [];
    state = 'anonymous';
    notify();
  }

  function snapshot() {
    const activeTenant = availableTenants.find(
      tenant => tenant.id === currentUser?.tenantId
    );
    return {
      state,
      currentUser,
      navigation,
      availableTenants,
      switching,
      currentContextName: activeTenant?.name ?? (
        currentUser?.tenantId ? currentUser.scope : 'Full.NET Host'
      )
    };
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

  return {
    login,
    restore,
    switchTenant,
    logout,
    can,
    snapshot,
    subscribe,
    dispose
  };
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
