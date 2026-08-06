<script setup lang="ts">
import {
  computed,
  onMounted,
  onUnmounted,
  ref
} from 'vue';
import { ArrowDown, Close } from '@element-plus/icons-vue';
import {
  ElDropdown,
  ElDropdownItem,
  ElDropdownMenu,
  ElIcon
} from 'element-plus';
import type { MessageKey } from '@fullnet/admin-i18n';
import type { ShellTabCloseScope, ShellTabItem } from '../adapters/fullNetShellAdapter';

defineOptions({ name: 'ArtTabs' });

const props = defineProps<{
  tabs: ShellTabItem[];
  activePath: string;
  tabStyle: 'default' | 'card' | 'google';
  tablistLabel: string;
  formatCloseTabLabel: (title: string) => string;
  translate: (key: MessageKey) => string;
}>();

const emit = defineEmits<{
  activate: [path: string];
  close: [path: string];
  closeScope: [scope: ShellTabCloseScope, path: string];
  refresh: [];
}>();

const scrollRef = ref<HTMLElement>();
const menuVisible = ref(false);
const menuPosition = ref({ x: 0, y: 0 });
const menuTargetPath = ref('');

const menuItems = computed(() => {
  const targetPath = menuTargetPath.value || props.activePath;
  const targetIndex = props.tabs.findIndex(tab => tab.path === targetPath);
  const isCurrentTab = targetPath === props.activePath;

  return [
    {
      scope: 'refresh' as const,
      label: props.translate('shell.tabMenu.refresh'),
      disabled: !isCurrentTab
    },
    {
      scope: 'current' as const,
      label: props.translate('shell.tabMenu.close'),
      disabled: props.tabs.length <= 1
    },
    {
      scope: 'left' as const,
      label: props.translate('shell.tabMenu.closeLeft'),
      disabled: targetIndex <= 0
    },
    {
      scope: 'right' as const,
      label: props.translate('shell.tabMenu.closeRight'),
      disabled: targetIndex < 0 || targetIndex >= props.tabs.length - 1
    },
    {
      scope: 'other' as const,
      label: props.translate('shell.tabMenu.closeOther'),
      disabled: props.tabs.length <= 1
    },
    {
      scope: 'all' as const,
      label: props.translate('shell.tabMenu.closeAll'),
      disabled: props.tabs.length <= 1
    }
  ];
});

function openMenu(event: MouseEvent, path: string): void {
  event.stopPropagation();
  menuTargetPath.value = path;
  menuPosition.value = {
    x: event.clientX,
    y: event.clientY
  };
  menuVisible.value = true;
}

function closeMenu(): void {
  menuVisible.value = false;
}

function handleMenuAction(scope: ShellTabCloseScope | 'refresh'): void {
  const targetPath = menuTargetPath.value || props.activePath;
  closeMenu();

  if (scope === 'refresh') {
    emit('refresh');
    return;
  }

  if (scope === 'current') {
    emit('close', targetPath);
    return;
  }

  emit('closeScope', scope, targetPath);
}

function handleDropdownVisibleChange(visible: boolean): void {
  if (visible) {
    menuTargetPath.value = props.activePath;
  }
}

function onDocumentClick(): void {
  closeMenu();
}

function onDocumentKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    closeMenu();
  }
}

onMounted(() => {
  document.addEventListener('click', onDocumentClick);
  document.addEventListener('keydown', onDocumentKeydown);
});

onUnmounted(() => {
  document.removeEventListener('click', onDocumentClick);
  document.removeEventListener('keydown', onDocumentKeydown);
});
</script>

<template>
  <div
    v-if="tabs.length > 0"
    class="art-tabs-bar"
  >
    <div ref="scrollRef" class="art-tabs-bar__scroll">
      <div
        class="art-tabs"
        role="tablist"
        :aria-label="tablistLabel"
      >
        <button
          v-for="tab in tabs"
          :key="tab.path"
          type="button"
          role="tab"
          class="art-tabs__item art-card-xs"
          :class="{
            'is-active': tab.path === activePath,
            'art-tabs__item--google': tabStyle === 'google'
          }"
          :aria-selected="tab.path === activePath"
          @click="emit('activate', tab.path)"
          @contextmenu.prevent="openMenu($event, tab.path)"
        >
          <ElIcon
            v-if="tab.icon"
            class="art-tabs__icon"
            :size="16"
            aria-hidden="true"
          >
            <component :is="tab.icon" />
          </ElIcon>
          <span class="art-tabs__title">{{ tab.title }}</span>
          <span
            v-if="tabs.length > 1"
            class="art-tabs__close"
            :title="formatCloseTabLabel(tab.title)"
            aria-hidden="true"
            @click.stop="emit('close', tab.path)"
          >
            <ElIcon :size="10"><Close /></ElIcon>
          </span>
          <span
            v-if="tabStyle === 'google'"
            class="art-tabs__divider"
            aria-hidden="true"
          />
        </button>
      </div>
    </div>

    <ElDropdown
      trigger="click"
      teleported
      popper-class="art-tabs-menu-popper"
      @command="handleMenuAction"
      @visible-change="handleDropdownVisibleChange"
    >
      <button
        type="button"
        class="art-tabs-bar__menu art-card-xs"
        :aria-label="translate('shell.tabMenu.more')"
        @click.stop
      >
        <ElIcon :size="18"><ArrowDown /></ElIcon>
      </button>
      <template #dropdown>
        <ElDropdownMenu>
          <ElDropdownItem
            v-for="item in menuItems"
            :key="item.scope"
            :command="item.scope"
            :disabled="item.disabled"
          >
            {{ item.label }}
          </ElDropdownItem>
        </ElDropdownMenu>
      </template>
    </ElDropdown>

    <Teleport to="body">
      <div
        v-if="menuVisible"
        class="art-tab-context-menu"
        :style="{ top: `${menuPosition.y}px`, left: `${menuPosition.x}px` }"
        role="menu"
        @click.stop
      >
        <button
          v-for="item in menuItems"
          :key="item.scope"
          type="button"
          class="art-tab-context-menu__item"
          role="menuitem"
          :disabled="item.disabled"
          @click="handleMenuAction(item.scope)"
        >
          {{ item.label }}
        </button>
      </div>
    </Teleport>
  </div>
</template>

<style scoped>
.art-tab-context-menu {
  position: fixed;
  z-index: 5000;
  min-width: 140px;
  padding: 6px;
  border: 1px solid var(--art-card-border);
  border-radius: 10px;
  background: var(--art-default-box-color);
  box-shadow: var(--art-shadow-soft);
}

.art-tab-context-menu__item {
  display: block;
  width: 100%;
  padding: 8px 10px;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: var(--art-gray-800);
  font: inherit;
  font-size: 12px;
  text-align: left;
  cursor: pointer;
}

.art-tab-context-menu__item:hover:not(:disabled) {
  background: var(--art-hover-color);
  color: var(--art-theme-color);
}

.art-tab-context-menu__item:disabled {
  color: var(--art-gray-500);
  cursor: not-allowed;
}
</style>
