<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { AgUiRunProgressState, AiAgentRunResponse, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { listAiModelConfigs } from '../api/ai-model-configs';
import {
  cancelAiAgentRun,
  createAiAgentRun,
  getAiAgentRun,
  parseAgUiStateSnapshot,
  resumeAiAgentRun,
  streamAiAgentRunEvents,
  type AgUiStreamEvent
} from '../api/ai-agent-runs';

defineOptions({ name: 'AiAgentRunsView' });

const { t } = useAdminI18n();
const runIdInput = ref('');
const activeRun = ref<AiAgentRunResponse>();
const progress = ref<AgUiRunProgressState>();
const events = ref<AgUiStreamEvent[]>([]);
const loading = ref(false);
const streaming = ref(false);
const problem = ref<FullNetProblemDetails>();
const streamController = ref<AbortController>();
const createModelId = ref('');
const createPrompt = ref('');
const createDefinitionKey = ref('fullnet-single-text-v1');

const definitionOptions = [
  { value: 'fullnet-single-text-v1', label: 'fullnet-single-text-v1' },
  { value: 'fullnet-readonly-tool-loop-v1', label: 'fullnet-readonly-tool-loop-v1' }
];

const canResume = computed(() => activeRun.value?.statusKey === 'awaiting_approval');
const canCancel = computed(() =>
  activeRun.value?.statusKey === 'queued' || activeRun.value?.statusKey === 'running');

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

function stopStream(): void {
  streamController.value?.abort();
  streamController.value = undefined;
  streaming.value = false;
}

async function loadRun(runId?: string): Promise<void> {
  const targetId = (runId ?? runIdInput.value).trim();
  if (!targetId) {
    return;
  }

  stopStream();
  loading.value = true;
  problem.value = undefined;
  events.value = [];
  progress.value = undefined;
  try {
    activeRun.value = await getAiAgentRun(targetId);
    runIdInput.value = activeRun.value.id;
    await startEventStream(activeRun.value.id);
  } catch (error: unknown) {
    activeRun.value = undefined;
    problem.value = toProblem(error, 'aiAgentRuns.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function startEventStream(runId: string): Promise<void> {
  stopStream();
  streaming.value = true;
  const controller = new AbortController();
  streamController.value = controller;
  try {
    await streamAiAgentRunEvents(runId, {
      onEvent: event => {
        events.value.push(event);
        if (event.eventType === 'STATE_SNAPSHOT') {
          progress.value = parseAgUiStateSnapshot(event.payload) ?? progress.value;
        }
      },
      onError: message => {
        problem.value = { code: 'client.stream_failed', title: message, status: 500, type: 'about:blank' };
      }
    }, controller.signal);
    await refreshRunQuietly(runId);
  } catch (error: unknown) {
    if (!controller.signal.aborted) {
      problem.value = toProblem(error, 'aiAgentRuns.streamFailed');
    }
  } finally {
    streaming.value = false;
    streamController.value = undefined;
  }
}

async function refreshRunQuietly(runId: string): Promise<void> {
  try {
    activeRun.value = await getAiAgentRun(runId);
  } catch {
    // 流结束后刷新失败不覆盖已有状态。
  }
}

async function handleCreate(): Promise<void> {
  if (!createModelId.value || !createPrompt.value.trim()) {
    ElMessage.warning(t('aiAgentRuns.createValidation'));
    return;
  }

  loading.value = true;
  problem.value = undefined;
  try {
    const created = await createAiAgentRun({
      clientRequestId: crypto.randomUUID(),
      definitionKey: createDefinitionKey.value,
      modelConfigId: createModelId.value,
      prompt: createPrompt.value.trim(),
      inputTokenLimit: 4096,
      outputTokenLimit: 4096
    });
    runIdInput.value = created.runId;
    await loadRun(created.runId);
    ElMessage.success(t('aiAgentRuns.createSucceeded'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'aiAgentRuns.createFailed');
  } finally {
    loading.value = false;
  }
}

async function handleCancel(): Promise<void> {
  if (!activeRun.value) {
    return;
  }
  try {
    await cancelAiAgentRun(activeRun.value.id);
    await loadRun(activeRun.value.id);
    ElMessage.success(t('aiAgentRuns.cancelSucceeded'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'aiAgentRuns.cancelFailed');
  }
}

async function handleResume(): Promise<void> {
  if (!activeRun.value) {
    return;
  }
  try {
    await resumeAiAgentRun(activeRun.value.id);
    await loadRun(activeRun.value.id);
    ElMessage.success(t('aiAgentRuns.resumeSucceeded'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'aiAgentRuns.resumeFailed');
  }
}

onBeforeUnmount(() => {
  stopStream();
});

void listAiModelConfigs({ page: 1, pageSize: 100, isEnabled: true })
  .then(result => {
    createModelId.value = result.items.find(item => item.isDefault)?.id ?? result.items[0]?.id ?? '';
  })
  .catch(() => undefined);
</script>

<template>
  <section class="ai-agent-runs-view art-page-stack art-full-height" :aria-busy="loading || streaming">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('aiAgentRuns.title') }}</h1>

    <ElAlert
      v-if="problem"
      type="error"
      :title="problem.title ?? problem.code"
      show-icon
      :closable="false"
      class="art-page-alert"
    />

    <ElCard shadow="never">
      <div class="ai-agent-runs-toolbar">
        <ElInput
          v-model="runIdInput"
          :placeholder="t('aiAgentRuns.runIdPlaceholder')"
          data-testid="ai-agent-runs-id"
        />
        <ElButton type="primary" :loading="loading" data-testid="ai-agent-runs-load" @click="loadRun()">
          {{ t('aiAgentRuns.loadRun') }}
        </ElButton>
        <PermissionGate code="ai.agent_runs.cancel">
          <ElButton
            v-if="canCancel"
            type="warning"
            data-testid="ai-agent-runs-cancel"
            @click="handleCancel"
          >
            {{ t('aiAgentRuns.cancelRun') }}
          </ElButton>
        </PermissionGate>
        <PermissionGate code="ai.agent_runs.resume">
          <ElButton
            v-if="canResume"
            type="success"
            data-testid="ai-agent-runs-resume"
            @click="handleResume"
          >
            {{ t('aiAgentRuns.resumeRun') }}
          </ElButton>
        </PermissionGate>
      </div>
    </ElCard>

    <PermissionGate code="ai.agent_runs.create">
      <ElCard shadow="never" class="ai-agent-runs-create-card">
        <template #header>{{ t('aiAgentRuns.createTitle') }}</template>
        <div class="ai-agent-runs-create-form">
          <ElSelect v-model="createDefinitionKey" :placeholder="t('aiAgentRuns.fieldDefinition')">
            <ElOption
              v-for="option in definitionOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </ElSelect>
          <ElInput v-model="createModelId" :placeholder="t('aiAgentRuns.fieldModelId')" />
          <ElInput
            v-model="createPrompt"
            type="textarea"
            :rows="3"
            :placeholder="t('aiAgentRuns.fieldPrompt')"
          />
          <ElButton type="primary" data-testid="ai-agent-runs-create" :loading="loading" @click="handleCreate">
            {{ t('aiAgentRuns.createRun') }}
          </ElButton>
        </div>
      </ElCard>
    </PermissionGate>

    <ElCard v-if="activeRun" shadow="never">
      <template #header>{{ t('aiAgentRuns.runSummary') }}</template>
      <ElDescriptions :column="2" border>
        <ElDescriptionsItem :label="t('aiAgentRuns.fieldStatus')">
          <ElTag>{{ activeRun.statusKey }}</ElTag>
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('aiAgentRuns.fieldDefinition')">{{ activeRun.definitionKey }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('aiAgentRuns.fieldDeadline')">{{ activeRun.deadlineAtUtc }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('aiAgentRuns.fieldUpdatedAt')">{{ activeRun.updatedAtUtc }}</ElDescriptionsItem>
        <ElDescriptionsItem v-if="progress?.budget" :label="t('aiAgentRuns.fieldTokens')">
          {{ progress.budget?.inputTokens ?? 'unknown' }} / {{ progress.budget?.outputTokens ?? 'unknown' }}
        </ElDescriptionsItem>
        <ElDescriptionsItem v-if="progress?.budget?.usageStatus" :label="t('aiAgentRuns.fieldUsageStatus')">
          {{ progress.budget?.usageStatus }} ({{ progress.budget?.outcome ?? 'unknown' }})
        </ElDescriptionsItem>
      </ElDescriptions>
    </ElCard>

    <ElCard v-if="progress?.steps?.length" shadow="never">
      <template #header>{{ t('aiAgentRuns.stepsTitle') }}</template>
      <ElTable :data="progress?.steps ?? []" size="small">
        <ElTableColumn prop="stepKey" :label="t('aiAgentRuns.fieldStepKey')" min-width="160" />
        <ElTableColumn prop="statusKey" :label="t('aiAgentRuns.fieldStatus')" width="120" />
        <ElTableColumn prop="inputTokens" :label="t('aiAgentRuns.fieldInputTokens')" width="120" />
        <ElTableColumn prop="outputTokens" :label="t('aiAgentRuns.fieldOutputTokens')" width="120" />
        <ElTableColumn prop="errorCode" :label="t('aiAgentRuns.fieldErrorCode')" min-width="160" />
      </ElTable>
    </ElCard>

    <ElCard shadow="never" class="ai-agent-runs-events-card">
      <template #header>{{ t('aiAgentRuns.eventsTitle') }}</template>
      <ElTable :data="events" size="small" max-height="320">
        <ElTableColumn prop="eventType" :label="t('aiAgentRuns.fieldEventType')" width="180" />
        <ElTableColumn :label="t('aiAgentRuns.fieldEventPayload')" min-width="320">
          <template #default="{ row }">
            <code class="ai-agent-runs-event-payload">{{ JSON.stringify(row.payload) }}</code>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>
  </section>
</template>

<style scoped>
.ai-agent-runs-toolbar {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
}

.ai-agent-runs-create-card {
  margin-top: 16px;
}

.ai-agent-runs-create-form {
  display: grid;
  gap: 12px;
}

.ai-agent-runs-events-card {
  margin-top: 16px;
}

.ai-agent-runs-event-payload {
  display: block;
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
