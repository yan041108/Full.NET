<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElInput, ElOption, ElSelect, ElTag } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import {
  cancelDataApprovalRequest,
  createDataApprovalRequest,
  getDataApprovalRequest,
  listDataApprovalRequests,
  type DataApprovalRequestResponse
} from '../api/data-approval-requests';

const session = useSessionStore();
const { t } = useAdminI18n();
const requests = ref<DataApprovalRequestResponse[]>([]);
const selectedRequestId = ref<string>();
const selectedRequest = ref<DataApprovalRequestResponse>();
const scenarioKey = ref('serial_numbers.host_rule.update');
const targetEntityId = ref('');
const proposedChangeJson = ref('{"displayName":"Updated rule","description":null,"scope":0,"resetInterval":1,"pattern":"INV-{sequence:5}","minimumValue":1,"maximumValue":99999,"displayOrder":10,"isEnabled":true,"version":1}');
const workflowDefinitionKey = ref('');
const idempotencyKey = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canCreate = computed(() => session.can('data_approvals.requests.create'));
const canCancel = computed(() => session.can('data_approvals.requests.cancel'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listDataApprovalRequests({ page: 1, pageSize: 20 });
    requests.value = result.items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dataApprovalRequests.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function selectRequest(id: string): Promise<void> {
  selectedRequestId.value = id;
  loading.value = true;
  problem.value = undefined;
  try {
    selectedRequest.value = await getDataApprovalRequest(id);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dataApprovalRequests.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function submitCreate(): Promise<void> {
  changing.value = true;
  problem.value = undefined;
  try {
    const created = await createDataApprovalRequest({
      scenarioKey: scenarioKey.value,
      targetEntityId: targetEntityId.value,
      proposedChangeJson: proposedChangeJson.value,
      workflowDefinitionKey: workflowDefinitionKey.value,
      idempotencyKey: idempotencyKey.value || crypto.randomUUID()
    });
    await load();
    await selectRequest(created.id);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dataApprovalRequests.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function submitCancel(): Promise<void> {
  if (!selectedRequest.value) return;
  changing.value = true;
  problem.value = undefined;
  try {
    selectedRequest.value = await cancelDataApprovalRequest(selectedRequest.value.id, {
      idempotencyKey: crypto.randomUUID()
    });
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dataApprovalRequests.operationFailed');
  } finally {
    changing.value = false;
  }
}

function statusTagType(statusKey: string): 'success' | 'warning' | 'danger' | 'info' {
  if (statusKey === 'approved') return 'success';
  if (statusKey === 'rejected' || statusKey === 'cancelled') return 'danger';
  if (statusKey === 'in_review') return 'warning';
  return 'info';
}

function toProblem(error: unknown, fallbackCode: string): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) return error;
  return { type: 'about:blank', status: 500, code: fallbackCode, title: fallbackCode };
}
</script>

<template>
  <section class="page-shell">
    <header class="page-header">
      <p class="eyebrow">{{ t('dataApprovalRequests.eyebrow') }}</p>
      <h1>{{ t('dataApprovalRequests.title') }}</h1>
      <p>{{ t('dataApprovalRequests.description') }}</p>
    </header>

    <p v-if="problem" role="alert" class="problem-banner">{{ problem.code }}</p>

    <ElCard>
      <template #header>{{ t('dataApprovalRequests.listTitle') }}</template>
      <div v-if="loading && !requests.length">{{ t('dataApprovalRequests.loadFailed') }}</div>
      <div v-else-if="!requests.length">{{ t('dataApprovalRequests.emptyList') }}</div>
      <ul v-else class="request-list">
        <li v-for="item in requests" :key="item.id">
          <button
            type="button"
            data-testid="data-approval-load"
            @click="selectRequest(item.id)"
          >
            <strong>{{ item.scenarioKey }}</strong>
            <ElTag :type="statusTagType(item.statusKey)" data-testid="data-approval-status">
              {{ item.statusKey }}
            </ElTag>
          </button>
        </li>
      </ul>
    </ElCard>

    <PermissionGate code="data_approvals.requests.create">
      <ElCard>
        <template #header>{{ t('dataApprovalRequests.createTitle') }}</template>
        <div class="form-grid">
          <label>{{ t('dataApprovalRequests.fieldScenario') }}
            <ElSelect v-model="scenarioKey" data-testid="data-approval-scenario">
              <ElOption value="serial_numbers.host_rule.update" :label="t('dataApprovalRequests.scenarioSerialRuleUpdate')" />
            </ElSelect>
          </label>
          <label>{{ t('dataApprovalRequests.fieldTargetEntityId') }}
            <ElInput v-model="targetEntityId" data-testid="data-approval-target-id" />
          </label>
          <label>{{ t('dataApprovalRequests.fieldWorkflowDefinitionKey') }}
            <ElInput v-model="workflowDefinitionKey" data-testid="data-approval-workflow-key" />
          </label>
          <label>{{ t('dataApprovalRequests.fieldProposedChangeJson') }}
            <ElInput v-model="proposedChangeJson" type="textarea" :rows="6" data-testid="data-approval-proposed-json" />
          </label>
          <label>{{ t('dataApprovalRequests.fieldIdempotencyKey') }}
            <ElInput v-model="idempotencyKey" data-testid="data-approval-idempotency-key" />
          </label>
        </div>
        <ElButton
          v-if="canCreate"
          type="primary"
          data-testid="data-approval-create"
          :loading="changing"
          @click="submitCreate"
        >
          {{ t('dataApprovalRequests.create') }}
        </ElButton>
      </ElCard>
    </PermissionGate>

    <ElCard v-if="selectedRequest">
      <template #header>{{ t('dataApprovalRequests.detailTitle') }}</template>
      <p data-testid="data-approval-detail-status">
        {{ t('dataApprovalRequests.fieldStatus') }}:
        <ElTag :type="statusTagType(selectedRequest.statusKey)">{{ selectedRequest.statusKey }}</ElTag>
      </p>
      <p data-testid="data-approval-detail-workflow">
        {{ t('dataApprovalRequests.fieldWorkflowInstanceId') }}: {{ selectedRequest.workflowInstanceId ?? '—' }}
      </p>
      <pre data-testid="data-approval-before">{{ selectedRequest.beforeSnapshotJson ?? '—' }}</pre>
      <pre data-testid="data-approval-after">{{ selectedRequest.afterSnapshotJson }}</pre>
      <PermissionGate code="data_approvals.requests.cancel">
        <ElButton
          v-if="canCancel && (selectedRequest.statusKey === 'pending' || selectedRequest.statusKey === 'in_review')"
          type="danger"
          data-testid="data-approval-cancel"
          :loading="changing"
          @click="submitCancel"
        >
          {{ t('dataApprovalRequests.cancel') }}
        </ElButton>
      </PermissionGate>
    </ElCard>
  </section>
</template>

<style scoped>
.request-list { list-style: none; padding: 0; margin: 0; }
.request-list button { display: flex; gap: 0.75rem; align-items: center; width: 100%; text-align: left; padding: 0.5rem 0; background: none; border: 0; cursor: pointer; }
.form-grid { display: grid; gap: 1rem; margin-bottom: 1rem; }
pre { white-space: pre-wrap; background: var(--el-fill-color-light); padding: 0.75rem; border-radius: 0.5rem; }
</style>
