<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { ElButton } from 'element-plus';
import { useAdminI18n } from '../i18n/adminI18n';
import { isSearchObjectId, readObjectSearchPage, type ObjectSearchHit, type ObjectSearchSource } from '../search/objectSearch';

const props = defineProps<{ active: boolean; query: string; context: string; sources: readonly ObjectSearchSource[] }>();
const emit = defineEmits<{ navigate: [] }>();
const router = useRouter(), { t } = useAdminI18n();
interface SourceState { source: ObjectSearchSource; items: ObjectSearchHit[]; page: number; hasMore: boolean; truncated: boolean;
  loading: boolean; error: boolean; sequence: number; controller?: AbortController; timeout?: ReturnType<typeof setTimeout> }
const states = ref<SourceState[]>([]);
let generation = 0, debounce: ReturnType<typeof setTimeout> | undefined;
const validQuery = () => { const query = props.query.trim(); return query.length >= 2 && query.length <= 64 && !/[\u0000-\u001f\u007f-\u009f]/.test(query); };
function cancel() {
  generation++; clearTimeout(debounce);
  for (const state of states.value) { state.controller?.abort(); clearTimeout(state.timeout); }
  states.value = [];
}
watch(() => [props.active, props.query, props.context, props.sources] as const, () => {
  cancel(); if (!props.active || !validQuery()) return;
  const current = generation;
  debounce = setTimeout(() => {
    if (current !== generation || !props.active) return;
    states.value = props.sources.slice(0, 2).map(source => ({ source, items: [], page: 1, hasMore: false,
      truncated: false, loading: false, error: false, sequence: 0 }));
    for (const state of states.value) void load(state, 1);
  }, 250);
}, { immediate: true, flush: 'sync' });
onBeforeUnmount(cancel);

async function load(state: SourceState, page: number) {
  if (!props.active || !validQuery() || state.loading || page < 1 || page > 20 || !props.sources.some(source => source.key === state.source.key)) return;
  state.controller?.abort(); clearTimeout(state.timeout);
  const controller = new AbortController(), current = generation, sequence = ++state.sequence, context = props.context;
  const matches = () => current === generation && sequence === state.sequence && context === props.context && props.active;
  state.controller = controller; state.loading = true; state.error = false;
  state.timeout = setTimeout(() => {
    if (!matches()) return;
    state.sequence++; controller.abort(); state.loading = false; state.error = true; state.items = [];
  }, 8000);
  try {
    const result = readObjectSearchPage(await state.source.load(props.query.trim(), page, controller.signal), page);
    if (!matches() || controller.signal.aborted) return;
    state.items = result.items; state.page = result.page; state.hasMore = result.hasMore; state.truncated = result.isTruncated;
  } catch {
    if (matches() && !controller.signal.aborted) { state.error = true; state.items = []; }
  } finally {
    if (matches()) { state.loading = false; clearTimeout(state.timeout); }
  }
}
async function navigate(state: SourceState, id: string) {
  if (!props.active || !isSearchObjectId(id) || state.error || state.loading || !states.value.includes(state)) return;
  const source = props.sources.find(candidate => candidate.key === state.source.key);
  if (!source || !state.items.some(item => item.id === id)) return;
  await router.push({ name: source.routeName, query: { [source.queryKey]: id } }); emit('navigate');
}
</script>
<template>
  <section v-if="sources.length" class="object-search-panel" data-testid="object-search-panel" :aria-label="t('objectSearch.title')" aria-live="polite">
    <h3>{{ t('objectSearch.title') }}</h3>
    <p class="object-search-hint">{{ t('objectSearch.window') }}</p>
    <p v-if="!validQuery()">{{ t('objectSearch.minimum') }}</p>
    <section v-for="state in states" :key="state.source.key" :aria-label="state.source.label" :data-search-source="state.source.key" :aria-busy="state.loading">
      <h4>{{ state.source.label }}</h4>
      <p v-if="state.loading" role="status">{{ t('objectSearch.loading') }}</p>
      <div v-else-if="state.error" role="status">{{ t('objectSearch.failed') }} <ElButton @click="load(state, state.page)">{{ t('objectSearch.retry') }}</ElButton></div>
      <template v-else>
        <p v-if="!state.items.length">{{ t('objectSearch.empty') }}</p>
        <button v-for="item in state.items" :key="item.id" class="object-search-result" type="button" data-global-search-result :data-search-object="item.id" @click="navigate(state, item.id)">
          <strong>{{ item.title }}</strong><small v-if="item.number">{{ item.number }}</small>
        </button>
        <p v-if="state.truncated" role="status">{{ t('objectSearch.truncated') }}</p>
        <div class="object-search-pages" v-if="state.items.length || state.page > 1">
          <ElButton :disabled="state.page === 1" @click="load(state, state.page - 1)" :aria-label="state.source.label + ' ' + t('objectSearch.previous')">{{ t('objectSearch.previous') }}</ElButton>
          <span>{{ t('objectSearch.page', { page: state.page }) }}</span>
          <ElButton :disabled="!state.hasMore" @click="load(state, state.page + 1)" :aria-label="state.source.label + ' ' + t('objectSearch.next')">{{ t('objectSearch.next') }}</ElButton>
        </div>
      </template>
    </section>
  </section>
</template>
<style scoped>
.object-search-panel { margin-top: 16px; border-top: 1px solid var(--el-border-color); padding-top: 8px; }
.object-search-panel h3,.object-search-panel h4 { font-size: 14px; margin: 12px 0 8px; }
.object-search-hint { color: var(--el-text-color-regular); font-size: 12px; }
.object-search-result { display: block; width: 100%; text-align: left; border: 1px solid var(--el-border-color); border-radius: 6px; background: var(--el-bg-color); color: var(--el-text-color-primary); padding: 10px; margin: 8px 0; cursor: pointer; overflow-wrap: anywhere; }
.object-search-result small { display: block; color: var(--el-text-color-regular); margin-top: 4px; }
.object-search-result:focus-visible { outline: 2px solid var(--el-color-primary); outline-offset: 2px; }
.object-search-pages { display: flex; align-items: center; justify-content: space-between; gap: 8px; }
</style>
