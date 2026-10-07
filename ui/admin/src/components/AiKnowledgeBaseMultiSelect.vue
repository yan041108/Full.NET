<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { ElButton, ElOption, ElSelect } from 'element-plus';
import type { AiKnowledgeBaseResponse } from '@fullnet/client-contracts';
import { listKnowledgeBases } from '../api/ai-knowledge';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showWarning } from '../feedback/fullNetMessage';
const props = defineProps<{ modelValue: string[]; disabled?: boolean }>();
const emit = defineEmits<{ 'update:modelValue': [value: string[]] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const rows = ref<AiKnowledgeBaseResponse[]>([]); const selectedRows = ref<AiKnowledgeBaseResponse[]>([]);
const page = ref(1); const total = ref(0); const loading = ref(false);
const canRead = computed(() => !session.switching && session.can('ai.knowledge_bases.read'));
const options = computed(() => [...selectedRows.value, ...rows.value.filter(row => !selectedRows.value.some(item => item.id === row.id))]);
let epoch = 0; let controller: AbortController | undefined;
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function choose(value: unknown) {
  if (!canRead.value || props.disabled || loading.value) return;
  if (!Array.isArray(value) || value.some(id => typeof id !== 'string')) return;
  const ids: string[] = value.map(id => String(id).toLowerCase());
  if (ids.length > 8 || new Set(ids).size !== ids.length || ids.some(id => !guid.test(id) || id === '00000000-0000-0000-0000-000000000000' || !options.value.some(row => row.id === id))) { showWarning(t('knowledge.invalidBases')); return; }
  // 只保留最多八个已选择的授权元数据，翻页不会丢失标签或改变请求集合。
  selectedRows.value = options.value.filter(row => ids.includes(row.id)); emit('update:modelValue', ids);
}
async function load(next = 1) {
  controller?.abort(); const current = ++epoch; rows.value = []; total.value = 0;
  if (!canRead.value) { loading.value = false; return; }
  controller = new AbortController(); loading.value = true;
  try {
    const result = await listKnowledgeBases({ page: next, pageSize: 20 }, controller.signal);
    if (current !== epoch) return; rows.value = result.items.filter(row => row.isEnabled); total.value = result.total; page.value = next;
  } catch (error) { if (current === epoch) showProblem(error, t('common.loadFailed')); }
  finally { if (current === epoch) loading.value = false; }
}
function clear() { ++epoch; controller?.abort(); rows.value = []; selectedRows.value = []; total.value = 0; page.value = 1; loading.value = false; }
watch(() => props.modelValue, ids => { selectedRows.value = selectedRows.value.filter(row => ids.includes(row.id)); }, { deep: true });
watch(() => [session.currentUser, session.switching], () => { clear(); emit('update:modelValue', []); void load(); }, { deep: true });
onMounted(() => void load()); onUnmounted(clear);
</script>
<template>
  <div v-if="canRead" class="knowledge-multi-select">
    <el-select :model-value="modelValue" multiple filterable :multiple-limit="8" :disabled="disabled || loading" :loading="loading" :placeholder="t('knowledge.selectBases')" :aria-label="t('knowledge.selectBases')" data-testid="knowledge-bases-select" @update:model-value="choose">
      <el-option v-for="row in options" :key="row.id" :value="row.id" :label="row.name" />
    </el-select>
    <el-button :disabled="disabled || loading || page <= 1" :aria-label="t('knowledge.previous')" data-testid="knowledge-bases-previous" @click="load(page - 1)">‹</el-button>
    <el-button :disabled="disabled || loading || page * 20 >= total" :aria-label="t('knowledge.next')" data-testid="knowledge-bases-next" @click="load(page + 1)">›</el-button>
  </div>
</template>
<style scoped>.knowledge-multi-select { display: flex; align-items: center; gap: 8px; } .el-select { flex: 1; min-width: 220px; }</style>
