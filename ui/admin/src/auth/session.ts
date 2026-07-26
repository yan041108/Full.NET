import { computed, ref, watch } from 'vue';
import { defineStore } from 'pinia';
import {
  createIdentitySession,
  type CurrentUserResponse,
  type IdentitySessionController,
  type IdentitySessionSnapshot,
  type NavigationNode,
  type SessionState,
  type TenantContextSummary
} from '@fullnet/client-contracts';
import type { SupportedLocale } from '@fullnet/admin-i18n';
import { http } from '../api/http';
import { useAdminI18n } from '../i18n/adminI18n';
import { isSupportedNavigationTree } from '../navigation/catalog';
import { sessionRefreshCoordinator } from './session-refresh-coordinator';

export type { SessionState };

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

  function getController(): IdentitySessionController {
    if (controller === undefined) {
      controller = createIdentitySession({
        http,
        i18n: {
          getLocale: () => adminI18n.locale.value,
          setLocale: locale => adminI18n.setLocale(locale)
        },
        isSupportedNavigationTree,
        sessionRefreshCoordinator
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

  function can(permission: string): boolean {
    return currentUser.value?.permissions.includes(permission) === true;
  }

  async function login(username: string, password: string): Promise<void> {
    await getController().login(username, password);
  }

  async function restore(): Promise<void> {
    await getController().restore();
  }

  async function switchTenant(tenantId: string | null): Promise<void> {
    await getController().switchTenant(tenantId);
  }

  async function changeLocale(locale: SupportedLocale): Promise<void> {
    await getController().changeLocale(locale);
  }

  async function logout(): Promise<void> {
    await getController().logout();
  }

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
    restore,
    switchTenant,
    changeLocale,
    logout,
    snapshot,
    subscribe,
    readAccessToken
  };
});
