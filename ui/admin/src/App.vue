<script setup lang="ts">
import {
  computed,
  defineAsyncComponent,
  nextTick,
  onMounted,
  onUnmounted,
  provide,
  ref,
  watch
} from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { ElConfigProvider } from 'element-plus';
import {
  isFullNetProblemDetails,
  resolveFullNetApiUrl,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import type { MessageKey } from '@fullnet/admin-i18n';
import LoginView from './views/LoginView.vue';
import { apiBaseUrl } from './api/http';
import { useSessionStore } from './auth/session';
import { createElementLocaleController } from './i18n/elementLocale';
import { useAdminI18n } from './i18n/adminI18n';
import ArtAdminShell from './framework/art-design/layout/ArtAdminShell.vue';
import { buildShellNavigation } from './framework/art-design/adapters/fullNetShellAdapter';
import { localNavigationFor } from './navigation/catalog';
import {
  createVueNotificationsRealtime,
  notificationsRealtimeKey
} from './notifications/realtime';
import './framework/art-design/theme/art-theme.css';
import './framework/art-design/theme/art-layout.css';
import './framework/art-design/theme/art-sidebar-menu.css';
import './framework/art-design/theme/art-menu-layouts.css';
import './framework/art-design/theme/art-settings-panel.css';
import './framework/art-design/auth/art-login.css';

const route = useRoute();
const router = useRouter();
const session = useSessionStore();
const notificationsRealtime = createVueNotificationsRealtime({
  session,
  enabled: import.meta.env.VITE_REALTIME_ENABLED !== 'false',
  hubPath: resolveFullNetApiUrl(apiBaseUrl, '/hubs/notifications')
});
provide(notificationsRealtimeKey, notificationsRealtime);
const pageCacheVersions = ref<Record<string, number>>({});

const pageComponentKey = computed(() => {
  const version = pageCacheVersions.value[route.path] ?? 0;
  return `${route.path}#${version}`;
});
const { locale, setLocale, setPageTitle, t } = useAdminI18n();
const elementLocaleController = createElementLocaleController({
  onFallback: setLocale
});
const elementLocale = elementLocaleController.locale;
const showComponentLocaleFixture = import.meta.env.DEV && new URLSearchParams(
  globalThis.location.search
).has('component-locale-fixture');
const ComponentLocaleFixture = import.meta.env.DEV
  ? defineAsyncComponent(() => import('./i18n/ComponentLocaleFixture.vue'))
  : undefined;
const contextProblem = ref<FullNetProblemDetails>();
const hostContextValue = '__fullnet_host__';
const statusPaths = new Set(['/403', '/404', '/500']);
const statusTitleKeys = new Map<string, MessageKey>([
  ['/403', 'status.403.title'],
  ['/404', 'status.404.title'],
  ['/500', 'status.500.title']
]);

onMounted(() => {
  if (session.state === 'initializing') {
    void session.restore();
    return;
  }

  if (session.isAuthenticated && session.navigation.length === 0) {
    void session.restore();
  }
});

watch(
  () => [session.isAuthenticated, session.navigation.length] as const,
  ([authenticated, navigationCount]) => {
    if (authenticated && navigationCount === 0) {
      void session.restore();
    }
  }
);

onUnmounted(() => {
  void notificationsRealtime.dispose();
});

const selectedContext = computed(() =>
  session.currentUser?.tenantId ?? hostContextValue
);
const roleLabel = computed(() => {
  if (session.currentUser?.isSuperAdministrator) {
    return t('shell.superAdministrator');
  }

  return session.currentUser?.scope === 'host'
    ? t('shell.hostAdmin')
    : session.currentUser?.username ?? '';
});
const shellLabels = computed(() => ({
  brandAria: t('shell.brandAria'),
  systemName: t('shell.systemName'),
  searchPlaceholder: t('shell.searchPlaceholder'),
  searchTitle: t('shell.searchTitle'),
  searchEmpty: t('shell.searchEmpty'),
  searchHint: t('shell.searchHint'),
  settingsTitle: t('shell.settingsTitle'),
  settingsThemeSection: t('shell.settingsThemeSection'),
  settingsClose: t('shell.settingsClose'),
  tenantSelectorLabel: t('shell.tenantSelector'),
  notificationsLabel: t('shell.notifications'),
  chatLabel: t('shell.chat'),
  languageLabel: t('shell.language'),
  noticeTitle: t('shell.noticeTitle'),
  noticeMarkReadLabel: t('shell.noticeMarkRead'),
  noticeViewAllLabel: t('shell.noticeViewAll'),
  noticeEmptyLabel: t('shell.noticeEmpty'),
  noticeTabNoticeLabel: t('shell.noticeTabNotice'),
  noticeTabMessageLabel: t('shell.noticeTabMessage'),
  noticeTabPendingLabel: t('shell.noticeTabPending'),
  chatTitle: t('shell.chatTitle'),
  chatOnlineLabel: t('shell.chatOnline'),
  chatOfflineLabel: t('shell.chatOffline'),
  chatInputPlaceholder: t('shell.chatInputPlaceholder'),
  chatSendLabel: t('shell.chatSend'),
  chatCloseLabel: t('shell.chatClose'),
  logoutLabel: t('shell.logout'),
  controlPlaneLabel: t('shell.controlPlane'),
  themeLightLabel: t('shell.themeLight'),
  themeDarkLabel: t('shell.themeDark'),
  mobileMenuLabel: t('shell.mobileMenu'),
  mainNavigationLabel: t('shell.mainNavigation'),
  pageTabsLabel: t('shell.pageTabs'),
  refreshLabel: t('shell.refresh'),
  fullscreenEnterLabel: t('shell.fullscreenEnter'),
  fullscreenExitLabel: t('shell.fullscreenExit'),
  collapseMenuLabel: t('shell.collapseMenu'),
  expandMenuLabel: t('shell.expandMenu')
}));

function refreshShellPage(): void {
  pageCacheVersions.value = {
    ...pageCacheVersions.value,
    [route.path]: (pageCacheVersions.value[route.path] ?? 0) + 1
  };
}
const activePageTitleKey = computed<MessageKey>(() => {
  const statusKey = statusTitleKeys.get(route.path);
  if (statusKey) {
    return statusKey;
  }

  const navigation = buildShellNavigation({
    navigation: session.navigation,
    translate: t
  });
  const active = navigation.find(item => item.path === route.path);
  const local = localNavigationFor(active?.componentKey ?? 'overview');
  return local?.titleKey ?? 'navigation.status.title';
});

async function switchFromSelector(value: string): Promise<void> {
  contextProblem.value = undefined;
  try {
    await session.switchTenant(value === hostContextValue ? null : value);
  } catch (error: unknown) {
    contextProblem.value = isFullNetProblemDetails(error)
      ? error
      : {
          status: 500,
          code: 'client.context_switch_failed',
          title: t('shell.contextSwitchFailed')
        };
  }
}

watch(
  () => locale.value,
  value => {
    void elementLocaleController.setLocale(value);
  },
  { immediate: true }
);

watch(
  () => [session.state, session.navigation, route.path] as const,
  () => {
    if (!session.isAuthenticated || statusPaths.has(route.path)) {
      return;
    }

    const allowed = buildShellNavigation({
      navigation: session.navigation,
      translate: t
    });
    if (!allowed.some(item => item.path === route.path)) {
      void router.replace(allowed[0]?.path ?? '/403');
    }
  },
  { deep: true }
);

watch(
  () => [route.path, locale.value, session.state] as const,
  () => {
    setPageTitle(activePageTitleKey.value);
  },
  { immediate: true }
);

watch(
  () => route.path,
  async () => {
    await nextTick();
    document.querySelector<HTMLElement>('[data-route-heading]')?.focus();
  }
);
</script>

<template>
  <el-config-provider
    :locale="elementLocale"
    :dialog="{ draggable: true }"
  >
    <div
      v-if="session.state === 'initializing'"
      class="session-boot"
      aria-live="polite"
    >
      <span>F</span>
      <strong>{{ t('session.restoring') }}</strong>
      <i />
    </div>
    <LoginView v-else-if="session.state === 'anonymous'" />
    <ArtAdminShell
      v-else
      :navigation-tree="session.navigation"
      :translate="t"
      :element-locale-name="elementLocale?.name"
      :selected-context="selectedContext"
      :host-context-value="hostContextValue"
      :can-read-tenants="session.can('tenancy.tenants.read')"
      :can-switch-tenant="session.can('tenancy.tenants.switch')"
      :switching="session.switching"
      :display-name="session.currentUser?.displayName ?? ''"
      :role-label="roleLabel"
      :available-tenants="session.availableTenants"
      :notification-unread-count="notificationsRealtime.unreadCount.value"
      :context-problem="contextProblem"
      :labels="shellLabels"
      @switch-tenant="switchFromSelector"
      @logout="session.logout"
      @refresh="refreshShellPage"
    >
      <router-view v-slot="{ Component }">
        <keep-alive :max="20">
          <component :is="Component" v-if="Component" :key="pageComponentKey" />
        </keep-alive>
      </router-view>
    </ArtAdminShell>
    <component
      v-if="showComponentLocaleFixture && session.state === 'authenticated'"
      :is="ComponentLocaleFixture"
    />
  </el-config-provider>
</template>

<style scoped>
.session-boot {
  display: grid;
  min-height: 100vh;
  place-content: center;
  justify-items: center;
  gap: 13px;
  background: #172027;
  color: #fff;
  font-family: var(--fullnet-font-display);
}

.session-boot span {
  display: grid;
  width: 46px;
  height: 46px;
  place-items: center;
  background: var(--fullnet-color-accent-bright);
  color: #172027;
  font-size: 20px;
  font-weight: 800;
}

.session-boot strong {
  font-size: 12px;
  letter-spacing: .1em;
}

.session-boot i {
  width: 120px;
  height: 2px;
  overflow: hidden;
  background: rgb(255 255 255 / 10%);
}

.session-boot i::after {
  display: block;
  width: 40%;
  height: 100%;
  animation: boot 1s infinite ease-in-out;
  background: var(--fullnet-color-accent-bright);
  content: "";
}

@keyframes boot {
  from { transform: translateX(-100%); }
  to { transform: translateX(350%); }
}
</style>
