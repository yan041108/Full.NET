<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { ElButton, ElCard, ElMessage, ElPagination, ElTable, ElTableColumn } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails, type WorkflowClaimPool } from '@fullnet/client-contracts';
import { useRouter } from 'vue-router';
import { claimWorkflowPool, listWorkflowClaimPools } from '../api/workflow-claims';
import { usePermission } from '../auth/permission';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { formatAdminDateTime } from '../workflow/workflowAdminFormat';

const { can } = usePermission(); const session = useSessionStore(); const { t, locale } = useAdminI18n(); const router = useRouter();
const items = ref<WorkflowClaimPool[]>([]); const total = ref(0); const page = ref(1); const pageSize = 20;
const loading = ref(false); const acting = ref<string>(); const problem = ref<FullNetProblemDetails>();
const pending = new Map<string, { revision: number; key: string }>();
let controller: AbortController | undefined; let epoch = 0; let operationEpoch = 0; let disposed = false;
const context = computed(() => JSON.stringify([session.currentUser?.id, session.currentUser?.sessionId, session.currentUser?.tenantId,
  session.currentUser?.scope, session.switching, can('workflow.claim_pools.read'), can('workflow.claim_pools.claim'), can('workflow.todos.read')]));
watch(context, () => { epoch++; operationEpoch++; controller?.abort(); items.value = []; total.value = 0; pending.clear(); page.value = 1; acting.value = undefined; void load(); }, { flush: 'sync' });
onMounted(load); onBeforeUnmount(() => { disposed = true; epoch++; operationEpoch++; controller?.abort(); });

async function load(): Promise<void> {
  controller?.abort(); controller = new AbortController(); const requestController = controller; const current = ++epoch;
  if (disposed || session.switching || !can('workflow.claim_pools.read')) { items.value = []; total.value = 0; loading.value = false; return; }
  loading.value = true; problem.value = undefined;
  try {
    const result = await listWorkflowClaimPools(page.value, pageSize, requestController.signal);
    if (current !== epoch || disposed) return;
    items.value = [...result.items]; total.value = result.totalCount;
  } catch (error) {
    if (current === epoch && !requestController.signal.aborted) problem.value = asProblem(error);
  } finally { if (current === epoch) loading.value = false; }
}
async function claim(poolId: string): Promise<void> {
  const pool = items.value.find(item => item.id === poolId);
  if (!pool) return;
  if (disposed || session.switching || acting.value || !can('workflow.claim_pools.claim')) return;
  const current = epoch; const operationId = ++operationEpoch; acting.value = pool.id; problem.value = undefined;
  let operation = pending.get(pool.id);
  if (operation?.revision !== pool.revision) { operation = { revision: pool.revision, key: crypto.randomUUID() }; pending.set(pool.id, operation); }
  try {
    const result = await claimWorkflowPool(pool.id, { expectedRevision: pool.revision, idempotencyKey: operation.key });
    if (current !== epoch || disposed) return;
    pending.delete(pool.id); ElMessage.success(t('workflowTodos.claimSuccess'));
    if (result.activeTodoId && can('workflow.todos.read')) await router.push({ path: '/workflow/todos', query: { resourceId: result.activeTodoId } });
    await load();
  } catch (error) {
    if (current !== epoch || disposed) return;
    const failure = asProblem(error);
    // 网络结果未知时保留同一幂等键；明确竞争失败后重新读取池修订。
    if (failure.status === 409) { pending.delete(pool.id); await load(); }
    problem.value = failure;
  } finally { if (!disposed && operationId === operationEpoch) acting.value = undefined; }
}
function asProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.workflow_failed', title: t('workflowTodos.operationFailed') };
}
</script>

<template>
  <section class="art-page-stack" :aria-busy="loading || !!acting">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowTodos.claimPools') }}</h1>
    <div v-if="problem" class="art-inline-alert" role="alert"><strong translate="no">{{ problem.code }}</strong><span>{{ problem.title }}</span></div>
    <ElCard v-if="can('workflow.claim_pools.read')">
      <template #header><div class="claim-header"><strong>{{ t('workflowTodos.claimPools') }}</strong><ElButton :loading="loading" @click="load">{{ t('common.refresh') }}</ElButton></div></template>
      <ElTable :data="items" :empty-text="t('workflowTodos.empty')" row-key="id">
        <ElTableColumn prop="businessTitle" :label="t('workflowTodos.business')" min-width="220"><template #default="{ row }">{{ row.businessTitle || `${row.businessType} · ${row.businessId}` }}</template></ElTableColumn>
        <ElTableColumn :label="t('workflowTodos.arrivedAt')" width="190"><template #default="{ row }">{{ formatAdminDateTime(locale, row.createdAtUtc) }}</template></ElTableColumn>
        <ElTableColumn width="140"><template #default="{ row }"><ElButton v-if="can('workflow.claim_pools.claim')" type="primary" :loading="acting === row.id" :disabled="!!acting" @click="claim(row.id)">{{ t('workflowTodos.claim') }}</ElButton></template></ElTableColumn>
      </ElTable>
      <ElPagination v-model:current-page="page" :page-size="pageSize" :total="total" layout="prev, pager, next" @current-change="load" />
    </ElCard>
  </section>
</template>
<style scoped>.claim-header { display: flex; align-items: center; justify-content: space-between; gap: 1rem; }</style>
