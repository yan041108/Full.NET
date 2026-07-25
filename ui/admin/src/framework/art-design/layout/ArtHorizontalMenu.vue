<script setup lang="ts">
import { computed } from 'vue';
import { useRoute } from 'vue-router';
import type { ShellNavigationItem } from '../adapters/fullNetShellAdapter';

defineOptions({ name: 'ArtHorizontalMenu' });

const props = defineProps<{
  navigation: ShellNavigationItem[];
  label: string;
}>();

const route = useRoute();
const activePath = computed(() => route.path);
</script>

<template>
  <nav class="art-horizontal-menu" :aria-label="label">
    <ul class="art-horizontal-menu__list">
      <li v-for="item in navigation" :key="item.path">
        <router-link
          :to="item.path"
          class="art-horizontal-menu__link"
          :class="{ 'is-active': activePath === item.path }"
        >
          <component :is="item.icon" class="art-horizontal-menu__icon" aria-hidden="true" />
          <span>{{ item.title }}</span>
        </router-link>
      </li>
    </ul>
  </nav>
</template>

<style scoped>
.art-horizontal-menu {
  border-bottom: 1px solid var(--art-card-border);
  background: var(--art-default-box-color);
}

.art-horizontal-menu__list {
  display: flex;
  align-items: center;
  gap: 4px;
  margin: 0;
  padding: 0 16px;
  overflow-x: auto;
  list-style: none;
}

.art-horizontal-menu__link {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-height: 44px;
  padding: 0 14px;
  border-radius: 6px;
  color: var(--art-gray-700);
  font-size: 14px;
  text-decoration: none;
  white-space: nowrap;
  transition: background 0.2s ease, color 0.2s ease;
}

.art-horizontal-menu__link:hover {
  background: var(--art-hover-color);
  color: var(--art-gray-800);
}

.art-horizontal-menu__link.is-active {
  color: var(--art-theme-text);
  background: var(--art-active-color);
  font-weight: 600;
}

.art-horizontal-menu__icon {
  width: 16px;
  height: 16px;
  flex-shrink: 0;
}
</style>
