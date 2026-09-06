<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElDatePicker,
  ElDrawer,
  ElMessage,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, HostJobDefinition, HostJobExecution, HostJobSchedule } from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isHostJobExecutionCancellable } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  cancelHostJobExecution,
  getHostJobExecution,
  listHostJobDefinitions,
  listHostJobExecutions
} from '../api/host-jobs';
import { listHostJobSchedules } from '../api/host-job-schedules';

defineOptions({ name: 'HostJobExecutionsView' });

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const definitions = ref<HostJobDefinition[]>([]);
const schedules = ref<HostJobSchedule[]>([]);
const executions = ref<HostJobExecution[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const filterDefinitionId = ref('');
const filterScheduleId = ref('');
const filterStatus = ref('');
const filterRange = ref<[Date, Date] | null>(null);
const detailOpen = ref(false);
const detailLoading = ref(false);
const detail = ref<HostJobExecution | null>(null);
const cancellingId = ref('');

const canRead = computed(() => session.can('jobs.executions.read'));
const canCancel = computed(() => session.can('jobs.executions.cancel'));

const scheduleOptions = computed(() => {
  if (!filterDefinitionId.value) {
    return schedules.value;
  }
  return schedules.value.filter(
    item => item.jobDefinitionId === filterDefinitionId.value
  );
});

const statusOptions = [
  'pending',
  'running',
  'cancelling',
  'cancelled',
  'succeeded',
  'failed'
] as const;

async function loadDefinitions(): Promise<void> {
  const result = await listHostJobDefinitions(1, 100);
  definitions.value = result.items;
}

async function loadSchedules(): Promise<void> {
  const result = await listHostJobSchedules({ page: 1, pageSize: 100 });
  schedules.value = result.items;
}

async function loadExecutions(): Promise<void> {
  if (!canRead.value) {
    return;
  }
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listHostJobExecutions({
      page: page.value,
      pageSize: pageSize.value,
      jobDefinitionId: filterDefinitionId.value || undefined,
      jobScheduleId: filterScheduleId.value || undefined,
      status: filterStatus.value || undefined,
      fromUtc: filterRange.value?.[0]?.toISOString(),
      toUtc: filterRange.value?.[1]?.toISOString()
    });
    executions.value = result.items;
    total.value = result.total;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostJobExecutions.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function cancelExecution(row: HostJobExecution): Promise<void> {
  if (!canCancel.value || !isHostJobExecutionCancellable(row.status)) {
    return;
  }
  cancellingId.value = row.id;
  problem.value = undefined;
  try {
    const updated = await cancelHostJobExecution(row.id);
    const index = executions.value.findIndex(item => item.id === row.id);
    if (index >= 0) {
      executions.value[index] = updated;
    }
    if (detail.value?.id === row.id) {
      detail.value = updated;
    }
    ElMessage.success(t('hostJobExecutions.cancelRequested'));
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    cancellingId.value = '';
  }
}

function canShowCancelAction(row: HostJobExecution): boolean {
  return canCancel.value && isHostJobExecutionCancellable(row.status);
}

function cancelActionLabel(status: HostJobExecution['status']): string {
  if (status === 'cancelling') {
    return t('hostJobExecutions.cancelInProgress');
  }
  return t('hostJobExecutions.cancelAction');
}

async function openDetail(row: HostJobExecution): Promise<void> {
  detailOpen.value = true;
  detailLoading.value = true;
  detail.value = null;
  try {
    detail.value = await getHostJobExecution(row.id);
  } catch (error: unknown) {
    problem.value = toProblem(error);
    detailOpen.value = false;
  } finally {
    detailLoading.value = false;
  }
}

function resetFilters(): void {
  filterDefinitionId.value = '';
  filterScheduleId.value = '';
  filterStatus.value = '';
  filterRange.value = null;
  page.value = 1;
  void loadExecutions();
}

function onDefinitionFilterChange(): void {
  if (
    filterScheduleId.value
    && !scheduleOptions.value.some(item => item.id === filterScheduleId.value)
  ) {
    filterScheduleId.value = '';
  }
}

function scheduleLabel(schedule: HostJobSchedule): string {
  const trigger = schedule.cronExpression ?? schedule.triggerKind;
  return `${schedule.jobDefinitionDisplayName} · ${trigger}`;
}

function formatElapsed(
  startedAtUtc: string | null | undefined,
  finishedAtUtc: string | null | undefined
): string {
  if (!startedAtUtc || !finishedAtUtc) {
    return '—';
  }
  const elapsedMs = new Date(finishedAtUtc).getTime() - new Date(startedAtUtc).getTime();
  if (!Number.isFinite(elapsedMs) || elapsedMs < 0) {
    return '—';
  }
  if (elapsedMs < 1000) {
    return t('hostJobExecutions.elapsedMs', { value: elapsedMs });
  }
  return t('hostJobExecutions.elapsedSeconds', {
    value: (elapsedMs / 1000).toFixed(2)
  });
}

/** 仅展示稳定机器码形态的错误标识，避免把异常堆栈或上游响应原文暴露到管理端。 */
function formatExecutionError(
  status: HostJobExecution['status'],
  errorMessage: string | null | undefined
): string {
  if (status !== 'failed' || !errorMessage?.trim()) {
    return '—';
  }
  const trimmed = errorMessage.trim();
  if (/^[a-z][\w.-]{2,127}$/i.test(trimmed)) {
    return trimmed;
  }
  return t('hostJobExecutions.detailErrorUnavailable');
}

function statusTagType(status: HostJobExecution['status']): 'info' | 'warning' | 'success' | 'danger' {
  switch (status) {
    case 'pending':
      return 'info';
    case 'running':
    case 'cancelling':
      return 'warning';
    case 'succeeded':
      return 'success';
    case 'cancelled':
      return 'info';
    default:
      return 'danger';
  }
}

function statusLabel(status: HostJobExecution['status']): string {
  switch (status) {
    case 'pending':
      return t('hostJobExecutions.status.pending');
    case 'running':
      return t('hostJobExecutions.status.running');
    case 'cancelling':
      return t('hostJobExecutions.status.cancelling');
    case 'cancelled':
      return t('hostJobExecutions.status.cancelled');
    case 'succeeded':
      return t('hostJobExecutions.status.succeeded');
    case 'failed':
      return t('hostJobExecutions.status.failed');
  }
}

function formatUtc(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

function toProblem(
  error: unknown,
  fallbackKey: 'hostJobExecutions.loadFailed' | 'hostJobExecutions.operationFailed' = 'hostJobExecutions.operationFailed'
): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return {
    type: 'about:blank',
    title: t(fallbackKey),
    status: 500,
    code: 'client.unexpected_error'
  };
}

onMounted(async () => {
  if (!canRead.value) {
    return;
  }
  await loadDefinitions();
  await loadSchedules();
  await loadExecutions();
});
</script>

<template>
  <section class="host-job-executions-view art-page-stack art-full-height" :aria-busy="loading">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('hostJobExecutions.eyebrow') }}</p>
      <h1>{{ t('hostJobExecutions.title') }}</h1>
      <p>{{ t('hostJobExecutions.description') }}</p>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <ElCard class="art-card">
      <div class="host-job-executions-filters art-form-grid">
        <label>
          <span>{{ t('hostJobExecutions.filterDefinition') }}</span>
          <ElSelect
            v-model="filterDefinitionId"
            clearable
            filterable
            style="width: 100%"
            data-testid="host-job-executions-filter-definition"
            @change="onDefinitionFilterChange"
          >
            <ElOption
              v-for="item in definitions"
              :key="item.id"
              :label="item.displayName"
              :value="item.id"
            />
          </ElSelect>
        </label>
        <label>
          <span>{{ t('hostJobExecutions.filterSchedule') }}</span>
          <ElSelect
            v-model="filterScheduleId"
            clearable
            filterable
            style="width: 100%"
            data-testid="host-job-executions-filter-schedule"
          >
            <ElOption
              v-for="item in scheduleOptions"
              :key="item.id"
              :label="scheduleLabel(item)"
              :value="item.id"
            />
          </ElSelect>
        </label>
        <label>
          <span>{{ t('hostJobExecutions.filterStatus') }}</span>
          <ElSelect v-model="filterStatus" clearable style="width: 100%">
            <ElOption
              v-for="status in statusOptions"
              :key="status"
              :label="t(`hostJobExecutions.status.${status}`)"
              :value="status"
            />
          </ElSelect>
        </label>
        <label>
          <span>{{ t('hostJobExecutions.filterRange') }}</span>
          <ElDatePicker
            v-model="filterRange"
            type="datetimerange"
            value-format="YYYY-MM-DDTHH:mm:ss.SSS[Z]"
            style="width: 100%"
          />
        </label>
        <div class="host-job-executions-filter-actions">
          <ElButton type="primary" data-testid="host-job-executions-search" @click="loadExecutions">
            {{ t('hostJobExecutions.search') }}
          </ElButton>
          <ElButton @click="resetFilters">{{ t('hostJobExecutions.reset') }}</ElButton>
        </div>
      </div>
    </ElCard>

    <ElCard v-loading="loading" class="art-card art-table-card">
      <ElTable :data="executions" stripe border style="width: 100%" @row-click="openDetail">
        <ElTableColumn :label="t('hostJobExecutions.columnStatus')" width="120" align="center">
          <template #default="{ row }">
            <ElTag :type="statusTagType(row.status)" effect="light">
              {{ statusLabel(row.status) }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('hostJobExecutions.columnTriggerKind')" prop="triggerKind" min-width="120" />
        <ElTableColumn :label="t('hostJobExecutions.columnStartedAt')" min-width="160">
          <template #default="{ row }">{{ formatUtc(row.startedAtUtc) }}</template>
        </ElTableColumn>
        <ElTableColumn :label="t('hostJobExecutions.columnFinishedAt')" min-width="160">
          <template #default="{ row }">{{ formatUtc(row.finishedAtUtc) }}</template>
        </ElTableColumn>
        <ElTableColumn :label="t('hostJobExecutions.columnAttemptCount')" prop="attemptCount" width="100" align="center" />
        <ElTableColumn :label="t('hostJobExecutions.columnCreatedAt')" min-width="160">
          <template #default="{ row }">{{ formatUtc(row.createdAtUtc) }}</template>
        </ElTableColumn>
        <ElTableColumn
          v-if="canCancel"
          :label="t('hostJobExecutions.columnActions')"
          width="140"
          align="center"
        >
          <template #default="{ row }">
            <ElButton
              v-if="canShowCancelAction(row)"
              link
              type="danger"
              :loading="cancellingId === row.id"
              data-testid="host-job-executions-cancel"
              @click.stop="cancelExecution(row)"
            >
              {{ cancelActionLabel(row.status) }}
            </ElButton>
            <span v-else-if="row.status === 'cancelled'">
              {{ t('hostJobExecutions.cancelUnsupported') }}
            </span>
            <span v-else-if="row.status === 'cancelling'">
              {{ t('hostJobExecutions.cancelInProgress') }}
            </span>
          </template>
        </ElTableColumn>
      </ElTable>
      <ElPagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        :total="total"
        layout="total, prev, pager, next"
        class="art-table-pagination"
        @current-change="loadExecutions"
        @size-change="loadExecutions"
      />
    </ElCard>

    <ElDrawer
      v-model="detailOpen"
      :title="t('hostJobExecutions.detailTitle')"
      size="40%"
      :append-to-body="true"
    >
      <div v-loading="detailLoading">
        <dl v-if="detail" class="host-job-executions-detail">
          <dt>{{ t('hostJobExecutions.detailStatus') }}</dt>
          <dd>{{ t(`hostJobExecutions.status.${detail.status}`) }}</dd>
          <dt>{{ t('hostJobExecutions.detailTriggerKind') }}</dt>
          <dd translate="no">{{ detail.triggerKind }}</dd>
          <dt>{{ t('hostJobExecutions.detailScheduleId') }}</dt>
          <dd translate="no">{{ detail.jobScheduleId ?? '—' }}</dd>
          <dt>{{ t('hostJobExecutions.detailScheduledFor') }}</dt>
          <dd>{{ formatUtc(detail.scheduledForUtc) }}</dd>
          <dt>{{ t('hostJobExecutions.detailNextRetry') }}</dt>
          <dd>{{ formatUtc(detail.nextAttemptAtUtc) }}</dd>
          <dt>{{ t('hostJobExecutions.detailStartedAt') }}</dt>
          <dd>{{ formatUtc(detail.startedAtUtc) }}</dd>
          <dt>{{ t('hostJobExecutions.detailFinishedAt') }}</dt>
          <dd>{{ formatUtc(detail.finishedAtUtc) }}</dd>
          <dt>{{ t('hostJobExecutions.detailElapsed') }}</dt>
          <dd>{{ formatElapsed(detail.startedAtUtc, detail.finishedAtUtc) }}</dd>
          <dt>{{ t('hostJobExecutions.detailAttemptCount') }}</dt>
          <dd>{{ detail.attemptCount }}</dd>
          <dt>{{ t('hostJobExecutions.detailError') }}</dt>
          <dd translate="no">{{ formatExecutionError(detail.status, detail.errorMessage) }}</dd>
        </dl>
        <div v-if="detail && canShowCancelAction(detail)" class="host-job-executions-detail-actions">
          <ElButton
            type="danger"
            :loading="cancellingId === detail.id"
            data-testid="host-job-executions-detail-cancel"
            @click="cancelExecution(detail)"
          >
            {{ cancelActionLabel(detail.status) }}
          </ElButton>
        </div>
      </div>
    </ElDrawer>
  </section>
</template>

<style scoped>
.host-job-executions-filters {
  margin-bottom: 0;
}

.host-job-executions-filter-actions {
  display: flex;
  align-items: flex-end;
  gap: 8px;
}

.host-job-executions-detail {
  display: grid;
  grid-template-columns: 160px 1fr;
  gap: 12px 16px;
}

.host-job-executions-detail dt {
  font-weight: 600;
}

.host-job-executions-detail-actions {
  margin-top: 16px;
}
</style>
