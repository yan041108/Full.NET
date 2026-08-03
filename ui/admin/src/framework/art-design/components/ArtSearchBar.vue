<script setup lang="ts">
import { computed, ref } from 'vue';
import { ArrowDownBold, ArrowUpBold } from '@element-plus/icons-vue';
import {
  ElButton,
  ElCol,
  ElForm,
  ElFormItem,
  ElIcon,
  ElInput,
  ElOption,
  ElRadio,
  ElRadioGroup,
  ElRow,
  ElSelect
} from 'element-plus';

defineOptions({ name: 'ArtSearchBar' });

export interface ArtSearchBarItem {
  key: string;
  label: string;
  type?: 'input' | 'select' | 'radiogroup';
  placeholder?: string;
  span?: number;
  hidden?: boolean;
  options?: Array<{ label: string; value: string }>;
}

const props = withDefaults(defineProps<{
  items: ArtSearchBarItem[];
  span?: number;
  gutter?: number;
  labelWidth?: string;
  searchLabel?: string;
  resetLabel?: string;
  expandLabel?: string;
  collapseLabel?: string;
  showExpand?: boolean;
  /** 收起时默认展示的主查询字段数（一行四列场景传 4） */
  defaultVisibleCount?: number;
}>(), {
  span: 6,
  gutter: 12,
  labelWidth: '70px',
  searchLabel: '查询',
  resetLabel: '重置',
  expandLabel: '展开',
  collapseLabel: '收起',
  showExpand: true,
  defaultVisibleCount: 4
});

const emit = defineEmits<{
  search: [Record<string, string | undefined>];
  reset: [];
}>();

const model = defineModel<Record<string, string | undefined>>({ default: {} });
const isExpanded = ref(false);

const activeItems = computed(() => props.items.filter(item => !item.hidden));

const visibleItems = computed(() => {
  if (props.showExpand && !isExpanded.value) {
    return activeItems.value.slice(0, props.defaultVisibleCount);
  }
  return activeItems.value;
});

const shouldShowExpand = computed(() => {
  return props.showExpand && activeItems.value.length > props.defaultVisibleCount;
});

const fieldSpan = computed(() => {
  if (isExpanded.value) {
    return props.span;
  }

  const visibleCount = Math.min(props.defaultVisibleCount, activeItems.value.length);
  if (visibleCount === 4) {
    return 4;
  }

  return props.span;
});

const actionSpan = computed(() => {
  if (isExpanded.value) {
    return props.span;
  }

  const visibleCount = Math.min(props.defaultVisibleCount, activeItems.value.length);
  if (visibleCount === 4) {
    return 8;
  }

  return props.span;
});

function resolveItemSpan(item: ArtSearchBarItem): number {
  return item.span ?? fieldSpan.value;
}

function setField(key: string, value: string | undefined): void {
  if (!value) {
    delete model.value[key];
    return;
  }
  model.value[key] = value;
}

function handleSearch(): void {
  const output: Record<string, string | undefined> = {};
  for (const [key, value] of Object.entries(model.value)) {
    const trimmed = value?.trim();
    if (trimmed) {
      output[key] = trimmed;
    }
  }
  emit('search', output);
}

function handleReset(): void {
  for (const key of Object.keys(model.value)) {
    delete model.value[key];
  }
  emit('reset');
}
</script>

<template>
  <section
    class="art-search-bar art-card-xs"
    :class="{ 'is-expanded': isExpanded }"
  >
    <el-form :model="model" label-position="right" :label-width="labelWidth" @submit.prevent>
      <el-row :gutter="gutter">
        <el-col
          v-for="item in visibleItems"
          :key="item.key"
          :xs="24"
          :sm="12"
          :md="resolveItemSpan(item)"
          :lg="resolveItemSpan(item)"
          :xl="resolveItemSpan(item)"
        >
          <el-form-item :label="item.label" class="art-search-bar__field">
            <el-input
              v-if="item.type === 'input' || !item.type"
              :model-value="model[item.key] ?? ''"
              clearable
              :placeholder="item.placeholder"
              @update:model-value="setField(item.key, $event)"
              @keyup.enter="handleSearch"
            />
            <el-select
              v-else-if="item.type === 'select'"
              :model-value="model[item.key] ?? ''"
              clearable
              :placeholder="item.placeholder"
              @update:model-value="setField(item.key, $event || undefined)"
            >
              <el-option
                v-for="option in item.options ?? []"
                :key="option.value"
                :label="option.label"
                :value="option.value"
              />
            </el-select>
            <el-radio-group
              v-else-if="item.type === 'radiogroup'"
              :model-value="model[item.key] ?? ''"
              @update:model-value="setField(item.key, $event || undefined)"
            >
              <el-radio
                v-for="option in item.options ?? []"
                :key="option.value"
                :value="option.value"
              >
                {{ option.label }}
              </el-radio>
            </el-radio-group>
          </el-form-item>
        </el-col>

        <el-col
          :xs="24"
          :sm="12"
          :md="actionSpan"
          :lg="actionSpan"
          :xl="actionSpan"
          class="art-search-bar__action-column"
        >
          <div class="art-search-bar__actions">
            <div class="art-search-bar__buttons">
              <el-button @click="handleReset">{{ resetLabel }}</el-button>
              <el-button type="primary" @click="handleSearch">{{ searchLabel }}</el-button>
            </div>
            <button
              v-if="shouldShowExpand"
              type="button"
              class="art-search-bar__expand"
              @click="isExpanded = !isExpanded"
            >
              <span>{{ isExpanded ? collapseLabel : expandLabel }}</span>
              <ElIcon>
                <ArrowUpBold v-if="isExpanded" />
                <ArrowDownBold v-else />
              </ElIcon>
            </button>
          </div>
        </el-col>
      </el-row>
    </el-form>
  </section>
</template>

<style scoped>
.art-search-bar {
  padding: 15px 20px 0;
  margin-bottom: 12px;
  border: 1px solid var(--art-card-border);
  border-radius: calc(var(--art-custom-radius) / 2 + 2px);
  background: var(--art-default-box-color);
  box-shadow: 0 0 4px 0 rgb(0 0 0 / 4%);
}

.art-search-bar__field {
  margin-bottom: 12px;
}

.art-search-bar__field :deep(.el-input),
.art-search-bar__field :deep(.el-select) {
  width: 100%;
}

.art-search-bar__action-column {
  flex: 1;
  max-width: 100%;
}

.art-search-bar__actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  margin-bottom: 12px;
  line-height: 32px;
}

.art-search-bar__buttons {
  display: flex;
  gap: 8px;
}

.art-search-bar__expand {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  margin-left: 10px;
  border: 0;
  background: transparent;
  color: var(--art-theme-color);
  font: inherit;
  font-size: 14px;
  line-height: 32px;
  white-space: nowrap;
  cursor: pointer;
}

@media (max-width: 768px) {
  .art-search-bar {
    padding: 16px 16px 0;
  }

  .art-search-bar__actions {
    flex-direction: column;
    gap: 8px;
    align-items: stretch;
  }

  .art-search-bar__buttons {
    justify-content: center;
  }

  .art-search-bar__expand {
    justify-content: center;
    margin-left: 0;
  }
}
</style>
