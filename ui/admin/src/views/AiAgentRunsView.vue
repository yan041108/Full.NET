<script setup lang="ts">
import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue';
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
import type { AgUiRunProgressState, AiAgentRunResponse, CreateAiAgentRunRequest, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import PermissionGate from '../components/PermissionGate.vue';
import AiPromptPanel from '../components/AiPromptPanel.vue';
import AiAgentReconciliationPanel from '../components/AiAgentReconciliationPanel.vue';
import type { AiPromptVersionResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { promptText } from '../i18n/prompts';
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

const { t, locale } = useAdminI18n();
const session = useSessionStore();
const promptVersion = ref<AiPromptVersionResponse>();
function selectPrompt(version: AiPromptVersionResponse | undefined) {
  promptVersion.value = version;
  if (version) { createModelId.value = version.body.modelConfigId; createDefinitionKey.value = 'fullnet-single-text-v1'; }
}
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
let pendingCreate: { fingerprint: string; body: CreateAiAgentRunRequest } | undefined;
const pendingAction = shallowRef<{ runId: string; kind: 'cancel' | 'resume' }>();
let contextEpoch = 0;
let loadEpoch = 0;
let contextController = new AbortController();
const current = (epoch: number) => epoch === contextEpoch && !contextController.signal.aborted;
// 会话及租户变化同时取消表单请求和事件流，旧模型、正文和运行状态不跨上下文显示。
watch(() => [session.currentUser, session.currentContextName, session.switching], () => {
  ++contextEpoch; ++loadEpoch; contextController.abort(); contextController = new AbortController(); stopStream();
  selectPrompt(undefined); createPrompt.value = ''; createModelId.value = ''; runIdInput.value = '';
  activeRun.value = undefined; progress.value = undefined; events.value = []; problem.value = undefined; loading.value = false; pendingCreate = undefined; pendingAction.value = undefined;
  if (!session.switching) void loadModels();
}, { deep: true, flush: 'sync' });

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
  const epoch = contextEpoch; const request = ++loadEpoch;
  loading.value = true;
  problem.value = undefined;
  events.value = [];
  progress.value = undefined;
  activeRun.value = undefined;
  try {
    const result = await getAiAgentRun(targetId, contextController.signal);
    if (!current(epoch) || request !== loadEpoch) return;
    if (result.id.toLowerCase() !== targetId.toLowerCase()) throw new Error('client.invalid_ai_agent_run_response');
    activeRun.value = result;
    runIdInput.value = activeRun.value.id;
    await startEventStream(activeRun.value.id);
  } catch (error: unknown) {
    if (!current(epoch) || request !== loadEpoch) return;
    activeRun.value = undefined;
    problem.value = toProblem(error, 'aiAgentRuns.loadFailed');
  } finally {
    if (current(epoch) && request === loadEpoch) loading.value = false;
  }
}

async function startEventStream(runId: string): Promise<void> {
  stopStream();
  streaming.value = true;
  const controller = new AbortController();
  const epoch = contextEpoch;
  streamController.value = controller;
  try {
    await streamAiAgentRunEvents(runId, {
      onEvent: event => {
        if (!current(epoch) || controller.signal.aborted) return;
        events.value.push(event);
        if (event.eventType === 'STATE_SNAPSHOT') {
          progress.value = parseAgUiStateSnapshot(event.payload) ?? progress.value;
        }
      },
      onError: message => {
        if (!current(epoch) || controller.signal.aborted) return;
        problem.value = { code: 'client.stream_failed', title: message, status: 500, type: 'about:blank' };
      }
    }, controller.signal);
    if (current(epoch) && !controller.signal.aborted) await refreshRunQuietly(runId);
  } catch (error: unknown) {
    if (current(epoch) && !controller.signal.aborted) {
      problem.value = toProblem(error, 'aiAgentRuns.streamFailed');
    }
  } finally {
    if (streamController.value === controller) { streaming.value = false; streamController.value = undefined; }
  }
}

async function refreshRunQuietly(runId: string): Promise<void> {
  const epoch = contextEpoch; const request = loadEpoch;
  try {
    const result = await getAiAgentRun(runId, contextController.signal);
    if (current(epoch) && request === loadEpoch && result.id.toLowerCase() === runId.toLowerCase()) activeRun.value = result;
  } catch {
    // 流结束后刷新失败不覆盖已有状态。
  }
}

async function handleCreate(): Promise<void> {
  if (loading.value || session.switching || !session.can('ai.agent_runs.create')) return;
  const epoch = contextEpoch;
  if (!createModelId.value || !createPrompt.value.trim()) {
    ElMessage.warning(t('aiAgentRuns.createValidation'));
    return;
  }

  loading.value = true;
  problem.value = undefined;
  try {
    const draft: Omit<CreateAiAgentRunRequest, 'clientRequestId'> = {
      definitionKey: createDefinitionKey.value,
      modelConfigId: createModelId.value,
      prompt: createPrompt.value.trim(),
      inputTokenLimit: Math.min(4096, promptVersion.value?.body.inputTokenLimit ?? 4096),
      outputTokenLimit: Math.min(4096, promptVersion.value?.body.outputTokenLimit ?? 4096),
      ...(promptVersion.value ? { promptVersionId: promptVersion.value.id } : {})
    };
    // 同一上下文中结果不明的相同载荷沿用原幂等身份；修改载荷或已知接受后才创建新身份。
    const fingerprint = JSON.stringify(draft);
    if (pendingCreate?.fingerprint !== fingerprint) pendingCreate = { fingerprint, body: { clientRequestId: crypto.randomUUID(), ...draft } };
    const attempt = pendingCreate;
    const created = await createAiAgentRun(attempt.body, contextController.signal);
    if (!current(epoch)) return;
    if (pendingCreate === attempt) pendingCreate = undefined;
    runIdInput.value = created.runId;
    await loadRun(created.runId);
    if (!current(epoch)) return;
    ElMessage.success(t('aiAgentRuns.createSucceeded'));
  } catch (error: unknown) {
    if (current(epoch)) problem.value = toProblem(error, 'aiAgentRuns.createFailed');
  } finally {
    if (current(epoch)) loading.value = false;
  }
}

async function handleAction(kind: 'cancel' | 'resume'): Promise<void> {
  if (!activeRun.value || pendingAction.value || session.switching || !session.can(`ai.agent_runs.${kind}`)
    || !(kind === 'cancel' ? canCancel.value : canResume.value)) return;
  const epoch = contextEpoch; const selection = loadEpoch; const runId = activeRun.value.id;
  const operation = { runId, kind }; pendingAction.value = operation; problem.value = undefined;
  // 操作属于派发时的运行和读取代次；切换后旧成功或错误都不能接管当前页面。
  const selected = () => current(epoch) && selection === loadEpoch && activeRun.value?.id === runId;
  try {
    const accepted = await (kind === 'cancel' ? cancelAiAgentRun : resumeAiAgentRun)(runId, contextController.signal);
    if (!selected()) return;
    if (accepted !== true) throw new Error('client.ai_agent_run_operation_rejected');
    const refresh = loadRun(runId); const refreshedSelection = loadEpoch;
    // POST 已获明确回执，读取已清除旧运行；长事件流不能继续占用操作派发锁。
    if (pendingAction.value === operation) pendingAction.value = undefined;
    await refresh;
    if (current(epoch) && refreshedSelection === loadEpoch && activeRun.value?.id === runId)
      ElMessage.success(t(kind === 'cancel' ? 'aiAgentRuns.cancelSucceeded' : 'aiAgentRuns.resumeSucceeded'));
  } catch (error: unknown) {
    if (selected()) problem.value = toProblem(error, kind === 'cancel' ? 'aiAgentRuns.cancelFailed' : 'aiAgentRuns.resumeFailed');
  } finally {
    // 上下文切换可以开启新操作；旧请求的收尾不能解锁新操作。
    if (pendingAction.value === operation) pendingAction.value = undefined;
  }
}
function handleCancel(): Promise<void> { return handleAction('cancel'); }
function handleResume(): Promise<void> { return handleAction('resume'); }

onBeforeUnmount(() => {
  ++contextEpoch; contextController.abort(); pendingCreate = undefined; pendingAction.value = undefined;
  stopStream();
});

async function loadModels() {
  if (!session.can('ai.model_configs.read') || session.switching) return;
  const epoch = contextEpoch;
  try {
    const result = await listAiModelConfigs({ page: 1, pageSize: 100, isEnabled: true }, contextController.signal);
    if (current(epoch) && !promptVersion.value) createModelId.value = result.items.find(item => item.isDefault)?.id ?? result.items[0]?.id ?? '';
  } catch { /* 模型列表失败不覆盖已选版本。 */ }
}
void loadModels();
</script>

<template>
  <section class="ai-agent-runs-view art-page-stack art-full-height" :aria-busy="loading || streaming || !!pendingAction">
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
            :disabled="!!pendingAction"
            :loading="pendingAction?.runId === activeRun?.id && pendingAction?.kind === 'cancel'"
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
            :disabled="!!pendingAction"
            :loading="pendingAction?.runId === activeRun?.id && pendingAction?.kind === 'resume'"
            @click="handleResume"
          >
            {{ t('aiAgentRuns.resumeRun') }}
          </ElButton>
        </PermissionGate>
      </div>
    </ElCard>

    <AiPromptPanel :model-id="createModelId" @select="selectPrompt" />
    <AiAgentReconciliationPanel v-if="activeRun?.statusKey === 'reconciliation_required'" :key="activeRun.id"
      :run-id="activeRun.id" @reconciled="loadRun(activeRun.id)" />

    <PermissionGate code="ai.agent_runs.create">
      <ElCard shadow="never" class="ai-agent-runs-create-card">
        <template #header>{{ t('aiAgentRuns.createTitle') }}</template>
        <div class="ai-agent-runs-create-form">
          <ElButton v-if="promptVersion" data-testid="prompt-clear-binding" @click="selectPrompt(undefined)">{{ promptText(locale, 'clear') }} · v{{ promptVersion.version }}</ElButton>
          <ElSelect v-model="createDefinitionKey" :disabled="!!promptVersion" :placeholder="t('aiAgentRuns.fieldDefinition')">
            <ElOption
              v-for="option in definitionOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </ElSelect>
          <ElInput v-model="createModelId" :disabled="!!promptVersion" :placeholder="t('aiAgentRuns.fieldModelId')" />
          <ElInput
            v-model="createPrompt"
            data-testid="ai-agent-runs-create-prompt"
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
