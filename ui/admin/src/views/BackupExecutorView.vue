<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElAlert,
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
import type {
  FullNetProblemDetails,
  PlatformBackupExecutorStatus,
  PlatformBackupRun,
  PlatformBackupTask
} from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  downloadBackupRunArtifact,
  getBackupExecutorStatus,
  getBackupRun,
  listBackupRuns,
  listBackupTasks
} from '../api/backup-executor';

defineOptions({ name: 'BackupExecutorView' });

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const status = ref<PlatformBackupExecutorStatus | null>(null);
const tasks = ref<PlatformBackupTask[]>([]);
const runs = ref<PlatformBackupRun[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const filterTaskId = ref('');
const filterStatus = ref('');
const filterRange = ref<[Date, Date] | null>(null);
const detailOpen = ref(false);
const detailLoading = ref(false);
const detail = ref<PlatformBackupRun | null>(null);
const downloadingId = ref('');

const canReadTasks = computed(() => session.can('platform.backup_tasks.read'));
const canReadRuns = computed(() => session.can('platform.backup_runs.read'));
const canDownload = computed(() => session.can('platform.backup_runs.download'));

const statusOptions = ['pending', 'running', 'succeeded', 'failed'] as const;

function statusLabel(value: string): string {
  switch (value) {
    case 'pending':
      return t('backupExecutor.status.pending');
    case 'running':
      return t('backupExecutor.status.running');
    case 'succeeded':
      return t('backupExecutor.status.succeeded');
    case 'failed':
      return t('backupExecutor.status.failed');
    default:
      return value;
  }
}

function statusTagType(value: string): 'info' | 'success' | 'warning' | 'danger' {
  switch (value) {
    case 'succeeded':
      return 'success';
    case 'failed':
      return 'danger';
    case 'running':
      return 'warning';
    default:
      return 'info';
  }
}

function formatDate(value: string | null): string {
  if (!value) {
    return '-';
  }
  return new Date(value).toLocaleString(locale.value);
}

function formatBytes(value: number | null): string {
  if (value === null || value < 0) {
    return '-';
  }
  if (value < 1024) {
    return `${value} B`;
  }
  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KiB`;
  }
  return `${(value / (1024 * 1024)).toFixed(1)} MiB`;
}

async function loadStatus(): Promise<void> {
  status.value = await getBackupExecutorStatus();
}

async function loadTasks(): Promise<void> {
  tasks.value = await listBackupTasks();
}

async function loadRuns(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listBackupRuns({
      page: page.value,
      pageSize: pageSize.value,
      taskId: filterTaskId.value || undefined,
      status: filterStatus.value || undefined,
      fromUtc: filterRange.value?.[0]?.toISOString(),
      toUtc: filterRange.value?.[1]?.toISOString()
    });
    runs.value = result.items;
    total.value = result.total;
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    loading.value = false;
  }
}

async function refreshAll(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    await Promise.all([loadStatus(), loadTasks(), loadRuns()]);
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    loading.value = false;
  }
}

async function openDetail(runId: string): Promise<void> {
  detailOpen.value = true;
  detailLoading.value = true;
  detail.value = null;
  try {
    detail.value = await getBackupRun(runId);
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
    detailOpen.value = false;
  } finally {
    detailLoading.value = false;
  }
}

async function handleDownload(run: PlatformBackupRun): Promise<void> {
  if (!run.canDownload) {
    return;
  }
  downloadingId.value = run.id;
  try {
    const blob = await downloadBackupRunArtifact(run.id);
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = run.artifactFileName ?? `backup-${run.id}.bin`;
    anchor.click();
    URL.revokeObjectURL(url);
    ElMessage.success(t('backupExecutor.downloadSuccess'));
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    downloadingId.value = '';
  }
}

function applyFilters(): void {
  page.value = 1;
  void loadRuns();
}

onMounted(() => {
  void refreshAll();
});
</script>

<template>
  <div class="backup-executor-view art-page-stack">
    <ArtTableHeader :title="t('backupExecutor.title')" />
    <p class="art-muted">{{ t('backupExecutor.description') }}</p>

    <ElAlert
      v-if="status"
      type="info"
      :closable="false"
      :title="t('backupExecutor.deploymentNoticeTitle')"
      :description="status.deploymentNotice"
      show-icon
      class="backup-executor-notice"
    />

    <div v-if="status" class="backup-executor-status-grid">
      <ElCard>
        <p><strong>{{ t('backupExecutor.artifactRootPath') }}:</strong> {{ status.artifactRootPath }}</p>
        <p>
          <strong>{{ t('backupExecutor.artifactRootExists') }}:</strong>
          <ElTag :type="status.artifactRootExists ? 'success' : 'warning'">
            {{ status.artifactRootExists ? t('backupExecutor.yes') : t('backupExecutor.no') }}
          </ElTag>
        </p>
        <p>{{ t('backupExecutor.enabledTaskCount', { count: status.enabledTaskCount }) }}</p>
      </ElCard>
    </div>

    <ElCard v-if="canReadTasks" v-loading="loading">
      <h2>{{ t('backupExecutor.tasksTitle') }}</h2>
      <ElTable :data="tasks" row-key="id">
        <ElTableColumn prop="taskKey" :label="t('backupExecutor.taskKey')" min-width="160" />
        <ElTableColumn prop="displayName" :label="t('backupExecutor.displayName')" min-width="180" />
        <ElTableColumn prop="databaseProvider" :label="t('backupExecutor.databaseProvider')" width="140" />
        <ElTableColumn :label="t('backupExecutor.enabled')" width="100">
          <template #default="{ row }">
            <ElTag :type="row.isEnabled ? 'success' : 'info'">
              {{ row.isEnabled ? t('backupExecutor.yes') : t('backupExecutor.no') }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="description" :label="t('backupExecutor.descriptionColumn')" min-width="240" />
      </ElTable>
    </ElCard>

    <ElCard v-if="canReadRuns">
      <h2>{{ t('backupExecutor.runsTitle') }}</h2>
      <div class="backup-executor-filters">
        <ElSelect v-model="filterTaskId" clearable :placeholder="t('backupExecutor.filterTask')">
          <ElOption
            v-for="task in tasks"
            :key="task.id"
            :label="task.displayName"
            :value="task.id"
          />
        </ElSelect>
        <ElSelect v-model="filterStatus" clearable :placeholder="t('backupExecutor.filterStatus')">
          <ElOption
            v-for="item in statusOptions"
            :key="item"
            :label="statusLabel(item)"
            :value="item"
          />
        </ElSelect>
        <ElDatePicker
          v-model="filterRange"
          type="datetimerange"
          :start-placeholder="t('backupExecutor.filterFrom')"
          :end-placeholder="t('backupExecutor.filterTo')"
        />
        <ElButton type="primary" @click="applyFilters">
          {{ t('backupExecutor.applyFilters') }}
        </ElButton>
        <ElButton :loading="loading" @click="refreshAll">
          {{ t('backupExecutor.refresh') }}
        </ElButton>
      </div>

      <ElTable v-loading="loading" :data="runs" row-key="id">
        <ElTableColumn prop="taskDisplayName" :label="t('backupExecutor.task')" min-width="160" />
        <ElTableColumn :label="t('backupExecutor.status')" width="120">
          <template #default="{ row }">
            <ElTag :type="statusTagType(row.status)">
              {{ statusLabel(row.status) }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('backupExecutor.startedAt')" min-width="180">
          <template #default="{ row }">{{ formatDate(row.startedAtUtc) }}</template>
        </ElTableColumn>
        <ElTableColumn :label="t('backupExecutor.completedAt')" min-width="180">
          <template #default="{ row }">{{ formatDate(row.completedAtUtc) }}</template>
        </ElTableColumn>
        <ElTableColumn :label="t('backupExecutor.artifactSize')" width="120">
          <template #default="{ row }">{{ formatBytes(row.artifactSizeBytes) }}</template>
        </ElTableColumn>
        <!-- @vue-generic {PlatformBackupRun} -->
          <ElTableColumn :label="t('backupExecutor.actions')" width="200" fixed="right">
          <template #default="{ row }">
            <ElButton link type="primary" @click="openDetail(row.id)">
              {{ t('backupExecutor.viewDetail') }}
            </ElButton>
            <ElButton
              v-if="canDownload && row.canDownload"
              link
              type="primary"
              :loading="downloadingId === row.id"
              @click="handleDownload(row)"
            >
              {{ t('backupExecutor.download') }}
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>

      <ElPagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        layout="total, prev, pager, next"
        :total="total"
        class="backup-executor-pagination"
        @current-change="loadRuns"
        @size-change="applyFilters"
      />
    </ElCard>

    <ElDrawer v-model="detailOpen" :title="t('backupExecutor.runDetailTitle')" size="480px">
      <div v-loading="detailLoading">
        <template v-if="detail">
          <p><strong>{{ t('backupExecutor.task') }}:</strong> {{ detail.taskDisplayName }}</p>
          <p><strong>{{ t('backupExecutor.status') }}:</strong> {{ statusLabel(detail.status) }}</p>
          <p><strong>{{ t('backupExecutor.startedAt') }}:</strong> {{ formatDate(detail.startedAtUtc) }}</p>
          <p><strong>{{ t('backupExecutor.completedAt') }}:</strong> {{ formatDate(detail.completedAtUtc) }}</p>
          <p><strong>{{ t('backupExecutor.artifactFileName') }}:</strong> {{ detail.artifactFileName ?? '-' }}</p>
          <p><strong>{{ t('backupExecutor.artifactSize') }}:</strong> {{ formatBytes(detail.artifactSizeBytes) }}</p>
          <p><strong>{{ t('backupExecutor.summaryMessage') }}:</strong> {{ detail.summaryMessage ?? '-' }}</p>
          <ElButton
            v-if="canDownload && detail.canDownload"
            type="primary"
            :loading="downloadingId === detail.id"
            @click="handleDownload(detail)"
          >
            {{ t('backupExecutor.download') }}
          </ElButton>
        </template>
      </div>
    </ElDrawer>

    <ElAlert
      v-if="problem"
      type="error"
      :title="problem.title ?? t('common.requestFailed')"
      :description="problem.detail"
      show-icon
      closable
      @close="problem = undefined"
    />
  </div>
</template>

<style scoped>
.backup-executor-notice {
  margin-bottom: 16px;
}

.backup-executor-status-grid {
  margin-bottom: 16px;
}

.backup-executor-filters {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-bottom: 16px;
}

.backup-executor-pagination {
  margin-top: 16px;
  justify-content: flex-end;
}
</style>
