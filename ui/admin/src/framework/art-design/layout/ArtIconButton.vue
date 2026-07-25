<script setup lang="ts">
import type { Component } from 'vue';

defineOptions({ name: 'ArtIconButton', inheritAttrs: false });

defineProps<{
  icon: Component;
  label: string;
}>();

const emit = defineEmits<{
  click: [event: MouseEvent];
}>();
</script>

<template>
  <button
    type="button"
    class="art-icon-button"
    :aria-label="label"
    v-bind="$attrs"
    @click="emit('click', $event)"
  >
    <component :is="icon" class="art-icon-button__icon" aria-hidden="true" />
    <slot />
  </button>
</template>

<style scoped>
.art-icon-button {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  min-width: 34px;
  height: 34px;
  margin: 0;
  padding: 0;
  border: 0;
  border-radius: 8px;
  background: transparent;
  color: var(--art-gray-700);
  font: inherit;
  line-height: 1;
  cursor: pointer;
  vertical-align: middle;
}

.art-icon-button:hover {
  background: var(--art-hover-color);
  color: var(--art-theme-color);
}

.art-icon-button:focus-visible {
  outline: 2px solid var(--el-color-primary);
  outline-offset: 2px;
}

.art-icon-button__icon {
  width: 18px;
  height: 18px;
}
</style>
