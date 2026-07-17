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
  let sessionGeneration = 0;
  const listeners = new Set();

  configureAuthentication({
    getAccessToken: () => token?.accessToken,
    refresh: refreshAccessToken
  });

  async function login(username, password) {
    const operationGeneration = ++sessionGeneration;
    const value = await request('/api/v1/auth/login', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ username, password })
    }, undefined, { retryUnauthorized: false });
    if (!isTokenResponse(value)) {
      throw new TypeError('登录响应不符合 TokenResponse 契约。');
    }

    if (operationGeneration !== sessionGeneration) {
      return;
    }

    token = value;
    try {
      if (!await loadAuthenticatedSnapshot(operationGeneration)) {
        return;
      }

      state = 'authenticated';
      notify();
    } catch (error) {
      if (operationGeneration !== sessionGeneration) {
        return;
      }

      clear();
      throw error;
    }
  }

  async function restore() {
    const operationGeneration = sessionGeneration;
    state = 'initializing';
    notify();
    if (!await refreshAccessToken(operationGeneration)) {
      return false;
    }

    try {
      if (!await loadAuthenticatedSnapshot(operationGeneration)) {
        return false;
      }

      state = 'authenticated';
      notify();
      return true;
    } catch {
      if (operationGeneration === sessionGeneration) {
        clear();
      }

      return false;
    }
  }

  async function refreshAccessToken(operationGeneration = sessionGeneration) {
    try {
      const value = await request('/api/v1/auth/refresh', {
        method: 'POST',
        headers: csrfHeaders()
      }, undefined, { retryUnauthorized: false });
      if (operationGeneration !== sessionGeneration) {
        return false;
      }

      if (!isTokenResponse(value)) {
        clear();
        return false;
      }

      token = value;
      return true;
    } catch {
      if (operationGeneration === sessionGeneration) {
        clear();
      }

      return false;
    }
  }

  async function switchTenant(tenantId) {
    if (switching) {
      return;
    }

    const operationGeneration = sessionGeneration;
    switching = true;
    notify();
    try {
      try {
        await changeTenantContext(tenantId, operationGeneration);
      } catch (error) {
        if (operationGeneration !== sessionGeneration) {
          return;
        }

        if (!isFullNetProblemDetails(error)
          || error.code !== contextConflictCode
          || !await refreshAccessToken(operationGeneration)) {
          throw error;
        }

        // 并发冲突只使用最新刷新会话重试一次，防止循环覆盖服务端较新的上下文。
        try {
          await changeTenantContext(tenantId, operationGeneration);
        } catch (retryError) {
          if (operationGeneration !== sessionGeneration) {
            return;
          }

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

  async function changeTenantContext(tenantId, operationGeneration) {
    const value = await request('/api/v1/tenancy/context', {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ tenantId })
    });
    if (!isTenantContextTokenResponse(value)) {
      throw new TypeError('租户上下文响应不符合契约。');
    }

    if (operationGeneration !== sessionGeneration) {
      return;
    }

    token = value;
    try {
      if (!await loadAuthenticatedSnapshot(operationGeneration)) {
        return;
      }

      state = 'authenticated';
      notify();
    } catch (error) {
      if (operationGeneration !== sessionGeneration) {
        return;
      }

      // 服务端已切换时不得继续使用旧授权快照；清空状态可阻断错误范围的后续请求。
      clear();
      throw error;
    }
  }

  async function logout() {
    const headers = csrfHeaders();
    // Logout 立即推进代际，避免在途 Refresh 或上下文切换重新写回旧令牌。
    clear();
    try {
      await request('/api/v1/auth/logout', {
        method: 'POST',
        headers
      }, undefined, { retryUnauthorized: false });
    } catch {
      // 本地清理不依赖网络成功，服务端仍由会话过期与重用检测兜底。
    }
  }

  async function loadAuthenticatedSnapshot(operationGeneration) {
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

    if (operationGeneration !== sessionGeneration) {
      return false;
    }

    // 所有不可信响应都通过守卫后再原子替换，避免新旧授权数据混杂。
    currentUser = userValue;
    navigation = navigationValue;
    availableTenants = tenantValues;
    return true;
  }

  function can(permission) {
    return currentUser?.permissions.includes(permission) === true;
  }

  function clear() {
    sessionGeneration++;
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
    sessionGeneration++;
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
