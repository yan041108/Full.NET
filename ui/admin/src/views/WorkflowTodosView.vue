<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
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
  approveWorkflowTodo,
  cancelWorkflowTodoCountersign,
  countersignWorkflowTodo,
  getWorkflowTodo,
  getWorkflowTodoCountersignChain,
  listWorkflowTodoReturnTargets,
  listMyWorkflowTodoHistory,
  listMyWorkflowTodos,
  rejectWorkflowTodo,
  returnWorkflowTodo,
  type WorkflowSubmission,
  type WorkflowTodoDetail,
  type WorkflowTodoListItemResponse,
  type WorkflowTodoReturnTargetResponse,
  type WorkflowTodoCountersignChain
} from '../api/workflow-todos';
import { listWorkflowRecipientCandidates } from '../api/workflow-definitions';
import { useAdminI18n } from '../i18n/adminI18n';
import { usePermission } from '../auth/permission';
import { useRouter } from 'vue-router';
import PermissionGate from '../components/PermissionGate.vue';
import WorkflowFormRenderer from '../workflow/WorkflowFormRenderer.vue';
import {
  findWorkflowBusinessDetailRoute,
  formatWorkflowBusinessLabel
} from '../workflow/workflowBusinessDetail';
import { formatAdminDateTime } from '../workflow/workflowAdminFormat';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';

const resultActionFilterOptions = [
  'approve',
  'reject',
  'return',
  'cancel',
  'cancelled'
] as const;

type TodoTabKey = 'pending' | 'history';

const { t, locale } = useAdminI18n();

function formatDateTime(value: string | null | undefined): string {
  return formatAdminDateTime(locale.value, value ?? '');
}
const router = useRouter();
const { can } = usePermission();
const activeTab = ref<TodoTabKey>('pending');
const listItems = ref<WorkflowTodoListItemResponse[]>([]);
const listPage = ref(1);
const listPageSize = ref(20);
const listTotal = ref(0);
const listLoading = ref(false);
const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(listLoading);
const definitionKeyFilter = ref('');
const businessTypeFilter = ref('');
const resultActionFilter = ref<string>();
const loading = ref(false);
const acting = ref(false);
const selected = ref<WorkflowTodoDetail>();
const selectedListItem = ref<WorkflowTodoListItemResponse>();
const fieldPatch = ref<WorkflowSubmission>({});
const comment = ref('');
const returnTargets = ref<WorkflowTodoReturnTargetResponse[]>([]);
const returnTargetStepId = ref('');
const countersignDirection = ref<'before' | 'after'>('before');
const countersignAssigneeIds = ref<string[]>([]);
const countersignCandidates = ref<Array<{ id: string; label: string }>>([]);
const countersignChain = ref<WorkflowTodoCountersignChain>();
const problem = ref<FullNetProblemDetails>();
let loadController: AbortController | undefined;

const detailReadOnly = computed(() => activeTab.value === 'history');

const requiredFieldsReady = computed(() => {
  const detail = selected.value;
  if (detail === undefined) {
    return false;
  }

  const merged = { ...detail.submission, ...fieldPatch.value };
  return Object.entries(detail.fieldPolicies).every(([key, policy]) =>
    policy !== 'required' || hasValue(merged[key]));
});
const canReturn = computed(() => can('workflow.todos.return'));
const canCountersign = computed(() => can('workflow.todos.countersign'));
const returnReady = computed(() =>
  requiredFieldsReady.value &&
  returnTargetStepId.value.length > 0 &&
  comment.value.trim().length > 0);

async function loadCountersignContext(todoId: string): Promise<void> {
  if (!canCountersign.value) {
    countersignChain.value = undefined;
    countersignCandidates.value = [];
    return;
  }

  const [candidates, chain] = await Promise.all([
    listWorkflowRecipientCandidates(1, 100).then(result =>
      result.items.map(item => ({
        id: item.id,
        label: item.displayName || item.username
      }))),
    getWorkflowTodoCountersignChain(todoId).catch(() => undefined)
  ]);
  countersignCandidates.value = candidates;
  countersignChain.value = chain;
  countersignAssigneeIds.value = [];
  countersignDirection.value = 'before';
}

async function submitCountersign(): Promise<void> {
  const detail = selected.value;
  if (acting.value || detail === undefined || countersignAssigneeIds.value.length === 0) {
    return;
  }

  acting.value = true;
  problem.value = undefined;
  try {
    await countersignWorkflowTodo(
      detail.id,
      countersignDirection.value,
      countersignAssigneeIds.value,
      detail.revision,
      comment.value.trim() || null,
      createIdempotencyKey()
    );
    ElMessage.success(t('workflowTodos.countersignSuccess'));
    closeDetail();
    await loadList();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'workflowTodos.operationFailed');
  } finally {
    acting.value = false;
  }
}

async function cancelCountersign(): Promise<void> {
  const detail = selected.value;
  if (acting.value || detail === undefined || countersignChain.value === undefined) {
    return;
  }

  acting.value = true;
  problem.value = undefined;
  try {
    await cancelWorkflowTodoCountersign(
      detail.id,
      detail.revision,
      comment.value.trim() || null,
      createIdempotencyKey()
    );
    ElMessage.success(t('workflowTodos.countersignCancelSuccess'));
    closeDetail();
    await loadList();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'workflowTodos.operationFailed');
  } finally {
    acting.value = false;
  }
}

onMounted(() => {
  void loadList();
});
onBeforeUnmount(() => loadController?.abort());

watch(activeTab, () => {
  listPage.value = 1;
  closeDetail();
  void loadList();
});

async function loadList(): Promise<void> {
  loadController?.abort();
  loadController = new AbortController();
  const signal = loadController.signal;
  listLoading.value = true;
  problem.value = undefined;
  try {
    const query = {
      page: listPage.value,
      pageSize: listPageSize.value,
      definitionKey: definitionKeyFilter.value.trim() || undefined,
      businessType: businessTypeFilter.value.trim() || undefined
    };
    const result = activeTab.value === 'history'
      ? await listMyWorkflowTodoHistory({
          ...query,
          resultActionKey: resultActionFilter.value || undefined
        }, signal)
      : await listMyWorkflowTodos(query, signal);
    if (!signal.aborted) {
      listItems.value = result.items;
      listPage.value = result.page;
      listPageSize.value = result.pageSize;
      listTotal.value = result.total;
    }
  } catch (error: unknown) {
    if (!loadController?.signal.aborted) {
      problem.value = toProblem(error, 'workflowTodos.loadFailed');
    }
  } finally {
    listLoading.value = false;
    void syncTableLayout();
  }
}

async function openTodo(todo: WorkflowTodoListItemResponse): Promise<void> {
  if (listLoading.value || loading.value || acting.value) {
    return;
  }
  loading.value = true;
  problem.value = undefined;
  selectedListItem.value = todo;
  try {
    const detail = await getWorkflowTodo(todo.id);
    selected.value = detail;
    if (!detailReadOnly.value) {
      const targets = canReturn.value
        ? await listWorkflowTodoReturnTargets(todo.id)
        : [];
      returnTargets.value = targets;
      await loadCountersignContext(todo.id);
    } else {
      returnTargets.value = [];
      countersignChain.value = undefined;
      countersignAssigneeIds.value = [];
      countersignCandidates.value = [];
    }
    fieldPatch.value = {};
    comment.value = '';
    returnTargetStepId.value = '';
  } catch (error: unknown) {
    problem.value = toProblem(error, 'workflowTodos.loadFailed');
    selectedListItem.value = undefined;
  } finally {
    loading.value = false;
  }
}

async function returnSelected(): Promise<void> {
  const detail = selected.value;
  const reason = comment.value.trim();
  if (acting.value || detail === undefined || !returnReady.value) {
    return;
  }

  acting.value = true;
  problem.value = undefined;
  try {
    await returnWorkflowTodo(
      detail.id,
      returnTargetStepId.value,
      detail.revision,
      fieldPatch.value,
      reason,
      createIdempotencyKey()
    );
    ElMessage.success(t('workflowTodos.returnSuccess'));
    closeDetail();
    await loadList();
  } catch (error: unknown) {
    const actionProblem = toProblem(error, 'workflowTodos.operationFailed');
    problem.value = actionProblem;
    if (actionProblem.status === 409) {
      closeDetail();
      try {
        await loadList();
      } catch {
        // 冲突后必须关闭过期退回动作；刷新失败时保留原始 409。
      }
      problem.value = actionProblem;
    }
  } finally {
    acting.value = false;
  }
}

async function act(action: 'approve' | 'reject'): Promise<void> {
  const detail = selected.value;
  if (acting.value || detail === undefined || !requiredFieldsReady.value) {
    return;
  }

  acting.value = true;
  problem.value = undefined;
  try {
    const execute = action === 'approve' ? approveWorkflowTodo : rejectWorkflowTodo;
    await execute(
      detail.id,
      detail.revision,
      fieldPatch.value,
      comment.value.trim() || null,
      createIdempotencyKey()
    );
    ElMessage.success(t(action === 'approve'
      ? 'workflowTodos.approveSuccess'
      : 'workflowTodos.rejectSuccess'));
    closeDetail();
    await loadList();
  } catch (error: unknown) {
    const actionProblem = toProblem(error, 'workflowTodos.operationFailed');
    problem.value = actionProblem;
    if (actionProblem.status === 409) {
      closeDetail();
      try {
        await loadList();
      } catch {
        // 冲突后必须先关闭过期动作；刷新失败时保留原始 409，避免用次生错误掩盖并发事实。
      }
      problem.value = actionProblem;
    }
  } finally {
    acting.value = false;
  }
}

function closeDetail(): void {
  selected.value = undefined;
  selectedListItem.value = undefined;
  fieldPatch.value = {};
  comment.value = '';
  returnTargets.value = [];
  returnTargetStepId.value = '';
  countersignChain.value = undefined;
  countersignAssigneeIds.value = [];
  countersignCandidates.value = [];
}

function openBusinessDetail(businessType: string, businessId: string): void {
  const route = findWorkflowBusinessDetailRoute(businessType);
  if (route === undefined) {
    return;
  }
  void router.push({ name: route.routeName, query: { [route.idQueryKey]: businessId } });
}

function resultActionLabel(actionKey: string | null | undefined): string {
  if (actionKey === null || actionKey === undefined || actionKey.length === 0) {
    return t('workflowInstances.timeoutStatus.not_configured');
  }
  const key = `workflowTodos.resultAction.${actionKey}` as MessageKey;
  return t(key);
}

function hasValue(value: unknown): boolean {
  return value !== null
    && value !== undefined
    && (typeof value !== 'string' || value.trim().length > 0);
}

function createIdempotencyKey(): string {
  if (typeof globalThis.crypto?.randomUUID === 'function') {
    return globalThis.crypto.randomUUID();
  }

  const bytes = new Uint8Array(16);
  globalThis.crypto.getRandomValues(bytes);
  return Array.from(bytes, value => value.toString(16).padStart(2, '0')).join('');
}

function approvalModeLabel(modeKey: string): string {
  switch (modeKey) {
    case 'all': return t('workflowDesigner.approval.all');
    case 'any': return t('workflowDesigner.approval.any');
    case 'nOfM': return t('workflowDesigner.approval.nOfM');
    default: return t('workflowDesigner.approval.single');
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'workflowTodos.loadFailed' | 'workflowTodos.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.workflow_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="workflow-todos art-page-stack art-full-height" :aria-busy="listLoading || loading || acting">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowTodos.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowTodos.caption') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card shadow="never" class="workflow-todos__list art-table-card" :aria-busy="listLoading">
      <el-tabs v-model="activeTab" data-testid="workflow-todo-tabs">
        <el-tab-pane :label="t('workflowTodos.tabs.pending')" name="pending" />
        <el-tab-pane :label="t('workflowTodos.tabs.history')" name="history" />
      </el-tabs>

      <form class="workflow-todos__filters" @submit.prevent="loadList">
        <el-input
          v-model="definitionKeyFilter"
          clearable
          data-testid="workflow-todo-definition-filter"
          :placeholder="t('workflowTodos.filters.definitionKey')"
        />
        <el-input
          v-model="businessTypeFilter"
          clearable
          data-testid="workflow-todo-business-type-filter"
          :placeholder="t('workflowTodos.filters.businessType')"
        />
        <el-select
          v-if="activeTab === 'history'"
          v-model="resultActionFilter"
          clearable
          data-testid="workflow-todo-result-action-filter"
          :placeholder="t('workflowTodos.filters.resultAction')"
        >
          <el-option
            v-for="actionKey in resultActionFilterOptions"
            :key="actionKey"
            :label="resultActionLabel(actionKey)"
            :value="actionKey"
          />
        </el-select>
        <el-button
          type="primary"
          native-type="submit"
          data-testid="workflow-todo-filter-apply"
          :loading="listLoading"
          @click="loadList"
        >
          {{ t('workflowTodos.filters.apply') }}
        </el-button>
      </form>

      <div
        v-if="!listItems.length && !listLoading"
        class="workflow-todos__empty"
      >
        {{ t(activeTab === 'history' ? 'workflowTodos.historyEmpty' : 'workflowTodos.empty') }}
      </div>
      <div v-else ref="tableMainRef" class="art-crud-table-main">
      <el-table v-loading="listLoading" :data="listItems" :height="tableHeight" row-key="id" class="workflow-todos__table">
        <el-table-column :label="t('workflowTodos.business')" min-width="200">
          <template #default="{ row }">
            <div class="workflow-todos__business-cell">
              <strong translate="no">{{ formatWorkflowBusinessLabel(row.businessTitle, row.businessType, row.businessId) }}</strong>
              <small translate="no">{{ row.definitionKey }}</small>
            </div>
          </template>
        </el-table-column>
        <el-table-column :label="t('workflowTodos.node')" min-width="120" show-overflow-tooltip>
          <template #default="{ row }"><span translate="no">{{ row.nodeKey }}</span></template>
        </el-table-column>
        <el-table-column v-if="activeTab === 'history'" :label="t('workflowTodos.resultAction')" width="120">
          <template #default="{ row }">
            <el-tag size="small" translate="no">{{ resultActionLabel(row.resultActionKey) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t(activeTab === 'history' ? 'workflowTodos.completedAt' : 'workflowTodos.arrivedAt')" width="168">
          <template #default="{ row }">
            <time
              translate="no"
              :datetime="activeTab === 'history' ? (row.completedAtUtc ?? '') : row.arrivedAtUtc"
            >
              {{ formatDateTime(activeTab === 'history' ? row.completedAtUtc : row.arrivedAtUtc) }}
            </time>
          </template>
        </el-table-column>
        <el-table-column :label="t('workflowTodos.actions')" width="200" fixed="right">
          <template #default="{ row }">
            <el-button size="small" data-testid="workflow-todo-open" :disabled="listLoading || loading || acting" @click="openTodo(row as WorkflowTodoListItemResponse)">
              {{ t(detailReadOnly ? 'workflowTodos.view' : 'workflowTodos.open') }}
            </el-button>
            <el-button
              v-if="findWorkflowBusinessDetailRoute(row.businessType)"
              link
              type="primary"
              size="small"
              data-testid="workflow-todo-open-business"
              @click="openBusinessDetail(row.businessType, row.businessId)"
            >
              {{ t('workflow.business.viewDocument') }}
            </el-button>
          </template>
        </el-table-column>
      </el-table>
      <el-pagination
        v-if="listTotal > 0"
        class="art-table-pagination"
        background
        layout="prev, pager, next, total"
        data-testid="workflow-todo-pagination"
        :current-page="listPage"
        :page-size="listPageSize"
        :total="listTotal"
        @current-change="value => { listPage = value; void loadList(); }"
      />
      </div>
    </el-card>

    <el-drawer
      :model-value="selected !== undefined"
      :title="t(detailReadOnly ? 'workflowTodos.historyDetail' : 'workflowTodos.detail')"
      size="min(680px, 94vw)"
      @close="closeDetail"
    >
      <template v-if="selected">
        <div
          v-if="detailReadOnly && selectedListItem"
          class="workflow-todos__history-snapshot"
          data-testid="workflow-todo-history-snapshot"
        >
          <el-tag translate="no">{{ resultActionLabel(selectedListItem.resultActionKey) }}</el-tag>
          <time :datetime="selectedListItem.completedAtUtc ?? ''">
            {{ selectedListItem.completedAtUtc }}
          </time>
        </div>
        <div
          v-else-if="selected.approvedCount + selected.rejectedCount + selected.pendingCount > 1"
          class="workflow-todos__approval-progress"
          data-testid="workflow-approval-progress"
        >
          <strong>{{ approvalModeLabel(selected.approvalModeKey) }}</strong>
          <span>{{ t('workflowTodos.approvalProgress', {
            approved: selected.approvedCount,
            rejected: selected.rejectedCount,
            pending: selected.pendingCount,
            required: selected.requiredApprovalCount
          }) }}</span>
        </div>
        <WorkflowFormRenderer
          :schema="selected.formSchema"
          :submission="selected.submission"
          :instance-id="selected.instanceId"
          :field-policies="detailReadOnly
            ? Object.fromEntries(Object.keys(selected.fieldPolicies).map(key => [key, 'readOnly']))
            : selected.fieldPolicies"
          @update:patch="fieldPatch = $event"
        />

        <label v-if="!detailReadOnly" class="workflow-todos__comment">
          <span>{{ t('workflowTodos.comment') }}</span>
          <textarea
            v-model="comment"
            data-testid="workflow-todo-comment"
            rows="3"
            maxlength="512"
            :placeholder="t('workflowTodos.commentPlaceholder')"
          ></textarea>
        </label>

        <label v-if="!detailReadOnly && canReturn" class="workflow-todos__return-target">
          <span>{{ t('workflowTodos.returnTarget') }}</span>
          <select
            v-model="returnTargetStepId"
            data-testid="workflow-todo-return-target"
            :disabled="acting || returnTargets.length === 0"
          >
            <option value="" disabled>{{ t(returnTargets.length === 0
              ? 'workflowTodos.noReturnTargets'
              : 'workflowTodos.returnTargetPlaceholder') }}</option>
            <option
              v-for="target in returnTargets"
              :key="target.stepId"
              :value="target.stepId"
            >
              {{ target.nodeKey }} · {{ target.completedAtUtc }}
            </option>
          </select>
        </label>

        <PermissionGate v-if="!detailReadOnly" code="workflow.todos.countersign">
          <div v-if="countersignChain" class="workflow-todos__countersign-chain">
            <strong>{{ t('workflowTodos.countersignChain') }}</strong>
            <ol>
              <li
                v-for="item in countersignChain.items"
                :key="item.itemId"
                translate="no"
              >
                #{{ item.sequenceNo }} · {{ item.assigneeUserId }} · {{ item.statusKey }}
              </li>
            </ol>
            <el-button
              data-testid="workflow-todo-countersign-cancel"
              :loading="acting"
              @click="cancelCountersign"
            >
              {{ t('workflowTodos.countersignCancel') }}
            </el-button>
          </div>
          <div v-else class="workflow-todos__countersign">
            <label>
              <span>{{ t('workflowTodos.countersignDirection') }}</span>
              <select v-model="countersignDirection" data-testid="workflow-todo-countersign-direction">
                <option value="before">{{ t('workflowTodos.countersignBefore') }}</option>
                <option value="after">{{ t('workflowTodos.countersignAfter') }}</option>
              </select>
            </label>
            <label>
              <span>{{ t('workflowTodos.countersignAssignees') }}</span>
              <select
                v-model="countersignAssigneeIds"
                data-testid="workflow-todo-countersign-assignees"
                multiple
                :disabled="acting || countersignCandidates.length === 0"
              >
                <option
                  v-for="candidate in countersignCandidates"
                  :key="candidate.id"
                  :value="candidate.id"
                >
                  {{ candidate.label }}
                </option>
              </select>
            </label>
            <el-button
              data-testid="workflow-todo-countersign-submit"
              :loading="acting"
              :disabled="countersignAssigneeIds.length === 0"
              @click="submitCountersign"
            >
              {{ t('workflowTodos.countersign') }}
            </el-button>
          </div>
        </PermissionGate>

        <div v-if="!detailReadOnly" class="workflow-todos__decision-bar">
          <el-button :disabled="acting" @click="closeDetail">
            {{ t('workflowTodos.close') }}
          </el-button>
          <PermissionGate code="workflow.todos.reject">
            <el-button
              type="danger"
              plain
              data-testid="workflow-todo-reject"
              :loading="acting"
              :disabled="!requiredFieldsReady"
              @click="act('reject')"
            >
              {{ t('workflowTodos.reject') }}
            </el-button>
          </PermissionGate>
          <PermissionGate code="workflow.todos.approve">
            <el-button
              type="primary"
              data-testid="workflow-todo-approve"
              :loading="acting"
              :disabled="!requiredFieldsReady"
              @click="act('approve')"
            >
              {{ t('workflowTodos.approve') }}
            </el-button>
          </PermissionGate>
          <PermissionGate code="workflow.todos.return">
            <el-button
              type="warning"
              plain
              data-testid="workflow-todo-return"
              :loading="acting"
              :disabled="!returnReady"
              @click="returnSelected"
            >
              {{ t('workflowTodos.return') }}
            </el-button>
          </PermissionGate>
        </div>
      </template>
    </el-drawer>
  </section>
</template>

<style scoped>
.workflow-todos {
  display: grid;
  gap: 1rem;
  min-height: 0;
}

.workflow-todos__list {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.workflow-todos__list :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 0;
}

.workflow-todos__list .art-crud-table-main {
  flex: 1;
  min-height: 200px;
}

.workflow-todos__header h1 {
  margin: 0;
  color: var(--el-text-color-primary);
  font-size: clamp(1.35rem, 2vw, 1.8rem);
}

.workflow-todos__header p {
  margin: 0.35rem 0 0;
  color: var(--el-text-color-secondary);
}

.workflow-todos__filters {
  display: flex;
  flex-wrap: wrap;
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.workflow-todos__filters .el-input,
.workflow-todos__filters .el-select {
  width: min(220px, 100%);
}

.workflow-todos__history-snapshot {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  margin-bottom: 1rem;
  padding: 0.75rem 1rem;
  border: 1px solid var(--el-border-color-light);
  border-radius: var(--el-border-radius-base);
  background: var(--el-fill-color-light);
}

.workflow-todos td small {
  display: block;
  margin-top: 0.25rem;
  color: var(--el-text-color-secondary);
  font-size: 0.75rem;
}

.workflow-todos__approval-progress {
  display: flex;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
  padding: 0.75rem 1rem;
  border: 1px solid var(--el-border-color-light);
  border-radius: var(--el-border-radius-base);
  background: var(--el-fill-color-light);
  color: var(--el-text-color-regular);
}

.workflow-todos__business-cell {
  display: grid;
  gap: 4px;
}

.workflow-todos__table-wrap {
  overflow-x: auto;
}

.workflow-todos table {
  width: 100%;
  border-collapse: collapse;
}

.workflow-todos th,
.workflow-todos td {
  padding: 0.8rem;
  border-bottom: 1px solid var(--el-border-color-lighter);
  text-align: left;
}

.workflow-todos th {
  color: var(--el-text-color-secondary);
  font-size: 0.78rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.workflow-todos code {
  font-size: 0.75rem;
}

.workflow-todos__status {
  display: inline-flex;
  padding: 0.2rem 0.55rem;
  border-radius: 999px;
  color: var(--el-color-primary-dark-2);
  background: var(--el-color-primary-light-9);
  font-size: 0.78rem;
  font-weight: 700;
}

.workflow-todos__empty {
  padding: 3rem 1rem;
  color: var(--el-text-color-secondary);
  text-align: center;
}

.workflow-todos__comment {
  display: grid;
  gap: 0.45rem;
  margin-top: 1rem;
  color: var(--el-text-color-regular);
  font-weight: 650;
}

.workflow-todos__return-target {
  display: grid;
  gap: 0.45rem;
  margin-top: 1rem;
  color: var(--el-text-color-regular);
  font-weight: 650;
}

.workflow-todos__comment textarea,
.workflow-todos__return-target select {
  box-sizing: border-box;
  width: 100%;
  padding: 0.65rem 0.75rem;
  border: 1px solid var(--el-border-color);
  border-radius: var(--el-border-radius-base);
  color: var(--el-text-color-primary);
  background: var(--el-fill-color-blank);
  font: inherit;
}

.workflow-todos__comment textarea {
  resize: vertical;
}

.workflow-todos__decision-bar {
  display: flex;
  justify-content: flex-end;
  gap: 0.65rem;
  margin-top: 1.25rem;
  padding-top: 1rem;
  border-top: 1px solid var(--el-border-color-lighter);
}
</style>
