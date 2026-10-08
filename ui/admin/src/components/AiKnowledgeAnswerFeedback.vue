<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { ElButton } from 'element-plus';
import type { AiKnowledgeAnswerFeedbackResponse, AiKnowledgeAnswerFeedbackStateResponse, AiKnowledgeCitation } from '@fullnet/client-contracts';
import { getAnswerFeedback, getAnswerFeedbackState, setAnswerFeedback, withdrawAnswerFeedback } from '../api/ai-answer-feedback';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { answerFeedbackText } from '../i18n/answer-feedback';
const props = defineProps<{ sessionId: string; messageId: string; citations: AiKnowledgeCitation[]; unavailable?: boolean; disabled?: boolean }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof answerFeedbackText>[1]) => answerFeedbackText(locale.value, key);
const open = ref(false); const busy = ref(false); const loaded = ref(false); const refreshRequired = ref(false);
const problem = ref(''); const saved = ref<AiKnowledgeAnswerFeedbackResponse>(); const version = ref(0); const statusKey = ref('');
const rating = ref('helpful'); const reason = ref('accurate'); const selected = ref<string[]>([]);
const readable = computed(() => ['ai.chat.sessions.read', 'ai.knowledge_answer_feedback.read', 'ai.knowledge_bases.read',
  'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read', 'ai.knowledge_documents.index_read'].every(code => session.can(code)) && !session.switching && !props.unavailable);
const canClear = computed(() => session.can('ai.chat.sessions.read') && session.can('ai.knowledge_answer_feedback.withdraw') && !session.switching);
const writable = computed(() => readable.value && session.can('ai.knowledge_answer_feedback.submit'));
const reasons = computed(() => rating.value === 'helpful' ? ['accurate', 'useful'] as const : ['incomplete', 'unsupported', 'outdated', 'other'] as const);
let epoch = 0; let controller: AbortController | undefined;
function clearPayload() { saved.value = undefined; rating.value = 'helpful'; reason.value = 'accurate'; selected.value = []; loaded.value = false; }
function reset() { ++epoch; controller?.abort(); controller = undefined; open.value = false; busy.value = false; refreshRequired.value = false; problem.value = ''; clearPayload(); version.value = 0; statusKey.value = ''; }
// 身份、租户、消息和精确权限变化均清空上下文；晚到结果不能重新显示旧评价。
watch(() => [props.sessionId, props.messageId, session.currentUser, session.currentContextName, session.switching], reset, { flush: 'sync', deep: true });
// 来源资格失效立即移除评价与引用，但版本状态可独立刷新，允许本人继续清除。
watch(readable, value => { if (!value) { ++epoch; controller?.abort(); busy.value = false; open.value = false; refreshRequired.value = false; clearPayload(); } }, { flush: 'sync' });
onUnmounted(reset);
watch(rating, () => { if (!reasons.value.some(value => value === reason.value)) reason.value = reasons.value[0]!; });
type Kind = 'read' | 'write' | 'withdraw' | 'state';
type FeedbackResult = AiKnowledgeAnswerFeedbackResponse | AiKnowledgeAnswerFeedbackStateResponse;
function apply(result: FeedbackResult, preserve: boolean, allowSubmit: boolean) {
  version.value = result.version; statusKey.value = result.statusKey; refreshRequired.value = false;
  if ('id' in result && readable.value) {
    saved.value = result; loaded.value = allowSubmit;
    if (!preserve && result.statusKey === 'pending_review') { rating.value = result.ratingKey!; reason.value = result.reasonKey!; selected.value = [...result.citationIds]; }
  } else clearPayload();
  if (result.statusKey === 'withdrawn') { clearPayload(); loaded.value = 'id' in result && readable.value && allowSubmit; }
}
async function request(action: (signal: AbortSignal) => Promise<FeedbackResult>, kind: Kind, preserve: boolean) {
  if (busy.value || props.disabled || ((kind === 'state' || kind === 'withdraw') ? !canClear.value : !readable.value) || (kind === 'write' && !writable.value)) return;
  const current = ++epoch; controller?.abort(); controller = new AbortController(); const signal = controller.signal;
  // 结果未知且实际没有反馈行时，显式刷新不得丢弃尚未提交的固定选择。
  const draft = kind === 'read' && preserve ? { rating: rating.value, reason: reason.value, selected: [...selected.value] } : undefined;
  // DELETE 不复核来源，不能把仅取得清除版本的状态升级为允许再次提交。
  const allowSubmit = kind === 'read' || kind === 'write' || (kind === 'withdraw' && loaded.value);
  busy.value = true; problem.value = '';
  try {
    const result = await action(signal); if (current !== epoch || signal.aborted) return;
    apply(result, preserve, allowSubmit);
  } catch (error) {
    if (current !== epoch || signal.aborted) return;
    const status = typeof error === 'object' && error !== null && 'status' in error ? error.status : undefined;
    if (kind === 'read' && (status === 403 || status === 404) && canClear.value) {
      clearPayload();
      try {
        const state = await getAnswerFeedbackState({ sessionId: props.sessionId, messageId: props.messageId }, signal);
        if (current !== epoch || signal.aborted) return; apply(state, true, false); problem.value = text('unavailable');
      } catch (stateError) {
        if (current !== epoch || signal.aborted) return;
        if (typeof stateError === 'object' && stateError !== null && 'status' in stateError && stateError.status === 404 && status === 404) {
          version.value = 0; statusKey.value = ''; loaded.value = true; refreshRequired.value = false;
          if (draft && readable.value) { rating.value = draft.rating; reason.value = draft.reason; selected.value = draft.selected; }
        }
        else { refreshRequired.value = true; problem.value = text('unavailable'); }
      }
    } else if ((kind === 'read' || kind === 'state') && status === 404 && version.value === 0) { loaded.value = kind === 'read'; refreshRequired.value = false; }
    else {
      if (status === 403 || status === 404) clearPayload();
      refreshRequired.value = true; problem.value = kind === 'write' || kind === 'withdraw' ? text('retry') : text('unavailable');
    }
  } finally { if (current === epoch) busy.value = false; }
}
async function show() { if ((!readable.value && !canClear.value) || props.disabled) return; open.value = true; await refresh(loaded.value); }
async function refresh(preserve = true) {
  if (readable.value) await request(signal => getAnswerFeedback({ sessionId: props.sessionId, messageId: props.messageId }, signal), 'read', preserve);
  else await request(signal => getAnswerFeedbackState({ sessionId: props.sessionId, messageId: props.messageId }, signal), 'state', true);
}
async function save() {
  if (!loaded.value || refreshRequired.value || !reasons.value.some(value => value === reason.value) || selected.value.length > 8) return;
  await request(signal => setAnswerFeedback({ sessionId: props.sessionId, messageId: props.messageId, body: { ratingKey: rating.value, reasonKey: reason.value, citationIds: [...selected.value], version: version.value } }, signal), 'write', true);
}
async function withdraw() { if (refreshRequired.value || version.value < 1) return; await request(signal => withdrawAnswerFeedback({ sessionId: props.sessionId, messageId: props.messageId, version: version.value }, signal), 'withdraw', true); }
function close() { ++epoch; controller?.abort(); busy.value = false; open.value = false; }
</script>
<template>
  <div v-if="readable || canClear" class="answer-feedback">
    <el-button v-if="!open" link data-testid="feedback-open" :disabled="disabled" @click="show">{{ text('open') }}</el-button>
    <section v-else :aria-label="text('open')" :aria-busy="busy">
      <p>{{ text('hint') }}</p><p v-if="statusKey" role="status">{{ text(statusKey === 'withdrawn' ? 'withdrawn' : 'pending') }}</p>
      <p v-if="problem" role="alert" data-testid="feedback-problem">{{ problem }}</p>
      <fieldset v-if="readable" :disabled="busy || disabled || !writable || !loaded">
        <legend>{{ text('citations') }}</legend>
        <label>{{ text('rating') }} <select v-model="rating" data-testid="feedback-rating"><option value="helpful">{{ text('helpful') }}</option><option value="unhelpful">{{ text('unhelpful') }}</option></select></label>
        <label>{{ text('reason') }} <select v-model="reason" data-testid="feedback-reason"><option v-for="value in reasons" :key="value" :value="value">{{ text(value) }}</option></select></label>
        <label v-for="(citation, index) in citations" :key="citation.id"><input v-model="selected" type="checkbox" :value="citation.id">{{ index + 1 }}</label>
      </fieldset>
      <el-button v-if="writable" data-testid="feedback-save" :disabled="busy || disabled || !loaded || refreshRequired" @click="save">{{ text('save') }}</el-button>
      <el-button v-if="canClear && version > 0 && statusKey !== 'withdrawn'" data-testid="feedback-withdraw" :disabled="busy || disabled || refreshRequired" @click="withdraw">{{ text('withdraw') }}</el-button>
      <el-button data-testid="feedback-refresh" :disabled="busy || disabled" @click="refresh()">{{ text('refresh') }}</el-button>
      <el-button data-testid="feedback-close" @click="close">{{ text('close') }}</el-button>
    </section>
  </div>
</template>
<style scoped>
.answer-feedback section { padding: .75rem; border: 1px solid var(--el-border-color); border-radius: var(--el-border-radius-base); }
fieldset { margin-block: .5rem; display: flex; flex-wrap: wrap; gap: .75rem; border: 0; padding: 0; }
select { color: var(--el-text-color-primary); background: var(--el-bg-color); border: 1px solid var(--el-border-color); border-radius: 4px; padding: .25rem; }
</style>
