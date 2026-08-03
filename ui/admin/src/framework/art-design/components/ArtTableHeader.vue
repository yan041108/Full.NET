<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import {
  DCaret,
  FullScreen,
  Grid,
  Refresh,
  ScaleToOriginal,
  Setting
} from '@element-plus/icons-vue';
import {
  ElCheckbox,
  ElDropdown,
  ElDropdownItem,
  ElDropdownMenu,
  ElIcon,
  ElPopover
} from 'element-plus';
import { useAdminI18n } from '../../../i18n/adminI18n';

defineOptions({ name: 'ArtTableHeader' });

export interface ArtTableColumnOption {
  key: string;
  label: string;
  visible?: boolean;
  disabled?: boolean;
}

type TableSize = 'large' | 'default' | 'small';

const props = withDefaults(defineProps<{
  loading?: boolean;
  layout?: string;
  fullClass?: string;
  showZebra?: boolean;
  showBorder?: boolean;
  showHeaderBackground?: boolean;
}>(), {
  layout: 'refresh,size,fullscreen,columns,settings',
  fullClass: 'users-view',
  showZebra: true,
  showBorder: true,
  showHeaderBackground: true
});

const columns = defineModel<ArtTableColumnOption[]>('columns', { default: () => [] });
const tableSize = defineModel<TableSize>('tableSize', { default: 'default' });
const zebra = defineModel<boolean>('zebra', { default: true });
const border = defineModel<boolean>('border', { default: true });
const headerBackground = defineModel<boolean>('headerBackground', { default: true });

const emit = defineEmits<{
  refresh: [];
}>();

const { t } = useAdminI18n();
const isManualRefresh = ref(false);
const isFullScreen = ref(false);
const originalOverflow = ref('');

const layoutItems = computed(() => props.layout.split(',').map((item) => item.trim()));

const tableSizeOptions = computed(() => [
  { value: 'small' as const, label: t('table.sizeSmall') },
  { value: 'default' as const, label: t('table.sizeDefault') },
  { value: 'large' as const, label: t('table.sizeLarge') }
]);

function shouldShow(componentName: string): boolean {
  return layoutItems.value.includes(componentName);
}

function getColumnVisibility(column: ArtTableColumnOption): boolean {
  return column.visible !== false;
}

function updateColumnVisibility(column: ArtTableColumnOption, value: boolean | string | number): void {
  const visible = Boolean(value);
  columns.value = columns.value.map((item) =>
    item.key === column.key ? { ...item, visible } : { ...item }
  );
}

function handleRefresh(): void {
  isManualRefresh.value = true;
  emit('refresh');
}

function handleTableSizeChange(command: TableSize): void {
  tableSize.value = command;
}

function toggleFullScreen(): void {
  const element = document.querySelector(`.${props.fullClass}`);
  if (!element) {
    return;
  }

  isFullScreen.value = !isFullScreen.value;

  if (isFullScreen.value) {
    originalOverflow.value = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    element.classList.add('el-full-screen');
    return;
  }

  document.body.style.overflow = originalOverflow.value;
  element.classList.remove('el-full-screen');
}

watch(
  () => props.loading,
  (loading) => {
    if (!loading) {
      isManualRefresh.value = false;
    }
  }
);

function handleEscapeKey(event: KeyboardEvent): void {
  if (event.key === 'Escape' && isFullScreen.value) {
    toggleFullScreen();
  }
}

onMounted(() => {
  document.addEventListener('keydown', handleEscapeKey);
});

onUnmounted(() => {
  document.removeEventListener('keydown', handleEscapeKey);

  if (!isFullScreen.value) {
    return;
  }

  document.body.style.overflow = originalOverflow.value;
  const element = document.querySelector(`.${props.fullClass}`);
  element?.classList.remove('el-full-screen');
});
</script>

<template>
  <div id="art-table-header" class="art-table-header">
    <div class="art-table-header__left">
      <slot name="left" />
    </div>
    <div class="art-table-header__right">
      <button
        v-if="shouldShow('refresh')"
        type="button"
        class="art-table-header__icon-btn"
        :class="{ 'is-loading': loading && isManualRefresh }"
        :aria-label="t('table.refresh')"
        @click="handleRefresh"
      >
        <ElIcon :class="{ 'is-loading': loading && isManualRefresh }">
          <Refresh />
        </ElIcon>
      </button>

      <ElDropdown
        v-if="shouldShow('size')"
        trigger="click"
        teleported
        popper-class="art-table-header-popper"
        @command="handleTableSizeChange"
      >
        <span
          class="art-table-header__icon-btn"
          role="button"
          tabindex="0"
          :aria-label="t('table.density')"
        >
          <ElIcon><DCaret /></ElIcon>
        </span>
        <template #dropdown>
          <ElDropdownMenu>
            <ElDropdownItem
              v-for="item in tableSizeOptions"
              :key="item.value"
              :command="item.value"
              :class="{ 'is-active': tableSize === item.value }"
            >
              {{ item.label }}
            </ElDropdownItem>
          </ElDropdownMenu>
        </template>
      </ElDropdown>

      <button
        v-if="shouldShow('fullscreen')"
        type="button"
        class="art-table-header__icon-btn"
        :aria-label="t('table.fullscreen')"
        @click="toggleFullScreen"
      >
        <ElIcon>
          <ScaleToOriginal v-if="isFullScreen" />
          <FullScreen v-else />
        </ElIcon>
      </button>

      <ElPopover
        v-if="shouldShow('columns') && columns.length > 0"
        placement="bottom-end"
        trigger="click"
        teleported
        popper-class="art-table-header-popper"
        :width="220"
      >
        <template #reference>
          <button type="button" class="art-table-header__icon-btn" :aria-label="t('table.columns')">
            <ElIcon><Grid /></ElIcon>
          </button>
        </template>
        <div class="art-table-header__column-list" @click.stop>
          <label
            v-for="item in columns"
            :key="item.key"
            class="art-table-header__column-item"
          >
            <ElCheckbox
              :model-value="getColumnVisibility(item)"
              :disabled="item.disabled"
              @update:model-value="(value) => updateColumnVisibility(item, value)"
            >
              {{ item.label }}
            </ElCheckbox>
          </label>
        </div>
      </ElPopover>

      <ElPopover
        v-if="shouldShow('settings')"
        placement="bottom-end"
        trigger="click"
        teleported
        popper-class="art-table-header-popper"
        :width="200"
      >
        <template #reference>
          <button type="button" class="art-table-header__icon-btn" :aria-label="t('table.settings')">
            <ElIcon><Setting /></ElIcon>
          </button>
        </template>
        <div class="art-table-header__settings" @click.stop>
          <ElCheckbox v-if="showZebra" v-model="zebra">
            {{ t('table.zebra') }}
          </ElCheckbox>
          <ElCheckbox v-if="showBorder" v-model="border">
            {{ t('table.border') }}
          </ElCheckbox>
          <ElCheckbox v-if="showHeaderBackground" v-model="headerBackground">
            {{ t('table.headerBackground') }}
          </ElCheckbox>
        </div>
      </ElPopover>

      <slot name="right" />
    </div>
  </div>
</template>

<style scoped>
.art-table-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 10px;
}

.art-table-header__left,
.art-table-header__right {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.art-table-header__right {
  margin-left: auto;
}

.art-table-header__icon-btn {
  display: inline-grid;
  width: 32px;
  height: 32px;
  place-items: center;
  border: 0;
  border-radius: 8px;
  background: rgb(148 163 184 / 18%);
  color: var(--art-gray-700);
  cursor: pointer;
  outline: none;
}

.art-table-header__icon-btn:hover {
  background: rgb(148 163 184 / 28%);
}

.art-table-header__icon-btn .is-loading {
  animation: art-table-refresh-spin 1s linear infinite;
}

.art-table-header__column-list,
.art-table-header__settings {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.art-table-header__column-item {
  display: block;
}

:deep(.el-dropdown-menu__item.is-active) {
  color: var(--art-theme-color);
  background: rgb(64 158 255 / 10%);
}

@keyframes art-table-refresh-spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
</style>
