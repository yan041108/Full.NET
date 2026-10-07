<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElCard, ElEmpty, ElInput, ElMessageBox, ElOption, ElSelect, ElSwitch } from 'element-plus';
import { Plus, VideoPause } from '@element-plus/icons-vue';
import type { AiChatMessage, AiChatSession, AiChatSessionListItem, AiKnowledgeCitation, AiModelConfigListItem, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import PermissionGate from '../components/PermissionGate.vue';
import AiKnowledgeBaseSelect from '../components/AiKnowledgeBaseSelect.vue';
import AiKnowledgeSourceDialog from '../components/AiKnowledgeSourceDialog.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showError, showProblem, showWarning } from '../feedback/fullNetMessage';
import { listAiModelConfigs } from '../api/ai-model-configs';
import { askAiKnowledge, cancelAiChatGeneration, createAiChatSession, deleteAiChatSession, getAiChatSession, listAiChatSessions, streamAiChatMessage, updateAiChatSession } from '../api/ai-chat';
defineOptions({ name: 'AiChatView' });
const { t } = useAdminI18n(); const session = useSessionStore();
const sessions = ref<AiChatSessionListItem[]>([]); const models = ref<AiModelConfigListItem[]>([]);
const selectedSessionId = ref(''); const activeSession = ref<AiChatSession>(); const draft = ref(''); const selectedModelId = ref('');
const loading = ref(false); const sending = ref(false); const cancelling = ref(false); const problem = ref<FullNetProblemDetails>();
const knowledgeMode = ref(false); const knowledgeBaseId = ref(''); const embeddingModelId = ref('');
const refusedIds = ref(new Set<string>()); const citationBases = new Map<string, string>();
const sourceOpen = ref(false); const sourceCitation = ref<AiKnowledgeCitation>(); const sourceBaseId = ref<string>();
let refreshTimer: ReturnType<typeof setTimeout> | undefined;
let epoch = 0; let readEpoch = 0; let streamController: AbortController | undefined;
const canAsk = computed(() => !session.switching && ['ai.chat.messages.send', 'ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read', 'ai.knowledge_documents.index_read'].every(code => session.can(code)));
const embeddingModels = computed(() => models.value.filter(model => ['text-embedding-3-small', 'text-embedding-3-large'].includes(model.modelId)));
const visibleMessages = computed(() => activeSession.value?.messages ?? []);
const canSend = computed(() => !!activeSession.value && draft.value.trim().length > 0 && draft.value.trim().length <= 4096
  && !session.switching && !loading.value && !sending.value && !activeSession.value.isGenerating && session.can('ai.chat.messages.send')
  && (!knowledgeMode.value || (canAsk.value && !!knowledgeBaseId.value && !!embeddingModelId.value)));
function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  return isFullNetProblemDetails(error) ? error : { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}
async function loadSession(id: string) {
  const scope = epoch; const current = ++readEpoch;
  const result = await getAiChatSession(id);
  if (scope === epoch && current === readEpoch && selectedSessionId.value === id) activeSession.value = result;
}
async function loadSessions() {
  if (session.switching || !session.can('ai.chat.sessions.read')) return;
  const scope = epoch; loading.value = true; problem.value = undefined;
  try {
    const result = await listAiChatSessions({ page: 1, pageSize: 50 }); if (scope !== epoch) return;
    sessions.value = result.items;
    if (!selectedSessionId.value && sessions.value[0]) { selectedSessionId.value = sessions.value[0].id; await loadSession(selectedSessionId.value); }
  } catch (error) { if (scope === epoch) problem.value = toProblem(error, 'aiChat.loadFailed'); }
  finally { if (scope === epoch) loading.value = false; }
}
async function loadModels() {
  if (session.switching || !session.can('ai.models.read')) return;
  const scope = epoch;
  try {
    const result = await listAiModelConfigs({ page: 1, pageSize: 100, isEnabled: true }); if (scope !== epoch) return;
    models.value = result.items.filter(item => item.isEnabled);
    selectedModelId.value = models.value.find(item => item.isDefault)?.id ?? models.value[0]?.id ?? '';
    embeddingModelId.value = embeddingModels.value[0]?.id ?? '';
  } catch (error) { if (scope === epoch) showProblem(error, t('common.loadFailed')); }
}
async function createSession() {
  if (loading.value || sending.value || !session.can('ai.chat.sessions.create')) return;
  if (!selectedModelId.value) { showWarning(t('aiChat.modelRequired')); return; }
  const scope = epoch; loading.value = true;
  try {
    const result = await createAiChatSession({ modelConfigId: selectedModelId.value }); if (scope !== epoch) return;
    sessions.value = [{ ...result, messageCount: result.messages.length, lastMessageAtUtc: result.messages.at(-1)?.createdAtUtc ?? null }, ...sessions.value];
    selectedSessionId.value = result.id; activeSession.value = result;
  } catch (error) { if (scope === epoch) showProblem(error, t('aiChat.createFailed')); }
  finally { if (scope === epoch) loading.value = false; }
}
async function selectSession(id: string) {
  if (sending.value || loading.value) return;
  activeSession.value = undefined; sourceOpen.value = false; selectedSessionId.value = id; loading.value = true;
  try { await loadSession(id); } catch (error) { showProblem(error, t('aiChat.loadFailed')); } finally { loading.value = false; }
}
async function renameSession() {
  const original = activeSession.value; const scope = epoch;
  if (!original || sending.value || loading.value || !session.can('ai.chat.sessions.update')) return;
  loading.value = true;
  try {
    const { value } = await ElMessageBox.prompt(t('aiChat.renamePrompt'), t('aiChat.renameTitle'), { inputValue: original.title });
    if (scope !== epoch || !session.can('ai.chat.sessions.update')) return;
    const result = await updateAiChatSession(original.id, { title: value.trim(), version: original.version });
    if (scope !== epoch) return; activeSession.value = result; await loadSessions();
  } catch (error) { if (scope === epoch && error !== 'cancel' && error !== 'close') showProblem(error, t('common.saveFailed')); }
  finally { if (scope === epoch) loading.value = false; }
}
async function removeSession() {
  const original = activeSession.value; const scope = epoch;
  if (!original || sending.value || loading.value || !session.can('ai.chat.sessions.delete')) return;
  loading.value = true;
  try {
    await ElMessageBox.confirm(t('aiChat.confirmDelete', { title: original.title }), { type: 'warning' });
    if (scope !== epoch || !session.can('ai.chat.sessions.delete')) return;
    await deleteAiChatSession(original.id); if (scope !== epoch) return;
    sessions.value = sessions.value.filter(item => item.id !== original.id); selectedSessionId.value = ''; activeSession.value = undefined; sourceOpen.value = false;
    await loadSessions();
  } catch (error) { if (scope === epoch && error !== 'cancel' && error !== 'close') showProblem(error, t('common.requestFailed')); }
  finally { if (scope === epoch) loading.value = false; }
}
function patchAssistant(id: string, content: string, statusKey: string) {
  if (activeSession.value) activeSession.value = { ...activeSession.value, isGenerating: statusKey === 'streaming',
    messages: activeSession.value.messages.map(item => item.id === id ? { ...item, content, statusKey } : item) };
}
async function sendMessage() {
  if (!activeSession.value || !canSend.value) return;
  const scope = epoch; const original = activeSession.value; const content = draft.value.trim(); const useKnowledge = knowledgeMode.value;
  const base = knowledgeBaseId.value; const embedding = embeddingModelId.value;
  draft.value = ''; sending.value = true; const controller = new AbortController(); streamController = controller;
  const current = () => scope === epoch && streamController === controller && !controller.signal.aborted && activeSession.value?.id === original.id;
  const assistant: AiChatMessage = { id: crypto.randomUUID(), sessionId: original.id, roleKey: 'assistant', content: '', statusKey: 'streaming', promptTokens: null, completionTokens: null, createdAtUtc: new Date().toISOString() };
  activeSession.value = { ...original, isGenerating: true, messages: [...original.messages, { ...assistant, id: crypto.randomUUID(), roleKey: 'user', content, statusKey: 'completed' }, assistant] };
  try {
    if (useKnowledge) {
      const result = await askAiKnowledge(original.id, { knowledgeBaseIds: [base], embeddingModelConfigId: embedding, question: content }, controller.signal);
      if (!current()) return;
      activeSession.value = { ...activeSession.value!, isGenerating: false, messages: activeSession.value!.messages.map(item => item.id === assistant.id ? result.message : item) };
      citationBases.set(result.message.id, base); if (result.refused) refusedIds.value.add(result.message.id);
    } else {
      await streamAiChatMessage(original.id, { content }, {
        onDelta: delta => { if (current()) { assistant.content += delta; patchAssistant(assistant.id, assistant.content, 'streaming'); } },
        onDone: () => { if (current()) patchAssistant(assistant.id, assistant.content, 'completed'); },
        onError: message => { if (current()) { patchAssistant(assistant.id, assistant.content, 'failed'); showError(message); } }
      }, controller.signal);
    }
    if (!current()) return; await loadSession(original.id); if (current()) await loadSessions();
  } catch (error) {
    if (!current()) return;
    patchAssistant(assistant.id, '', 'failed'); if (!draft.value) draft.value = content;
    showProblem(error, t('aiChat.sendFailed'));
  } finally { if (scope === epoch && streamController === controller) { sending.value = false; streamController = undefined; } }
}
function scheduleGenerationRefresh(id: string, scope: number, remaining = 20) {
  if (refreshTimer) clearTimeout(refreshTimer);
  if (remaining <= 0 || scope !== epoch || activeSession.value?.id !== id || !activeSession.value.isGenerating) return;
  refreshTimer = setTimeout(async () => {
    refreshTimer = undefined;
    if (scope !== epoch || selectedSessionId.value !== id || session.switching || !session.can('ai.chat.sessions.read')) return;
    try { await loadSession(id); if (scope === epoch) scheduleGenerationRefresh(id, scope, remaining - 1); }
    catch (error) { if (scope === epoch) problem.value = toProblem(error, 'aiChat.loadFailed'); }
  }, 1500);
}
async function refreshSession() {
  if (!activeSession.value || loading.value || sending.value || session.switching || !session.can('ai.chat.sessions.read')) return;
  const id = activeSession.value.id; const scope = epoch; loading.value = true;
  try { await loadSession(id); if (scope === epoch) scheduleGenerationRefresh(id, scope); }
  catch (error) { if (scope === epoch) showProblem(error, t('aiChat.loadFailed')); }
  finally { if (scope === epoch) loading.value = false; }
}
async function stopGeneration() {
  const original = activeSession.value; const scope = epoch; const controller = streamController;
  if (!original || cancelling.value || !session.can('ai.chat.messages.cancel')) return;
  cancelling.value = true;
  try {
    // 先取消服务器共享槽位，再终止浏览器等待；Abort 本身不代表计费任务已停止。
    await cancelAiChatGeneration(original.id); if (scope !== epoch) return;
    controller?.abort(); await loadSession(original.id); if (scope === epoch) scheduleGenerationRefresh(original.id, scope);
  } catch (error) {
    if (scope === epoch) {
      showProblem(error, t('common.requestFailed'));
      try { await loadSession(original.id); if (scope === epoch) scheduleGenerationRefresh(original.id, scope); }
      catch (refreshError) { if (scope === epoch) problem.value = toProblem(refreshError, 'aiChat.loadFailed'); }
    }
  }
  finally { if (scope === epoch) cancelling.value = false; }
}
function openCitation(message: AiChatMessage, citation: AiKnowledgeCitation) {
  if (message.knowledgeUnavailableCode || !['ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read'].every(code => session.can(code))) return;
  sourceCitation.value = citation; sourceBaseId.value = citationBases.get(message.id); sourceOpen.value = true;
}
function reset() {
  if (refreshTimer) { clearTimeout(refreshTimer); refreshTimer = undefined; }
  ++epoch; ++readEpoch; streamController?.abort(); streamController = undefined; sessions.value = []; models.value = [];
  activeSession.value = undefined; selectedSessionId.value = ''; draft.value = ''; knowledgeBaseId.value = ''; embeddingModelId.value = ''; selectedModelId.value = '';
  refusedIds.value.clear(); citationBases.clear(); sourceOpen.value = false; sourceCitation.value = undefined; problem.value = undefined;
  sending.value = false; loading.value = false; cancelling.value = false; knowledgeMode.value = false;
}
watch(() => [session.currentUser, session.switching], () => { reset(); if (!session.switching && session.can('ai.chat.sessions.read')) { void loadModels(); void loadSessions(); } }, { deep: true });
onMounted(() => { void loadModels(); void loadSessions(); }); onUnmounted(reset);
</script>

<template>
  <section class="ai-chat-view art-page-stack art-full-height" :aria-busy="loading || sending">
    <div class="ai-chat-heading-row">
      <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('aiChat.title') }}</h1>
      <PermissionGate code="ai.knowledge_bases.read"><router-link to="/ai/knowledge-bases">{{ t('knowledge.title') }}</router-link></PermissionGate>
      <PermissionGate code="ai.agent_runs.read">
        <router-link class="ai-chat-runs-link" to="/ai/agent-runs">{{ t('navigation.aiAgentRuns.title') }}</router-link>
      </PermissionGate>
    </div>

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
          <el-select v-model="selectedModelId" :disabled="sending || loading" :placeholder="t('aiChat.fieldModel')" style="width: 100%">
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
              :disabled="sending || loading"
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
          :disabled="sending || loading"
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
              <PermissionGate code="ai.chat.sessions.read"><el-button :disabled="sending || loading" data-testid="ai-chat-refresh" @click="refreshSession">{{ t('common.refresh') }}</el-button></PermissionGate>
              <PermissionGate code="ai.chat.sessions.update">
                <el-button :disabled="sending || loading" @click="renameSession">{{ t('aiChat.rename') }}</el-button>
              </PermissionGate>
              <PermissionGate code="ai.chat.sessions.delete">
                <el-button type="danger" :disabled="sending || loading" @click="removeSession">{{ t('aiChat.delete') }}</el-button>
              </PermissionGate>
              <PermissionGate code="ai.chat.messages.cancel">
                <el-button
                  v-if="activeSession.isGenerating || sending"
                  :icon="VideoPause"
                  :loading="cancelling"
                  :disabled="cancelling"
                  data-testid="ai-chat-stop"
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
              <p v-if="message.knowledgeUnavailableCode" data-testid="knowledge-answer-unavailable">{{ t('knowledge.answerUnavailable') }}</p>
              <p v-else-if="refusedIds.has(message.id)" data-testid="knowledge-refused">{{ t('knowledge.refused') }}</p>
              <p v-else-if="message.roleKey === 'assistant' && message.statusKey === 'completed' && !message.content" data-testid="knowledge-no-answer">{{ t('knowledge.noAnswer') }}</p>
              <p v-else>{{ message.content || (message.statusKey === 'streaming' ? '...' : '') }}</p>
              <div v-if="!message.knowledgeUnavailableCode && message.citations?.length && session.can('ai.knowledge_bases.read') && session.can('ai.knowledge_documents.read') && session.can('ai.knowledge_documents.source_read')" class="ai-chat-citations">
                <el-button v-for="(citation, index) in message.citations" :key="citation.id" link type="primary" :disabled="!session.can('ai.knowledge_bases.read') || !session.can('ai.knowledge_documents.read') || !session.can('ai.knowledge_documents.source_read')" data-testid="knowledge-citation" @click="openCitation(message, citation)">{{ t('knowledge.citation', { number: index + 1 }) }} · {{ t('knowledge.position', { page: citation.pageNumber ?? t('knowledge.noPage'), sequence: citation.sequence, offset: citation.offset, length: citation.length }) }}</el-button>
              </div>
            </article>
          </div>

          <div v-if="canAsk" class="ai-chat-knowledge">
            <el-switch v-model="knowledgeMode" :disabled="sending || loading" :active-text="t('knowledge.mode')" :aria-label="t('knowledge.mode')" data-testid="knowledge-mode" />
            <template v-if="knowledgeMode">
              <AiKnowledgeBaseSelect v-model="knowledgeBaseId" :disabled="sending || loading" />
              <el-select v-model="embeddingModelId" :disabled="sending || loading" :placeholder="t('knowledge.embedding')" :aria-label="t('knowledge.embedding')" data-testid="knowledge-embedding">
                <el-option v-for="model in embeddingModels" :key="model.id" :label="model.name" :value="model.id" />
              </el-select><p>{{ t('knowledge.askHint') }}</p>
            </template>
          </div>
          <footer class="ai-chat-composer">
            <el-input
              v-model="draft"
              type="textarea"
              :rows="4"
              :maxlength="4096"
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
    <AiKnowledgeSourceDialog v-if="sourceCitation" v-model:open="sourceOpen" :knowledge-base-id="sourceBaseId" :document-id="sourceCitation.documentId" :version-id="sourceCitation.versionId" :citation="sourceCitation" />
  </section>
</template>

<style scoped>
.ai-chat-knowledge { display: grid; gap: 12px; margin: 16px 0; }
.ai-chat-knowledge p { color: var(--el-text-color-secondary); margin: 0; font-size: 13px; }
.ai-chat-citations { display: flex; flex-wrap: wrap; gap: 8px; }
.ai-chat-citations :deep(.el-button) { height: auto; white-space: normal; text-align: left; }
.ai-chat-citations :deep(.el-button span) { white-space: normal; overflow-wrap: anywhere; }

.ai-chat-heading-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.ai-chat-runs-link {
  font-size: 14px;
  white-space: nowrap;
}

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
