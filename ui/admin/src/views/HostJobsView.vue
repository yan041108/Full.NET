<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElSelect,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, HostJobDefinition, HostJobExecution } from '@fullnet/client-contracts';
import { isFullNetProblemDetails, JOBS_WELL_KNOWN_KEYS } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostJobDefinition,
  disableHostJobDefinition,
  listHostJobDefinitions,
  listHostJobExecutions,
  triggerHostJobDefinition,
  updateHostJobDefinition
} from '../api/host-jobs';

const session = useSessionStore();
const { t } = useAdminI18n();
const definitions = ref<HostJobDefinition[]>([]);
const executions = ref<HostJobExecution[]>([]);
const jobKey = ref(JOBS_WELL_KNOWN_KEYS.ping);
const displayName = ref('');
const description = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const editingId = ref<string>();
const selectedDefinitionId = ref<string>();
const canWrite = computed(() => session.can('jobs.definitions.write'));
const canReadExecutions = computed(() => session.can('jobs.executions.read'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listHostJobDefinitions();
    definitions.value = page.items;
    if (selectedDefinitionId.value && canReadExecutions.value) {
      await loadExecutions(selectedDefinitionId.value);
    }
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

async function loadExecutions(definitionId: string): Promise<void> {
  const page = await listHostJobExecutions(definitionId);
  executions.value = page.items;
  selectedDefinitionId.value = definitionId;
}

async function create(): Promise<void> {
  if (changing.value || !displayName.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createHostJobDefinition(
      jobKey.value,
      displayName.value.trim(),
      description.value.trim() || undefined
    );
    displayName.value = '';
    description.value = '';
    ElMessage.success(t('hostJobs.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostJobs.operationFailed');
  } finally {
    changing.value = false;
  }
}

function startEdit(item: HostJobDefinition): void {
  if (!item.isEnabled) {
    return;
  }
  editingId.value = item.id;
  displayName.value = item.displayName;
  description.value = item.description ?? '';
}

function cancelEdit(): void {
  editingId.value = undefined;
  displayName.value = '';
  description.value = '';
}

async function saveEdit(): Promise<void> {
  const item = definitions.value.find(entry => entry.id === editingId.value);
  if (!item || changing.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateHostJobDefinition(
      item.id,
      displayName.value.trim(),
      description.value.trim() || null,
      item.version
    );
    cancelEdit();
    ElMessage.success(t('hostJobs.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostJobs.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function trigger(item: HostJobDefinition): Promise<void> {
  if (changing.value || !item.isEnabled) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const execution = await triggerHostJobDefinition(item.id);
    ElMessage.success(t('hostJobs.triggerSuccess'));
    await loadExecutions(item.id);
    if (execution.status !== 'succeeded') {
      ElMessage.warning(t('hostJobs.triggerFinishedWithStatus', { status: execution.status }));
    }
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostJobs.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(item: HostJobDefinition): Promise<void> {
  if (changing.value || !item.isEnabled) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('hostJobs.confirmDisable', { name: item.displayName }),
      t('hostJobs.disable'),
      {
        type: 'warning',
        confirmButtonText: t('hostJobs.disable'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    problem.value = undefined;
    await disableHostJobDefinition(item.id, item.version);
    ElMessage.success(t('hostJobs.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error !== 'cancel') {
      problem.value = toProblem(error, 'hostJobs.operationFailed');
    }
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'hostJobs.loadFailed' | 'hostJobs.operationFailed' = 'hostJobs.operationFailed'
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
  <section class="host-jobs-view art-page-stack art-full-height" :aria-busy="loading">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('hostJobs.eyebrow') }}</p>
      <h1>{{ t('hostJobs.title') }}</h1>
      <p>{{ t('hostJobs.description') }}</p>
    </header>

    <ElCard v-if="canWrite" class="art-card">
      <template #header>
        <h2>{{ editingId ? t('hostJobs.editTitle') : t('hostJobs.createTitle') }}</h2>
      </template>
      <form class="art-form-grid" @submit.prevent="editingId ? saveEdit() : create()">
        <label v-if="!editingId">
          <span>{{ t('hostJobs.fieldJobKey') }}</span>
          <ElSelect v-model="jobKey" :disabled="changing">
            <ElOption :label="JOBS_WELL_KNOWN_KEYS.ping" :value="JOBS_WELL_KNOWN_KEYS.ping" />
          </ElSelect>
        </label>
        <label>
          <span>{{ t('hostJobs.fieldDisplayName') }}</span>
          <ElInput v-model="displayName" :disabled="changing" />
        </label>
        <label class="art-span-2">
          <span>{{ t('hostJobs.fieldDescription') }}</span>
          <ElInput v-model="description" type="textarea" :rows="3" :disabled="changing" />
        </label>
        <div class="art-form-actions">
          <ElButton v-if="editingId" @click="cancelEdit">{{ t('hostJobs.cancel') }}</ElButton>
          <ElButton type="primary" native-type="submit" :loading="changing">
            {{ editingId ? t('hostJobs.save') : t('hostJobs.create') }}
          </ElButton>
        </div>
      </form>
    </ElCard>

    <ElCard class="art-card">
      <template #header>
        <h2>{{ t('hostJobs.listTitle') }}</h2>
      </template>
      <p v-if="!definitions.length">{{ t('hostJobs.emptyList') }}</p>
      <ul v-else class="art-list">
        <li v-for="item in definitions" :key="item.id">
          <div>
            <strong>{{ item.displayName }}</strong>
            <span class="art-muted">{{ item.jobKey }}</span>
            <ElTag :type="item.isEnabled ? 'success' : 'info'">
              {{ item.isEnabled ? t('hostJobs.statusEnabled') : t('hostJobs.statusDisabled') }}
            </ElTag>
          </div>
          <div class="art-list-actions" v-if="canWrite && item.isEnabled">
            <ElButton size="small" @click="startEdit(item)">{{ t('hostJobs.edit') }}</ElButton>
            <ElButton size="small" type="primary" @click="trigger(item)">{{ t('hostJobs.trigger') }}</ElButton>
            <ElButton size="small" type="danger" @click="disable(item)">{{ t('hostJobs.disable') }}</ElButton>
          </div>
          <ElButton
            v-if="canReadExecutions"
            size="small"
            link
            @click="loadExecutions(item.id)"
          >
            {{ t('hostJobs.viewExecutions') }}
          </ElButton>
        </li>
      </ul>
    </ElCard>

    <ElCard v-if="canReadExecutions && selectedDefinitionId" class="art-card">
      <template #header>
        <h2>{{ t('hostJobs.executionsTitle') }}</h2>
      </template>
      <p v-if="!executions.length">{{ t('hostJobs.emptyExecutions') }}</p>
      <ul v-else class="art-list">
        <li v-for="item in executions" :key="item.id">
          <span>{{ item.status }}</span>
          <span class="art-muted">{{ item.createdAtUtc }}</span>
        </li>
      </ul>
    </ElCard>
  </section>
</template>
