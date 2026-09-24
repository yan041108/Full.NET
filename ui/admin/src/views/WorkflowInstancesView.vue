<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElSelect,
  ElTabPane,
  ElTabs,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { MessageKey } from '@fullnet/admin-i18n';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import {
  cancelWorkflowInstance,
  getWorkflowInstance,
  listMyWorkflowInstances,
  listWorkflowInstanceExecutionLogs,
  listWorkflowInstances,
  pauseWorkflowInstance,
  recoverWorkflowInstance,
  resumeWorkflowInstance,
  type WorkflowExecutionLogResponse,
  type WorkflowInstanceListItemResponse,
  type WorkflowInstanceResponse
} from '../api/workflow-instances';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  findWorkflowBusinessDetailRoute,
  formatWorkflowBusinessLabel
} from '../workflow/workflowBusinessDetail';
import { formatAdminDateTime } from '../workflow/workflowAdminFormat';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';

const statusFilterOptions = [
  'active',
  'completed',
  'rejected',
  'cancelled',
  'suspended'
] as const;

type InstanceStatusKey = typeof statusFilterOptions[number];
type InstanceTabKey = 'mine' | 'all';

const { t, locale } = useAdminI18n();

function formatDateTime(value: string | null | undefined): string {
  return formatAdminDateTime(locale.value, value ?? '');
}
const router = useRouter();
const session = useSessionStore();
const activeTab = ref<InstanceTabKey>('mine');
const listItems = ref<WorkflowInstanceListItemResponse[]>([]);
const listPage = ref(1);
const listPageSize = ref(20);
const listTotal = ref(0);
const listLoading = ref(false);
const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(listLoading);
const statusFilter = ref<string>();
const definitionKeyFilter = ref('');
const instanceId = ref('');
const loading = ref(false);
const cancelling = ref(false);
const pausing = ref(false);
const resuming = ref(false);
const recovering = ref(false);
const instance = ref<WorkflowInstanceResponse>();
const executionLogs = ref<WorkflowExecutionLogResponse[]>([]);
const problem = ref<FullNetProblemDetails>();
let loadController: AbortController | undefined;

/** 把执行轨迹迁移键映射为可读标签，并行网关使用专用文案。 */
function transitionLabel(transitionKey: string): string {
  if (transitionKey === 'node.gateway.parallel.fork') {
    return t('workflowInstances.parallelFork');
  }
  if (transitionKey === 'node.gateway.parallel.join') {
    return t('workflowInstances.parallelJoin');
  }
  if (transitionKey === 'node.gateway.inclusive.fork') {
    return t('workflowInstances.inclusiveFork');
  }
  if (transitionKey === 'node.gateway.inclusive.join') {
    return t('workflowInstances.inclusiveJoin');
  }
  return transitionKey;
}

type WorkflowGatewayJoinBranch = { branchKey: string; arrivedAtUtc?: string | null };
type WorkflowGatewayJoin = {
  id: string;
  gatewayTypeKey: string;
  forkNodeKey: string;
  joinNodeKey: string;
  requiredBranchCount: number;
  arrivedBranchCount: number;
  statusKey: string;
  branches: WorkflowGatewayJoinBranch[];
};
type WorkflowInstanceDetail = WorkflowInstanceResponse & { gatewayJoins?: WorkflowGatewayJoin[] | null };

const gatewayJoins = computed(() => (instance.value as WorkflowInstanceDetail | undefined)?.gatewayJoins ?? []);
const timeoutStatusKeys = new Set(['scheduled', 'overdue', 'escalated', 'not_configured']);

/** 把服务端稳定机器码映射为当前语言文案，未知值安全回落为未配置。 */
function timeoutStatusLabel(statusKey: string | undefined): string {
  const normalized = statusKey !== undefined && timeoutStatusKeys.has(statusKey)
    ? statusKey
    : 'not_configured';
  return t(`workflowInstances.timeoutStatus.${normalized}` as MessageKey);
}

/** 把多人审批模式键映射为设计器同源文案。 */
function approvalModeLabel(modeKey: string): string {
  switch (modeKey) {
    case 'all': return t('workflowDesigner.approval.all');
    case 'any': return t('workflowDesigner.approval.any');
    case 'nOfM': return t('workflowDesigner.approval.nOfM');
    default: return modeKey;
  }
}

const showApprovalProgress = computed(() => {
  const current = instance.value;
  if (current?.approvalModeKey === undefined || current.approvalModeKey === null) {
    return false;
  }

  const approved = current.approvedCount ?? 0;
  const rejected = current.rejectedCount ?? 0;
  const pending = current.pendingCount ?? 0;
  return approved + rejected + pending > 1;
});

const canSearch = computed(() => instanceId.value.trim().length > 0 && !loading.value);
const canListAll = computed(() => session.can('workflow.instances.list'));
const mutating = computed(() =>
  cancelling.value || pausing.value || resuming.value || recovering.value
);
const canCancel = computed(() =>
  (instance.value?.statusKey === 'active' || instance.value?.statusKey === 'suspended') &&
  session.can('workflow.instances.cancel') &&
  !mutating.value
);
const canPause = computed(() =>
  instance.value?.statusKey === 'active' &&
  session.can('workflow.instances.pause') &&
  !mutating.value
);
const canResume = computed(() =>
  instance.value?.statusKey === 'suspended' &&
  session.can('workflow.instances.resume') &&
  !mutating.value
);
const canRecover = computed(() =>
  instance.value?.statusKey === 'suspended' &&
  session.can('workflow.instances.recover') &&
  !mutating.value
);

onBeforeUnmount(() => loadController?.abort());

onMounted(() => {
  void loadList();
});

watch(activeTab, () => {
  listPage.value = 1;
  void loadList();
});

async function loadList(): Promise<void> {
  listLoading.value = true;
  problem.value = undefined;
  try {
    const query = {
      page: listPage.value,
      pageSize: listPageSize.value,
      statusKey: statusFilter.value || undefined,
      definitionKey: definitionKeyFilter.value.trim() || undefined
    };
    const result = activeTab.value === 'all' && canListAll.value
      ? await listWorkflowInstances(query)
      : await listMyWorkflowInstances(query);
    listItems.value = result.items;
    listPage.value = result.page;
    listPageSize.value = result.pageSize;
    listTotal.value = result.total;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'workflowInstances.listFailed');
  } finally {
    listLoading.value = false;
    void syncTableLayout();
  }
}

async function selectListItem(item: WorkflowInstanceListItemResponse): Promise<void> {
  instanceId.value = item.id;
  await load();
}

function openBusinessDetail(businessType: string, businessId: string): void {
  const route = findWorkflowBusinessDetailRoute(businessType);
  if (route === undefined) {
    return;
  }
  void router.push({ name: route.routeName, query: { [route.idQueryKey]: businessId } });
}

function instanceStatusLabel(statusKey: string): string {
  return statusFilterOptions.includes(statusKey as InstanceStatusKey)
    ? t(`workflowInstances.statusKey.${statusKey}` as MessageKey)
    : statusKey;
}

function instanceStatusTone(statusKey: string): 'success' | 'warning' | 'info' | 'danger' | undefined {
  switch (statusKey) {
    case 'completed':
      return 'success';
    case 'active':
      return 'info';
    case 'suspended':
      return 'warning';
    case 'rejected':
    case 'cancelled':
      return 'danger';
    default:
      return undefined;
  }
}

async function load(): Promise<void> {
  const requestedId = instanceId.value.trim();
  if (requestedId.length === 0 || loading.value) {
    return;
  }

  loadController?.abort();
  const controller = new AbortController();
  loadController = controller;
  loading.value = true;
  problem.value = undefined;
  instance.value = undefined;
  executionLogs.value = [];

  try {
    const [loadedInstance, loadedLogs] = await Promise.all([
      getWorkflowInstance(requestedId, controller.signal),
      listWorkflowInstanceExecutionLogs(requestedId, controller.signal)
    ]);
    if (!controller.signal.aborted) {
      instance.value = loadedInstance;
      executionLogs.value = loadedLogs;
    }
  } catch (error: unknown) {
    if (!controller.signal.aborted) {
      problem.value = toProblem(error);
    }
  } finally {
    if (loadController === controller) {
      loading.value = false;
    }
  }
}

async function cancelInstance(): Promise<void> {
  const current = instance.value;
  if (current === undefined || !canCancel.value) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('workflowInstances.cancelConfirm'),
      t('workflowInstances.cancelTitle'),
      {
        type: 'warning',
        confirmButtonText: t('workflowInstances.cancel'),
        cancelButtonText: t('status.back')
      }
    );
    cancelling.value = true;
    await mutateInstance(
      current.id,
      () => cancelWorkflowInstance(current.id, {
        expectedRevision: current.revision,
        reason: null,
        idempotencyKey: `cancel-${crypto.randomUUID()}`
      }),
      'workflowInstances.cancelSuccess'
    );
  } catch (error: unknown) {
    captureActionError(error);
  } finally {
    cancelling.value = false;
  }
}

async function pauseInstance(): Promise<void> {
  const current = instance.value;
  if (current === undefined || !canPause.value) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('workflowInstances.pauseConfirm'),
      t('workflowInstances.pauseTitle'),
      {
        type: 'warning',
        confirmButtonText: t('workflowInstances.pause'),
        cancelButtonText: t('status.back')
      }
    );
    pausing.value = true;
    await mutateInstance(
      current.id,
      () => pauseWorkflowInstance(current.id, {
        expectedRevision: current.revision,
        reason: null,
        idempotencyKey: `pause-${crypto.randomUUID()}`
      }),
      'workflowInstances.pauseSuccess'
    );
  } catch (error: unknown) {
    captureActionError(error);
  } finally {
    pausing.value = false;
  }
}

async function resumeInstance(): Promise<void> {
  const current = instance.value;
  if (current === undefined || !canResume.value) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('workflowInstances.resumeConfirm'),
      t('workflowInstances.resumeTitle'),
      {
        confirmButtonText: t('workflowInstances.resume'),
        cancelButtonText: t('status.back')
      }
    );
    resuming.value = true;
    await mutateInstance(
      current.id,
      () => resumeWorkflowInstance(current.id, {
        expectedRevision: current.revision,
        reason: null,
        idempotencyKey: `resume-${crypto.randomUUID()}`
      }),
      'workflowInstances.resumeSuccess'
    );
  } catch (error: unknown) {
    captureActionError(error);
  } finally {
    resuming.value = false;
  }
}

async function recoverInstance(): Promise<void> {
  const current = instance.value;
  if (current === undefined || !canRecover.value) {
    return;
  }

  try {
    const prompt = await ElMessageBox.prompt(
      t('workflowInstances.recoverConfirm'),
      t('workflowInstances.recoverTitle'),
      {
        inputType: 'textarea',
        inputValidator: (value: string) =>
          (value ?? '').trim().length > 0 || t('workflowInstances.recoverReasonRequired'),
        confirmButtonText: t('workflowInstances.recover'),
        cancelButtonText: t('status.back')
      }
    );
    recovering.value = true;
    await mutateInstance(
      current.id,
      () => recoverWorkflowInstance(current.id, {
        expectedRevision: current.revision,
        reason: prompt.value.trim(),
        idempotencyKey: `recover-${crypto.randomUUID()}`
      }),
      'workflowInstances.recoverSuccess'
    );
  } catch (error: unknown) {
    captureActionError(error);
  } finally {
    recovering.value = false;
  }
}

async function mutateInstance(
  currentId: string,
  submit: () => Promise<WorkflowInstanceResponse>,
  successKey: MessageKey
): Promise<void> {
  problem.value = undefined;
  instance.value = await submit();
  executionLogs.value = await listWorkflowInstanceExecutionLogs(currentId);
  ElMessage.success(t(successKey));
}

function captureActionError(error: unknown): void {
  if (error === 'cancel' || error === 'close') {
    return;
  }

  problem.value = toProblem(error);
}

function toProblem(
  error: unknown,
  fallbackCode: MessageKey = 'workflowInstances.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: fallbackCode,
        title: t(fallbackCode)
      };
}
</script>

<template>
  <section class="workflow-instances art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowInstances.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowInstances.caption') }}</p>

    <div v-if="instance" class="workflow-instances__toolbar">
      <div class="workflow-instances__actions">
        <el-button
          v-if="canPause"
          type="warning"
          plain
          data-testid="workflow-instance-pause"
          :loading="pausing"
          @click="pauseInstance"
        >
          {{ t('workflowInstances.pause') }}
        </el-button>
        <el-button
          v-if="canResume"
          type="primary"
          plain
          data-testid="workflow-instance-resume"
          :loading="resuming"
          @click="resumeInstance"
        >
          {{ t('workflowInstances.resume') }}
        </el-button>
        <el-button
          v-if="canRecover"
          type="warning"
          data-testid="workflow-instance-recover"
          :loading="recovering"
          @click="recoverInstance"
        >
          {{ t('workflowInstances.recover') }}
        </el-button>
        <el-button
          v-if="canCancel"
          type="danger"
          plain
          data-testid="workflow-instance-cancel"
          :loading="cancelling"
          @click="cancelInstance"
        >
          {{ t('workflowInstances.cancel') }}
        </el-button>
      </div>
    </div>

    <el-card shadow="never" class="workflow-instances__list-card art-table-card" :aria-busy="listLoading">
      <el-tabs v-model="activeTab" data-testid="workflow-instance-tabs">
        <el-tab-pane :label="t('workflowInstances.tabs.mine')" name="mine" />
        <el-tab-pane
          v-if="canListAll"
          :label="t('workflowInstances.tabs.all')"
          name="all"
        />
      </el-tabs>

      <form class="workflow-instances__filters" @submit.prevent="loadList">
        <el-select
          v-model="statusFilter"
          clearable
          data-testid="workflow-instance-status-filter"
          :placeholder="t('workflowInstances.filters.status')"
        >
          <el-option
            v-for="statusKey in statusFilterOptions"
            :key="statusKey"
            :label="instanceStatusLabel(statusKey)"
            :value="statusKey"
          />
        </el-select>
        <el-input
          v-model="definitionKeyFilter"
          clearable
          data-testid="workflow-instance-definition-filter"
          :placeholder="t('workflowInstances.filters.definitionKey')"
        />
        <el-button
          type="primary"
          native-type="submit"
          data-testid="workflow-instance-filter-apply"
          :loading="listLoading"
          @click="loadList"
        >
          {{ t('workflowInstances.filters.apply') }}
        </el-button>
      </form>

      <p v-if="!listItems.length && !listLoading" class="workflow-instances__empty">
        {{ t('workflowInstances.listEmpty') }}
      </p>
      <div v-else ref="tableMainRef" class="art-crud-table-main">
      <el-table
        v-loading="listLoading"
        :data="listItems"
        :height="tableHeight"
        row-key="id"
        class="workflow-instances__table"
        highlight-current-row
        @row-click="selectListItem"
      >
        <el-table-column :label="t('workflowInstances.business')" min-width="200">
          <template #default="{ row }">
            <button type="button" class="workflow-instances__row-btn" data-testid="workflow-instance-list-item" @click.stop="selectListItem(row as WorkflowInstanceListItemResponse)">
              <strong translate="no">{{ formatWorkflowBusinessLabel(row.businessTitle, row.businessType, row.businessId) }}</strong>
            </button>
            <small translate="no">{{ row.definitionKey }}</small>
          </template>
        </el-table-column>
        <el-table-column :label="t('workflowInstances.status')" width="110" align="center">
          <template #default="{ row }">
            <el-tag size="small" :type="instanceStatusTone(row.statusKey)">{{ instanceStatusLabel(row.statusKey) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('workflowInstances.startedAt')" width="168">
          <template #default="{ row }">
            <time translate="no" :datetime="row.startedAtUtc">{{ formatDateTime(row.startedAtUtc) }}</time>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination
        v-if="listTotal > 0"
        class="art-table-pagination"
        background
        layout="prev, pager, next, total"
        data-testid="workflow-instance-pagination"
        :current-page="listPage"
        :page-size="listPageSize"
        :total="listTotal"
        @current-change="value => { listPage = value; void loadList(); }"
      />
      </div>
    </el-card>

    <el-card shadow="never" class="workflow-instances__search-card">
      <form class="workflow-instances__search" @submit.prevent="load">
        <label for="workflow-instance-id">{{ t('workflowInstances.instanceId') }}</label>
        <div class="workflow-instances__search-row">
          <el-input
            id="workflow-instance-id"
            v-model="instanceId"
            data-testid="workflow-instance-id"
            clearable
            autocomplete="off"
            :placeholder="t('workflowInstances.instanceIdPlaceholder')"
          />
          <el-button
            type="primary"
            native-type="submit"
            data-testid="workflow-instance-search"
            :loading="loading"
            :disabled="!canSearch"
            @click="load"
          >
            {{ t('workflowInstances.search') }}
          </el-button>
        </div>
        <small>{{ t('workflowInstances.searchHint') }}</small>
      </form>
    </el-card>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
      <el-button
        v-if="problem.status === 409"
        text
        type="primary"
        data-testid="workflow-instance-conflict-refresh"
        @click="load"
      >
        {{ t('workflowInstances.conflictRefresh') }}
      </el-button>
    </div>

    <template v-if="instance">
      <section
        class="workflow-instances__summary"
        data-testid="workflow-instance-summary"
        :aria-label="t('workflowInstances.summary')"
      >
        <article class="workflow-instances__identity">
          <span>{{ t('workflowInstances.business') }}</span>
          <strong>{{ formatWorkflowBusinessLabel(instance.businessTitle, instance.businessType, instance.businessId) }}</strong>
          <el-button
            v-if="findWorkflowBusinessDetailRoute(instance.businessType)"
            type="primary"
            link
            data-testid="workflow-instance-open-business"
            @click="openBusinessDetail(instance.businessType, instance.businessId)"
          >
            {{ t('workflow.business.viewDocument') }}
          </el-button>
          <code translate="no">{{ instance.id }}</code>
        </article>
        <article>
          <span>{{ t('workflowInstances.status') }}</span>
          <strong class="workflow-instances__status" translate="no">{{ instance.statusKey }}</strong>
        </article>
        <article>
          <span>{{ t('workflowInstances.revision') }}</span>
          <strong translate="no">#{{ instance.revision }}</strong>
        </article>
        <article>
          <span>{{ t('workflowInstances.startedAt') }}</span>
          <time :datetime="instance.startedAtUtc">{{ instance.startedAtUtc }}</time>
        </article>
        <article>
          <span>{{ t('workflowInstances.activeTodo') }}</span>
          <code translate="no">{{ instance.activeTodoId ?? t('workflowInstances.none') }}</code>
        </article>
        <article data-testid="workflow-instance-timeout-status">
          <span>{{ t('workflowInstances.timeoutStatus') }}</span>
          <strong>{{ timeoutStatusLabel(instance.timeoutStatusKey) }}</strong>
          <time v-if="instance.dueAtUtc" :datetime="instance.dueAtUtc">{{ instance.dueAtUtc }}</time>
        </article>
        <article>
          <span>{{ t('workflowInstances.reminderCount') }}</span>
          <strong translate="no">{{ instance.reminderCount ?? 0 }}</strong>
        </article>
        <article v-if="instance.escalatedAtUtc">
          <span>{{ t('workflowInstances.escalatedAt') }}</span>
          <time :datetime="instance.escalatedAtUtc">{{ instance.escalatedAtUtc }}</time>
        </article>
        <article
          v-if="showApprovalProgress"
          class="workflow-instances__approval-progress"
          data-testid="workflow-instance-approval-progress"
        >
          <span>{{ t('workflowInstances.approvalProgressTitle') }}</span>
          <strong>{{ approvalModeLabel(instance.approvalModeKey ?? '') }}</strong>
          <span>{{ t('workflowTodos.approvalProgress', {
            approved: instance.approvedCount ?? 0,
            rejected: instance.rejectedCount ?? 0,
            pending: instance.pendingCount ?? 0,
            required: instance.requiredApprovalCount ?? 0
          }) }}</span>
          <code v-if="instance.activeNodeKey" translate="no">{{ instance.activeNodeKey }}</code>
        </article>
      </section>

      <el-card
        v-if="gatewayJoins.length > 0"
        shadow="never"
        class="workflow-instances__timeline-card"
        data-testid="workflow-instance-gateway-joins"
      >
        <template #header>
          <h2>{{ t('workflowInstances.gatewayJoinsTitle') }}</h2>
        </template>
        <ul class="workflow-instances__timeline">
          <li v-for="join in gatewayJoins" :key="join.id">
            <strong translate="no">{{ join.gatewayTypeKey }} · {{ join.forkNodeKey }} → {{ join.joinNodeKey }}</strong>
            <span>{{ t('workflowInstances.gatewayJoinProgress', {
              arrived: join.arrivedBranchCount,
              required: join.requiredBranchCount,
              status: join.statusKey
            }) }}</span>
            <ul v-if="join.branches.length > 0">
              <li v-for="branch in join.branches" :key="branch.branchKey">
                <code translate="no">{{ branch.branchKey }}</code>
                <time v-if="branch.arrivedAtUtc" :datetime="branch.arrivedAtUtc">{{ branch.arrivedAtUtc }}</time>
              </li>
            </ul>
          </li>
        </ul>
      </el-card>

      <el-card shadow="never" class="workflow-instances__timeline-card">
        <template #header>
          <div class="workflow-instances__timeline-heading">
            <div>
              <h2>{{ t('workflowInstances.timeline') }}</h2>
              <p>{{ t('workflowInstances.timelineCaption') }}</p>
            </div>
            <span translate="no">{{ executionLogs.length }}</span>
          </div>
        </template>

        <div v-if="executionLogs.length === 0" class="workflow-instances__empty">
          {{ t('workflowInstances.noLogs') }}
        </div>
        <ol v-else class="workflow-instances__timeline">
          <li
            v-for="log in executionLogs"
            :key="log.id"
            data-testid="workflow-execution-log"
          >
            <span class="workflow-instances__marker" aria-hidden="true"></span>
            <div class="workflow-instances__event">
              <div class="workflow-instances__event-heading">
                <strong translate="no">{{ transitionLabel(log.transitionKey) }}</strong>
                <time :datetime="log.createdAtUtc">{{ log.createdAtUtc }}</time>
              </div>
              <div class="workflow-instances__transition">
                <code translate="no">{{ log.fromStatusKey ?? '∅' }}</code>
                <span aria-hidden="true">→</span>
                <code translate="no">{{ log.toStatusKey }}</code>
              </div>
              <small v-if="log.stepId">
                {{ t('workflowInstances.step') }}
                <code translate="no">{{ log.stepId }}</code>
              </small>
            </div>
          </li>
        </ol>
      </el-card>
    </template>

    <div v-else-if="!problem && !loading && !instance" class="workflow-instances__landing">
      <span aria-hidden="true">01 — N</span>
      <p>{{ t('workflowInstances.empty') }}</p>
    </div>
  </section>
</template>

<style scoped>
.workflow-instances {
  display: grid;
  gap: 1rem;
  max-width: 1120px;
  margin: 0 auto;
}

.workflow-instances__toolbar {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 4px;
}

.workflow-instances__row-btn {
  padding: 0;
  border: none;
  background: none;
  color: var(--el-color-primary);
  font: inherit;
  cursor: pointer;
  text-align: left;
}

.workflow-instances__header h1 {
  margin: 0.2rem 0 0;
  color: var(--el-text-color-primary);
  font-size: clamp(1.5rem, 2.5vw, 2.15rem);
  letter-spacing: -0.035em;
}

.workflow-instances__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.workflow-instances__actions {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 0.5rem;
}

.workflow-instances__header p,
.workflow-instances__timeline-heading p {
  margin: 0.45rem 0 0;
  color: var(--el-text-color-secondary);
}

.workflow-instances__eyebrow {
  color: var(--el-color-primary);
  font-size: 0.72rem;
  font-weight: 800;
  letter-spacing: 0.16em;
  text-transform: uppercase;
}

.workflow-instances__search {
  display: grid;
  gap: 0.55rem;
}

.workflow-instances__search label {
  color: var(--el-text-color-primary);
  font-weight: 720;
}

.workflow-instances__search small {
  color: var(--el-text-color-secondary);
}

.workflow-instances__search-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 0.75rem;
}

.workflow-instances {
  min-height: 0;
}

.workflow-instances__list-card {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.workflow-instances__list-card :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 0;
}

.workflow-instances__list-card .art-crud-table-main {
  flex: 1;
  min-height: 200px;
}

.workflow-instances__filters {
  display: grid;
  grid-template-columns: minmax(160px, 220px) minmax(0, 1fr) auto;
  gap: 0.75rem;
}

.workflow-instances__list {
  display: grid;
  gap: 0.5rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.workflow-instances__list button {
  display: grid;
  gap: 0.35rem;
  width: 100%;
  padding: 0.85rem 1rem;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
  background: var(--el-bg-color);
  text-align: left;
  cursor: pointer;
}

.workflow-instances__list button:hover {
  border-color: var(--el-color-primary-light-5);
  background: var(--el-color-primary-light-9);
}

.workflow-instances__list small,
.workflow-instances__list time {
  color: var(--el-text-color-secondary);
  font-size: 0.75rem;
}

.workflow-instances__summary {
  display: grid;
  grid-template-columns: minmax(260px, 1.55fr) repeat(4, minmax(130px, 1fr));
  overflow: hidden;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
  background: var(--el-bg-color);
}

.workflow-instances__summary article {
  display: grid;
  align-content: start;
  gap: 0.4rem;
  min-height: 108px;
  padding: 1rem;
  border-left: 1px solid var(--el-border-color-lighter);
}

.workflow-instances__summary article:first-child {
  border-left: 0;
}

.workflow-instances__summary span {
  color: var(--el-text-color-secondary);
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

.workflow-instances__summary strong,
.workflow-instances__summary time {
  color: var(--el-text-color-primary);
}

.workflow-instances__summary .workflow-instances__identity {
  grid-column: 1 / -1;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  overflow-wrap: anywhere;
  background: linear-gradient(145deg, var(--el-color-primary-light-9), transparent 72%);
}

.workflow-instances__identity code,
.workflow-instances__summary article > code {
  overflow-wrap: anywhere;
  color: var(--el-text-color-secondary);
  font-size: 0.72rem;
}

.workflow-instances__status {
  width: fit-content;
  padding: 0.25rem 0.55rem;
  border-radius: 999px;
  color: var(--el-color-primary-dark-2) !important;
  background: var(--el-color-primary-light-9);
}

.workflow-instances__timeline-heading,
.workflow-instances__event-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.workflow-instances__timeline-heading h2 {
  margin: 0;
  font-size: 1.05rem;
}

.workflow-instances__timeline-heading > span {
  display: grid;
  min-width: 2.2rem;
  height: 2.2rem;
  place-items: center;
  border: 1px solid var(--el-border-color);
  border-radius: 50%;
  color: var(--el-text-color-secondary);
  font-weight: 750;
}

.workflow-instances__timeline {
  display: grid;
  gap: 0;
  margin: 0;
  padding: 0;
  list-style: none;
}

.workflow-instances__timeline li {
  position: relative;
  display: grid;
  grid-template-columns: 20px minmax(0, 1fr);
  gap: 0.9rem;
  padding-bottom: 1.25rem;
}

.workflow-instances__timeline li:not(:last-child)::before {
  position: absolute;
  top: 12px;
  bottom: -2px;
  left: 5px;
  width: 1px;
  background: var(--el-border-color);
  content: '';
}

.workflow-instances__marker {
  z-index: 1;
  width: 11px;
  height: 11px;
  margin-top: 0.25rem;
  border: 2px solid var(--el-bg-color);
  border-radius: 50%;
  background: var(--el-color-primary);
  box-shadow: 0 0 0 1px var(--el-color-primary);
}

.workflow-instances__event {
  display: grid;
  gap: 0.55rem;
  padding: 0 0 1rem;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.workflow-instances__event-heading time,
.workflow-instances__event small {
  color: var(--el-text-color-secondary);
  font-size: 0.75rem;
}

.workflow-instances__transition {
  display: flex;
  align-items: center;
  gap: 0.55rem;
}

.workflow-instances__transition code {
  padding: 0.2rem 0.45rem;
  border-radius: 4px;
  background: var(--el-fill-color-light);
}

.workflow-instances__landing,
.workflow-instances__empty {
  display: grid;
  min-height: 150px;
  place-items: center;
  color: var(--el-text-color-secondary);
  text-align: center;
}

.workflow-instances__landing span {
  color: var(--el-border-color-darker);
  font-size: clamp(2.5rem, 8vw, 5.5rem);
  font-weight: 800;
  letter-spacing: -0.08em;
}

.workflow-instances__landing p {
  margin: -3rem 1rem 0;
}

@media (max-width: 900px) {
  .workflow-instances__summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .workflow-instances__summary article,
  .workflow-instances__summary article:first-child {
    border-top: 1px solid var(--el-border-color-lighter);
    border-left: 1px solid var(--el-border-color-lighter);
  }

  .workflow-instances__identity {
    grid-column: 1 / -1;
  }
}

@media (max-width: 560px) {
  .workflow-instances__search-row,
  .workflow-instances__summary,
  .workflow-instances__filters {
    grid-template-columns: 1fr;
  }

  .workflow-instances__summary article,
  .workflow-instances__identity {
    grid-column: auto;
  }

  .workflow-instances__event-heading {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
