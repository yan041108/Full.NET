<script setup lang="ts">
import { useAdminI18n } from './adminI18n';

withDefaults(defineProps<{
  id?: string;
  compact?: boolean;
}>(), {
  id: 'admin-locale',
  compact: false
});

const { locale, setLocale, t } = useAdminI18n();

function changeLocale(event: Event): void {
  const value = (event.currentTarget as HTMLSelectElement).value;
  if (value === 'zh-CN' || value === 'en-US') {
    setLocale(value);
  }
}
</script>

<template>
  <div class="locale-selector" :class="{ 'locale-selector--compact': compact }">
    <label :for="id">{{ t('locale.label') }}</label>
    <select
      :id="id"
      name="locale"
      :value="locale"
      @change="changeLocale"
    >
      <option value="zh-CN">{{ t('locale.zhCN') }}</option>
      <option value="en-US">{{ t('locale.enUS') }}</option>
    </select>
  </div>
</template>

<style scoped>
.locale-selector { display: flex; align-items: center; gap: 8px; color: var(--fullnet-color-ink-muted); font-size: 11px; }
.locale-selector label { margin: 0; color: inherit; font-size: inherit; font-weight: 700; }
.locale-selector select { min-height: 36px; padding: 0 28px 0 10px; border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-sm); background: var(--fullnet-color-panel); color: var(--fullnet-color-ink); font: inherit; }
.locale-selector select:focus-visible { outline: 3px solid var(--fullnet-color-accent-bright); outline-offset: 2px; }
.locale-selector--compact label { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
</style>
