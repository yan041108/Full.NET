<script setup lang="ts">
import {
  computed,
  onMounted,
  onUnmounted,
  ref,
  watch
} from 'vue';
import { useRoute, useRouter } from 'vue-router';
import type { FullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSidebar from './ArtSidebar.vue';
import ArtTopBar from './ArtTopBar.vue';
import ArtTabs from './ArtTabs.vue';
import ArtBreadcrumb from './ArtBreadcrumb.vue';
import ArtGlobalSearch from './ArtGlobalSearch.vue';
import ArtSettingsPanel from './ArtSettingsPanel.vue';
import ArtHorizontalMenu from './ArtHorizontalMenu.vue';
import ArtMixedMenu from './ArtMixedMenu.vue';
import ArtDualMenuRail from './ArtDualMenuRail.vue';
import {
  buildFlatShellNavigationTree,
  buildShellNavigation,
  buildShellNavigationGroups,
  buildShellNavigationTree,
  closeShellTab,
  closeShellTabs,
  resolveActiveGroupId,
  resolveDefaultOpenedMenuPaths,
  resolveNavigationBreadcrumb,
  type ShellTabCloseScope,
  type ShellTabItem,
  upsertShellTab
} from '../adapters/fullNetShellAdapter';
import { useArtShellPreferences } from '../composables/useArtShellPreferences';
import type { NavigationNode } from '@fullnet/client-contracts';
import type { MessageKey, MessageParameters } from '@fullnet/admin-i18n';

defineOptions({ name: 'ArtAdminShell' });

const props = defineProps<{
  navigationTree: NavigationNode[];
  translate: (key: MessageKey, parameters?: MessageParameters) => string;
  elementLocaleName?: string;
  selectedContext: string;
  hostContextValue: string;
  canReadTenants: boolean;
  canSwitchTenant: boolean;
  switching: boolean;
  displayName: string;
  roleLabel: string;
  availableTenants: Array<{ id: string; name: string }>;
  notificationUnreadCount: number;
  contextProblem?: FullNetProblemDetails;
  labels: {
    brandAria: string;
    systemName: string;
    searchPlaceholder: string;
    searchTitle: string;
    searchEmpty: string;
    searchHint: string;
    settingsTitle: string;
    settingsThemeSection: string;
    settingsClose: string;
    tenantSelectorLabel: string;
    notificationsLabel: string;
    chatLabel: string;
    languageLabel: string;
    noticeTitle: string;
    noticeMarkReadLabel: string;
    noticeViewAllLabel: string;
    noticeEmptyLabel: string;
    noticeTabNoticeLabel: string;
    noticeTabMessageLabel: string;
    noticeTabPendingLabel: string;
    chatTitle: string;
    chatOnlineLabel: string;
    chatOfflineLabel: string;
    chatInputPlaceholder: string;
    chatSendLabel: string;
    chatCloseLabel: string;
    logoutLabel: string;
    controlPlaneLabel: string;
    themeLightLabel: string;
    themeDarkLabel: string;
    mobileMenuLabel: string;
    mainNavigationLabel: string;
    pageTabsLabel: string;
    refreshLabel: string;
    fullscreenEnterLabel: string;
    fullscreenExitLabel: string;
    collapseMenuLabel: string;
    expandMenuLabel: string;
  };
}>();

const emit = defineEmits<{
  switchTenant: [value: string];
  logout: [];
  refresh: [];
}>();

const route = useRoute();
const router = useRouter();
const {
  settings,
  themeMode,
  menuCollapsed,
  toggleTheme,
  toggleMenuCollapsed
} = useArtShellPreferences();
const tabs = ref<ShellTabItem[]>([]);
const mobileNavOpen = ref(false);
const settingsOpen = ref(false);
const isMobileViewport = ref(false);
const activeMenuGroupId = ref('');
const globalSearchRef = ref<InstanceType<typeof ArtGlobalSearch>>();
const navigation = computed(() => buildShellNavigation({
  navigation: props.navigationTree,
  translate: props.translate
}));
const navigationGroups = computed(() => buildShellNavigationGroups({
  navigation: props.navigationTree,
  translate: props.translate
}));
const navigationTree = computed(() => buildShellNavigationTree({
  navigation: props.navigationTree,
  translate: props.translate
}));
const effectiveMenuLayout = computed(() =>
  isMobileViewport.value ? 'left' : settings.value.menuLayout
);
const showPrimarySidebar = computed(() =>
  effectiveMenuLayout.value === 'left'
    || effectiveMenuLayout.value === 'top-left'
    || effectiveMenuLayout.value === 'dual-menu'
);
const showDualRail = computed(() => effectiveMenuLayout.value === 'dual-menu');
const showHorizontalMenu = computed(() => effectiveMenuLayout.value === 'top');
const showMixedMenu = computed(() => effectiveMenuLayout.value === 'top-left');
const sidebarNavigationTree = computed(() => {
  if (effectiveMenuLayout.value === 'left') {
    return navigationTree.value;
  }

  const group = navigationGroups.value.find(
    item => item.id === activeMenuGroupId.value
  );
  if (group?.items.length) {
    return buildFlatShellNavigationTree(group.items);
  }

  return buildFlatShellNavigationTree(navigation.value);
});
const sidebarDefaultOpeneds = computed(() =>
  resolveDefaultOpenedMenuPaths(navigationTree.value, route.path)
);
const breadcrumbSegments = computed(() =>
  resolveNavigationBreadcrumb(
    navigationTree.value,
    route.path,
    props.labels.controlPlaneLabel
  )
);
const sidebarShowBrand = computed(() =>
  effectiveMenuLayout.value !== 'dual-menu'
);
const activeTitle = computed(() =>
  navigation.value.find(item => item.path === route.path)?.title
    ?? props.translate('navigation.status.title')
);
const sidebarCollapsed = computed(() =>
  isMobileViewport.value ? false : menuCollapsed.value
);
const menuButtonLabel = computed(() =>
  isMobileViewport.value
    ? props.labels.mobileMenuLabel
    : menuCollapsed.value
      ? props.labels.expandMenuLabel
      : props.labels.collapseMenuLabel
);

function syncTabs(): void {
  tabs.value = upsertShellTab(tabs.value, navigation.value, route.path);
}

function activateTab(path: string): void {
  void router.push(path);
}

function closeTab(path: string): void {
  const result = closeShellTab(tabs.value, path, route.path);
  tabs.value = result.tabs;
  if (result.nextPath !== route.path) {
    void router.push(result.nextPath);
  }
}

function closeTabs(scope: ShellTabCloseScope, path: string): void {
  const result = closeShellTabs(tabs.value, scope, path, route.path);
  tabs.value = result.tabs;
  if (result.nextPath !== route.path) {
    void router.push(result.nextPath);
  }
}

function openSearch(): void {
  globalSearchRef.value?.open();
}

function toggleMenu(): void {
  if (isMobileViewport.value) {
    mobileNavOpen.value = !mobileNavOpen.value;
    return;
  }

  toggleMenuCollapsed();
}

function closeMobileNav(): void {
  mobileNavOpen.value = false;
}

function focusMainContent(): void {
  document.getElementById('main-content')?.focus();
}

function syncActiveMenuGroup(path: string = route.path): void {
  activeMenuGroupId.value = resolveActiveGroupId(navigationGroups.value, path);
}

function selectMenuGroup(groupId: string): void {
  activeMenuGroupId.value = groupId;
  const group = navigationGroups.value.find(item => item.id === groupId);
  if (!group) {
    return;
  }

  if (!group.items.some(item => item.path === route.path)) {
    const nextPath = group.items[0]?.path;
    if (nextPath) {
      void router.push(nextPath);
    }
  }
}

function onDocumentKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape' && mobileNavOpen.value) {
    closeMobileNav();
  }
}

function updateViewport(): void {
  if (typeof window.matchMedia !== 'function') {
    isMobileViewport.value = false;
    return;
  }

  isMobileViewport.value = window.matchMedia('(max-width: 820px)').matches;
  if (!isMobileViewport.value) {
    mobileNavOpen.value = false;
  }
}

onMounted(() => {
  syncTabs();
  syncActiveMenuGroup();
  updateViewport();
  window.addEventListener('resize', updateViewport);
  window.addEventListener('keydown', onDocumentKeydown);
  if (typeof window.matchMedia === 'function') {
    window.matchMedia('(max-width: 820px)').addEventListener('change', updateViewport);
  }
});

onUnmounted(() => {
  window.removeEventListener('resize', updateViewport);
  window.removeEventListener('keydown', onDocumentKeydown);
  if (typeof window.matchMedia === 'function') {
    window.matchMedia('(max-width: 820px)').removeEventListener('change', updateViewport);
  }
});

watch(
  () => props.navigationTree,
  () => {
    syncTabs();
    syncActiveMenuGroup();
  },
  { deep: true }
);

watch(
  () => route.path,
  path => {
    syncTabs();
    syncActiveMenuGroup(path);
    closeMobileNav();
  }
);

watch(navigationGroups, () => {
  syncActiveMenuGroup();
});
</script>

<template>
  <div
    class="art-admin-shell"
    data-client-kind="vue"
    :class="`art-admin-shell--layout-${effectiveMenuLayout}`"
    :data-component-locale="elementLocaleName"
    :data-art-tab-style="settings.tabStyle"
  >
    <a
      class="skip-link"
      href="#main-content"
      @click.prevent="focusMainContent"
    >{{ props.translate('a11y.skipToMain') }}</a>

    <button
      v-if="mobileNavOpen"
      type="button"
      class="art-admin-shell__backdrop"
      :aria-label="labels.mobileMenuLabel"
      @click="closeMobileNav"
    />

    <ArtDualMenuRail
      v-if="showDualRail"
      :groups="navigationGroups"
      :active-group-id="activeMenuGroupId"
      :brand-title="labels.brandAria"
      :system-name="labels.systemName"
      :label="labels.mainNavigationLabel"
      @select-group="selectMenuGroup"
    />

    <div
      v-if="showPrimarySidebar"
      class="art-admin-shell__sidebar"
      :class="{ 'is-open': mobileNavOpen }"
    >
      <ArtSidebar
        :navigation="sidebarNavigationTree"
        :brand-title="labels.brandAria"
        :system-name="labels.systemName"
        :menu-collapsed="sidebarCollapsed"
        :menu-style="settings.menuStyle"
        :main-navigation-label="labels.mainNavigationLabel"
        :show-brand="sidebarShowBrand"
        :unique-opened="settings.uniqueOpened"
        :default-openeds="sidebarDefaultOpeneds"
      />
    </div>

    <div class="art-admin-shell__main">
      <div class="art-admin-shell__header">
        <ArtTopBar
          :search-placeholder="labels.searchPlaceholder"
          :refresh-label="labels.refreshLabel"
          :fullscreen-enter-label="labels.fullscreenEnterLabel"
          :fullscreen-exit-label="labels.fullscreenExitLabel"
          :menu-button-label="menuButtonLabel"
          :menu-collapsed="sidebarCollapsed"
          :tenant-selector-label="labels.tenantSelectorLabel"
          :notifications-label="labels.notificationsLabel"
          :notification-unread-count="notificationUnreadCount"
          :chat-label="labels.chatLabel"
          :language-label="labels.languageLabel"
          :notice-title="labels.noticeTitle"
          :notice-mark-read-label="labels.noticeMarkReadLabel"
          :notice-view-all-label="labels.noticeViewAllLabel"
          :notice-empty-label="labels.noticeEmptyLabel"
          :notice-tab-notice-label="labels.noticeTabNoticeLabel"
          :notice-tab-message-label="labels.noticeTabMessageLabel"
          :notice-tab-pending-label="labels.noticeTabPendingLabel"
          :chat-title="labels.chatTitle"
          :chat-online-label="labels.chatOnlineLabel"
          :chat-offline-label="labels.chatOfflineLabel"
          :chat-input-placeholder="labels.chatInputPlaceholder"
          :chat-send-label="labels.chatSendLabel"
          :chat-close-label="labels.chatCloseLabel"
          :settings-label="labels.settingsTitle"
          :logout-label="labels.logoutLabel"
          :theme-mode="themeMode"
          :theme-toggle-label="themeMode === 'dark' ? labels.themeLightLabel : labels.themeDarkLabel"
          :selected-context="selectedContext"
          :host-context-value="hostContextValue"
          :can-read-tenants="canReadTenants"
          :can-switch-tenant="canSwitchTenant"
          :switching="switching"
          :display-name="displayName"
          :role-label="roleLabel"
          :available-tenants="availableTenants"
          :show-menu-button="settings.showMenuButton || isMobileViewport"
          :show-refresh-button="settings.showRefreshButton"
          :show-breadcrumb="settings.showBreadcrumb"
          :show-fullscreen="settings.showFullscreen"
          :show-language="settings.showLanguage"
          @switch-tenant="value => emit('switchTenant', value)"
          @logout="emit('logout')"
          @toggle-theme="toggleTheme"
          @open-search="openSearch"
          @toggle-menu="toggleMenu"
          @refresh="emit('refresh')"
          @open-settings="settingsOpen = true"
        >
          <template #breadcrumb>
            <ArtBreadcrumb
              v-if="settings.showBreadcrumb"
              :segments="breadcrumbSegments"
            />
          </template>
          <template #menu>
            <ArtHorizontalMenu
              v-if="showHorizontalMenu"
              :navigation="navigation"
              :label="labels.mainNavigationLabel"
            />
            <ArtMixedMenu
              v-if="showMixedMenu"
              :groups="navigationGroups"
              :active-group-id="activeMenuGroupId"
              :label="labels.mainNavigationLabel"
              @select-group="selectMenuGroup"
            />
          </template>
          <template #tabs>
            <ArtTabs
              v-if="settings.showPageTabs"
              :tabs="tabs"
              :active-path="route.path"
              :tab-style="settings.tabStyle"
              :tablist-label="labels.pageTabsLabel"
              :format-close-tab-label="title => props.translate('shell.closeTab', { title })"
              :translate="props.translate"
              @activate="activateTab"
              @close="closeTab"
              @close-scope="closeTabs"
              @refresh="emit('refresh')"
            />
          </template>
        </ArtTopBar>
      </div>

      <div class="art-admin-shell__content art-layout-content">
        <div
          v-if="contextProblem"
          class="art-admin-shell__problem"
          role="alert"
        >
          <strong translate="no">{{ contextProblem.code }}</strong>
          <span>{{ contextProblem.title }}</span>
          <code v-if="contextProblem.traceId" translate="no">{{ contextProblem.traceId }}</code>
        </div>

        <main id="main-content" class="art-page-content" tabindex="-1">
          <slot />
        </main>
      </div>
    </div>

    <ArtGlobalSearch
      ref="globalSearchRef"
      :navigation="navigation"
      :title="labels.searchTitle"
      :placeholder="labels.searchPlaceholder"
      :empty-label="labels.searchEmpty"
      :hint-label="labels.searchHint"
    />

    <ArtSettingsPanel
      v-model:open="settingsOpen"
      :close-label="labels.settingsClose"
      :translate="translate"
    />
  </div>
</template>

<style scoped>
.art-admin-shell__backdrop {
  position: fixed;
  inset: 0;
  z-index: 40;
  border: 0;
  background: rgb(16 22 26 / 48%);
  cursor: pointer;
}

.art-admin-shell__problem {
  display: flex;
  align-items: center;
  gap: 12px;
  margin: 0 0 12px;
  padding: 11px 14px;
  border-left: 3px solid var(--fullnet-color-danger);
  background: rgb(201 74 74 / 8%);
  font-size: 11px;
}

.art-admin-shell__problem strong {
  color: var(--fullnet-color-danger);
}

.art-admin-shell__problem code {
  margin-left: auto;
  color: var(--fullnet-color-ink-muted);
}

@media (max-width: 820px) {
  .art-admin-shell__sidebar {
    position: fixed;
    top: 0;
    left: 0;
    z-index: 50;
    height: 100vh;
    transform: translateX(-105%);
    transition: transform var(--fullnet-motion-fast);
  }

  .art-admin-shell__sidebar.is-open {
    transform: translateX(0);
  }

  .art-admin-shell__main {
    width: 100%;
  }
}
</style>
