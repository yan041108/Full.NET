<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { ElButton, ElDialog, ElForm, ElFormItem, ElInput, ElMessageBox, ElCheckbox } from 'element-plus';
import type { AiFeedbackEvaluationCandidate, AiKnowledgeAnswerFeedbackReviewItem } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { feedbackEvaluationText } from '../i18n/feedback-evaluation';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import * as api from '../api/ai-feedback-evaluation';

const props = defineProps<{ feedback: AiKnowledgeAnswerFeedbackReviewItem }>();
const session = useSessionStore(); const { locale, t } = useAdminI18n();
const text = (key: Parameters<typeof feedbackEvaluationText>[1]) => feedbackEvaluationText(locale.value, key);
const canPrepare = computed(() => !session.switching && session.can('ai.chat.sessions.read') && session.can('ai.knowledge_answer_feedback.review_read')
  && session.can('ai.knowledge_answer_feedback.evaluation_prepare') && props.feedback.reviewStateKey === 'approved');
const canAdmit = computed(() => canPrepare.value && session.can('ai.knowledge_answer_feedback.evaluation_admit'));
const open = ref(false); const busy = ref(false); const candidate = ref<AiFeedbackEvaluationCandidate>();
const question = ref(''); const answer = ref(''); const evidence = ref(''); const language = ref('zh-CN'); const refuse = ref(false);
let controller: AbortController | undefined; let epoch = 0; let confirmationOpen = false;
function reset() {
  ++epoch; controller?.abort(); controller = undefined;
  if (confirmationOpen) ElMessageBox.close(); confirmationOpen = false;
  open.value = false; busy.value = false; candidate.value = undefined;
  question.value = ''; answer.value = ''; evidence.value = ''; language.value = 'zh-CN'; refuse.value = false;
}
// 原登录、审核版本和权限变化即时清空正文；迟到响应不能下载到新上下文。
watch(() => [session.currentUser, session.currentContextName, session.switching, canPrepare.value, canAdmit.value, props.feedback], reset, { deep: true, flush: 'sync' });
watch([question, answer, evidence, language, refuse], () => { candidate.value = undefined; }, { flush: 'sync' });
onUnmounted(reset);
const current = (run: number, signal: AbortSignal) => run === epoch && !signal.aborted && open.value && canPrepare.value;
async function prepare() {
  if (!canPrepare.value || busy.value || !question.value.trim() || !answer.value.trim() || !evidence.value.trim()) return;
  const run = ++epoch; const abort = new AbortController(); controller = abort; busy.value = true; candidate.value = undefined;
  try {
    const result = await api.prepareFeedbackEvaluation({ feedbackId: props.feedback.feedbackId, body: {
      feedbackVersion: props.feedback.feedbackVersion, reviewVersion: props.feedback.reviewVersion,
      sample: { languageTag: language.value, question: question.value, expectedAnswer: answer.value, evidence: evidence.value, shouldRefuse: refuse.value }
    } }, abort.signal);
    if (current(run, abort.signal)) candidate.value = result;
  } catch (error) { if (current(run, abort.signal)) showProblem(error, t('common.requestFailed')); }
  finally { if (run === epoch) { busy.value = false; controller = undefined; } }
}
async function admit() {
  if (!canAdmit.value || busy.value || !candidate.value) return;
  const frozen = candidate.value; const run = ++epoch; const abort = new AbortController(); controller = abort; busy.value = true;
  try {
    confirmationOpen = true;
    try { await ElMessageBox.confirm(`${text('confirm')} SHA256: ${frozen.sampleHash}`, text('title'), { type: 'warning', dangerouslyUseHTMLString: false }); }
    finally { if (run === epoch) confirmationOpen = false; }
    if (!current(run, abort.signal) || !canAdmit.value || candidate.value !== frozen) return;
    const result = await api.admitFeedbackEvaluation({ body: { candidateToken: frozen.candidateToken, confirmManualRedaction: true } }, abort.signal);
    if (!current(run, abort.signal) || !canAdmit.value || candidate.value !== frozen) return;
    const url = URL.createObjectURL(new Blob([JSON.stringify(result, null, 2)], { type: 'application/json' }));
    try { const link = document.createElement('a'); link.href = url; link.download = `evaluation-${result.caseKey}.json`; link.click(); }
    finally { URL.revokeObjectURL(url); }
    candidate.value = undefined; showSuccess(text('success'));
  } catch (error) { if (current(run, abort.signal) && error !== 'cancel' && error !== 'close') { candidate.value = undefined; showProblem(error, t('common.requestFailed')); } }
  finally { if (run === epoch) { busy.value = false; controller = undefined; } }
}
</script>

<template>
  <ElButton v-if="canPrepare" size="small" data-testid="feedback-evaluation-open" @click="open = true">{{ text('open') }}</ElButton>
  <ElDialog v-model="open" :title="text('title')" width="min(760px, 94vw)" append-to-body @closed="reset">
    <p>{{ text('hint') }}</p>
    <ElForm label-position="top" :aria-busy="busy">
      <ElFormItem :label="text('language')" required><select v-model="language" :disabled="busy"><option>zh-CN</option><option>en-US</option></select></ElFormItem>
      <ElFormItem :label="text('question')" required><ElInput v-model="question" type="textarea" maxlength="2048" :disabled="busy" data-testid="evaluation-question" /></ElFormItem>
      <ElFormItem :label="text('answer')" required><ElInput v-model="answer" type="textarea" maxlength="4096" :disabled="busy" data-testid="evaluation-answer" /></ElFormItem>
      <ElFormItem :label="text('evidence')" required><ElInput v-model="evidence" type="textarea" maxlength="8192" :disabled="busy" data-testid="evaluation-evidence" /></ElFormItem>
      <ElCheckbox v-model="refuse" :disabled="busy">{{ text('refuse') }}</ElCheckbox>
    </ElForm>
    <p v-if="candidate">SHA256: {{ candidate.sampleHash }} · {{ candidate.expiresAtUtc }}</p><p>{{ text('changed') }}</p>
    <template #footer>
      <ElButton @click="reset">{{ text('close') }}</ElButton>
      <ElButton v-if="canPrepare" :disabled="busy || !question.trim() || !answer.trim() || !evidence.trim()" data-testid="evaluation-prepare" @click="prepare">{{ text('prepare') }}</ElButton>
      <ElButton v-if="canAdmit" :disabled="busy || !candidate" data-testid="evaluation-admit" @click="admit">{{ text('admit') }}</ElButton>
    </template>
  </ElDialog>
</template>
