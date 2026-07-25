<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { Search } from '@element-plus/icons-vue';
import { ElDialog, ElInput, ElScrollbar } from 'element-plus';
import type { ShellNavigationItem } from '../adapters/fullNetShellAdapter';
import { filterShellNavigation } from '../adapters/fullNetShellSearch';

defineOptions({ name: 'ArtGlobalSearch' });

const props = defineProps<{
  navigation: ShellNavigationItem[];
  title: string;
  placeholder: string;
  emptyLabel: string;
  hintLabel: string;
}>();

const router = useRouter();
const visible = ref(false);
const query = ref('');
const highlightedIndex = ref(0);
const inputRef = ref<InstanceType<typeof ElInput>>();

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

  if (!visible.value || results.value.length === 0) {
    return;
  }

  if (event.key === 'ArrowDown') {
    event.preventDefault();
    highlightedIndex.value = (highlightedIndex.value + 1) % results.value.length;
  } else if (event.key === 'ArrowUp') {
    event.preventDefault();
    highlightedIndex.value = (highlightedIndex.value - 1 + results.value.length)
      % results.value.length;
  } else if (event.key === 'Enter') {
    event.preventDefault();
    const target = results.value[highlightedIndex.value];
    if (target) {
      navigateTo(target);
    }
  } else if (event.key === 'Escape') {
    event.preventDefault();
    close();
  }
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

defineExpose({ open, close });
</script>

<template>
  <el-dialog
    v-model="visible"
    :title="title"
    width="560px"
    class="art-global-search"
    append-to-body
    @close="close"
  >
    <el-input
      ref="inputRef"
      v-model="query"
      :placeholder="placeholder"
      :prefix-icon="Search"
      clearable
      autocomplete="off"
      spellcheck="false"
    />

    <el-scrollbar max-height="320px" class="art-global-search__results">
      <button
        v-for="(item, index) in results"
        :key="item.path"
        type="button"
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
    </el-scrollbar>

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
.art-global-search__item:hover {
  border-color: color-mix(in srgb, var(--art-theme-color) 35%, var(--art-card-border));
  background: var(--art-tab-active-bg);
}

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
