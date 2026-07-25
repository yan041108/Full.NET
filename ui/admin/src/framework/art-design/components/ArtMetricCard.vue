<script setup lang="ts">
import type { Component } from 'vue';

defineOptions({ name: 'ArtMetricCard' });

defineProps<{
  label: string;
  value: string;
  delta?: string;
  deltaPositive?: boolean;
  icon: Component;
}>();
</script>

<template>
  <article class="art-metric-card art-card">
    <span class="art-metric-card__label">{{ label }}</span>
    <strong class="art-metric-card__value">{{ value }}</strong>
    <div v-if="delta" class="art-metric-card__delta">
      <slot name="caption" />
      <span
        class="art-metric-card__delta-value"
        :class="deltaPositive === false ? 'is-down' : 'is-up'"
      >
        {{ delta }}
      </span>
    </div>
    <span class="art-metric-card__icon" aria-hidden="true">
      <component :is="icon" />
    </span>
  </article>
</template>
