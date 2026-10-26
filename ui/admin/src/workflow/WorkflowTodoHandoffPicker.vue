<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElPagination } from 'element-plus';
import { listWorkflowTodoTransferCandidates } from '../api/workflow-todos';
import { listWorkflowTodoDelegationCandidates } from '../api/workflow-delegations';
import { useAdminI18n } from '../i18n/adminI18n';

const props = defineProps<{ todoId: string; action: 'transfer' | 'delegate'; excludedUserId: string;
  modelValue: string; disabled: boolean; testId: string }>();
const emit = defineEmits<{ 'update:modelValue': [value: string] }>();
const { t } = useAdminI18n();
const page = ref(1); const total = ref(0); const loading = ref(false); const failed = ref(false);
const candidates = ref<Array<{ id: string; label: string }>>([]);
const selectedLabel = ref(''); let epoch = 0; let controller: AbortController | undefined;
const options = computed(() => props.modelValue && !candidates.value.some(item => item.id === props.modelValue)
  ? [{ id: props.modelValue, label: selectedLabel.value || props.modelValue }, ...candidates.value] : candidates.value);

/** 页码和候选始终由真实目录响应提供；身份/资源切换先清空并拒绝迟到结果。 */
async function load(nextPage: number): Promise<void> {
  controller?.abort(); controller = new AbortController(); const current = ++epoch;
  loading.value = true; failed.value = false;
  try {
    const result = await (props.action === 'transfer' ? listWorkflowTodoTransferCandidates : listWorkflowTodoDelegationCandidates)
      (props.todoId, nextPage, 20, controller.signal);
    if (current !== epoch) return;
    candidates.value = result.items.filter(item => item.id !== props.excludedUserId)
      .map(item => ({ id: item.id, label: item.displayName || item.username }));
    page.value = result.page; total.value = result.total;
  } catch { if (current === epoch) { failed.value = true; candidates.value = []; } }
  finally { if (current === epoch) loading.value = false; }
}
function choose(event: Event): void {
  const id = (event.target as HTMLSelectElement).value;
  selectedLabel.value = options.value.find(item => item.id === id)?.label ?? '';
  emit('update:modelValue', id);
}
watch(() => [props.todoId, props.action, props.excludedUserId], () => {
  candidates.value = []; selectedLabel.value = ''; page.value = 1; total.value = 0;
  emit('update:modelValue', ''); void load(1);
}, { immediate: true });
onBeforeUnmount(() => { epoch++; controller?.abort(); });
</script>

<template>
  <div>
    <select :value="modelValue" :data-testid="testId" :disabled="disabled || loading || failed || options.length === 0"
      :aria-label="t(action === 'delegate' ? 'workflowTodos.delegateTarget' : 'workflowTodos.transferTarget')" @change="choose">
      <option value="" disabled>{{ t('workflowTodos.transferTargetPlaceholder') }}</option>
      <option v-for="candidate in options" :key="candidate.id" :value="candidate.id">{{ candidate.label }}</option>
    </select>
    <el-pagination v-if="total > 20" :current-page="page" :page-size="20" :total="total" :disabled="disabled || loading"
      layout="prev, pager, next" :data-testid="`${testId}-pages`" @current-change="load" />
    <p v-if="failed" role="alert">{{ t('workflowTodos.candidateLoadFailed') }}</p>
    <button v-if="failed" type="button" :disabled="disabled || loading" @click="load(page)">{{ t('workflowTodos.candidateRetry') }}</button>
  </div>
</template>
