<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import {
  ElButton,
  ElCheckbox,
  ElDialog,
  ElOption,
  ElSelect
} from 'element-plus';
import { ArrowDown, ArrowUp } from '@element-plus/icons-vue';
import { useAdminI18n } from '../../../i18n/adminI18n';

defineOptions({ name: 'ArtTableColumnEditor' });

export interface ArtTableColumnEditorItem {
  key: string;
  label: string;
  visible: boolean;
  fixed: 'left' | 'right' | null;
  disabled?: boolean;
}

const open = defineModel<boolean>('open', { default: false });
const props = defineProps<{
  columns: readonly ArtTableColumnEditorItem[];
  saving?: boolean;
  resetting?: boolean;
}>();

const emit = defineEmits<{
  save: [columns: ArtTableColumnEditorItem[]];
  reset: [];
}>();

const { t } = useAdminI18n();
const draft = ref<ArtTableColumnEditorItem[]>([]);

watch(
  () => [open.value, props.columns] as const,
  ([isOpen, columns]) => {
    if (!isOpen) {
      return;
    }

    draft.value = columns.map(column => ({ ...column }));
  },
  { deep: true }
);

const editableColumns = computed(() =>
  draft.value.filter(column => !column.disabled));

function moveColumn(index: number, direction: -1 | 1): void {
  const targetIndex = index + direction;
  if (targetIndex < 0 || targetIndex >= draft.value.length) {
    return;
  }

  const next = [...draft.value];
  const [item] = next.splice(index, 1);
  next.splice(targetIndex, 0, item);
  draft.value = next;
}

function updateFixed(key: string, fixed: 'left' | 'right' | null): void {
  draft.value = draft.value.map(column =>
    column.key === key ? { ...column, fixed } : column);
}

function updateVisible(key: string, visible: boolean): void {
  draft.value = draft.value.map(column =>
    column.key === key ? { ...column, visible } : column);
}

function handleSave(): void {
  emit('save', draft.value.map(column => ({ ...column })));
}

function handleReset(): void {
  emit('reset');
}
</script>

<template>
  <el-dialog
    v-model="open"
    :title="t('table.columnEditorTitle')"
    width="520px"
    destroy-on-close
  >
    <p class="art-column-editor__hint">{{ t('table.columnEditorHint') }}</p>
    <ul class="art-column-editor__list">
      <li
        v-for="(column, index) in draft"
        :key="column.key"
        class="art-column-editor__item"
      >
        <div class="art-column-editor__main">
          <el-checkbox
            :model-value="column.visible"
            :disabled="column.disabled"
            @update:model-value="(value) => updateVisible(column.key, Boolean(value))"
          >
            {{ column.label }}
          </el-checkbox>
          <el-select
            class="art-column-editor__fixed"
            :model-value="column.fixed"
            :disabled="column.disabled"
            size="small"
            @update:model-value="(value) => updateFixed(column.key, value)"
          >
            <el-option :label="t('table.fixedNone')" :value="null" />
            <el-option :label="t('table.fixedLeft')" value="left" />
            <el-option :label="t('table.fixedRight')" value="right" />
          </el-select>
        </div>
        <div class="art-column-editor__actions">
          <el-button
            :icon="ArrowUp"
            circle
            size="small"
            :disabled="column.disabled || index === 0"
            :aria-label="t('table.moveColumnUp')"
            @click="moveColumn(index, -1)"
          />
          <el-button
            :icon="ArrowDown"
            circle
            size="small"
            :disabled="column.disabled || index === draft.length - 1"
            :aria-label="t('table.moveColumnDown')"
            @click="moveColumn(index, 1)"
          />
        </div>
      </li>
    </ul>
    <p v-if="editableColumns.length === 0" class="art-column-editor__empty">
      {{ t('table.columnEditorEmpty') }}
    </p>
    <template #footer>
      <el-button :loading="resetting" @click="handleReset">
        {{ t('table.resetColumns') }}
      </el-button>
      <el-button @click="open = false">{{ t('users.cancel') }}</el-button>
      <el-button type="primary" :loading="saving" @click="handleSave">
        {{ t('users.confirm') }}
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.art-column-editor__hint {
  margin: 0 0 12px;
  color: var(--art-gray-600);
  font-size: 13px;
}

.art-column-editor__list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.art-column-editor__item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  padding: 8px 10px;
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
}

.art-column-editor__main {
  display: flex;
  flex: 1;
  align-items: center;
  gap: 12px;
  min-width: 0;
}

.art-column-editor__fixed {
  width: 120px;
}

.art-column-editor__actions {
  display: flex;
  gap: 4px;
}

.art-column-editor__empty {
  margin: 12px 0 0;
  color: var(--art-gray-600);
  font-size: 13px;
}
</style>
