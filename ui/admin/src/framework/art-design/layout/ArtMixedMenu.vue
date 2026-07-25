<script setup lang="ts">
import type { ShellNavigationGroup } from '../adapters/fullNetShellAdapter';

defineOptions({ name: 'ArtMixedMenu' });

defineProps<{
  groups: ShellNavigationGroup[];
  activeGroupId: string;
  label: string;
}>();

const emit = defineEmits<{
  selectGroup: [groupId: string];
}>();
</script>

<template>
  <nav class="art-mixed-menu" :aria-label="label">
    <ul class="art-mixed-menu__list">
      <li v-for="group in groups" :key="group.id">
        <button
          type="button"
          class="art-mixed-menu__item"
          :class="{ 'is-active': activeGroupId === group.id }"
          @click="emit('selectGroup', group.id)"
        >
          <component :is="group.icon" class="art-mixed-menu__icon" aria-hidden="true" />
          <span>{{ group.title }}</span>
        </button>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
.art-mixed-menu {
  border-bottom: 1px solid var(--art-card-border);
  background: var(--art-default-box-color);
}

.art-mixed-menu__list {
  display: flex;
  align-items: center;
  gap: 4px;
  margin: 0;
  padding: 0 16px;
  overflow-x: auto;
  list-style: none;
}

.art-mixed-menu__item {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-height: 44px;
  padding: 0 14px;
  border: 0;
  border-radius: 6px;
  background: transparent;
  color: var(--art-gray-700);
  font: inherit;
  font-size: 14px;
  white-space: nowrap;
  cursor: pointer;
  transition: background 0.2s ease, color 0.2s ease;
}

.art-mixed-menu__item:hover {
  background: var(--art-hover-color);
  color: var(--art-gray-800);
}

.art-mixed-menu__item.is-active {
  color: var(--art-theme-text);
  background: var(--art-active-color);
  font-weight: 600;
}

.art-mixed-menu__icon {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}
</style>
