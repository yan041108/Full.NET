<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElPagination,
  ElTag
} from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type WorkflowRecoveryTaskResponse
} from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
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

const { t } = useAdminI18n();
const items = ref<WorkflowRecoveryTaskResponse[]>([]);
const selected = ref<WorkflowRecoveryTaskResponse>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const retryReason = ref('');
const loading = ref(false);
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
  }
}

async function selectItem(item: WorkflowRecoveryTaskResponse): Promise<void> {
  problem.value = undefined;
  try {
    selected.value = await getWorkflowRecoveryTask(item.id);
    retryReason.value = '';
  } catch (error: unknown) {
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
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('workflowRecoveryTasks.eyebrow') }}</p>
      <h1 data-route-heading tabindex="-1">{{ t('workflowRecoveryTasks.title') }}</h1>
      <p>{{ t('workflowRecoveryTasks.description') }}</p>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <ElCard class="art-card">
      <template #header>
        <h2>{{ t('workflowRecoveryTasks.listTitle') }}</h2>
      </template>
      <p v-if="!items.length" class="art-empty-state">{{ t('workflowRecoveryTasks.emptyList') }}</p>
      <ul v-else class="art-list">
        <li v-for="item in items" :key="item.id">
          <button type="button" data-testid="workflow-recovery-tasks-load" @click="selectItem(item)">
            <strong translate="no">{{ item.id }}</strong>
            <ElTag
              :type="statusTone(item.statusKey)"
              :data-testid="`workflow-recovery-tasks-status-${item.statusKey}`"
            >
              {{ statusLabel(item.statusKey) }}
            </ElTag>
            <small translate="no">{{ item.kindKey }} · {{ item.attemptCount }}</small>
          </button>
        </li>
      </ul>
      <ElPagination
        v-if="total > 0"
        background
        layout="prev, pager, next, total"
        :current-page="page"
        :page-size="pageSize"
        :total="total"
        @current-change="value => { page = value; void load(); }"
      />
    </ElCard>

    <ElCard v-if="selected" class="art-card">
      <template #header>
        <h2>{{ t('workflowRecoveryTasks.detailTitle') }}</h2>
      </template>
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
    </ElCard>
  </section>
</template>
