<script setup lang="ts">
import { ref } from 'vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from './adminI18n';

withDefaults(defineProps<{
  id?: string;
  compact?: boolean;
}>(), {
  id: 'admin-locale',
  compact: false
});

const { locale, t } = useAdminI18n();
const session = useSessionStore();
const busy = ref(false);
const problem = ref('');

async function changeLocale(event: Event): Promise<void> {
  if (busy.value) {
    return;
  }

  const selector = event.currentTarget as HTMLSelectElement;
  const value = (event.currentTarget as HTMLSelectElement).value;
  if (value === 'zh-CN' || value === 'en-US') {
    problem.value = '';
    busy.value = true;
    try {
      await session.changeLocale(value);
    } catch {
      // 服务端未确认时恢复受信快照，不能把选择控件留在乐观状态。
      selector.value = locale.value;
      problem.value = t('locale.saveFailed');
    } finally {
      busy.value = false;
    }
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
      :disabled="busy"
      :aria-busy="busy ? 'true' : 'false'"
      @change="changeLocale"
    >
      <option value="zh-CN">{{ t('locale.zhCN') }}</option>
      <option value="en-US">{{ t('locale.enUS') }}</option>
    </select>
    <span v-if="busy" class="locale-selector__status" aria-live="polite">
      {{ t('locale.saving') }}
    </span>
    <span v-if="problem" class="locale-selector__problem" role="alert" aria-live="assertive">
      {{ problem }}
    </span>
  </div>
</template>

<style scoped>
.locale-selector { position: relative; display: flex; align-items: center; gap: 8px; color: var(--fullnet-color-ink-muted); font-size: 11px; }
.locale-selector label { margin: 0; color: inherit; font-size: inherit; font-weight: 700; }
.locale-selector select { min-height: 36px; padding: 0 28px 0 10px; border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-sm); background: var(--fullnet-color-panel); color: var(--fullnet-color-ink); font: inherit; }
.locale-selector select:focus-visible { outline: 3px solid var(--fullnet-color-accent-bright); outline-offset: 2px; }
.locale-selector--compact label { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
.locale-selector__status { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
.locale-selector__problem { position: absolute; top: calc(100% + 8px); right: 0; z-index: 30; width: max-content; max-width: min(320px, calc(100vw - 32px)); padding: 9px 12px; border: 1px solid #d8a09a; border-radius: var(--fullnet-radius-sm); background: #fff3f1; color: #8f2e25; box-shadow: 0 10px 28px rgb(83 31 25 / 14%); font-size: 12px; line-height: 1.5; }
</style>
