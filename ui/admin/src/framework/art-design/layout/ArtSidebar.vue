<script setup lang="ts">
import { computed } from 'vue';
import { useRoute } from 'vue-router';
import type { ShellNavigationItem } from '../adapters/fullNetShellAdapter';

defineOptions({ name: 'ArtSidebar' });

const props = withDefaults(defineProps<{
  navigation: ShellNavigationItem[];
  brandTitle: string;
  systemName: string;
  menuCollapsed: boolean;
  menuStyle: 'design' | 'light' | 'dark';
  mainNavigationLabel: string;
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

  return '';
});
</script>

<template>
  <aside
    class="art-sidebar"
    :class="[{ 'is-collapsed': menuCollapsed }, sidebarClass]"
    :aria-label="mainNavigationLabel"
  >
    <router-link
      v-if="showBrand"
      class="art-sidebar__brand"
      to="/"
      :aria-label="brandTitle"
      :title="systemName"
    >
      <span class="art-sidebar__logo" aria-hidden="true">F</span>
      <p v-show="!menuCollapsed">{{ systemName }}</p>
    </router-link>

    <div class="art-sidebar__menu-scroll" tabindex="0">
      <nav :aria-label="mainNavigationLabel">
        <ul class="art-sidebar__menu">
          <li v-for="item in navigation" :key="item.path">
            <router-link
              :to="item.path"
              class="art-sidebar__link"
              :class="{ 'is-active': activePath === item.path }"
              :title="menuCollapsed ? item.title : undefined"
            >
              <component :is="item.icon" class="art-sidebar__icon" aria-hidden="true" />
              <span v-show="!menuCollapsed" class="art-sidebar__text">{{ item.title }}</span>
            </router-link>
          </li>
        </ul>
      </nav>
    </div>
  </aside>
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
  margin: 8px 0 16px;
  padding: 0 8px;
  list-style: none;
}

.art-sidebar__link {
  display: flex;
  align-items: center;
  width: calc(100% - 16px);
  min-height: 42px;
  margin: 4px 8px;
  padding: 0 12px;
  border-radius: 6px;
  color: var(--art-gray-700);
  text-decoration: none;
  transition: background 0.2s ease, color 0.2s ease;
}

.art-sidebar.is-collapsed .art-sidebar__link {
  justify-content: center;
  width: 42px;
  margin-inline: auto;
  padding: 0;
}

.art-sidebar__link:hover {
  background: var(--art-hover-color);
  color: var(--art-gray-800);
}

.art-sidebar__link.is-active {
  color: var(--art-theme-text);
  background: var(--art-active-color);
  font-weight: 600;
}

.art-sidebar__icon {
  width: 18px;
  height: 18px;
  flex-shrink: 0;
}

.art-sidebar__text {
  margin-left: 10px;
  overflow: hidden;
  font-size: 14px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
