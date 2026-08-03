<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElDatePicker,
  ElInput,
  ElMessage,
  ElOption,
  ElPagination,
  ElSelect,
  ElTag
} from 'element-plus';
import type {
  FullNetProblemDetails,
  HostJobSchedule,
  HostJobScheduleDefinitionOption
} from '@fullnet/client-contracts';
import {
  isFullNetProblemDetails,
  JOB_MISFIRE_POLICIES,
  JOB_TRIGGER_KINDS
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import {
  createHostJobSchedule,
  listHostJobScheduleDefinitionOptions,
  listHostJobSchedules,
  pauseHostJobSchedule,
  previewHostJobScheduleCron,
  resumeHostJobSchedule,
  updateHostJobSchedule
} from '../api/host-job-schedules';

const IANA_TIME_ZONES = [
  'UTC',
  'America/New_York',
  'America/Chicago',
  'America/Denver',
  'America/Los_Angeles',
  'Europe/London',
  'Europe/Paris',
  'Asia/Shanghai',
  'Asia/Tokyo',
  'Australia/Sydney'
] as const;

const session = useSessionStore();
const { t } = useAdminI18n();
const definitionOptions = ref<HostJobScheduleDefinitionOption[]>([]);
const schedules = ref<HostJobSchedule[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const filterSearch = ref('');
const filterEnabled = ref<'all' | 'enabled' | 'paused'>('all');
const filterTriggerKind = ref('');
const jobDefinitionId = ref('');
const triggerKind = ref<string>(JOB_TRIGGER_KINDS.cron);
const cronExpression = ref('0 9 * * *');
const timeZoneId = ref('UTC');
const oneTimeAtUtc = ref('');
const misfirePolicy = ref<string>(JOB_MISFIRE_POLICIES.skip);
const cronPreviewUtc = ref<string>();
const cronPreviewLoading = ref(false);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const editingId = ref<string>();
const canCreate = computed(() => session.can('jobs.schedules.create'));
const canUpdate = computed(() => session.can('jobs.schedules.update'));
const canPause = computed(() => session.can('jobs.schedules.pause'));
const canResume = computed(() => session.can('jobs.schedules.resume'));
const showForm = computed(() =>
  editingId.value ? canUpdate.value : canCreate.value
);
const isCron = computed(() => triggerKind.value === JOB_TRIGGER_KINDS.cron);
const misfirePolicyLabel = computed(() => ({
  [JOB_MISFIRE_POLICIES.skip]: t('hostJobSchedules.misfireSkip'),
  [JOB_MISFIRE_POLICIES.fireOnce]: t('hostJobSchedules.misfireFireOnce')
}));

onMounted(load);

watch([cronExpression, timeZoneId, triggerKind], () => {
  void refreshCronPreview();
});

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    await Promise.all([loadSchedules(), loadDefinitionOptions()]);
    if (!jobDefinitionId.value && definitionOptions.value.length) {
      jobDefinitionId.value = definitionOptions.value[0].id;
    }
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostJobSchedules.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function loadSchedules(): Promise<void> {
  const isEnabled =
    filterEnabled.value === 'all'
      ? undefined
      : filterEnabled.value === 'enabled';
  const result = await listHostJobSchedules({
    page: page.value,
    pageSize: pageSize.value,
    search: filterSearch.value || undefined,
    isEnabled,
    triggerKind: filterTriggerKind.value || undefined
  });
  schedules.value = result.items;
  page.value = result.page;
  pageSize.value = result.pageSize;
  total.value = result.total;
}

async function loadDefinitionOptions(): Promise<void> {
  if (!canCreate.value) {
    definitionOptions.value = [];
    return;
  }
  definitionOptions.value = await listHostJobScheduleDefinitionOptions();
}

async function applyFilters(): Promise<void> {
  page.value = 1;
  loading.value = true;
  problem.value = undefined;
  try {
    await loadSchedules();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostJobSchedules.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function onPageChange(nextPage: number): Promise<void> {
  page.value = nextPage;
  loading.value = true;
  problem.value = undefined;
  try {
    await loadSchedules();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostJobSchedules.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function refreshCronPreview(): Promise<void> {
  if (!isCron.value || !canCreate.value && !canUpdate.value) {
    cronPreviewUtc.value = undefined;
    return;
  }
  const expression = cronExpression.value.trim();
  const zone = timeZoneId.value.trim();
  if (!expression || !zone) {
    cronPreviewUtc.value = undefined;
    return;
  }
  cronPreviewLoading.value = true;
  try {
    const preview = await previewHostJobScheduleCron(expression, zone);
    cronPreviewUtc.value = preview.nextExecutionAtUtc;
  } catch {
    cronPreviewUtc.value = undefined;
  } finally {
    cronPreviewLoading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value || !canCreate.value || !jobDefinitionId.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createHostJobSchedule(
      jobDefinitionId.value,
      triggerKind.value,
      timeZoneId.value.trim(),
      misfirePolicy.value,
      isCron.value ? cronExpression.value.trim() : null,
      isCron.value ? null : oneTimeAtUtc.value || null
    );
    resetForm();
    ElMessage.success(t('hostJobSchedules.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function startEdit(item: HostJobSchedule): void {
  editingId.value = item.id;
  jobDefinitionId.value = item.jobDefinitionId;
  triggerKind.value = item.triggerKind;
  cronExpression.value = item.cronExpression ?? '';
  timeZoneId.value = item.timeZoneId;
  oneTimeAtUtc.value = item.oneTimeAtUtc ?? '';
  misfirePolicy.value = item.misfirePolicy;
}

function cancelEdit(): void {
  editingId.value = undefined;
  resetForm();
}

function resetForm(): void {
  triggerKind.value = JOB_TRIGGER_KINDS.cron;
  cronExpression.value = '0 9 * * *';
  timeZoneId.value = 'UTC';
  oneTimeAtUtc.value = '';
  misfirePolicy.value = JOB_MISFIRE_POLICIES.skip;
  cronPreviewUtc.value = undefined;
}

async function saveEdit(): Promise<void> {
  const item = schedules.value.find(entry => entry.id === editingId.value);
  if (!item || changing.value || !canUpdate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateHostJobSchedule(
      item.id,
      triggerKind.value,
      timeZoneId.value.trim(),
      misfirePolicy.value,
      item.version,
      isCron.value ? cronExpression.value.trim() : null,
      isCron.value ? null : oneTimeAtUtc.value || null
    );
    cancelEdit();
    ElMessage.success(t('hostJobSchedules.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function pause(item: HostJobSchedule): Promise<void> {
  if (changing.value || !item.isEnabled || !canPause.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await pauseHostJobSchedule(item.id, item.version);
    ElMessage.success(t('hostJobSchedules.pauseSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function resume(item: HostJobSchedule): Promise<void> {
  if (changing.value || item.isEnabled || !canResume.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await resumeHostJobSchedule(item.id, item.version);
    ElMessage.success(t('hostJobSchedules.resumeSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function scheduleExpression(item: HostJobSchedule): string {
  return item.triggerKind === JOB_TRIGGER_KINDS.cron
    ? item.cronExpression ?? '—'
    : item.oneTimeAtUtc ?? '—';
}

function formatUtc(value: string | null | undefined): string {
  return value ?? '—';
}

function toProblem(
  error: unknown,
  fallbackKey: 'hostJobSchedules.loadFailed' | 'hostJobSchedules.operationFailed' = 'hostJobSchedules.operationFailed'
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
</script>

<template>
  <section class="host-job-schedules-view art-page-stack art-full-height" :aria-busy="loading">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('hostJobSchedules.eyebrow') }}</p>
      <h1>{{ t('hostJobSchedules.title') }}</h1>
      <p>{{ t('hostJobSchedules.description') }}</p>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ElCard v-if="showForm" class="art-card">
      <template #header>
        <h2>{{ editingId ? t('hostJobSchedules.editTitle') : t('hostJobSchedules.createTitle') }}</h2>
      </template>
      <form class="art-form-grid" @submit.prevent="editingId ? saveEdit() : create()">
        <label v-if="!editingId">
          <span>{{ t('hostJobSchedules.fieldDefinition') }}</span>
          <ElSelect
            v-model="jobDefinitionId"
            :disabled="changing"
            data-testid="host-job-schedules-definition"
          >
            <ElOption
              v-for="item in definitionOptions"
              :key="item.id"
              :label="item.displayName"
              :value="item.id"
            />
          </ElSelect>
        </label>
        <label>
          <span>{{ t('hostJobSchedules.fieldTriggerKind') }}</span>
          <ElSelect v-model="triggerKind" :disabled="changing" data-testid="host-job-schedules-trigger-kind">
            <ElOption :label="t('hostJobSchedules.triggerCron')" :value="JOB_TRIGGER_KINDS.cron" />
            <ElOption :label="t('hostJobSchedules.triggerOneTime')" :value="JOB_TRIGGER_KINDS.oneTime" />
          </ElSelect>
        </label>
        <label v-if="isCron">
          <span>{{ t('hostJobSchedules.fieldCron') }}</span>
          <ElInput v-model="cronExpression" :disabled="changing" data-testid="host-job-schedules-cron" />
          <small class="art-muted">{{ t('hostJobSchedules.cronHelp') }}</small>
          <small
            v-if="cronPreviewUtc || cronPreviewLoading"
            class="art-muted"
            data-testid="host-job-schedules-cron-preview"
          >
            {{
              cronPreviewLoading
                ? t('hostJobSchedules.cronPreviewLoading')
                : t('hostJobSchedules.cronPreview', { instant: cronPreviewUtc })
            }}
          </small>
        </label>
        <label v-else>
          <span>{{ t('hostJobSchedules.fieldOneTimeAt') }}</span>
          <ElDatePicker
            v-model="oneTimeAtUtc"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss[Z]"
            :disabled="changing"
            data-testid="host-job-schedules-one-time"
          />
        </label>
        <label>
          <span>{{ t('hostJobSchedules.fieldTimeZone') }}</span>
          <ElSelect v-model="timeZoneId" :disabled="changing" data-testid="host-job-schedules-time-zone">
            <ElOption
              v-for="zone in IANA_TIME_ZONES"
              :key="zone"
              :label="zone"
              :value="zone"
            />
          </ElSelect>
        </label>
        <label>
          <span>{{ t('hostJobSchedules.fieldMisfirePolicy') }}</span>
          <ElSelect v-model="misfirePolicy" :disabled="changing" data-testid="host-job-schedules-misfire">
            <ElOption
              :label="misfirePolicyLabel[JOB_MISFIRE_POLICIES.skip]"
              :value="JOB_MISFIRE_POLICIES.skip"
            />
            <ElOption
              :label="misfirePolicyLabel[JOB_MISFIRE_POLICIES.fireOnce]"
              :value="JOB_MISFIRE_POLICIES.fireOnce"
            />
          </ElSelect>
        </label>
        <div class="art-form-actions">
          <ElButton v-if="editingId" @click="cancelEdit">{{ t('hostJobSchedules.cancel') }}</ElButton>
          <ElButton type="primary" native-type="submit" data-testid="host-job-schedules-submit" :loading="changing">
            {{ editingId ? t('hostJobSchedules.save') : t('hostJobSchedules.create') }}
          </ElButton>
        </div>
      </form>
    </ElCard>

    <ElCard class="art-card">
      <template #header>
        <h2>{{ t('hostJobSchedules.listTitle') }}</h2>
      </template>
      <form class="art-form-grid art-filter-row" @submit.prevent="applyFilters">
        <label>
          <span>{{ t('hostJobSchedules.filterSearch') }}</span>
          <ElInput
            v-model="filterSearch"
            :disabled="loading"
            data-testid="host-job-schedules-filter-search"
          />
        </label>
        <label>
          <span>{{ t('hostJobSchedules.filterStatus') }}</span>
          <ElSelect
            v-model="filterEnabled"
            :disabled="loading"
            data-testid="host-job-schedules-filter-status"
          >
            <ElOption :label="t('hostJobSchedules.filterStatusAll')" value="all" />
            <ElOption :label="t('hostJobSchedules.statusEnabled')" value="enabled" />
            <ElOption :label="t('hostJobSchedules.statusPaused')" value="paused" />
          </ElSelect>
        </label>
        <label>
          <span>{{ t('hostJobSchedules.filterTriggerKind') }}</span>
          <ElSelect
            v-model="filterTriggerKind"
            :disabled="loading"
            data-testid="host-job-schedules-filter-trigger"
          >
            <ElOption :label="t('hostJobSchedules.filterTriggerAll')" value="" />
            <ElOption :label="t('hostJobSchedules.triggerCron')" :value="JOB_TRIGGER_KINDS.cron" />
            <ElOption :label="t('hostJobSchedules.triggerOneTime')" :value="JOB_TRIGGER_KINDS.oneTime" />
          </ElSelect>
        </label>
        <div class="art-form-actions">
          <ElButton type="primary" native-type="submit" data-testid="host-job-schedules-apply-filters">
            {{ t('hostJobSchedules.applyFilters') }}
          </ElButton>
        </div>
      </form>

      <p v-if="!schedules.length">{{ t('hostJobSchedules.emptyList') }}</p>
      <ul v-else class="art-list host-job-schedules-list">
        <li v-for="item in schedules" :key="item.id">
          <div class="host-job-schedules-row">
            <div>
              <strong data-testid="host-job-schedules-definition-label">
                {{ item.jobDefinitionDisplayName }}
              </strong>
              <span class="art-muted" translate="no">{{ item.jobDefinitionJobKey }}</span>
              <ElTag :type="item.isEnabled ? 'success' : 'info'">
                {{ item.isEnabled ? t('hostJobSchedules.statusEnabled') : t('hostJobSchedules.statusPaused') }}
              </ElTag>
            </div>
            <dl class="host-job-schedules-meta">
              <div>
                <dt>{{ t('hostJobSchedules.columnTrigger') }}</dt>
                <dd>{{ item.triggerKind }}</dd>
              </div>
              <div>
                <dt>{{ t('hostJobSchedules.columnExpression') }}</dt>
                <dd translate="no">{{ scheduleExpression(item) }}</dd>
              </div>
              <div>
                <dt>{{ t('hostJobSchedules.fieldTimeZone') }}</dt>
                <dd translate="no">{{ item.timeZoneId }}</dd>
              </div>
              <div>
                <dt>{{ t('hostJobSchedules.fieldMisfirePolicy') }}</dt>
                <dd>{{ misfirePolicyLabel[item.misfirePolicy as keyof typeof misfirePolicyLabel] ?? item.misfirePolicy }}</dd>
              </div>
              <div>
                <dt>{{ t('hostJobSchedules.columnNextRun') }}</dt>
                <dd translate="no">{{ formatUtc(item.nextExecutionAtUtc) }}</dd>
              </div>
              <div>
                <dt>{{ t('hostJobSchedules.columnLastRun') }}</dt>
                <dd translate="no">{{ formatUtc(item.lastExecutionAtUtc) }}</dd>
              </div>
            </dl>
          </div>
          <div class="art-list-actions">
            <PermissionGate code="jobs.schedules.update">
              <ElButton size="small" data-testid="host-job-schedules-edit" @click="startEdit(item)">
                {{ t('hostJobSchedules.edit') }}
              </ElButton>
            </PermissionGate>
            <PermissionGate v-if="item.isEnabled" code="jobs.schedules.pause">
              <ElButton size="small" type="warning" data-testid="host-job-schedules-pause" @click="pause(item)">
                {{ t('hostJobSchedules.pause') }}
              </ElButton>
            </PermissionGate>
            <PermissionGate v-else code="jobs.schedules.resume">
              <ElButton size="small" type="primary" data-testid="host-job-schedules-resume" @click="resume(item)">
                {{ t('hostJobSchedules.resume') }}
              </ElButton>
            </PermissionGate>
          </div>
        </li>
      </ul>

      <ElPagination
        v-if="total > pageSize"
        class="host-job-schedules-pagination"
        background
        layout="prev, pager, next, total"
        :current-page="page"
        :page-size="pageSize"
        :total="total"
        data-testid="host-job-schedules-pagination"
        @current-change="onPageChange"
      />
    </ElCard>
  </section>
</template>

<style scoped>
.host-job-schedules-list li {
  align-items: flex-start;
}

.host-job-schedules-row {
  display: grid;
  gap: 0.75rem;
}

.host-job-schedules-meta {
  display: grid;
  gap: 0.5rem 1rem;
  grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
  margin: 0;
}

.host-job-schedules-meta dt {
  font-size: 0.75rem;
  opacity: 0.7;
}

.host-job-schedules-meta dd {
  margin: 0;
}

.host-job-schedules-pagination {
  margin-top: 1rem;
  justify-content: flex-end;
}
</style>
