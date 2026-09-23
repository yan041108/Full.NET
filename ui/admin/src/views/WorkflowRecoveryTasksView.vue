<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type WorkflowRecoveryTaskResponse
} from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import {
  getWorkflowRecoveryTask,
  listWorkflowRecoveryTasks,
  reconcileWorkflowRecoveryTask,
  retryWorkflowRecoveryTask
} from '../api/workflow-recovery-tasks';

const statusKeys = [
  'pending',
  'failed',
  'dead_lettered',
  'succeeded',
  'cancelled'
] as const;

type RecoveryStatusKey = typeof statusKeys[number];

import { formatAdminDateTime } from '../workflow/workflowAdminFormat';

const { t, locale } = useAdminI18n();

function formatDateTime(value: string | null | undefined): string {
  return formatAdminDateTime(locale.value, value);
}
const items = ref<WorkflowRecoveryTaskResponse[]>([]);
const selectedId = ref<string>();
const selected = ref<WorkflowRecoveryTaskResponse>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const retryReason = ref('');
const loading = ref(false);
const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(loading);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listWorkflowRecoveryTasks(page.value, pageSize.value);
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'workflowRecoveryTasks.loadFailed');
  } finally {
    loading.value = false;
    void syncTableLayout();
  }
}

async function selectItem(item: WorkflowRecoveryTaskResponse): Promise<void> {
  selectedId.value = item.id;
  problem.value = undefined;
  try {
    selected.value = await getWorkflowRecoveryTask(item.id);
    retryReason.value = '';
  } catch (error: unknown) {
    selected.value = undefined;
    problem.value = toProblem(error);
  }
}

function canMutate(statusKey: string): boolean {
  return statusKey === 'failed' || statusKey === 'dead_lettered' || statusKey === 'pending';
}

async function retryItem(): Promise<void> {
  const current = selected.value;
  if (!current || changing.value || !retryReason.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    selected.value = await retryWorkflowRecoveryTask(current.id, {
      expectedRevision: current.revision,
      reason: retryReason.value.trim(),
      idempotencyKey: `retry-${crypto.randomUUID()}`
    });
    ElMessage.success(t('workflowRecoveryTasks.retrySuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function reconcileItem(): Promise<void> {
  const current = selected.value;
  if (!current || changing.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    selected.value = await reconcileWorkflowRecoveryTask(current.id, {
      expectedRevision: current.revision,
      reason: retryReason.value.trim() || null,
      idempotencyKey: `reconcile-${crypto.randomUUID()}`
    });
    ElMessage.success(t('workflowRecoveryTasks.reconcileSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function statusLabel(statusKey: string): string {
  return statusKeys.includes(statusKey as RecoveryStatusKey)
    ? t(`workflowRecoveryTasks.status.${statusKey}` as 'workflowRecoveryTasks.status.pending')
    : statusKey;
}

function statusTone(statusKey: string): 'success' | 'warning' | 'info' | 'danger' | undefined {
  switch (statusKey) {
    case 'succeeded':
      return 'success';
    case 'pending':
      return 'info';
    case 'failed':
      return 'warning';
    case 'dead_lettered':
      return 'danger';
    default:
      return undefined;
  }
}

function toProblem(
  error: unknown,
  fallbackCode: 'workflowRecoveryTasks.loadFailed' | 'workflowRecoveryTasks.operationFailed'
    = 'workflowRecoveryTasks.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: fallbackCode, title: t(fallbackCode) };
}
</script>

<template>
  <section class="workflow-recovery-tasks art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowRecoveryTasks.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowRecoveryTasks.description') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <div class="workflow-recovery-tasks__layout art-split-layout">
      <ElCard class="workflow-recovery-tasks__list art-table-card" shadow="never">
        <template #header>
          <h2>{{ t('workflowRecoveryTasks.listTitle') }}</h2>
        </template>
        <div ref="tableMainRef" class="art-crud-table-main">
        <ElTable
          v-loading="loading"
          :data="items"
          :height="tableHeight"
          row-key="id"
          highlight-current-row
          empty-text=""
          :current-row-key="selectedId"
          @row-click="row => void selectItem(row)"
        >
          <ElTableColumn label="ID" min-width="200" show-overflow-tooltip>
            <template #default="{ row }">
              <button type="button" class="workflow-recovery-tasks__id" data-testid="workflow-recovery-tasks-load" translate="no" @click.stop="void selectItem(row as WorkflowRecoveryTaskResponse)">
                {{ row.id }}
              </button>
            </template>
          </ElTableColumn>
          <ElTableColumn :label="t('workflowRecoveryTasks.fieldStatus')" width="120" align="center">
            <template #default="{ row }">
              <ElTag size="small" :type="statusTone(row.statusKey)" :data-testid="`workflow-recovery-tasks-status-${row.statusKey}`">
                {{ statusLabel(row.statusKey) }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn :label="t('workflowRecoveryTasks.fieldAttempts')" width="100" align="center">
            <template #default="{ row }"><span translate="no">{{ row.attemptCount }}</span></template>
          </ElTableColumn>
          <template #empty>
            <p class="art-empty-state">{{ t('workflowRecoveryTasks.emptyList') }}</p>
          </template>
        </ElTable>
        <ElPagination
          v-if="total > 0"
          class="art-table-pagination"
          background
          layout="prev, pager, next, total"
          :current-page="page"
          :page-size="pageSize"
          :total="total"
          @current-change="value => { page = value; void load(); }"
        />
        </div>
      </ElCard>

      <ElCard class="workflow-recovery-tasks__detail art-form-card" shadow="never" data-testid="workflow-recovery-tasks-detail">
      <template #header>
        <h2>{{ t('workflowRecoveryTasks.detailTitle') }}</h2>
      </template>
      <p v-if="!selected" class="art-empty-state">{{ t('workflowRecoveryTasks.selectDetail') }}</p>
      <template v-else>
      <p>
        {{ t('workflowRecoveryTasks.fieldStatus') }}:
        <ElTag
          :type="statusTone(selected.statusKey)"
          :data-testid="`workflow-recovery-tasks-detail-status-${selected.statusKey}`"
        >
          {{ statusLabel(selected.statusKey) }}
        </ElTag>
      </p>
      <p>{{ t('workflowRecoveryTasks.fieldAttempts') }}: <strong translate="no">{{ selected.attemptCount }}</strong></p>
      <p>{{ t('workflowRecoveryTasks.fieldRevision') }}: <strong translate="no">{{ selected.revision }}</strong></p>
      <p>{{ t('workflowRecoveryTasks.fieldLease') }}:
        <code translate="no">{{ selected.leaseOwnerKey ?? t('workflowRecoveryTasks.none') }}</code>
      </p>
      <p v-if="selected.lastError">{{ t('workflowRecoveryTasks.fieldLastError') }}: {{ selected.lastError }}</p>
      <template v-if="canMutate(selected.statusKey)">
        <PermissionGate code="workflow.recovery_tasks.retry">
          <div class="art-form-grid">
            <ElInput
              v-model="retryReason"
              data-testid="workflow-recovery-tasks-retry-reason"
              maxlength="512"
              :placeholder="t('workflowRecoveryTasks.fieldReason')"
            />
            <ElButton
              data-testid="workflow-recovery-tasks-retry"
              type="primary"
              :disabled="changing || !retryReason.trim() || selected.statusKey === 'pending'"
              @click="retryItem"
            >
              {{ t('workflowRecoveryTasks.retry') }}
            </ElButton>
          </div>
        </PermissionGate>
        <PermissionGate code="workflow.recovery_tasks.reconcile">
          <ElButton
            data-testid="workflow-recovery-tasks-reconcile"
            :disabled="changing"
            @click="reconcileItem"
          >
            {{ t('workflowRecoveryTasks.reconcile') }}
          </ElButton>
        </PermissionGate>
      </template>
      </template>
    </ElCard>
    </div>
  </section>
</template>

<style scoped>
.workflow-recovery-tasks { min-height: 0; }
.workflow-recovery-tasks__layout { display: flex; flex: 1; gap: 12px; min-height: 0; }
.workflow-recovery-tasks__list { flex: 1 1 0; min-width: 0; }
.workflow-recovery-tasks__list :deep(.el-card__body) { padding-top: 0; }
.workflow-recovery-tasks__list :deep(.el-card__header) { padding: 12px 16px; }
.workflow-recovery-tasks__list :deep(.el-card__header) h2 { margin: 0; font-size: 16px; font-weight: 600; }
.workflow-recovery-tasks__detail { flex: 0 0 320px; max-width: 400px; overflow: auto; }
.workflow-recovery-tasks__detail :deep(.el-card__header) { padding: 12px 16px; }
.workflow-recovery-tasks__detail :deep(.el-card__body) { padding: 12px 16px; }
.workflow-recovery-tasks__id { padding: 0; border: none; background: none; color: var(--el-color-primary); font: inherit; cursor: pointer; text-align: left; }
@media (max-width: 960px) {
  .workflow-recovery-tasks__layout { flex-direction: column; }
  .workflow-recovery-tasks__detail { flex: none; max-width: none; width: 100%; }
}
</style>
