<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect,
  ElTag
} from 'element-plus';
import type {
  FullNetProblemDetails,
  HostJobDefinition,
  HostJobSchedule
} from '@fullnet/client-contracts';
import {
  isFullNetProblemDetails,
  JOB_MISFIRE_POLICIES,
  JOB_TRIGGER_KINDS
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import { listHostJobDefinitions } from '../api/host-jobs';
import {
  createHostJobSchedule,
  listHostJobSchedules,
  pauseHostJobSchedule,
  resumeHostJobSchedule,
  updateHostJobSchedule
} from '../api/host-job-schedules';

const session = useSessionStore();
const { t } = useAdminI18n();
const definitions = ref<HostJobDefinition[]>([]);
const schedules = ref<HostJobSchedule[]>([]);
const jobDefinitionId = ref('');
const triggerKind = ref<string>(JOB_TRIGGER_KINDS.cron);
const cronExpression = ref('0 9 * * *');
const timeZoneId = ref('UTC');
const oneTimeAtUtc = ref('');
const misfirePolicy = ref<string>(JOB_MISFIRE_POLICIES.skip);
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

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [definitionPage, schedulePage] = await Promise.all([
      listHostJobDefinitions(),
      listHostJobSchedules()
    ]);
    definitions.value = definitionPage.items.filter(item => item.isEnabled);
    schedules.value = schedulePage.items;
    if (!jobDefinitionId.value && definitions.value.length) {
      jobDefinitionId.value = definitions.value[0].id;
    }
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostJobSchedules.loadFailed');
  } finally {
    loading.value = false;
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

function definitionLabel(definitionId: string): string {
  return definitions.value.find(item => item.id === definitionId)?.displayName
    ?? definitionId;
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
              v-for="item in definitions"
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
        </label>
        <label v-else>
          <span>{{ t('hostJobSchedules.fieldOneTimeAt') }}</span>
          <ElInput v-model="oneTimeAtUtc" :disabled="changing" data-testid="host-job-schedules-one-time" />
        </label>
        <label>
          <span>{{ t('hostJobSchedules.fieldTimeZone') }}</span>
          <ElInput v-model="timeZoneId" :disabled="changing" data-testid="host-job-schedules-time-zone" />
        </label>
        <label>
          <span>{{ t('hostJobSchedules.fieldMisfirePolicy') }}</span>
          <ElSelect v-model="misfirePolicy" :disabled="changing" data-testid="host-job-schedules-misfire">
            <ElOption :label="JOB_MISFIRE_POLICIES.skip" :value="JOB_MISFIRE_POLICIES.skip" />
            <ElOption :label="JOB_MISFIRE_POLICIES.fireOnce" :value="JOB_MISFIRE_POLICIES.fireOnce" />
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
      <p v-if="!schedules.length">{{ t('hostJobSchedules.emptyList') }}</p>
      <ul v-else class="art-list">
        <li v-for="item in schedules" :key="item.id">
          <div>
            <strong>{{ definitionLabel(item.jobDefinitionId) }}</strong>
            <span class="art-muted">{{ item.triggerKind }}</span>
            <ElTag :type="item.isEnabled ? 'success' : 'info'">
              {{ item.isEnabled ? t('hostJobSchedules.statusEnabled') : t('hostJobSchedules.statusPaused') }}
            </ElTag>
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
    </ElCard>
  </section>
</template>
