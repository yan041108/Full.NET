<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { ElMessage } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import {
  approveWorkflowTodo,
  getWorkflowTodo,
  listWorkflowTodoReturnTargets,
  listMyWorkflowTodos,
  rejectWorkflowTodo,
  returnWorkflowTodo,
  type WorkflowSubmission,
  type WorkflowTodoDetail,
  type WorkflowTodoResponse,
  type WorkflowTodoReturnTargetResponse
} from '../api/workflow-todos';
import { useAdminI18n } from '../i18n/adminI18n';
import { usePermission } from '../auth/permission';
import PermissionGate from '../components/PermissionGate.vue';
import WorkflowFormRenderer from '../workflow/WorkflowFormRenderer.vue';

const { t } = useAdminI18n();
const { can } = usePermission();
const loading = ref(false);
const acting = ref(false);
const todos = ref<WorkflowTodoResponse[]>([]);
const selected = ref<WorkflowTodoDetail>();
const fieldPatch = ref<WorkflowSubmission>({});
const comment = ref('');
const returnTargets = ref<WorkflowTodoReturnTargetResponse[]>([]);
const returnTargetStepId = ref('');
const problem = ref<FullNetProblemDetails>();
let loadController: AbortController | undefined;

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
const returnReady = computed(() =>
  requiredFieldsReady.value &&
  returnTargetStepId.value.length > 0 &&
  comment.value.trim().length > 0);

onMounted(load);
onBeforeUnmount(() => loadController?.abort());

async function load(): Promise<void> {
  loadController?.abort();
  loadController = new AbortController();
  loading.value = true;
  problem.value = undefined;
  try {
    todos.value = await listMyWorkflowTodos(loadController.signal);
  } catch (error: unknown) {
    if (!loadController.signal.aborted) {
      problem.value = toProblem(error, 'workflowTodos.loadFailed');
    }
  } finally {
    loading.value = false;
  }
}

async function openTodo(todo: WorkflowTodoResponse): Promise<void> {
  if (loading.value || acting.value) {
    return;
  }
  loading.value = true;
  problem.value = undefined;
  try {
    const [detail, targets] = await Promise.all([
      getWorkflowTodo(todo.id),
      canReturn.value ? listWorkflowTodoReturnTargets(todo.id) : Promise.resolve([])
    ]);
    selected.value = detail;
    returnTargets.value = targets;
    fieldPatch.value = {};
    comment.value = '';
    returnTargetStepId.value = '';
  } catch (error: unknown) {
    problem.value = toProblem(error, 'workflowTodos.loadFailed');
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
    await load();
  } catch (error: unknown) {
    const actionProblem = toProblem(error, 'workflowTodos.operationFailed');
    problem.value = actionProblem;
    if (actionProblem.status === 409) {
      closeDetail();
      try {
        todos.value = await listMyWorkflowTodos();
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
    await load();
  } catch (error: unknown) {
    const actionProblem = toProblem(error, 'workflowTodos.operationFailed');
    problem.value = actionProblem;
    if (actionProblem.status === 409) {
      closeDetail();
      try {
        todos.value = await listMyWorkflowTodos();
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
  fieldPatch.value = {};
  comment.value = '';
  returnTargets.value = [];
  returnTargetStepId.value = '';
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
  <section class="workflow-todos art-page-stack art-full-height" :aria-busy="loading || acting">
    <header class="workflow-todos__header">
      <div>
        <h1 data-route-heading tabindex="-1">{{ t('workflowTodos.title') }}</h1>
        <p>{{ t('workflowTodos.caption') }}</p>
      </div>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card shadow="never" class="workflow-todos__list">
      <div v-if="todos.length === 0 && !loading" class="workflow-todos__empty">
        {{ t('workflowTodos.empty') }}
      </div>
      <div v-else class="workflow-todos__table-wrap">
        <table>
          <thead>
            <tr>
              <th>{{ t('workflowTodos.instance') }}</th>
              <th>{{ t('workflowTodos.status') }}</th>
              <th>{{ t('workflowTodos.arrivedAt') }}</th>
              <th>{{ t('workflowTodos.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="todo in todos" :key="todo.id">
              <td><code translate="no">{{ todo.instanceId }}</code></td>
              <td><span class="workflow-todos__status" translate="no">{{ todo.statusKey }}</span></td>
              <td><time :datetime="todo.arrivedAtUtc">{{ todo.arrivedAtUtc }}</time></td>
              <td>
                <el-button
                  data-testid="workflow-todo-open"
                  :disabled="loading || acting"
                  @click="openTodo(todo)"
                >
                  {{ t('workflowTodos.open') }}
                </el-button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </el-card>

    <el-drawer
      :model-value="selected !== undefined"
      :title="t('workflowTodos.detail')"
      size="min(680px, 94vw)"
      @close="closeDetail"
    >
      <template v-if="selected">
        <WorkflowFormRenderer
          :schema="selected.formSchema"
          :submission="selected.submission"
          :field-policies="selected.fieldPolicies"
          @update:patch="fieldPatch = $event"
        />

        <label class="workflow-todos__comment">
          <span>{{ t('workflowTodos.comment') }}</span>
          <textarea
            v-model="comment"
            data-testid="workflow-todo-comment"
            rows="3"
            maxlength="512"
            :placeholder="t('workflowTodos.commentPlaceholder')"
          ></textarea>
        </label>

        <label v-if="canReturn" class="workflow-todos__return-target">
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

        <div class="workflow-todos__decision-bar">
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
