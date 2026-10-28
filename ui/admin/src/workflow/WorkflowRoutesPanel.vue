<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElButton, ElInput, ElOption, ElSelect } from 'element-plus';
import type { MessageKey } from '@fullnet/admin-i18n';
import { getWorkflowRouteDecision, reconcileWorkflowRoute, type WorkflowRouteDecision, type WorkflowRouteResolution } from '../api/workflow-routes';
import { useAdminI18n } from '../i18n/adminI18n';
const props = defineProps<{ instanceId: string; routeId: string; revision: number; canReconcile: boolean }>();
const emit = defineEmits<{ resolved: []; close: [] }>(); const { t } = useAdminI18n();
const row = ref<WorkflowRouteDecision>(); const receipt = ref<WorkflowRouteResolution>();
const loading = ref(false); const pending = ref(false); const failed = ref(false); const submitFailed = ref(false);
const target = ref(''); const reason = ref(''); let generation = 0; let controller: AbortController | undefined;
let intent = ''; let idempotencyKey = '';
const canSubmit = computed(() => props.canReconcile && row.value?.statusKey === 'decision_unknown' && row.value.instanceStatusKey === 'suspended'
  && !loading.value && !pending.value && reason.value.trim().length > 0 && reason.value.trim().length <= 512
  && (target.value === '' || row.value.targets.some(item => item.targetKey === target.value)));
async function load(): Promise<void> {
  controller?.abort(); controller = new AbortController(); const expected = ++generation;
  row.value = undefined; receipt.value = undefined; pending.value = false; loading.value = true; failed.value = false;
  target.value = ''; reason.value = ''; intent = ''; idempotencyKey = ''; submitFailed.value = false;
  try { const result = await getWorkflowRouteDecision(props.instanceId, props.routeId, controller.signal); if (expected === generation) row.value = result; }
  catch { if (expected === generation) failed.value = true; }
  finally { if (expected === generation) loading.value = false; }
}
watch(() => [props.instanceId, props.routeId, props.revision, props.canReconcile], () => { void load(); }, { immediate: true });
onBeforeUnmount(() => { generation++; controller?.abort(); });
async function submit(): Promise<void> {
  if (!canSubmit.value || !row.value) return;
  const current = row.value; const expected = generation;
  const body = { expectedInstanceRevision: current.instanceRevision, expectedRevision: current.revision, selectedTargetKey: target.value || null, reason: reason.value.trim() };
  const nextIntent = JSON.stringify([props.instanceId, props.routeId, body]);
  if (nextIntent !== intent) { intent = nextIntent; idempotencyKey = crypto.randomUUID(); }
  pending.value = true; submitFailed.value = false;
  try {
    const result = await reconcileWorkflowRoute(props.instanceId, props.routeId, { ...body, idempotencyKey }, controller?.signal);
    if (expected !== generation) return;
    receipt.value = result; row.value = undefined; emit('resolved');
  } catch { if (expected === generation) submitFailed.value = true; }
  finally { if (expected === generation) pending.value = false; }
}
const status = computed(() => row.value ? t(`workflowDesigner.route.${row.value.statusKey === 'decision_unknown' ? 'unknown' : row.value.statusKey}` as MessageKey) : '');
</script>
<template>
  <section class="route-panel" :aria-label="t('workflowDesigner.route.operations')" :aria-busy="loading || pending">
    <header><h3>{{ t('workflowDesigner.route.operations') }}</h3><div><ElButton :disabled="loading || pending" @click="load">{{ t('workflowDesigner.route.refresh') }}</ElButton><ElButton :disabled="pending" @click="emit('close')">{{ t('workflowDesigner.route.cancel') }}</ElButton></div></header>
    <p v-if="failed" role="alert">{{ t('workflowDesigner.route.loadFailed') }}</p>
    <template v-if="row">
      <p><strong translate="no">{{ row.nodeKey }}</strong> · {{ status }}</p>
      <dl><dt>{{ t('workflowDesigner.route.normal') }}</dt><dd translate="no">{{ row.normalNextNodeKey }}</dd></dl>
      <dl v-if="row.nextNodeKey"><dt>{{ t('workflowDesigner.route.target') }}</dt><dd translate="no">{{ row.nextNodeKey }}</dd></dl>
      <p v-if="row.continuationWorkId">{{ t('workflowDesigner.route.receipt') }} · <code translate="no">{{ row.continuationWorkId }}</code></p>
      <form v-if="canReconcile && row.statusKey === 'decision_unknown' && row.instanceStatusKey === 'suspended'" @submit.prevent="submit">
        <p>{{ t('workflowDesigner.route.reconcileHelp') }}</p>
        <label><span>{{ t('workflowDesigner.route.target') }}</span><ElSelect v-model="target" :disabled="pending" :aria-label="t('workflowDesigner.route.target')">
          <ElOption value="" :label="`${t('workflowDesigner.route.normal')} · ${row.normalNextNodeKey}`" />
          <ElOption v-for="item in row.targets" :key="item.targetKey" :value="item.targetKey" :label="`${item.targetKey} → ${item.nodeKey}`" />
        </ElSelect></label>
        <label><span>{{ t('workflowDesigner.route.reason') }}</span><ElInput v-model="reason" type="textarea" :maxlength="512" :disabled="pending" :aria-label="t('workflowDesigner.route.reason')" /></label>
        <p v-if="submitFailed" role="alert">{{ t('workflowDesigner.route.failed') }}</p>
        <ElButton type="primary" native-type="submit" :disabled="!canSubmit" data-testid="workflow-resolve-route">{{ t('workflowDesigner.route.reconcile') }}</ElButton>
      </form>
    </template>
    <p v-if="receipt" role="status">{{ t('workflowDesigner.route.receipt') }} · <code translate="no">{{ receipt.continuationWorkId }}</code></p>
  </section>
</template>
<style scoped>
.route-panel { display: grid; gap: 12px; padding: 20px; border: 1px solid var(--el-border-color); border-left: 3px solid var(--el-color-warning); border-radius: 8px; }
header { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
h3, p { margin: 0; } form, label { display: grid; gap: 10px; } form { gap: 16px; } dl { display: flex; gap: 12px; margin: 0; } dd { margin: 0; }
code { overflow-wrap: anywhere; } form > .el-button { justify-self: start; }
</style>
