import { computed, ref } from 'vue';
import { defineStore } from 'pinia';
import {
  isCurrentUserResponse,
  isFullNetProblemDetails,
  isLocalePreferenceResponse,
  isNavigationTree,
  isTenantContextSummaryArray,
  isTenantContextTokenResponse,
  isTokenResponse,
  type CurrentUserResponse,
  type NavigationNode,
  type TenantContextSummary,
  type TokenResponse
} from '@fullnet/client-contracts';
import type { SupportedLocale } from '@fullnet/admin-i18n';
import {
  configureAuthentication,
  configureRequestLocale,
  request
} from '../api/http';
import { useAdminI18n } from '../i18n/adminI18n';
import { isSupportedNavigationTree } from '../navigation/catalog';

export type SessionState = 'initializing' | 'authenticated' | 'anonymous';

const readTenantsPermission = 'tenancy.tenants.read';
const contextConflictCode = 'identity.session_context_conflict';

export const useSessionStore = defineStore('identity-session', () => {
  const state = ref<SessionState>('initializing');
  const currentUser = ref<CurrentUserResponse>();
  const navigation = ref<NavigationNode[]>([]);
  const availableTenants = ref<TenantContextSummary[]>([]);
  const switching = ref(false);
  const savingLocale = ref(false);
  const adminI18n = useAdminI18n();
  let token: TokenResponse | undefined;
  let sessionGeneration = 0;

  configureAuthentication({
    getAccessToken: () => token?.accessToken,
    refresh: refreshAccessToken
  });
  configureRequestLocale(() => adminI18n.locale.value);

  const isAuthenticated = computed(() => state.value === 'authenticated');
  const currentContextName = computed(() => {
    const tenantId = currentUser.value?.tenantId;
    if (!tenantId) {
      return 'Full.NET Host';
    }

    return availableTenants.value.find(tenant => tenant.id === tenantId)?.name
      ?? currentUser.value?.scope
      ?? '未知租户';
  });

  function acceptToken(value: TokenResponse): void {
    token = value;
  }

  function can(permission: string): boolean {
    return currentUser.value?.permissions.includes(permission) === true;
  }

  async function login(username: string, password: string): Promise<void> {
    const operationGeneration = ++sessionGeneration;
    const value = await request<unknown>('/api/v1/auth/login', {
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

    acceptToken(value);
    try {
      if (!await loadAuthenticatedSnapshot(operationGeneration)) {
        return;
      }

      state.value = 'authenticated';
    } catch (error: unknown) {
      if (operationGeneration !== sessionGeneration) {
        return;
      }

      clear();
      throw error;
    }
  }

  async function restore(): Promise<void> {
    const operationGeneration = sessionGeneration;
    state.value = 'initializing';
    if (!await refreshAccessToken(operationGeneration)) {
      return;
    }

    try {
      if (!await loadAuthenticatedSnapshot(operationGeneration)) {
        return;
      }

      state.value = 'authenticated';
    } catch {
      if (operationGeneration === sessionGeneration) {
        clear();
      }
    }
  }

  async function refreshAccessToken(
    operationGeneration = sessionGeneration
  ): Promise<boolean> {
    try {
      const value = await request<unknown>('/api/v1/auth/refresh', {
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

      acceptToken(value);
      return true;
    } catch {
      if (operationGeneration === sessionGeneration) {
        clear();
      }

      return false;
    }
  }

  async function switchTenant(tenantId: string | null): Promise<void> {
    if (switching.value) {
      return;
    }

    const operationGeneration = sessionGeneration;
    switching.value = true;
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

        // 并发冲突只允许在刷新最新会话后重试一次，防止循环覆盖较新的上下文。
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
      switching.value = false;
    }
  }

  async function changeLocale(locale: SupportedLocale): Promise<void> {
    if (!isAuthenticated.value || currentUser.value === undefined) {
      adminI18n.setLocale(locale);
      return;
    }

    if (savingLocale.value) {
      return;
    }

    const operationGeneration = sessionGeneration;
    const profileVersion = currentUser.value.profileVersion;
    savingLocale.value = true;
    try {
      const value = await request<unknown>('/api/v1/me/locale', {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ locale, profileVersion })
      });
      if (!isLocalePreferenceResponse(value)) {
        throw new TypeError('语言偏好响应不符合契约。');
      }

      if (operationGeneration !== sessionGeneration
        || currentUser.value === undefined) {
        return;
      }

      // 使用当前快照保留可能并发完成的租户上下文，只替换资料偏好字段。
      currentUser.value = {
        ...currentUser.value,
        preferredLocale: value.preferredLocale,
        profileVersion: value.profileVersion
      };
      adminI18n.setLocale(value.preferredLocale);
    } finally {
      savingLocale.value = false;
    }
  }

  async function changeTenantContext(
    tenantId: string | null,
    operationGeneration: number
  ): Promise<void> {
    const value = await request<unknown>('/api/v1/tenancy/context', {
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

    acceptToken(value);
    try {
      if (!await loadAuthenticatedSnapshot(operationGeneration)) {
        return;
      }

      state.value = 'authenticated';
    } catch (error: unknown) {
      if (operationGeneration !== sessionGeneration) {
        return;
      }

      // 服务端已完成切换时不能退回旧 Token；清空状态可阻止旧导航继续发起请求。
      clear();
      throw error;
    }
  }

  async function logout(): Promise<void> {
    const headers = csrfHeaders();
    // 先推进代际并清空内存状态，所有较晚返回的旧异步操作都会失效。
    clear();
    try {
      await request<void>('/api/v1/auth/logout', {
        method: 'POST',
        headers
      }, undefined, { retryUnauthorized: false });
    } catch {
      // 本地状态清理不能依赖网络成功，服务端会话仍由过期和重用检测兜底。
    }
  }

  async function loadAuthenticatedSnapshot(
    operationGeneration: number
  ): Promise<boolean> {
    const userValue = await request<unknown>('/api/v1/me');
    if (!isCurrentUserResponse(userValue)) {
      throw new TypeError('当前用户响应不符合契约。');
    }

    const navigationValue = await request<unknown>('/api/v1/navigation');
    if (!isNavigationTree(navigationValue)
      || !isSupportedNavigationTree(navigationValue)) {
      throw new TypeError('导航响应不符合本地组件白名单。');
    }

    let tenantValues: TenantContextSummary[] = [];
    if (userValue.permissions.includes(readTenantsPermission)) {
      const tenantValue = await request<unknown>('/api/v1/tenancy/available');
      if (!isTenantContextSummaryArray(tenantValue)) {
        throw new TypeError('可用租户响应不符合契约。');
      }

      tenantValues = tenantValue;
    }

    if (operationGeneration !== sessionGeneration) {
      return false;
    }

    // 三类响应全部通过守卫后再原子替换，避免 UI 混用新旧授权快照。
    currentUser.value = userValue;
    navigation.value = navigationValue;
    availableTenants.value = tenantValues;
    adminI18n.setLocale(userValue.preferredLocale);
    return true;
  }

  function clear(): void {
    sessionGeneration++;
    token = undefined;
    currentUser.value = undefined;
    navigation.value = [];
    availableTenants.value = [];
    state.value = 'anonymous';
  }

  return {
    state,
    currentUser,
    navigation,
    availableTenants,
    switching,
    savingLocale,
    isAuthenticated,
    currentContextName,
    can,
    login,
    restore,
    switchTenant,
    changeLocale,
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
