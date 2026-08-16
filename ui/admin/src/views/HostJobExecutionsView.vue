<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElDatePicker,
  ElDrawer,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, HostJobDefinition, HostJobExecution } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getHostJobExecution,
  listHostJobDefinitions,
  listHostJobExecutions
} from '../api/host-jobs';

defineOptions({ name: 'HostJobExecutionsView' });

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const definitions = ref<HostJobDefinition[]>([]);
const executions = ref<HostJobExecution[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const filterDefinitionId = ref('');
const filterStatus = ref('');
const filterRange = ref<[Date, Date] | null>(null);
const detailOpen = ref(false);
const detailLoading = ref(false);
const detail = ref<HostJobExecution | null>(null);

const canRead = computed(() => session.can('jobs.executions.read'));

const statusOptions = [
  'pending',
  'running',
  'succeeded',
  'failed'
] as const;

async function loadDefinitions(): Promise<void> {
  const result = await listHostJobDefinitions(1, 100);
  definitions.value = result.items;
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
  filterStatus.value = '';
  filterRange.value = null;
  page.value = 1;
  void loadExecutions();
}

function statusTagType(status: HostJobExecution['status']): 'info' | 'warning' | 'success' | 'danger' {
  switch (status) {
    case 'pending':
      return 'info';
    case 'running':
      return 'warning';
    case 'succeeded':
      return 'success';
    default:
      return 'danger';
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
          <ElSelect v-model="filterDefinitionId" clearable filterable style="width: 100%">
            <ElOption
              v-for="item in definitions"
              :key="item.id"
              :label="item.displayName"
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
              {{ t(`hostJobExecutions.status.${row.status}`) }}
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
          <dt>{{ t('hostJobExecutions.detailAttemptCount') }}</dt>
          <dd>{{ detail.attemptCount }}</dd>
          <dt>{{ t('hostJobExecutions.detailError') }}</dt>
          <dd translate="no">{{ detail.errorMessage ?? '—' }}</dd>
        </dl>
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
</style>
