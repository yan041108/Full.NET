<script setup lang="ts">
import { Close } from '@element-plus/icons-vue';
import type { ShellTabItem } from '../adapters/fullNetShellAdapter';

defineOptions({ name: 'ArtTabs' });

defineProps<{
  tabs: ShellTabItem[];
  activePath: string;
  tabStyle: 'default' | 'card' | 'google';
  tablistLabel: string;
  formatCloseTabLabel: (title: string) => string;
}>();

const emit = defineEmits<{
  activate: [path: string];
  close: [path: string];
}>();
</script>

<template>
  <div
    v-if="tabs.length > 0"
    class="art-tabs"
    :class="{
      'art-tabs--card': tabStyle === 'card',
      'art-tabs--google': tabStyle === 'google'
    }"
    role="tablist"
    :aria-label="tablistLabel"
  >
    <button
      v-for="tab in tabs"
      :key="tab.path"
      type="button"
      role="tab"
      class="art-tabs__item art-card-xs"
      :class="{ 'is-active': tab.path === activePath }"
      :aria-selected="tab.path === activePath"
      @click="emit('activate', tab.path)"
    >
      <span>{{ tab.title }}</span>
      <span
        v-if="tabs.length > 1"
        class="art-tabs__close"
        :title="formatCloseTabLabel(tab.title)"
        aria-hidden="true"
        @click.stop="emit('close', tab.path)"
      >
        <Close />
      </span>
    </button>
  </div>
</template>

<style scoped>
.art-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 8px 20px 12px;
  border-bottom: 1px solid var(--art-card-border);
  background: var(--art-default-box-color);
}

.art-tabs__item {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  height: 32px;
  padding: 0 12px;
  border: 1px solid transparent;
  background: transparent;
  color: var(--art-gray-600);
  font: inherit;
  font-size: 12px;
  cursor: pointer;
  transition: color 0.2s ease, background 0.2s ease, border-color 0.2s ease;
}

.art-tabs__item.is-active {
  color: var(--art-theme-text);
  background: var(--art-tab-active-bg);
  border-color: color-mix(in srgb, var(--art-theme-color) 25%, var(--art-card-border));
}

.art-tabs__item:hover {
  color: var(--art-theme-color);
}

.art-tabs__close {
  display: grid;
  width: 18px;
  height: 18px;
  place-items: center;
  border-radius: 50%;
}

.art-tabs__close:hover {
  background: var(--art-hover-color);
}

.art-tabs__close svg {
  width: 10px;
}
</style>
