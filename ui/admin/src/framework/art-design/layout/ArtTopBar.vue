<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import {
  Bell,
  ChatLineRound,
  Expand,
  Fold,
  FullScreen,
  Moon,
  Refresh,
  Search,
  Setting,
  Sunny
} from '@element-plus/icons-vue';
import ArtLocaleDropdown from './ArtLocaleDropdown.vue';
import ArtIconButton from './ArtIconButton.vue';
import ArtNotificationPanel from './ArtNotificationPanel.vue';
import ArtChatDrawer from './ArtChatDrawer.vue';
import ArtUserMenu from './ArtUserMenu.vue';

defineOptions({ name: 'ArtTopBar' });

const props = defineProps<{
  searchPlaceholder: string;
  refreshLabel: string;
  fullscreenEnterLabel: string;
  fullscreenExitLabel: string;
  menuButtonLabel: string;
  menuCollapsed: boolean;
  tenantSelectorLabel: string;
  notificationsLabel: string;
  notificationUnreadCount: number;
  chatLabel: string;
  languageLabel: string;
  logoutLabel: string;
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
  settingsLabel: string;
  themeMode: 'light' | 'dark';
  themeToggleLabel: string;
  selectedContext: string;
  hostContextValue: string;
  canReadTenants: boolean;
  canSwitchTenant: boolean;
  switching: boolean;
  displayName: string;
  roleLabel: string;
  /** 当前 Host/租户上下文显示名，供真实栈 E2E 与读屏稳定读取。 */
  currentContextName: string;
  availableTenants: Array<{ id: string; name: string }>;
  showMenuButton: boolean;
  showRefreshButton: boolean;
  showBreadcrumb: boolean;
  showFullscreen: boolean;
  showLanguage: boolean;
}>();

const emit = defineEmits<{
  switchTenant: [value: string];
  logout: [];
  toggleTheme: [];
  openSearch: [];
  toggleMenu: [];
  refresh: [];
  openSettings: [];
}>();

const isFullscreen = ref(false);
const noticeOpen = ref(false);
const chatOpen = ref(false);
const isWindows = typeof navigator !== 'undefined' && navigator.userAgent.includes('Windows');

const menuIcon = computed(() => props.menuCollapsed ? Expand : Fold);
const fullscreenLabel = computed(() =>
  isFullscreen.value ? props.fullscreenExitLabel : props.fullscreenEnterLabel
);
const fullscreenIcon = computed(() => FullScreen);
const fullscreenButtonClass = computed(() =>
  isFullscreen.value ? 'art-header__exit-fullscreen-btn' : 'art-header__fullscreen-btn'
);
const notificationButtonLabel = computed(() =>
  props.notificationUnreadCount > 0
    ? `${props.notificationsLabel} (${props.notificationUnreadCount})`
    : props.notificationsLabel
);
const notificationBadge = computed(() =>
  props.notificationUnreadCount > 99 ? '99+' : String(props.notificationUnreadCount)
);

function toggleFullscreen(): void {
  if (!document.fullscreenElement) {
    void document.documentElement.requestFullscreen();
    return;
  }

  void document.exitFullscreen();
}

function onFullscreenChange(): void {
  isFullscreen.value = Boolean(document.fullscreenElement);
}

function toggleNotice(): void {
  noticeOpen.value = !noticeOpen.value;
  if (noticeOpen.value) {
    chatOpen.value = false;
  }
}

function openChat(): void {
  chatOpen.value = true;
  noticeOpen.value = false;
}

function onDocumentClick(event: MouseEvent): void {
  if (!noticeOpen.value) {
    return;
  }

  const target = event.target as HTMLElement;
  if (target.closest('.art-header__notice-btn') || target.closest('.art-notification-panel')) {
    return;
  }

  noticeOpen.value = false;
}

onMounted(() => {
  document.addEventListener('fullscreenchange', onFullscreenChange);
  document.addEventListener('click', onDocumentClick);
});

onUnmounted(() => {
  document.removeEventListener('fullscreenchange', onFullscreenChange);
  document.removeEventListener('click', onDocumentClick);
});
</script>

<template>
  <header class="art-header">
    <div class="art-header__bar">
      <div class="art-header__left">
        <ArtIconButton
          v-if="showMenuButton"
          class="art-header__menu-btn"
          :icon="menuIcon"
          :label="menuButtonLabel"
          @click="emit('toggleMenu')"
        />
        <ArtIconButton
          v-if="showRefreshButton"
          class="art-header__refresh-btn"
          :icon="Refresh"
          :label="refreshLabel"
          @click="emit('refresh')"
        />
        <slot name="breadcrumb" />
      </div>

      <div class="art-header__right">
        <button
          type="button"
          class="art-header__search"
          :aria-label="searchPlaceholder"
          @click="emit('openSearch')"
        >
          <Search aria-hidden="true" />
          <span>{{ searchPlaceholder }}</span>
          <kbd>
            <template v-if="isWindows">Ctrl</template>
            <template v-else>⌘</template>
            K
          </kbd>
        </button>

        <ArtIconButton
          v-if="showFullscreen"
          :class="fullscreenButtonClass"
          :icon="fullscreenIcon"
          :label="fullscreenLabel"
          @click="toggleFullscreen"
        />

        <ArtLocaleDropdown v-if="showLanguage" :label="languageLabel" />

        <ArtIconButton
          class="art-header__notice-btn"
          :icon="Bell"
          :label="notificationButtonLabel"
          @click.stop="toggleNotice"
        >
          <span
            v-if="notificationUnreadCount > 0"
            class="art-header__badge art-header__badge--danger"
            aria-hidden="true"
          >
            {{ notificationBadge }}
          </span>
        </ArtIconButton>

        <ArtIconButton
          class="art-header__chat-btn"
          :icon="ChatLineRound"
          :label="chatLabel"
          @click="openChat"
        >
          <span class="art-header__badge art-header__badge--success" aria-hidden="true" />
        </ArtIconButton>

        <ArtIconButton
          class="art-header__setting-btn"
          :icon="Setting"
          :label="settingsLabel"
          @click="emit('openSettings')"
        />

        <ArtIconButton
          class="art-header__theme-btn"
          :icon="themeMode === 'dark' ? Sunny : Moon"
          :label="themeToggleLabel"
          @click="emit('toggleTheme')"
        />

        <span
          class="art-header__current-context"
          data-current-context
          data-testid="shell-current-context"
          translate="no"
          aria-live="polite"
        >{{ currentContextName }}</span>

        <ArtUserMenu
          :display-name="displayName"
          :role-label="roleLabel"
          :logout-label="logoutLabel"
          :tenant-selector-label="tenantSelectorLabel"
          :selected-context="selectedContext"
          :host-context-value="hostContextValue"
          :can-read-tenants="canReadTenants"
          :can-switch-tenant="canSwitchTenant"
          :switching="switching"
          :available-tenants="availableTenants"
          @switch-tenant="value => emit('switchTenant', value)"
          @logout="emit('logout')"
        />
      </div>
    </div>

    <slot name="menu" />

    <slot name="tabs" />

    <ArtNotificationPanel
      v-model:open="noticeOpen"
      :title="noticeTitle"
      :mark-read-label="noticeMarkReadLabel"
      :view-all-label="noticeViewAllLabel"
      :empty-label="noticeEmptyLabel"
      :tab-notice-label="noticeTabNoticeLabel"
      :tab-message-label="noticeTabMessageLabel"
      :tab-pending-label="noticeTabPendingLabel"
    />

    <ArtChatDrawer
      v-model:open="chatOpen"
      :title="chatTitle"
      :online-label="chatOnlineLabel"
      :offline-label="chatOfflineLabel"
      :input-placeholder="chatInputPlaceholder"
      :send-label="chatSendLabel"
      :close-label="chatCloseLabel"
    />
  </header>
</template>

<style scoped>
.art-header {
  position: relative;
  display: flex;
  flex-direction: column;
  background: var(--art-default-box-color);
}

.art-header__bar {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  height: var(--art-header-height);
  padding: 0 20px;
  border-bottom: 1px solid var(--art-card-border);
}

.art-header__current-context {
  position: absolute;
  width: 1px;
  height: 1px;
  margin: -1px;
  padding: 0;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

.art-header__left,
.art-header__right {
  display: flex;
  align-items: center;
  min-width: 0;
}

.art-header__left {
  flex: 1;
  gap: 4px;
}

.art-header__right {
  gap: 10px;
  flex-shrink: 0;
}

.art-header__search {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  width: 160px;
  height: 36px;
  padding: 0 10px;
  border: 1px solid var(--art-card-border);
  border-radius: calc(var(--art-custom-radius) / 2);
  background: var(--art-default-box-color);
  color: var(--art-gray-700);
  font: inherit;
  font-size: 12px;
  cursor: pointer;
}

.art-header__search svg {
  width: 14px;
}

.art-header__search kbd {
  margin-left: auto;
  padding: 2px 6px;
  border: 1px solid var(--art-card-border);
  border-radius: 4px;
  background: var(--art-gray-200);
  color: var(--art-gray-700);
  font-size: 10px;
}

.art-header__badge {
  position: absolute;
  top: 2px;
  right: 0;
  min-width: 16px;
  height: 16px;
  padding: 0 4px;
  border-radius: 8px;
  color: #fff;
  font-size: 9px;
  font-weight: 700;
  line-height: 16px;
  text-align: center;
  pointer-events: none;
}

.art-header__badge--danger {
  background: var(--fullnet-color-danger);
}

.art-header__badge--success {
  background: var(--fullnet-color-success);
  animation: art-header-breathing 1.5s ease-in-out infinite;
}

@keyframes art-header-rotate180 {
  from { transform: rotate(0); }
  to { transform: rotate(180deg); }
}

@keyframes art-header-shake {
  0% { transform: rotate(0); }
  25% { transform: rotate(-5deg); }
  50% { transform: rotate(5deg); }
  75% { transform: rotate(-5deg); }
  100% { transform: rotate(0); }
}

@keyframes art-header-expand {
  0% { transform: scale(1); }
  50% { transform: scale(1.1); }
  100% { transform: scale(1); }
}

@keyframes art-header-shrink {
  0% { transform: scale(1); }
  50% { transform: scale(0.9); }
  100% { transform: scale(1); }
}

@keyframes art-header-move-up {
  0% { transform: translateY(0); }
  50% { transform: translateY(-3px); }
  100% { transform: translateY(0); }
}

@keyframes art-header-breathing {
  0% { opacity: 0.4; transform: scale(0.9); }
  50% { opacity: 1; transform: scale(1.1); }
  100% { opacity: 0.4; transform: scale(0.9); }
}

.art-header__refresh-btn:hover :deep(.art-icon-button__icon) {
  animation: art-header-rotate180 0.5s;
}

:deep(.art-header__language-btn:hover .art-icon-button__icon) {
  animation: art-header-move-up 0.4s;
}

:deep(.art-header__setting-btn:hover .art-icon-button__icon) {
  animation: art-header-rotate180 0.5s;
}

.art-header__fullscreen-btn:hover :deep(.art-icon-button__icon) {
  animation: art-header-expand 0.6s forwards;
}

.art-header__exit-fullscreen-btn:hover :deep(.art-icon-button__icon) {
  animation: art-header-shrink 0.6s forwards;
}

.art-header__notice-btn:hover :deep(.art-icon-button__icon),
.art-header__chat-btn:hover :deep(.art-icon-button__icon) {
  animation: art-header-shake 0.5s ease-in-out;
}

@media (max-width: 820px) {
  .art-header__bar {
    padding-inline: 14px;
  }

  .art-header__search,
  .art-header__fullscreen-btn,
  .art-header__exit-fullscreen-btn,
  .art-header__notice-btn,
  .art-header__chat-btn,
  .art-header__refresh-btn {
    display: none;
  }

  .art-header__left {
    position: relative;
    z-index: 2;
  }

  .art-header__right {
    gap: 6px;
  }
}

@media (prefers-reduced-motion: reduce) {
  .art-header__badge--success,
  .art-header__refresh-btn:hover :deep(.art-icon-button__icon),
  :deep(.art-header__language-btn:hover .art-icon-button__icon),
  :deep(.art-header__setting-btn:hover .art-icon-button__icon),
  .art-header__fullscreen-btn:hover :deep(.art-icon-button__icon),
  .art-header__exit-fullscreen-btn:hover :deep(.art-icon-button__icon),
  .art-header__notice-btn:hover :deep(.art-icon-button__icon),
  .art-header__chat-btn:hover :deep(.art-icon-button__icon) {
    animation: none;
  }
}
</style>
