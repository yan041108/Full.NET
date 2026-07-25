<script setup lang="ts">
import type { ShellNavigationGroup } from '../adapters/fullNetShellAdapter';

defineOptions({ name: 'ArtDualMenuRail' });

defineProps<{
  groups: ShellNavigationGroup[];
  activeGroupId: string;
  brandTitle: string;
  systemName: string;
  label: string;
}>();

const emit = defineEmits<{
  selectGroup: [groupId: string];
}>();
</script>

<template>
  <aside class="art-dual-rail" :aria-label="label">
    <router-link
      class="art-dual-rail__brand"
      to="/"
      :aria-label="brandTitle"
      :title="systemName"
    >
      <span class="art-dual-rail__logo" aria-hidden="true">F</span>
    </router-link>

    <nav class="art-dual-rail__nav" :aria-label="label">
      <ul class="art-dual-rail__list">
        <li v-for="group in groups" :key="group.id">
          <button
            type="button"
            class="art-dual-rail__item"
            :class="{ 'is-active': activeGroupId === group.id }"
            :title="group.title"
            :aria-label="group.title"
            @click="emit('selectGroup', group.id)"
          >
            <component :is="group.icon" class="art-dual-rail__icon" aria-hidden="true" />
            <span class="art-dual-rail__text">{{ group.title }}</span>
          </button>
        </li>
      </ul>
    </nav>
  </aside>
</template>

<style scoped>
.art-dual-rail {
  display: flex;
  flex-direction: column;
  width: var(--art-dual-rail-width);
  height: 100vh;
  flex-shrink: 0;
  border-right: 1px solid var(--art-card-border);
  background: var(--art-default-box-color);
}

.art-dual-rail__brand {
  display: grid;
  place-items: center;
  height: var(--art-header-height);
  color: var(--art-gray-800);
  text-decoration: none;
}

.art-dual-rail__logo {
  display: grid;
  width: 36px;
  height: 36px;
  place-items: center;
  border-radius: 10px;
  background: linear-gradient(135deg, var(--art-theme-color), #79bbff);
  color: #fff;
  font-family: var(--fullnet-font-display);
  font-size: 18px;
  font-weight: 800;
}

.art-dual-rail__nav {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
}

.art-dual-rail__list {
  margin: 8px 0 16px;
  padding: 0 6px;
  list-style: none;
}

.art-dual-rail__item {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 4px;
  width: 100%;
  min-height: 56px;
  margin-bottom: 4px;
  padding: 8px 4px;
  border: 0;
  border-radius: 8px;
  background: transparent;
  color: var(--art-gray-700);
  font: inherit;
  cursor: pointer;
  transition: background 0.2s ease, color 0.2s ease;
}

.art-dual-rail__item:hover {
  background: var(--art-hover-color);
  color: var(--art-gray-800);
}

.art-dual-rail__item.is-active {
  color: var(--art-theme-text);
  background: var(--art-active-color);
  font-weight: 600;
}

.art-dual-rail__icon {
  width: 18px;
  height: 18px;
  flex-shrink: 0;
}

.art-dual-rail__text {
  max-width: 100%;
  overflow: hidden;
  font-size: 10px;
  line-height: 1.2;
  text-align: center;
  text-overflow: ellipsis;
  white-space: nowrap;
}

html:not([data-art-dual-menu-show-text='true']) .art-dual-rail__text {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
