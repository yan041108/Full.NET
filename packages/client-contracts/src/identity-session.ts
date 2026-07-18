import { readCsrfHeaders } from './csrf.js';
import type { HttpClient } from './http.js';
import type { SessionRefreshCoordinator } from './session-refresh-coordinator.js';
import {
  isCurrentUserResponse,
  isLocalePreferenceResponse,
  isTokenResponse,
  type CurrentUserResponse,
  type SupportedLocale,
  type TokenResponse
} from './identity.js';
import { isNavigationTree } from './authorization.js';
import type { NavigationNode } from './authorization.js';
import {
  isTenantContextSummaryArray,
  isTenantContextTokenResponse,
  type TenantContextSummary
} from './tenancy.js';
import { isFullNetProblemDetails } from './problem-details.js';

export type SessionState = 'initializing' | 'authenticated' | 'anonymous';

export interface IdentitySessionSnapshot {
  state: SessionState;
  currentUser?: CurrentUserResponse;
  navigation: NavigationNode[];
  availableTenants: TenantContextSummary[];
  switching: boolean;
  savingLocale: boolean;
  currentContextName: string;
}

export interface IdentitySessionController {
  login(username: string, password: string): Promise<void>;
  restore(): Promise<boolean>;
  switchTenant(tenantId: string | null): Promise<void>;
  changeLocale(locale: SupportedLocale): Promise<void>;
  logout(): Promise<void>;
  can(permission: string): boolean;
  snapshot(): IdentitySessionSnapshot;
  subscribe(listener: (snapshot: IdentitySessionSnapshot) => void): () => void;
  dispose(): void;
}

export interface IdentitySessionOptions {
  http: HttpClient;
  i18n: {
    getLocale: () => SupportedLocale;
    setLocale: (locale: SupportedLocale) => void;
  };
  isSupportedNavigationTree: (navigation: readonly NavigationNode[]) => boolean;
  sessionRefreshCoordinator?: SessionRefreshCoordinator;
}

const readTenantsPermission = 'tenancy.tenants.read';
const contextConflictCode = 'identity.session_context_conflict';

/**
 * 创建无框架身份会话状态机；Access Token 只保存在闭包内，不写入浏览器持久化存储。
 */
export function createIdentitySession(
  options: IdentitySessionOptions
): IdentitySessionController {
  const { http, i18n, isSupportedNavigationTree, sessionRefreshCoordinator } = options;
  let state: SessionState = 'initializing';
  let currentUser: CurrentUserResponse | undefined;
  let navigation: NavigationNode[] = [];
  let availableTenants: TenantContextSummary[] = [];
  let switching = false;
  let savingLocale = false;
  let token: TokenResponse | undefined;
  let sessionGeneration = 0;
  const listeners = new Set<(snapshot: IdentitySessionSnapshot) => void>();
  const unsubscribeCoordinator = sessionRefreshCoordinator?.subscribe(message => {
    if (message.sourceId === sessionRefreshCoordinator.tabId) {
      return;
    }

    if (message.type === 'session-cleared') {
      clearLocal();
      return;
    }

    if (message.type === 'refresh-complete' && message.success
      && state === 'authenticated') {
      void restore();
    }
  });

  http.configureAuthentication({
    getAccessToken: () => token?.accessToken,
    refresh: refreshAccessToken
  });
  http.configureRequestLocale(() => i18n.getLocale());

  async function login(username: string, password: string): Promise<void> {
    const operationGeneration = ++sessionGeneration;
    const value = await http.request<unknown>('/api/v1/auth/login', {
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
    } catch (error: unknown) {
      if (operationGeneration !== sessionGeneration) {
        return;
      }

      clear();
      throw error;
    }
  }

  async function restore(): Promise<boolean> {
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

  async function refreshAccessToken(
    operationGeneration = sessionGeneration
  ): Promise<boolean> {
    const execute = async (): Promise<boolean> => {
      try {
        const value = await http.request<unknown>('/api/v1/auth/refresh', {
          method: 'POST',
          headers: readCsrfHeaders()
        }, undefined, { retryUnauthorized: false });
        if (operationGeneration !== sessionGeneration) {
          return false;
        }

        if (!isTokenResponse(value)) {
          clearLocal();
          return false;
        }

        token = value;
        return true;
      } catch {
        if (operationGeneration === sessionGeneration) {
          clearLocal();
        }

        return false;
      }
    };

    if (sessionRefreshCoordinator === undefined) {
      return execute();
    }

    return sessionRefreshCoordinator.runExclusive(execute);
  }

  async function switchTenant(tenantId: string | null): Promise<void> {
    if (switching) {
      return;
    }

    const operationGeneration = sessionGeneration;
    switching = true;
    notify();
    try {
      try {
        await changeTenantContext(tenantId, operationGeneration);
      } catch (error: unknown) {
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
        } catch (retryError: unknown) {
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

  async function changeLocale(locale: SupportedLocale): Promise<void> {
    if (state !== 'authenticated' || currentUser === undefined) {
      i18n.setLocale(locale);
      return;
    }

    if (savingLocale) {
      return;
    }

    const operationGeneration = sessionGeneration;
    const profileVersion = currentUser.profileVersion;
    savingLocale = true;
    notify();
    try {
      const value = await http.request<unknown>('/api/v1/me/locale', {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ locale, profileVersion })
      });
      if (!isLocalePreferenceResponse(value)) {
        throw new TypeError('语言偏好响应不符合契约。');
      }

      if (operationGeneration !== sessionGeneration || currentUser === undefined) {
        return;
      }

      // 保留可能并发更新的租户上下文，只提交服务端确认的资料偏好字段。
      currentUser = {
        ...currentUser,
        preferredLocale: value.preferredLocale,
        profileVersion: value.profileVersion
      };
      i18n.setLocale(value.preferredLocale);
    } finally {
      savingLocale = false;
      notify();
    }
  }

  async function changeTenantContext(
    tenantId: string | null,
    operationGeneration: number
  ): Promise<void> {
    const value = await http.request<unknown>('/api/v1/tenancy/context', {
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
    } catch (error: unknown) {
      if (operationGeneration !== sessionGeneration) {
        return;
      }

      // 服务端已切换时不得继续使用旧授权快照；清空状态可阻断错误范围的后续请求。
      clear();
      throw error;
    }
  }

  async function logout(): Promise<void> {
    const headers = readCsrfHeaders();
    // Logout 立即推进代际，避免在途 Refresh 或上下文切换重新写回旧令牌。
    clearLocal();
    sessionRefreshCoordinator?.notifySessionCleared();
    try {
      await http.request<void>('/api/v1/auth/logout', {
        method: 'POST',
        headers
      }, undefined, { retryUnauthorized: false });
    } catch {
      // 本地清理不依赖网络成功，服务端仍由会话过期与重用检测兜底。
    }
  }

  async function loadAuthenticatedSnapshot(
    operationGeneration: number
  ): Promise<boolean> {
    const userValue = await http.request<unknown>('/api/v1/me');
    if (!isCurrentUserResponse(userValue)) {
      throw new TypeError('当前用户响应不符合契约。');
    }

    const navigationValue = await http.request<unknown>('/api/v1/navigation');
    if (!isNavigationTree(navigationValue)
      || !isSupportedNavigationTree(navigationValue)) {
      throw new TypeError('导航响应不符合本地组件白名单。');
    }

    let tenantValues: TenantContextSummary[] = [];
    if (userValue.permissions.includes(readTenantsPermission)) {
      const tenantValue = await http.request<unknown>('/api/v1/tenancy/available');
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
    i18n.setLocale(userValue.preferredLocale);
    return true;
  }

  function can(permission: string): boolean {
    return currentUser?.permissions.includes(permission) === true;
  }

  function clearLocal(): void {
    sessionGeneration++;
    token = undefined;
    currentUser = undefined;
    navigation = [];
    availableTenants = [];
    state = 'anonymous';
    notify();
  }

  function clear(): void {
    clearLocal();
  }

  function buildSnapshot(): IdentitySessionSnapshot {
    const activeTenant = availableTenants.find(
      tenant => tenant.id === currentUser?.tenantId
    );
    return {
      state,
      currentUser,
      navigation,
      availableTenants,
      switching,
      savingLocale,
      currentContextName: activeTenant?.name ?? (
        currentUser?.tenantId ? currentUser.scope : 'Full.NET Host'
      )
    };
  }

  function snapshot(): IdentitySessionSnapshot {
    return buildSnapshot();
  }

  function subscribe(
    listener: (value: IdentitySessionSnapshot) => void
  ): () => void {
    listeners.add(listener);
    listener(buildSnapshot());
    return () => listeners.delete(listener);
  }

  function notify(): void {
    const value = buildSnapshot();
    listeners.forEach(listener => listener(value));
  }

  function dispose(): void {
    sessionGeneration++;
    listeners.clear();
    unsubscribeCoordinator?.();
    token = undefined;
    http.configureAuthentication();
    http.configureRequestLocale();
  }

  return {
    login,
    restore,
    switchTenant,
    changeLocale,
    logout,
    can,
    snapshot,
    subscribe,
    dispose
  };
}
