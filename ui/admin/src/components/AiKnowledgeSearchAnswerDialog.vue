<script setup lang="ts">
import { defineAsyncComponent, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDialog, ElOption, ElSelect } from 'element-plus';
import { isFullNetProblemDetails, type AiChatMessage, type AiChatSessionListItem, type AiKnowledgeCitation, type AiPersonalScheduleCitation, type AskAiKnowledgeRequest } from '@fullnet/client-contracts';
import AiKnowledgeCitationPosition from './AiKnowledgeCitationPosition.vue';
import AiKnowledgeSourceDialog from './AiKnowledgeSourceDialog.vue';
import AiKnowledgeAnswerFeedback from './AiKnowledgeAnswerFeedback.vue';
import { askAiKnowledge, getAiChatSession, listAiChatSessions } from '../api/ai-chat';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeSearchText } from '../i18n/ai-knowledge-search';
import { personalScheduleAssistantText } from '../i18n/personal-schedule-assistant';
const AiPersonalScheduleSourceDialog = defineAsyncComponent(() => import('./AiPersonalScheduleSourceDialog.vue'));

const props = defineProps<{ open: boolean; target: AskAiKnowledgeRequest }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; changed: []; unavailable: [] }>();
const session = useSessionStore(); const { locale, t } = useAdminI18n();
const tr = (key: Parameters<typeof knowledgeSearchText>[1]) => knowledgeSearchText(locale.value, key);
const rows = ref<AiChatSessionListItem[]>([]); const page = ref(1); const total = ref(0); const sessionId = ref('');
const busy = ref(false); const failed = ref(false); const attempted = ref(false); const message = ref<AiChatMessage>();
const sourceOpen = ref(false); const preview = ref<AiKnowledgeCitation>();
const schedulePreview = ref<AiPersonalScheduleCitation>();
let epoch = 0; let controller: AbortController | undefined;
function clear() { ++epoch; controller?.abort(); busy.value = false; failed.value = false; message.value = undefined; preview.value = undefined; sourceOpen.value = false; schedulePreview.value = undefined; }
function allowed() { return props.open && !session.switching && ['ai.chat.messages.send', 'ai.chat.sessions.read',
  ...(props.target.searchScope ? ['ai.knowledge_search.read'] : []),
  ...(props.target.personalSchedules?.length ? ['calendar.personal_schedules.read', 'calendar.personal_schedules.assistant_read'] : [])].every(code => session.can(code)); }
async function load(next = 1) {
  clear(); rows.value = []; sessionId.value = ''; attempted.value = false;
  if (!allowed()) return;
  const current = ++epoch; controller = new AbortController(); busy.value = true;
  try { const result = await listAiChatSessions({ page: next, pageSize: 20 }, controller.signal);
    if (current === epoch && allowed()) { rows.value = result.items; page.value = next; total.value = result.total; }
  } catch { if (current === epoch) failed.value = true; }
  finally { if (current === epoch) busy.value = false; }
}
async function run(historyOnly = false) {
  if (!allowed() || busy.value || !sessionId.value || !historyOnly && attempted.value) return;
  const previous = message.value?.id; clear(); const current = ++epoch; const selected = sessionId.value;
  controller = new AbortController(); busy.value = true;
  try {
    if (!historyOnly) {
      // 一次显式点击只派发一次；失败或取消后只能核对历史，不能隐式重试收费。
      attempted.value = true;
      const answer = await askAiKnowledge(selected, props.target, controller.signal);
      if (current !== epoch || !allowed()) return;
      message.value = answer.message;
    }
    const id = message.value?.id ?? previous;
    const history = await getAiChatSession(selected, controller.signal);
    if (current !== epoch || !allowed()) return;
    message.value = id ? history.messages.find(row => row.id === id && row.isKnowledgeAnswer) : undefined;
    if (id && !message.value) failed.value = true;
    if (message.value?.knowledgeUnavailableCode) { clear(); emit('unavailable'); }
  } catch (error) {
    if (current !== epoch || !allowed()) return;
    clear(); failed.value = true;
    // 业务问答未知失败也不能继续显示先前预览的私人来源；原结果仍可在正式聊天历史核对。
    if (props.target.personalSchedules?.length) { emit('unavailable'); return; }
    if (isFullNetProblemDetails(error) && error.code === 'ai.knowledge.search_source_changed') emit('changed');
    else if (isFullNetProblemDetails(error) && (error.status === 403
      || ['ai.knowledge.answer_unavailable', 'ai.knowledge.model_unavailable'].includes(error.code))) emit('unavailable');
  } finally { if (current === epoch) busy.value = false; }
}
watch(sessionId, () => { clear(); attempted.value = false; });
watch(() => [props.open, props.target], () => { page.value = 1; total.value = 0; void load(); }, { immediate: true, deep: true });
watch(() => [session.currentUser, session.switching], () => { clear(); rows.value = []; sessionId.value = ''; emit('update:open', false); }, { deep: true });
onUnmounted(clear);
</script>

<template>
  <section class="knowledge-answer-layer">
  <ElDialog :model-value="open" :title="tr('answerTitle')" width="min(800px, calc(100vw - 32px))"
    class="knowledge-search-answer-dialog" data-testid="search-answer-dialog" @update:model-value="value => { clear(); emit('update:open', value); }">
    <ElAlert :title="tr('answerHint')" type="warning" :closable="false" />
    <p class="answer-question">{{ target.question }}</p>
    <label for="search-answer-session">{{ tr('answerSession') }}</label>
    <ElSelect id="search-answer-session" v-model="sessionId" :teleported="false" :disabled="busy" :aria-label="tr('answerSession')" data-testid="answer-session">
      <ElOption v-for="row in rows" :key="row.id" :value="row.id" :label="row.title + ' · ' + row.modelName" />
    </ElSelect>
    <div class="answer-actions"><ElButton :disabled="busy || page <= 1" @click="load(page - 1)">{{ tr('answerPrevious') }}</ElButton>
      <ElButton :disabled="busy || page * 20 >= total" @click="load(page + 1)">{{ tr('answerNext') }}</ElButton>
      <ElButton :disabled="busy || !sessionId || attempted" data-testid="answer-confirm" @click="run()">{{ tr('answerConfirm') }}</ElButton>
      <ElButton :disabled="busy || !sessionId || !attempted" data-testid="answer-history" @click="run(true)">{{ tr('answerHistory') }}</ElButton></div>
    <p v-if="busy" role="status">{{ tr('loading') }}</p>
    <ElAlert v-if="failed" :title="tr('answerFailed')" type="error" :closable="false" />
    <template v-if="message">
      <ElAlert v-if="message.knowledgeUnavailableCode" :title="tr('unavailable')" type="warning" :closable="false" data-testid="answer-unavailable" />
      <template v-else>
        <pre data-testid="search-answer-text">{{ message.content || tr('answerRefused') }}</pre>
        <div v-for="item in message.citations" :key="item.id"><AiKnowledgeCitationPosition :citation="item" />
          <ElButton data-testid="answer-preview" @click="preview = item; sourceOpen = true">{{ tr('preview') }}</ElButton></div>
        <ElButton v-for="item in message.personalScheduleCitations ?? []" :key="item.id" class="schedule-citation" data-testid="answer-schedule-preview" @click="schedulePreview = item">
          {{ personalScheduleAssistantText(locale, 'citation') }} · {{ item.scheduleId }} · {{ item.version }}
        </ElButton>
      </template>
      <AiKnowledgeAnswerFeedback :session-id="sessionId" :message-id="message.id" :citations="message.citations ?? []" :unavailable="!!message.knowledgeUnavailableCode" :disabled="busy" />
    </template>
    <template #footer><ElButton @click="clear(); emit('update:open', false)">{{ t('common.cancel') }}</ElButton></template>
  </ElDialog>
  <AiKnowledgeSourceDialog v-if="preview" v-model:open="sourceOpen" :knowledge-base-id="target.searchScope?.sources.find(source => source.documentId === preview?.documentId)?.knowledgeBaseId"
    :candidate-base-ids="target.knowledgeBaseIds" :document-id="preview.documentId" :version-id="preview.versionId" :citation="preview" @unavailable="clear(); emit('unavailable')" />
  <AiPersonalScheduleSourceDialog v-if="schedulePreview" :schedule-id="schedulePreview.scheduleId" :expected-version="schedulePreview.version"
    :expected-hash="schedulePreview.projectionHash" :allow-ask="false" @close="schedulePreview = undefined" @invalidated="clear(); emit('unavailable')" />
  </section>
</template>

<style scoped>
.answer-question, pre { white-space: pre-wrap; overflow-wrap: anywhere; font: inherit; max-height: 35vh; overflow-y: auto; }
.answer-actions { display: flex; gap: 10px; flex-wrap: wrap; margin-block: 12px; }
.answer-actions :deep(.el-button + .el-button) { margin-left: 0; }
.answer-actions :deep(.el-button span) { background-color: var(--el-fill-color-blank); }
.el-select { max-width: 100%; margin-block: 12px; }
.schedule-citation { white-space: normal; height: auto; max-width: 100%; }
.schedule-citation :deep(span) { overflow-wrap: anywhere; min-width: 0; }
/* 对话框内选值文字使用实体表面，保持遮罩下文字对比度。 */
:global(.knowledge-search-answer-dialog .el-select__placeholder > span) { background-color: var(--el-fill-color-blank); }
</style>
