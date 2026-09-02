<script setup lang="ts">
import { computed } from 'vue';
import { useRoute } from 'vue-router';
import { ElMenu } from 'element-plus';
import type { ShellNavigationTreeItem } from '../adapters/fullNetShellAdapter';
import ArtSidebarSubmenu from './ArtSidebarSubmenu.vue';

defineOptions({ name: 'ArtSidebar' });

const props = withDefaults(defineProps<{
  navigation: ShellNavigationTreeItem[];
  brandTitle: string;
  systemName: string;
  menuCollapsed: boolean;
  menuStyle: 'design' | 'light' | 'dark';
  mainNavigationLabel: string;
  uniqueOpened: boolean;
  defaultOpeneds: string[];
  showBrand?: boolean;
}>(), {
  showBrand: true
});

const route = useRoute();
const activePath = computed(() => route.path);
const sidebarClass = computed(() => {
  if (props.menuStyle === 'design') {
    return 'art-sidebar--design';
  }

  if (props.menuStyle === 'dark') {
    return 'art-sidebar--dark';
  }

  return 'art-sidebar--light';
});
const menuPopperClass = computed(() =>
  `art-sidebar-menu-popper art-sidebar-menu-popper--${props.menuStyle}`
);
</script>

<template>
  <nav
    class="art-sidebar"
    :class="[{ 'is-collapsed': menuCollapsed }, sidebarClass]"
    :aria-label="mainNavigationLabel"
  >
    <router-link
      v-if="showBrand"
      class="art-sidebar__brand"
      to="/"
      :aria-label="systemName"
      :title="brandTitle"
    >
      <span class="art-sidebar__logo" aria-hidden="true">F</span>
      <p v-show="!menuCollapsed">{{ systemName }}</p>
    </router-link>

    <div class="art-sidebar__menu-scroll" tabindex="0">
      <ElMenu
        class="art-sidebar__menu"
        :class="`art-sidebar__menu--${menuStyle}`"
        :collapse="menuCollapsed"
        :default-active="activePath"
        :unique-opened="uniqueOpened"
        :default-openeds="defaultOpeneds"
        :popper-class="menuPopperClass"
        :aria-label="mainNavigationLabel"
      >
        <ArtSidebarSubmenu :items="navigation" />
      </ElMenu>
    </div>
  </nav>
</template>

<style scoped>
.art-sidebar {
  display: flex;
  flex-direction: column;
  width: var(--art-menu-open-width);
  height: 100vh;
  border-right: 1px solid var(--art-card-border);
  background: var(--art-default-box-color);
  transition: width 0.25s ease;
}

.art-sidebar.is-collapsed {
  width: var(--art-menu-close-width);
}

.art-sidebar__brand {
  display: flex;
  align-items: center;
  height: var(--art-header-height);
  padding: 0 16px;
  color: var(--art-gray-800);
  text-decoration: none;
  cursor: pointer;
}

.art-sidebar__logo {
  display: grid;
  width: 36px;
  height: 36px;
  flex-shrink: 0;
  place-items: center;
  border-radius: 10px;
  background: linear-gradient(135deg, var(--art-theme-color), #79bbff);
  color: #fff;
  font-family: var(--fullnet-font-display);
  font-size: 18px;
  font-weight: 800;
}

.art-sidebar__brand p {
  margin: 0 0 0 10px;
  overflow: hidden;
  font-size: 18px;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.art-sidebar.is-collapsed .art-sidebar__brand {
  justify-content: center;
  padding-inline: 0;
}

.art-sidebar__menu-scroll {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}

.art-sidebar__menu {
  border-right: 0;
  background: transparent;
}

.art-sidebar__menu:not(.el-menu--collapse) {
  width: 100%;
}

.art-sidebar.art-sidebar--design {
  border-right-color: rgb(255 255 255 / 8%);
  background: linear-gradient(180deg, #1d2b45 0%, #243552 100%);
}

.art-sidebar.art-sidebar--design .art-sidebar__brand {
  color: rgb(255 255 255 / 88%);
}

.art-sidebar.art-sidebar--dark {
  border-right-color: rgb(255 255 255 / 8%);
  background: #141414;
}

.art-sidebar.art-sidebar--dark .art-sidebar__brand {
  color: rgb(255 255 255 / 78%);
}

:deep(.art-sidebar__icon) {
  width: 18px;
  height: 18px;
  flex-shrink: 0;
  margin-right: 10px;
}

:deep(.art-sidebar__text) {
  overflow: hidden;
  font-size: 14px;
  text-overflow: ellipsis;
  white-space: nowrap;
}

:deep(.el-menu-item),
:deep(.el-sub-menu__title) {
  height: 42px;
  line-height: 42px;
  margin: 4px 8px;
  border-radius: 6px;
}

:deep(.el-menu-item.is-active) {
  font-weight: 600;
}

.art-sidebar--design :deep(.el-menu),
.art-sidebar--design :deep(.el-menu-item),
.art-sidebar--design :deep(.el-sub-menu__title) {
  --el-menu-text-color: rgb(255 255 255 / 88%);
  --el-menu-hover-text-color: #ffffff;
  --el-menu-active-color: #ffffff;
  --el-menu-bg-color: transparent;
  --el-menu-hover-bg-color: rgb(255 255 255 / 12%);
}

.art-sidebar--dark :deep(.el-menu),
.art-sidebar--dark :deep(.el-menu-item),
.art-sidebar--dark :deep(.el-sub-menu__title) {
  --el-menu-text-color: rgb(255 255 255 / 78%);
  --el-menu-hover-text-color: #ffffff;
  --el-menu-active-color: #ffffff;
  --el-menu-bg-color: transparent;
  --el-menu-hover-bg-color: rgb(255 255 255 / 8%);
}

.art-sidebar--light :deep(.el-menu-item.is-active) {
  color: var(--art-theme-text);
  background: var(--art-active-color);
}
</style>
