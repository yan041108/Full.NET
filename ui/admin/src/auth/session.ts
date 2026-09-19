import { computed, ref, watch } from 'vue';
import { defineStore } from 'pinia';
import {
  createIdentitySession,
  type CurrentUserResponse,
  type IdentitySessionController,
  type IdentitySessionSnapshot,
  type NavigationNode,
  type SessionState,
  type TenantContextSummary,
  type TokenResponse
} from '@fullnet/client-contracts';
import type { SupportedLocale } from '@fullnet/admin-i18n';
import { http } from '../api/http';
import { adminIdentityAuthMode } from '../config/identity-auth';
import { useAdminI18n } from '../i18n/adminI18n';
import { isSupportedNavigationTree } from '../navigation/catalog';
import {
  clearAdminOidcSessionCredentials,
  refreshAdminOidcAccessToken,
  revokeAdminOidcCenterSession
} from './oidc-center-login';
import { resolveAdminOidcClientId } from '../config/identity-auth';
import { writeOidcRefreshCredential } from './oidc-session-credentials';
import { sessionRefreshCoordinator } from './session-refresh-coordinator';
import { shouldLogoutOnSessionRevoke } from './sessionRevokePolicy';

export type { SessionState };

/** 统一封装管理端当前会话、导航目录、租户切换与语言切换状态。 */
export const useSessionStore = defineStore('identity-session', () => {
  const state = ref<SessionState>('initializing');
  const currentUser = ref<CurrentUserResponse>();
  const navigation = ref<NavigationNode[]>([]);
  const availableTenants = ref<TenantContextSummary[]>([]);
  const switching = ref(false);
  const savingLocale = ref(false);
  const currentContextName = ref('Full.NET Host');
  const adminI18n = useAdminI18n();
  let controller: IdentitySessionController | undefined;

  /** 延迟创建底层会话控制器，避免未使用会话能力时提前建立副作用订阅。 */
  function getController(): IdentitySessionController {
    if (controller === undefined) {
      controller = createIdentitySession({
        http,
        i18n: {
          getLocale: () => adminI18n.locale.value,
          setLocale: locale => adminI18n.setLocale(locale)
        },
        isSupportedNavigationTree,
        sessionRefreshCoordinator,
        externalRefreshAccessToken: adminIdentityAuthMode === 'oidc-center'
          ? async () => refreshAdminOidcAccessToken()
          : undefined,
        onTenantContextTokenResponse: adminIdentityAuthMode === 'oidc-center'
          ? response => {
              if (response.refreshToken !== undefined) {
                writeOidcRefreshCredential({
                  refreshToken: response.refreshToken,
                  clientId: resolveAdminOidcClientId()
                });
              }
            }
          : undefined
      });
      controller.subscribe(snapshot => {
        state.value = snapshot.state;
        currentUser.value = snapshot.currentUser;
        navigation.value = snapshot.navigation;
        availableTenants.value = snapshot.availableTenants;
        switching.value = snapshot.switching;
        savingLocale.value = snapshot.savingLocale;
        currentContextName.value = snapshot.currentContextName;
      });
    }

    return controller;
  }

  const isAuthenticated = computed(() => state.value === 'authenticated');

  /** 基于当前用户权限快照做失败关闭判断，缺少用户或权限时一律返回 false。 */
  function can(permission: string): boolean {
    return currentUser.value?.permissions.includes(permission) === true;
  }

  /** 使用用户名和密码启动登录流程，并由底层控制器负责刷新本地快照。 */
  async function login(username: string, password: string): Promise<void> {
    await getController().login(username, password);
  }

  /** 使用 OIDC 授权码流程兑换的访问令牌建立本地会话。 */
  async function completeOidcAuthorization(accessToken: TokenResponse): Promise<void> {
    await getController().completeOidcAuthorization(accessToken);
  }

  /** 从现有凭据恢复会话，用于应用启动或页面刷新后的状态重建。 */
  async function restore(): Promise<void> {
    await getController().restore();
  }

  /** 在已认证前提下重新加载当前上下文，确保导航与租户信息保持最新。 */
  async function reloadContext(): Promise<void> {
    await getController().reloadAuthenticatedContext();
  }

  /** 切换宿主或租户上下文，`null` 表示回到 Host 作用域。 */
  async function switchTenant(tenantId: string | null): Promise<void> {
    await getController().switchTenant(tenantId);
  }

  /** 持久化并应用用户语言偏好，保持服务端会话与前端界面语言一致。 */
  async function changeLocale(locale: SupportedLocale): Promise<void> {
    await getController().changeLocale(locale);
  }

  async function changePassword(
    currentPassword: string,
    newPassword: string
  ): Promise<void> {
    await getController().changePassword(currentPassword, newPassword);
  }

  /** 退出当前会话，并清空受认证状态保护的本地快照。 */
  async function logout(): Promise<void> {
    if (adminIdentityAuthMode === 'oidc-center') {
      try {
        await sessionRefreshCoordinator.runExclusive(async () => {
          try {
            // 闲置后的 access 可能过期；刷新与退出共用串行边界，避免 refresh 重放。
            const refreshed = await refreshAdminOidcAccessToken();
            const accessToken = refreshed?.accessToken ?? getController().readAccessToken();
            // 中心退出已覆盖应用会话，不能先撤销应用再使用已失效凭据退出中心。
            if (accessToken !== undefined) {
              await revokeAdminOidcCenterSession(accessToken);
            }
          } finally {
            // 在释放刷新锁前清理，防止排队恢复操作读到退出前的 refresh。
            clearAdminOidcSessionCredentials();
            getController().invalidateLocalSession();
          }
        });
      } catch {
        // 本地清理不依赖网络成功，服务端仍由 grant 撤销与会话过期兜底。
      }

      clearAdminOidcSessionCredentials();
      getController().invalidateLocalSession();
      return;
    }

    await getController().logout();
  }

  /** 在服务端已撤销当前会话时仅清理本地状态，不再调用 Logout 端点。 */
  function invalidateLocalSession(): void {
    if (adminIdentityAuthMode === 'oidc-center') {
      clearAdminOidcSessionCredentials();
    }

    getController().invalidateLocalSession();
  }

  /**
   * 处理实时会话撤销通知；匹配当前会话时清理本地状态（含 OIDC refresh token）。
   * @returns 是否已因本次通知清理本地会话。
   */
  function handleRemoteSessionRevoke(revokedSessionId: string | undefined): boolean {
    if (!shouldLogoutOnSessionRevoke(currentUser.value?.sessionId, revokedSessionId)) {
      return false;
    }

    invalidateLocalSession();
    return true;
  }

  /** 返回当前会话快照，供外部订阅者一次性读取一致视图。 */
  function snapshot(): IdentitySessionSnapshot {
    return {
      state: state.value,
      currentUser: currentUser.value,
      navigation: navigation.value,
      availableTenants: availableTenants.value,
      switching: switching.value,
      savingLocale: savingLocale.value,
      currentContextName: currentContextName.value
    };
  }

  /** 先推送当前快照，再在任一关键状态变化时持续通知订阅方。 */
  function subscribe(
    listener: Parameters<IdentitySessionController['subscribe']>[0]
  ): () => void {
    listener(snapshot());
    return watch(
      [
        state,
        currentUser,
        navigation,
        availableTenants,
        switching,
        savingLocale,
        currentContextName
      ],
      () => listener(snapshot()),
      { deep: true }
    );
  }

  /** 读取当前内存中的访问令牌，供 HTTP 层或测试代码按需透传。 */
  function readAccessToken(): string | undefined {
    return controller?.readAccessToken();
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
    completeOidcAuthorization,
    restore,
    reloadContext,
    switchTenant,
    changeLocale,
    changePassword,
    logout,
    invalidateLocalSession,
    handleRemoteSessionRevoke,
    snapshot,
    subscribe,
    readAccessToken
  };
});
