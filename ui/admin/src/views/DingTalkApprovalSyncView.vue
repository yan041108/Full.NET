<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElPagination,
  ElTag
} from 'element-plus';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
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
const selected = ref<DingTalkApprovalSyncResponse>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const saving = ref(false);
const retrying = ref(false);
const errorMessage = ref<string>();
const workflowInstanceId = ref('');
const originatorUserId = ref('');
const deptId = ref('');
const title = ref('');
const summary = ref('');
const canRead = computed(() => session.can('notifications.dingtalk_approval_sync.read'));
const canCreate = computed(() => session.can('notifications.dingtalk_approval_sync.create'));
const canRetry = computed(() => session.can('notifications.dingtalk_approval_sync.retry'));

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
  } catch {
    errorMessage.value = t('dingtalkApprovalSync.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function createSync(): Promise<void> {
  if (!canCreate.value || saving.value) {
    return;
  }
  const parsedDeptId = Number(deptId.value);
  if (!workflowInstanceId.value.trim() || !originatorUserId.value.trim() || !title.value.trim()
    || !Number.isFinite(parsedDeptId) || parsedDeptId <= 0) {
    errorMessage.value = t('dingtalkApprovalSync.formInvalid');
    return;
  }
  saving.value = true;
  errorMessage.value = undefined;
  try {
    const created = await createDingTalkApprovalSync({
      workflowInstanceId: workflowInstanceId.value.trim(),
      originatorUserId: originatorUserId.value.trim(),
      deptId: parsedDeptId,
      title: title.value.trim(),
      summary: summary.value.trim() || null
    });
    workflowInstanceId.value = '';
    originatorUserId.value = '';
    deptId.value = '';
    title.value = '';
    summary.value = '';
    selected.value = created;
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
    selected.value = await retryDingTalkApprovalSync(current.id);
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
  const translated = t(key);
  return translated === key ? statusKey : translated;
}
</script>

<template>
  <section class="dingtalk-approval-sync-view art-page-stack">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('dingtalkApprovalSync.eyebrow') }}</p>
      <h1 data-route-heading tabindex="-1">{{ t('dingtalkApprovalSync.title') }}</h1>
      <p>{{ t('dingtalkApprovalSync.description') }}</p>
    </header>

    <ElAlert
      type="info"
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

    <PermissionGate :permission="'notifications.dingtalk_approval_sync.create'">
      <ElCard shadow="never">
        <template #header>
          <h2>{{ t('dingtalkApprovalSync.createTitle') }}</h2>
        </template>
        <div class="sync-form">
          <ElInput
            v-model="workflowInstanceId"
            data-testid="dingtalk-sync-workflow-instance-id"
            :placeholder="t('dingtalkApprovalSync.workflowInstanceIdPlaceholder')"
          />
          <ElInput
            v-model="originatorUserId"
            data-testid="dingtalk-sync-originator-user-id"
            :placeholder="t('dingtalkApprovalSync.originatorUserIdPlaceholder')"
          />
          <ElInput
            v-model="deptId"
            data-testid="dingtalk-sync-dept-id"
            inputmode="numeric"
            :placeholder="t('dingtalkApprovalSync.deptIdPlaceholder')"
          />
          <ElInput
            v-model="title"
            data-testid="dingtalk-sync-title"
            :placeholder="t('dingtalkApprovalSync.titlePlaceholder')"
          />
          <ElInput
            v-model="summary"
            data-testid="dingtalk-sync-summary"
            :placeholder="t('dingtalkApprovalSync.summaryPlaceholder')"
          />
          <ElButton
            type="primary"
            :loading="saving"
            data-testid="dingtalk-sync-create"
            @click="createSync"
          >
            {{ t('dingtalkApprovalSync.register') }}
          </ElButton>
        </div>
      </ElCard>
    </PermissionGate>

    <ElCard v-if="canRead" shadow="never">
      <template #header>
        <div class="art-section-heading">
          <h2>{{ t('dingtalkApprovalSync.listTitle') }}</h2>
          <ElButton :loading="loading" data-testid="dingtalk-sync-load" @click="load">
            {{ t('dingtalkApprovalSync.refresh') }}
          </ElButton>
        </div>
      </template>

      <div class="sync-list" data-testid="dingtalk-sync-list">
        <article
          v-for="item in items"
          :key="item.id"
          class="sync-item"
          :class="{ selected: selected?.id === item.id }"
          @click="selected = item"
        >
          <div>
            <strong>{{ item.title }}</strong>
            <p>{{ item.workflowInstanceId }}</p>
          </div>
          <ElTag>{{ statusText(item.statusKey) }}</ElTag>
        </article>
      </div>

      <ElPagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        layout="total, prev, pager, next"
        :total="total"
        @current-change="load"
        @size-change="load"
      />
    </ElCard>

    <ElCard v-if="selected" shadow="never" data-testid="dingtalk-sync-detail">
      <template #header>
        <h2>{{ t('dingtalkApprovalSync.detailTitle') }}</h2>
      </template>
      <dl class="sync-detail">
        <dt>{{ t('dingtalkApprovalSync.fields.status') }}</dt>
        <dd>{{ statusText(selected.statusKey) }}</dd>
        <dt>{{ t('dingtalkApprovalSync.fields.processInstanceId') }}</dt>
        <dd>{{ selected.dingTalkProcessInstanceId ?? '—' }}</dd>
        <dt>{{ t('dingtalkApprovalSync.fields.externalStatus') }}</dt>
        <dd>{{ selected.externalStatusKey ?? '—' }}</dd>
        <dt>{{ t('dingtalkApprovalSync.fields.externalResult') }}</dt>
        <dd>{{ selected.externalResultKey ?? '—' }}</dd>
        <dt>{{ t('dingtalkApprovalSync.fields.lastError') }}</dt>
        <dd>{{ selected.lastErrorCode ?? '—' }}</dd>
      </dl>
      <PermissionGate :permission="'notifications.dingtalk_approval_sync.retry'">
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
    </ElCard>
  </section>
</template>

<style scoped>
.sync-form {
  display: grid;
  gap: 12px;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
}

.sync-list {
  display: grid;
  gap: 8px;
  margin-bottom: 16px;
}

.sync-item {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  padding: 12px;
  border: 1px solid var(--el-border-color-light);
  border-radius: 8px;
  cursor: pointer;
}

.sync-item.selected {
  border-color: var(--el-color-primary);
}

.sync-detail {
  display: grid;
  grid-template-columns: 180px 1fr;
  gap: 8px 16px;
  margin-bottom: 16px;
}
</style>
