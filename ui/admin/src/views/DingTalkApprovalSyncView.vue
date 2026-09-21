<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag,
  type FormInstance,
  type FormRules
} from 'element-plus';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import {
  createDingTalkApprovalSync,
  listDingTalkApprovalSync,
  retryDingTalkApprovalSync,
  type DingTalkApprovalSyncResponse
} from '../api/dingtalk-approval-sync';

defineOptions({ name: 'DingTalkApprovalSyncView' });

/** 钉钉审批镜像只用于可观测同步；Full.NET Workflow 保持流程权威，页面不伪装双向驱动。 */
const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<DingTalkApprovalSyncResponse[]>([]);
const selectedId = ref<string>();
const selected = ref<DingTalkApprovalSyncResponse>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(loading);
const saving = ref(false);
const retrying = ref(false);
const errorMessage = ref<string>();
const createFormRef = ref<FormInstance>();
const createForm = reactive({
  workflowInstanceId: '',
  originatorUserId: '',
  deptId: '',
  title: '',
  summary: ''
});
const canRead = computed(() => session.can('notifications.dingtalk_approval_sync.read'));
const canCreate = computed(() => session.can('notifications.dingtalk_approval_sync.create'));
const canRetry = computed(() => session.can('notifications.dingtalk_approval_sync.retry'));
const showRightPanel = computed(() => canRead.value);

const createRules = computed<FormRules>(() => {
  const requiredMessage = t('dingtalkApprovalSync.validationRequired');
  const requiredTextRule = {
    validator: (_rule: unknown, value: unknown, callback: (error?: Error) => void) => {
      if (typeof value !== 'string' || !value.trim()) {
        callback(new Error(requiredMessage));
        return;
      }
      callback();
    },
    trigger: ['blur', 'change'] as const
  };
  return {
    workflowInstanceId: [requiredTextRule],
    originatorUserId: [requiredTextRule],
    title: [requiredTextRule],
    deptId: [
      {
        validator: (_rule: unknown, value: unknown, callback: (error?: Error) => void) => {
          const parsed = Number(typeof value === 'string' ? value.trim() : value);
          if (!Number.isFinite(parsed) || parsed <= 0) {
            callback(new Error(t('dingtalkApprovalSync.validationDeptId')));
            return;
          }
          callback();
        },
        trigger: ['blur', 'change'] as const
      }
    ]
  };
});

onMounted(load);

async function load(): Promise<void> {
  if (!canRead.value) {
    return;
  }
  loading.value = true;
  errorMessage.value = undefined;
  try {
    const result = await listDingTalkApprovalSync(page.value, pageSize.value);
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
    if (selectedId.value) {
      selected.value = items.value.find(item => item.id === selectedId.value) ?? selected.value;
    }
  } catch {
    errorMessage.value = t('dingtalkApprovalSync.loadFailed');
  } finally {
    loading.value = false;
    void syncTableLayout();
  }
}

function selectItem(item: DingTalkApprovalSyncResponse): void {
  selectedId.value = item.id;
  selected.value = item;
}

function passesCreateValidation(): boolean {
  const parsedDeptId = Number(createForm.deptId.trim());
  return Boolean(createForm.workflowInstanceId.trim())
    && Boolean(createForm.originatorUserId.trim())
    && Boolean(createForm.title.trim())
    && Number.isFinite(parsedDeptId)
    && parsedDeptId > 0;
}

async function validateCreateForm(): Promise<boolean> {
  const form = createFormRef.value;
  if (!form) {
    return false;
  }
  let formValid = true;
  try {
    await form.validate();
  } catch {
    formValid = false;
  }
  if (!passesCreateValidation()) {
    const fields = ['workflowInstanceId', 'originatorUserId', 'deptId', 'title'] as const;
    for (const field of fields) {
      try {
        await form.validateField(field);
      } catch {
        /* 单字段错误由表单项展示 */
      }
    }
    return false;
  }
  return formValid;
}

function resetCreateForm(): void {
  createForm.workflowInstanceId = '';
  createForm.originatorUserId = '';
  createForm.deptId = '';
  createForm.title = '';
  createForm.summary = '';
  createFormRef.value?.clearValidate();
}

async function createSync(): Promise<void> {
  if (!canCreate.value || saving.value || !(await validateCreateForm())) {
    return;
  }
  const parsedDeptId = Number(createForm.deptId.trim());
  saving.value = true;
  errorMessage.value = undefined;
  try {
    const created = await createDingTalkApprovalSync({
      workflowInstanceId: createForm.workflowInstanceId.trim(),
      originatorUserId: createForm.originatorUserId.trim(),
      deptId: parsedDeptId,
      title: createForm.title.trim(),
      summary: createForm.summary.trim() || null
    });
    resetCreateForm();
    selectItem(created);
    ElMessage.success(t('dingtalkApprovalSync.createSuccess'));
    await load();
  } catch {
    errorMessage.value = t('dingtalkApprovalSync.operationFailed');
  } finally {
    saving.value = false;
  }
}

async function retrySelected(): Promise<void> {
  const current = selected.value;
  if (!current || !canRetry.value || retrying.value) {
    return;
  }
  retrying.value = true;
  errorMessage.value = undefined;
  try {
    const updated = await retryDingTalkApprovalSync(current.id);
    selectItem(updated);
    ElMessage.success(t('dingtalkApprovalSync.retrySuccess'));
    await load();
  } catch {
    errorMessage.value = t('dingtalkApprovalSync.operationFailed');
  } finally {
    retrying.value = false;
  }
}

function canRetryStatus(statusKey: string): boolean {
  return statusKey === 'pending_outbound' || statusKey === 'outbound_failed';
}

function statusText(statusKey: string): string {
  const key = `dingtalkApprovalSync.status.${statusKey}`;
  const translated = translateRuntimeMessage(t, key);
  return translated === key ? statusKey : translated;
}

function statusTone(statusKey: string): 'success' | 'warning' | 'info' | 'danger' | undefined {
  switch (statusKey) {
    case 'completed':
      return 'success';
    case 'running':
      return 'warning';
    case 'outbound_failed':
    case 'terminated':
      return 'danger';
    case 'pending_outbound':
      return 'info';
    default:
      return undefined;
  }
}
</script>

<template>
  <section class="dingtalk-approval-sync-view art-page-stack art-full-height">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('dingtalkApprovalSync.title') }}</h1>
    <p class="art-sr-heading">{{ t('dingtalkApprovalSync.description') }}</p>

    <ElAlert
      type="info"
      class="dingtalk-sync-notice"
      :title="t('dingtalkApprovalSync.authorityNotice')"
      :closable="false"
      show-icon
    />

    <ElAlert
      v-if="errorMessage"
      type="error"
      :title="errorMessage"
      :closable="false"
      show-icon
    />

    <div v-if="canRead" class="dingtalk-sync-layout art-split-layout">
      <ElCard class="dingtalk-sync-list art-table-card" shadow="never">
        <template #header>
          <div class="dingtalk-sync-list__header">
            <h2>{{ t('dingtalkApprovalSync.listTitle') }}</h2>
            <ElButton :loading="loading" data-testid="dingtalk-sync-load" @click="load">
              {{ t('dingtalkApprovalSync.refresh') }}
            </ElButton>
          </div>
        </template>

        <div ref="tableMainRef" data-testid="dingtalk-sync-list" class="art-crud-table-main dingtalk-sync-table-wrap">
          <ElTable
            v-loading="loading"
            :data="items"
            :height="tableHeight"
            class="dingtalk-sync-table"
            highlight-current-row
            row-key="id"
            empty-text=""
            :current-row-key="selectedId"
            @row-click="selectItem"
          >
            <ElTableColumn
              :label="t('dingtalkApprovalSync.fields.title')"
              min-width="160"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <button
                  type="button"
                  class="dingtalk-sync-table__title"
                  translate="no"
                  @click.stop="selectItem(row)"
                >
                  {{ row.title }}
                </button>
              </template>
            </ElTableColumn>

            <ElTableColumn
              :label="t('dingtalkApprovalSync.fields.workflowInstanceId')"
              min-width="200"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ row.workflowInstanceId }}</span>
              </template>
            </ElTableColumn>

            <ElTableColumn :label="t('dingtalkApprovalSync.fields.status')" width="120" align="center">
              <template #default="{ row }">
                <ElTag size="small" :type="statusTone(row.statusKey)">
                  {{ statusText(row.statusKey) }}
                </ElTag>
              </template>
            </ElTableColumn>

            <template #empty>
              <p class="art-empty-state">{{ t('dingtalkApprovalSync.emptyList') }}</p>
            </template>
          </ElTable>
        <ElPagination
          v-if="total > 0"
          class="art-table-pagination"
          background
          layout="total, prev, pager, next"
          :current-page="page"
          :page-size="pageSize"
          :total="total"
          @current-change="value => { page = value; void load(); }"
          @size-change="value => { pageSize = value; void load(); }"
        />
        </div>
      </ElCard>

      <div v-if="showRightPanel" class="dingtalk-sync-side">
        <PermissionGate code="notifications.dingtalk_approval_sync.create">
          <ElCard class="dingtalk-sync-create art-form-card" shadow="never">
            <template #header>
              <h2>{{ t('dingtalkApprovalSync.createTitle') }}</h2>
            </template>

            <ElForm
              ref="createFormRef"
              :model="createForm"
              :rules="createRules"
              label-position="top"
              class="dingtalk-sync-create__grid"
              @submit.prevent
            >
              <ElFormItem
                prop="workflowInstanceId"
                :label="t('dingtalkApprovalSync.fields.workflowInstanceId')"
                required
              >
                <ElInput
                  v-model="createForm.workflowInstanceId"
                  data-testid="dingtalk-sync-workflow-instance-id"
                  :placeholder="t('dingtalkApprovalSync.workflowInstanceIdPlaceholder')"
                />
              </ElFormItem>
              <ElFormItem
                prop="originatorUserId"
                :label="t('dingtalkApprovalSync.fields.originatorUserId')"
                required
              >
                <ElInput
                  v-model="createForm.originatorUserId"
                  data-testid="dingtalk-sync-originator-user-id"
                  :placeholder="t('dingtalkApprovalSync.originatorUserIdPlaceholder')"
                />
              </ElFormItem>
              <ElFormItem
                prop="deptId"
                :label="t('dingtalkApprovalSync.fields.deptId')"
                required
              >
                <ElInput
                  v-model="createForm.deptId"
                  data-testid="dingtalk-sync-dept-id"
                  inputmode="numeric"
                  :placeholder="t('dingtalkApprovalSync.deptIdPlaceholder')"
                />
              </ElFormItem>
              <ElFormItem prop="title" :label="t('dingtalkApprovalSync.fields.title')" required>
                <ElInput
                  v-model="createForm.title"
                  data-testid="dingtalk-sync-title"
                  :placeholder="t('dingtalkApprovalSync.titlePlaceholder')"
                />
              </ElFormItem>
              <ElFormItem
                class="dingtalk-sync-create__full"
                prop="summary"
                :label="t('dingtalkApprovalSync.fields.summary')"
              >
                <ElInput
                  v-model="createForm.summary"
                  data-testid="dingtalk-sync-summary"
                  type="textarea"
                  :rows="2"
                  :placeholder="t('dingtalkApprovalSync.summaryPlaceholder')"
                />
              </ElFormItem>
              <ElFormItem class="dingtalk-sync-create__full">
                <ElButton
                  type="primary"
                  :loading="saving"
                  data-testid="dingtalk-sync-create"
                  @click="createSync"
                >
                  {{ t('dingtalkApprovalSync.register') }}
                </ElButton>
              </ElFormItem>
            </ElForm>
          </ElCard>
        </PermissionGate>

        <ElCard
          class="dingtalk-sync-detail art-form-card"
          shadow="never"
          data-testid="dingtalk-sync-detail"
        >
          <template #header>
            <h2>{{ t('dingtalkApprovalSync.detailTitle') }}</h2>
          </template>

          <p v-if="!selected" class="art-empty-state dingtalk-sync-detail__empty">
            {{ t('dingtalkApprovalSync.selectDetail') }}
          </p>

          <template v-else>
            <dl class="sync-detail">
              <dt>{{ t('dingtalkApprovalSync.fields.status') }}</dt>
              <dd>
                <ElTag size="small" :type="statusTone(selected.statusKey)">
                  {{ statusText(selected.statusKey) }}
                </ElTag>
              </dd>
              <dt>{{ t('dingtalkApprovalSync.fields.processInstanceId') }}</dt>
              <dd translate="no">{{ selected.dingTalkProcessInstanceId ?? '—' }}</dd>
              <dt>{{ t('dingtalkApprovalSync.fields.externalStatus') }}</dt>
              <dd translate="no">{{ selected.externalStatusKey ?? '—' }}</dd>
              <dt>{{ t('dingtalkApprovalSync.fields.externalResult') }}</dt>
              <dd translate="no">{{ selected.externalResultKey ?? '—' }}</dd>
              <dt>{{ t('dingtalkApprovalSync.fields.lastError') }}</dt>
              <dd translate="no">{{ selected.lastErrorCode ?? '—' }}</dd>
            </dl>
            <PermissionGate code="notifications.dingtalk_approval_sync.retry">
              <ElButton
                v-if="canRetryStatus(selected.statusKey)"
                type="warning"
                :loading="retrying"
                data-testid="dingtalk-sync-retry"
                @click="retrySelected"
              >
                {{ t('dingtalkApprovalSync.retry') }}
              </ElButton>
            </PermissionGate>
          </template>
        </ElCard>
      </div>
    </div>
  </section>
</template>

<style scoped>
.dingtalk-approval-sync-view {
  min-height: 0;
}

.dingtalk-sync-notice {
  margin-bottom: 0;
}

.dingtalk-sync-layout {
  display: flex;
  flex: 1;
  gap: 12px;
  min-height: 0;
}

.dingtalk-sync-list {
  flex: 1 1 0;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.dingtalk-sync-list :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 0;
}

.dingtalk-sync-list :deep(.el-card__header) {
  padding: 12px 16px;
}

.dingtalk-sync-list__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.dingtalk-sync-list__header h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}

.dingtalk-sync-table-wrap {
  flex: 1;
  min-height: 200px;
}

.dingtalk-sync-table :deep(.el-table__row) {
  cursor: pointer;
}

.dingtalk-sync-table__title {
  padding: 0;
  border: none;
  background: none;
  color: var(--el-color-primary);
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.dingtalk-sync-list__pagination {
  display: flex;
  justify-content: flex-end;
  padding-top: 12px;
}

.dingtalk-sync-side {
  flex: 0 0 360px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 300px;
  max-width: 420px;
}

.dingtalk-sync-create :deep(.el-card__header),
.dingtalk-sync-detail :deep(.el-card__header) {
  padding: 12px 16px;
}

.dingtalk-sync-create :deep(.el-card__header) h2,
.dingtalk-sync-detail :deep(.el-card__header) h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}

.dingtalk-sync-create :deep(.el-card__body),
.dingtalk-sync-detail :deep(.el-card__body) {
  padding: 12px 16px 16px;
}

.dingtalk-sync-create__grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px 12px;
  align-items: start;
}

.dingtalk-sync-create__grid :deep(.el-form-item) {
  margin-bottom: 0;
}

.dingtalk-sync-create__grid :deep(.el-form-item__label) {
  padding-bottom: 4px;
  line-height: 1.3;
}

.dingtalk-sync-create__full {
  grid-column: 1 / -1;
}

.dingtalk-sync-detail__empty {
  margin: 16px 0;
}

.sync-detail {
  display: grid;
  grid-template-columns: 120px 1fr;
  gap: 8px 12px;
  margin: 0 0 16px;
}

.sync-detail dt {
  margin: 0;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  font-weight: 400;
}

.sync-detail dd {
  margin: 0;
  font-size: 14px;
}

@media (max-width: 960px) {
  .dingtalk-sync-layout {
    flex-direction: column;
  }

  .dingtalk-sync-side {
    flex: none;
    max-width: none;
    width: 100%;
  }

  .dingtalk-sync-create__grid {
    grid-template-columns: 1fr;
  }
}
</style>
