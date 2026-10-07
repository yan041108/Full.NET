<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue';
import { ElButton, ElOption, ElSelect } from 'element-plus';
import type { AiKnowledgeBaseResponse } from '@fullnet/client-contracts';
import { listKnowledgeBases } from '../api/ai-knowledge';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem } from '../feedback/fullNetMessage';
const props = defineProps<{ modelValue: string; disabled?: boolean; candidateIds?: readonly string[] }>();
const emit = defineEmits<{ 'update:modelValue': [value: string] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const rows = ref<AiKnowledgeBaseResponse[]>([]); const page = ref(1); const total = ref(0); const loading = ref(false);
let epoch = 0; let controller: AbortController | undefined;
async function load(next = 1) {
  controller?.abort(); const current = ++epoch; rows.value = []; total.value = 0;
  if (session.switching || !session.can('ai.knowledge_bases.read')) { loading.value = false; return; }
  controller = new AbortController(); loading.value = true;
  try {
    const result = await listKnowledgeBases({ page: next, pageSize: 20 }, controller.signal);
    if (current !== epoch) return;
    rows.value = result.items.filter(row => row.isEnabled && (!props.candidateIds || props.candidateIds.includes(row.id))); page.value = next; total.value = result.total;
  } catch (error) { if (current === epoch) showProblem(error, t('common.loadFailed')); }
  finally { if (current === epoch) loading.value = false; }
}
watch(() => [session.currentUser, session.switching], () => { emit('update:modelValue', ''); void load(); }, { deep: true });
watch(() => props.candidateIds, () => { emit('update:modelValue', ''); void load(); }, { deep: true });
onMounted(() => void load()); onUnmounted(() => { ++epoch; controller?.abort(); });
</script>
<template>
  <div v-if="session.can('ai.knowledge_bases.read')" class="knowledge-select">
    <el-select :model-value="props.modelValue" :disabled="disabled || loading" :loading="loading" :placeholder="t('knowledge.selectBase')" :aria-label="t('knowledge.selectBase')" data-testid="knowledge-base-select" @update:model-value="emit('update:modelValue', $event)">
      <el-option v-for="row in rows" :key="row.id" :value="row.id" :label="row.name" />
    </el-select>
    <el-button :disabled="disabled || loading || page <= 1" :aria-label="t('knowledge.previous')" @click="load(page - 1)">‹</el-button>
    <el-button :disabled="disabled || loading || page * 20 >= total" :aria-label="t('knowledge.next')" @click="load(page + 1)">›</el-button>
  </div>
</template>
<style scoped>.knowledge-select { display: flex; gap: 8px; align-items: center; } .el-select { flex: 1; min-width: 180px; }</style>
