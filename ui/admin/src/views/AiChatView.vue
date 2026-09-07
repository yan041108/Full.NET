<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElSelect
} from 'element-plus';
import { Plus, VideoPause } from '@element-plus/icons-vue';
import type { AiChatMessage, AiChatSession, AiChatSessionListItem, AiModelConfigListItem, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { listAiModelConfigs } from '../api/ai-model-configs';
import {
  cancelAiChatGeneration,
  createAiChatSession,
  deleteAiChatSession,
  getAiChatSession,
  listAiChatSessions,
  streamAiChatMessage,
  updateAiChatSession
} from '../api/ai-chat';

defineOptions({ name: 'AiChatView' });

const { t } = useAdminI18n();
const sessions = ref<AiChatSessionListItem[]>([]);
const models = ref<AiModelConfigListItem[]>([]);
const selectedSessionId = ref('');
const activeSession = ref<AiChatSession>();
const draft = ref('');
const selectedModelId = ref('');
const loading = ref(false);
const sending = ref(false);
const problem = ref<FullNetProblemDetails>();
const streamController = ref<AbortController>();

const selectedSession = computed(() =>
  sessions.value.find(item => item.id === selectedSessionId.value));

const visibleMessages = computed(() => activeSession.value?.messages ?? []);

const canSend = computed(() =>
  !!selectedSessionId.value && draft.value.trim().length > 0 && !sending.value);

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

async function loadSessions(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listAiChatSessions({ page: 1, pageSize: 50 });
    sessions.value = result.items;
    if (!selectedSessionId.value && sessions.value[0]) {
      selectedSessionId.value = sessions.value[0].id;
      await loadSession(selectedSessionId.value);
    }
  } catch (error: unknown) {
    problem.value = toProblem(error, 'aiChat.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function loadModels(): Promise<void> {
  const result = await listAiModelConfigs({ page: 1, pageSize: 100, isEnabled: true });
  models.value = result.items.filter(item => item.isEnabled);
  selectedModelId.value = models.value.find(item => item.isDefault)?.id
    ?? models.value[0]?.id
    ?? '';
}

async function loadSession(sessionId: string): Promise<void> {
  activeSession.value = await getAiChatSession(sessionId);
}

async function createSession(): Promise<void> {
  if (!selectedModelId.value) {
    ElMessage.warning(t('aiChat.modelRequired'));
    return;
  }
  loading.value = true;
  try {
    const session = await createAiChatSession({ modelConfigId: selectedModelId.value });
    sessions.value = [{ ...session, messageCount: session.messages.length, lastMessageAtUtc: session.messages.at(-1)?.createdAtUtc ?? null }, ...sessions.value];
    selectedSessionId.value = session.id;
    activeSession.value = session;
  } catch (error: unknown) {
    ElMessage.error(toProblem(error, 'aiChat.createFailed').title);
  } finally {
    loading.value = false;
  }
}

async function selectSession(sessionId: string): Promise<void> {
  selectedSessionId.value = sessionId;
  await loadSession(sessionId);
}

async function renameSession(): Promise<void> {
  if (!activeSession.value) {
    return;
  }
  const { value } = await ElMessageBox.prompt(
    t('aiChat.renamePrompt'),
    t('aiChat.renameTitle'),
    { inputValue: activeSession.value.title }
  );
  const updated = await updateAiChatSession(activeSession.value.id, {
    title: value.trim(),
    version: activeSession.value.version
  });
  activeSession.value = updated;
  sessions.value = sessions.value.map(item =>
    item.id === updated.id ? { ...item, title: updated.title, version: updated.version } : item);
}

async function removeSession(): Promise<void> {
  if (!activeSession.value) {
    return;
  }
  await ElMessageBox.confirm(
    t('aiChat.confirmDelete', { title: activeSession.value.title }),
    { type: 'warning' }
  );
  await deleteAiChatSession(activeSession.value.id);
  sessions.value = sessions.value.filter(item => item.id !== activeSession.value!.id);
  selectedSessionId.value = sessions.value[0]?.id ?? '';
  activeSession.value = selectedSessionId.value
    ? await getAiChatSession(selectedSessionId.value)
    : undefined;
}

function appendLocalMessage(message: AiChatMessage): void {
  if (!activeSession.value) {
    return;
  }
  activeSession.value = {
    ...activeSession.value,
    messages: [...activeSession.value.messages, message]
  };
}

function patchAssistantContent(messageId: string, content: string, statusKey: string): void {
  if (!activeSession.value) {
    return;
  }
  activeSession.value = {
    ...activeSession.value,
    isGenerating: false,
    messages: activeSession.value.messages.map(item =>
      item.id === messageId ? { ...item, content, statusKey } : item)
  };
}

async function sendMessage(): Promise<void> {
  if (!activeSession.value || !canSend.value) {
    return;
  }
  const content = draft.value.trim();
  draft.value = '';
  sending.value = true;
  streamController.value = new AbortController();
  const userMessage: AiChatMessage = {
    id: crypto.randomUUID(),
    sessionId: activeSession.value.id,
    roleKey: 'user',
    content,
    statusKey: 'completed',
    promptTokens: null,
    completionTokens: null,
    createdAtUtc: new Date().toISOString()
  };
  const assistantMessage: AiChatMessage = {
    id: crypto.randomUUID(),
    sessionId: activeSession.value.id,
    roleKey: 'assistant',
    content: '',
    statusKey: 'streaming',
    promptTokens: null,
    completionTokens: null,
    createdAtUtc: new Date().toISOString()
  };
  appendLocalMessage(userMessage);
  appendLocalMessage(assistantMessage);
  activeSession.value = { ...activeSession.value, isGenerating: true };

  try {
    await streamAiChatMessage(
      activeSession.value.id,
      { content },
      {
        onDelta: delta => {
          assistantMessage.content += delta;
          patchAssistantContent(assistantMessage.id, assistantMessage.content, 'streaming');
        },
        onDone: event => {
          patchAssistantContent(assistantMessage.id, assistantMessage.content, 'completed');
          assistantMessage.id = event.assistantMessageId;
        },
        onError: message => {
          patchAssistantContent(assistantMessage.id, assistantMessage.content, 'failed');
          ElMessage.error(message);
        }
      },
      streamController.value.signal
    );
    await loadSession(activeSession.value.id);
    await loadSessions();
  } catch (error: unknown) {
    patchAssistantContent(assistantMessage.id, assistantMessage.content, 'failed');
    ElMessage.error(toProblem(error, 'aiChat.sendFailed').title);
  } finally {
    sending.value = false;
    streamController.value = undefined;
  }
}

async function stopGeneration(): Promise<void> {
  if (!activeSession.value) {
    return;
  }
  streamController.value?.abort();
  await cancelAiChatGeneration(activeSession.value.id);
}

onMounted(async () => {
  await loadModels();
  await loadSessions();
});
</script>

<template>
  <section class="ai-chat-view art-page-stack art-full-height" :aria-busy="loading || sending">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('aiChat.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail"
      show-icon
      class="art-page-alert"
    />

    <div class="ai-chat-layout">
      <el-card class="ai-chat-sidebar" shadow="never">
        <div class="ai-chat-sidebar-actions">
          <el-select v-model="selectedModelId" :placeholder="t('aiChat.fieldModel')" style="width: 100%">
            <el-option
              v-for="model in models"
              :key="model.id"
              :label="model.name"
              :value="model.id"
            />
          </el-select>
          <PermissionGate code="ai.chat.sessions.create">
            <el-button
              type="primary"
              :icon="Plus"
              data-testid="ai-chat-create"
              @click="createSession"
            >
              {{ t('aiChat.newSession') }}
            </el-button>
          </PermissionGate>
        </div>
        <button
          v-for="item in sessions"
          :key="item.id"
          type="button"
          class="ai-chat-session-item"
          :class="{ active: item.id === selectedSessionId }"
          @click="selectSession(item.id)"
        >
          <strong>{{ item.title }}</strong>
          <span>{{ item.modelName }}</span>
        </button>
      </el-card>

      <el-card class="ai-chat-main art-full-height-card" shadow="never">
        <template v-if="activeSession">
          <header class="ai-chat-header">
            <div>
              <h2>{{ activeSession.title }}</h2>
              <p>{{ activeSession.modelName }}</p>
            </div>
            <div class="ai-chat-header-actions">
              <PermissionGate code="ai.chat.sessions.update">
                <el-button @click="renameSession">{{ t('aiChat.rename') }}</el-button>
              </PermissionGate>
              <PermissionGate code="ai.chat.sessions.delete">
                <el-button type="danger" @click="removeSession">{{ t('aiChat.delete') }}</el-button>
              </PermissionGate>
              <PermissionGate code="ai.chat.messages.cancel">
                <el-button
                  v-if="activeSession.isGenerating || sending"
                  :icon="VideoPause"
                  @click="stopGeneration"
                >
                  {{ t('aiChat.stop') }}
                </el-button>
              </PermissionGate>
            </div>
          </header>

          <div class="ai-chat-messages">
            <article
              v-for="message in visibleMessages"
              :key="message.id"
              class="ai-chat-message"
              :class="`role-${message.roleKey}`"
            >
              <header>{{ message.roleKey }}</header>
              <p>{{ message.content || (message.statusKey === 'streaming' ? '...' : '') }}</p>
            </article>
          </div>

          <footer class="ai-chat-composer">
            <el-input
              v-model="draft"
              type="textarea"
              :rows="4"
              :placeholder="t('aiChat.inputPlaceholder')"
              @keydown.ctrl.enter.prevent="sendMessage"
            />
            <PermissionGate code="ai.chat.messages.send">
              <el-button type="primary" :disabled="!canSend" @click="sendMessage">
                {{ t('aiChat.send') }}
              </el-button>
            </PermissionGate>
          </footer>
        </template>
        <el-empty v-else :description="t('aiChat.emptyState')" />
      </el-card>
    </div>
  </section>
</template>

<style scoped>
.ai-chat-layout {
  display: grid;
  grid-template-columns: 280px 1fr;
  gap: 16px;
  min-height: 70vh;
}

.ai-chat-sidebar-actions {
  display: grid;
  gap: 12px;
  margin-bottom: 16px;
}

.ai-chat-session-item {
  display: grid;
  gap: 4px;
  width: 100%;
  margin-bottom: 8px;
  padding: 12px;
  border: 1px solid var(--el-border-color);
  border-radius: 8px;
  background: transparent;
  text-align: left;
  cursor: pointer;
}

.ai-chat-session-item.active {
  border-color: var(--el-color-primary);
  background: var(--el-color-primary-light-9);
}

.ai-chat-header,
.ai-chat-composer {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  align-items: flex-start;
}

.ai-chat-messages {
  display: grid;
  gap: 12px;
  min-height: 360px;
  max-height: 55vh;
  overflow: auto;
  margin: 16px 0;
}

.ai-chat-message {
  padding: 12px;
  border-radius: 8px;
  background: var(--el-fill-color-light);
}

.ai-chat-message.role-user {
  justify-self: end;
  max-width: 80%;
}

.ai-chat-message.role-assistant {
  justify-self: start;
  max-width: 90%;
}
</style>
