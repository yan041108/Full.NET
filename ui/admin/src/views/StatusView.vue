<script setup lang="ts">
import { computed } from 'vue';
import type { MessageKey } from '@fullnet/admin-i18n';
import { useAdminI18n } from '../i18n/adminI18n';

const props = defineProps<{ code: '403' | '404' | '500' }>();
const { t } = useAdminI18n();
const titleKey = computed<MessageKey>(() => `status.${props.code}.title`);
const descriptionKey = computed<MessageKey>(() =>
  `status.${props.code}.description`
);
</script>

<template>
  <section class="status-view">
    <span translate="no">{{ code }}</span>
    <p class="status-view__kicker">{{ t('status.routeKicker') }}</p>
    <h1 data-route-heading tabindex="-1">{{ t(titleKey) }}</h1>
    <p>{{ t(descriptionKey) }}</p>
    <router-link to="/">{{ t('status.back') }}</router-link>
  </section>
</template>

<style scoped>
.status-view { display: grid; place-items: start; align-content: center; min-height: calc(100vh - 160px); padding: clamp(32px, 8vw, 96px); border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-md); background: radial-gradient(circle at 85% 15%, rgb(11 143 135 / 12%), transparent 32%), var(--fullnet-color-panel); box-shadow: var(--fullnet-shadow-panel); }
.status-view span { color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 68px; font-weight: 650; letter-spacing: -.06em; }
.status-view .status-view__kicker { margin: 8px 0 0; color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 10px; letter-spacing: .16em; }
.status-view h1 { margin: 8px 0; color: var(--fullnet-color-ink); font-family: var(--fullnet-font-display); font-size: 30px; }
.status-view p { max-width: 560px; color: var(--fullnet-color-ink-muted); line-height: 1.8; }
.status-view a { margin-top: 20px; padding: 11px 18px; border-radius: var(--fullnet-radius-sm); background: var(--fullnet-color-ink); color: #fff; text-decoration: none; }
</style>
