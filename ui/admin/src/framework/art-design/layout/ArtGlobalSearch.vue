<script setup lang="ts">
import { computed, defineAsyncComponent, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { Search } from '@element-plus/icons-vue';
import { ElDialog, ElInput, ElScrollbar } from 'element-plus';
import type { ShellNavigationItem } from '../adapters/fullNetShellAdapter';
import { filterShellNavigation } from '../adapters/fullNetShellSearch';
import PersonalShortcuts from '../../../components/PersonalShortcuts.vue';
import type { PersonalShortcutScope } from '../../../navigation/personal-shortcuts';
import type { ObjectSearchSource } from '../../../search/objectSearch';

const ObjectSearchPanel = defineAsyncComponent(() => import('../../../components/ObjectSearchPanel.vue'));

defineOptions({ name: 'ArtGlobalSearch' });

const props = defineProps<{
  navigation: ShellNavigationItem[];
  title: string;
  placeholder: string;
  emptyLabel: string;
  hintLabel: string;
  shortcutScope?: PersonalShortcutScope;
  switching?: boolean;
  objectSources?: readonly ObjectSearchSource[];
  objectContext?: string;
  menuLabel?: string;
}>();

const router = useRouter();
const visible = ref(false);
const query = ref('');
const highlightedIndex = ref(0);
const inputRef = ref<InstanceType<typeof ElInput>>();
const searchBodyRef = ref<HTMLElement>();

const results = computed(() => filterShellNavigation(props.navigation, query.value));

function open(): void {
  visible.value = true;
  highlightedIndex.value = 0;
  void nextTick(() => {
    inputRef.value?.focus?.();
  });
}

function close(): void {
  visible.value = false;
  query.value = '';
  highlightedIndex.value = 0;
}

function navigateTo(item: ShellNavigationItem): void {
  close();
  void router.push(item.path);
}

function onKeydown(event: KeyboardEvent): void {
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
    event.preventDefault();
    if (visible.value) {
      close();
    } else {
      open();
    }
    return;
  }

  if (!visible.value) return;
  if (event.key === 'Escape') { event.preventDefault(); close(); return; }
  // 收藏、重试和分页保留原生按键；方向键只在搜索输入与结果按钮之间移动焦点。
  const target = event.target instanceof Element ? event.target : undefined;
  if (target?.closest('[data-testid="personal-shortcuts"]')) return;
  if (!target?.closest('.el-input') && !target?.matches('[data-global-search-result]')) return;
  const buttons = Array.from(searchBodyRef.value?.querySelectorAll<HTMLButtonElement>('[data-global-search-result]:not(:disabled)') ?? []);
  if (!buttons.length) return;
  const index = buttons.indexOf(target as HTMLButtonElement);
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault();
    const next = index < 0 ? event.key === 'ArrowDown' ? 0 : buttons.length - 1
      : (index + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length;
    buttons[next]?.focus();
  } else if (event.key === 'Enter' && index < 0) { event.preventDefault(); buttons[0]?.click(); }
}

onMounted(() => {
  window.addEventListener('keydown', onKeydown);
});

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeydown);
});

watch(query, () => {
  highlightedIndex.value = 0;
});
watch(() => [props.objectContext, props.switching], (_value, previous) => {
  if (previous) close();
}, { flush: 'sync' });

defineExpose({ open, close });
</script>

<template>
  <el-dialog
    v-model="visible"
    :title="title"
    width="min(720px, calc(100vw - 32px))"
    class="art-global-search"
    append-to-body
    @close="close"
  >
    <div ref="searchBodyRef">
    <el-input
      ref="inputRef"
      v-model="query"
      :placeholder="placeholder"
      :aria-label="placeholder"
      :maxlength="64"
      :prefix-icon="Search"
      clearable
      autocomplete="off"
      spellcheck="false"
    />

    <PersonalShortcuts v-if="shortcutScope" :scope="shortcutScope" :navigation="navigation" :switching="switching ?? false" @navigate="close" />
    <el-scrollbar max-height="min(60vh, 520px)" class="art-global-search__results">
      <h3 v-if="menuLabel" class="art-global-search__group">{{ menuLabel }}</h3>
      <button
        v-for="(item, index) in results"
        :key="item.path"
        type="button"
        data-global-search-result
        class="art-global-search__item"
        :class="{ 'is-active': index === highlightedIndex }"
        @click="navigateTo(item)"
        @mouseenter="highlightedIndex = index"
      >
        <span>
          <strong>{{ item.title }}</strong>
          <small>{{ item.caption }}</small>
        </span>
        <code translate="no">{{ item.path }}</code>
      </button>
      <p v-if="results.length === 0" class="art-global-search__empty">{{ emptyLabel }}</p>
      <ObjectSearchPanel v-if="objectSources?.length" :active="visible && !switching" :query="query" :context="objectContext ?? ''" :sources="objectSources" @navigate="close" />
    </el-scrollbar>
    </div>

    <template #footer>
      <span class="art-global-search__hint">{{ hintLabel }}</span>
    </template>
  </el-dialog>
</template>

<style scoped>
.art-global-search__results {
  margin-top: 14px;
}

.art-global-search__item {
  display: flex;
  width: 100%;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 8px;
  padding: 12px 14px;
  border: 1px solid var(--art-card-border);
  border-radius: var(--art-radius);
  background: var(--art-tab-bg);
  color: inherit;
  text-align: left;
  cursor: pointer;
}

.art-global-search__item.is-active,
.art-global-search__item:hover,
.art-global-search__item:focus-visible {
  border-color: color-mix(in srgb, var(--art-theme-color) 35%, var(--art-card-border));
  background: var(--art-tab-active-bg);
}
.art-global-search__group { font-size: 14px; margin: 4px 0 12px; }

.art-global-search__item strong,
.art-global-search__item small {
  display: block;
}

.art-global-search__item small {
  margin-top: 4px;
  color: var(--art-breadcrumb-text);
  font-size: 10px;
}

.art-global-search__item code {
  color: var(--art-breadcrumb-text);
  font-size: 10px;
}

.art-global-search__empty {
  margin: 0;
  padding: 24px 0;
  color: var(--art-breadcrumb-text);
  font-size: 12px;
  text-align: center;
}

.art-global-search__hint {
  color: var(--art-breadcrumb-text);
  font-size: 11px;
}
</style>
