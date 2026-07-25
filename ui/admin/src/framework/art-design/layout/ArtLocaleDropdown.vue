<script setup lang="ts">
import { ref } from 'vue';
import { Check } from '@element-plus/icons-vue';
import { ElDropdown, ElDropdownItem, ElDropdownMenu } from 'element-plus';
import { useSessionStore } from '../../../auth/session';
import { useAdminI18n } from '../../../i18n/adminI18n';
import ArtTranslateIcon from '../icons/ArtTranslateIcon.vue';

defineOptions({ name: 'ArtLocaleDropdown' });

defineProps<{
  label: string;
}>();

const { locale, t } = useAdminI18n();
const session = useSessionStore();
const busy = ref(false);
const problem = ref('');

const options = [
  { value: 'zh-CN' as const, labelKey: 'locale.zhCN' as const },
  { value: 'en-US' as const, labelKey: 'locale.enUS' as const }
];

async function selectLocale(value: 'zh-CN' | 'en-US'): Promise<void> {
  if (busy.value || value === locale.value) {
    return;
  }

  problem.value = '';
  busy.value = true;
  try {
    await session.changeLocale(value);
  } catch {
    problem.value = t('locale.saveFailed');
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <div class="art-locale-dropdown">
    <el-dropdown
      trigger="click"
      popper-class="art-locale-dropdown__popper"
      :disabled="busy"
      @command="value => selectLocale(value as 'zh-CN' | 'en-US')"
    >
      <button
        type="button"
        class="art-icon-button art-header__language-btn"
        :aria-label="label"
        data-testid="shell-locale-trigger"
        :data-active-locale="locale"
        :aria-busy="busy ? 'true' : 'false'"
      >
        <ArtTranslateIcon class="art-icon-button__icon" aria-hidden="true" />
      </button>

      <template #dropdown>
        <el-dropdown-menu>
          <el-dropdown-item
            v-for="item in options"
            :key="item.value"
            :command="item.value"
            :class="{ 'is-selected': locale === item.value }"
          >
            <span>{{ t(item.labelKey) }}</span>
            <Check v-if="locale === item.value" aria-hidden="true" />
          </el-dropdown-item>
        </el-dropdown-menu>
      </template>
    </el-dropdown>

    <span v-if="busy" class="art-locale-dropdown__status" aria-live="polite">
      {{ t('locale.saving') }}
    </span>
    <span
      v-if="problem"
      class="art-locale-dropdown__problem"
      role="alert"
      aria-live="assertive"
    >
      {{ problem }}
    </span>
  </div>
</template>

<style scoped>
.art-locale-dropdown {
  position: relative;
  display: inline-flex;
  align-items: center;
}

.art-header__language-btn .art-icon-button__icon {
  width: 19px;
  height: 19px;
}

.art-locale-dropdown__status {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip-path: inset(50%);
  white-space: nowrap;
}

.art-locale-dropdown__problem {
  position: absolute;
  top: calc(100% + 8px);
  right: 0;
  z-index: 30;
  width: max-content;
  max-width: min(320px, calc(100vw - 32px));
  padding: 9px 12px;
  border: 1px solid #d8a09a;
  border-radius: var(--fullnet-radius-sm);
  background: #fff3f1;
  color: #8f2e25;
  box-shadow: 0 10px 28px rgb(83 31 25 / 14%);
  font-size: 12px;
  line-height: 1.5;
}
</style>

<style>
.art-locale-dropdown__popper .el-dropdown-menu__item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  min-width: 132px;
}

.art-locale-dropdown__popper .el-dropdown-menu__item.is-selected {
  color: var(--art-theme-color);
  font-weight: 600;
}

.art-locale-dropdown__popper .el-dropdown-menu__item svg {
  width: 14px;
  height: 14px;
}
</style>
