<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDialog, ElMessageBox, ElTag } from 'element-plus';
import { Check, Close } from '@element-plus/icons-vue';
import type { AiKnowledgeAnswerFeedbackExportResponse, AiKnowledgeAnswerFeedbackReviewItem } from '@fullnet/client-contracts';
import * as api from '../api/ai-answer-feedback-review';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { feedbackReviewText, type FeedbackReviewTextKey } from '../i18n/feedback-review';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import { ART_TABLE_ACTION_COLUMN_WIDTH } from '../framework/art-design/components/artTableActions';

const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: FeedbackReviewTextKey) => feedbackReviewText(locale.value, key);
const parent = computed(() => !session.switching && session.can('ai.chat.sessions.read'));
const readable = computed(() => parent.value && session.can('ai.knowledge_answer_feedback.review_read'));
const reviewer = computed(() => readable.value && session.can('ai.knowledge_answer_feedback.review'));
const exportable = computed(() => parent.value && session.can('ai.knowledge_answer_feedback.export'));
const available = computed(() => readable.value || exportable.value);
const open = ref(false); const busy = ref(false); const refreshRequired = ref(false);
const rows = ref<AiKnowledgeAnswerFeedbackReviewItem[]>([]); const stats = ref<AiKnowledgeAnswerFeedbackExportResponse>();
const page = ref(0); const cursors = ref<(string | undefined)[]>([undefined]); const next = ref<string | null>();
let epoch = 0; let controller: AbortController | undefined; let confirming = false;

function reset() {
  ++epoch; controller?.abort(); controller = undefined;
  if (confirming) { confirming = false; ElMessageBox.close(); }
  open.value = false; busy.value = false; refreshRequired.value = false; rows.value = []; stats.value = undefined;
  page.value = 0; cursors.value = [undefined]; next.value = undefined;
}
// 权限、身份或可信范围改变时清除所有标签与游标；即使服务器忽略取消也不能恢复旧上下文。
watch(() => [session.currentUser, session.currentContextName, session.switching, readable.value, reviewer.value, exportable.value], reset, { deep: true, flush: 'sync' });
onUnmounted(reset);
type Mode = 'read' | 'review' | 'export';
const allowed = (mode: Mode) => mode === 'read' ? readable.value : mode === 'review' ? reviewer.value : exportable.value;
const current = (run: number, signal: AbortSignal, mode: Mode) => run === epoch && !signal.aborted && open.value && allowed(mode);

async function perform(mode: Mode, action: (signal: AbortSignal, run: number, dispatched: () => void) => Promise<void>) {
  if (!open.value || busy.value || !allowed(mode)) return;
  const run = ++epoch; const abort = new AbortController(); controller = abort; busy.value = true; let sent = false;
  try { await action(abort.signal, run, () => { sent = true; }); }
  catch (error) {
    if (!current(run, abort.signal, mode)) return;
    const status = typeof error === 'object' && error !== null && 'status' in error ? error.status : undefined;
    if (mode !== 'export') refreshRequired.value = true;
    if (mode === 'export' || sent) stats.value = undefined;
    if (status === 400 && mode === 'read') { cursors.value = [undefined]; page.value = 0; next.value = undefined; }
    if (status === 401 || status === 403) reset();
    showProblem(error, text(sent ? 'retry' : 'unavailable'));
  } finally { if (run === epoch) { busy.value = false; controller = undefined; } }
}
async function readPage(signal: AbortSignal, run: number, index: number, history: (string | undefined)[]) {
  const after = history[index]; const result = await api.getAnswerFeedbackReviews(after ? { after } : {}, signal);
  if (!current(run, signal, 'read')) return;
  rows.value = result.items; next.value = result.nextCursor; page.value = index; cursors.value = history; refreshRequired.value = false;
}
async function show() { if (!available.value) return; open.value = true; if (readable.value) await refresh(); }
function refresh() { return perform('read', (signal, run) => readPage(signal, run, page.value, [...cursors.value])); }
function move(direction: -1 | 1) {
  if (direction === -1 && page.value === 0 || direction === 1 && !next.value || refreshRequired.value) return;
  const index = page.value + direction; const history = cursors.value.slice(0, index + 1);
  if (direction === 1) history[index] = next.value!;
  return perform('read', (signal, run) => readPage(signal, run, index, history));
}
function decide(row: AiKnowledgeAnswerFeedbackReviewItem, decisionKey: 'approved' | 'rejected') {
  if (!reviewer.value || refreshRequired.value || row.feedbackVersion >= 2147483647 || row.reviewVersion >= 2147483647) return;
  const request = { feedbackId: row.feedbackId, body: { decisionKey, feedbackVersion: row.feedbackVersion, reviewVersion: row.reviewVersion } };
  return perform('review', async (signal, run, dispatched) => {
    confirming = true;
    try { await ElMessageBox.confirm(`${text('confirm')} ${text(decisionKey)} · ${text(row.ratingKey as FeedbackReviewTextKey)} · ${text(row.reasonKey as FeedbackReviewTextKey)} · v${row.feedbackVersion} / v${row.reviewVersion}`, text('title'), { type: 'warning', dangerouslyUseHTMLString: false }); }
    catch { return; }
    finally { if (run === epoch) confirming = false; }
    if (!current(run, signal, 'review')) return;
    stats.value = undefined; dispatched(); await api.reviewAnswerFeedback(request, signal);
    if (!current(run, signal, 'review')) return;
    showSuccess(text('updated')); await readPage(signal, run, page.value, [...cursors.value]);
  });
}
function statistics(download = false) {
  return perform('export', async (signal, run) => {
    // 下载也重新复核服务端当前批准和来源资格，不导出已缓存的统计或原始响应附加字段。
    const result = await api.exportAnswerFeedbackLabels(signal); if (!current(run, signal, 'export')) return;
    const projection = { policyVersion: result.policyVersion, isPartial: result.isPartial, cells: result.cells.map(cell => ({ ratingKey: cell.ratingKey, reasonKey: cell.reasonKey, citationCount: cell.citationCount, feedbackCount: cell.feedbackCount })) };
    stats.value = projection;
    if (download) {
      const url = URL.createObjectURL(new Blob([JSON.stringify(projection, null, 2)], { type: 'application/json' }));
      try { const link = document.createElement('a'); link.href = url; link.download = 'ai-feedback-labels.json'; link.click(); }
      finally { URL.revokeObjectURL(url); }
    }
  });
}
</script>

<template>
  <ElButton v-if="available" data-testid="feedback-review-open" @click="show">{{ text('title') }}</ElButton>
  <ElDialog v-if="available" v-model="open" :title="text('title')" width="min(980px, 94vw)" destroy-on-close :close-on-click-modal="false" @close="reset">
    <div class="review-panel" :aria-busy="busy">
      <ElAlert :title="text('hint')" type="info" :closable="false" show-icon />
      <section v-if="readable" :aria-label="text('queue')">
        <div class="section-heading"><h3>{{ text('queue') }}</h3><ElButton data-testid="feedback-review-refresh" :disabled="busy" @click="refresh">{{ text('refresh') }}</ElButton></div>
        <ElAlert v-if="refreshRequired" :title="text('retry')" type="warning" :closable="false" show-icon />
        <div class="table-scroll">
          <table :aria-label="text('queue')"><thead><tr><th>{{ text('rating') }}</th><th>{{ text('reason') }}</th><th>{{ text('citations') }}</th><th>{{ text('state') }}</th><th>{{ text('versions') }}</th><th v-if="reviewer" :style="{ width: `${ART_TABLE_ACTION_COLUMN_WIDTH}px` }">{{ text('actions') }}</th></tr></thead>
            <tbody><tr v-for="row in rows" :key="row.feedbackId">
              <td>{{ text(row.ratingKey as FeedbackReviewTextKey) }}</td><td>{{ text(row.reasonKey as FeedbackReviewTextKey) }}</td><td>{{ row.citationCount }}</td>
              <td><ElTag :type="row.reviewStateKey === 'approved' ? 'success' : row.reviewStateKey === 'rejected' ? 'warning' : 'info'">{{ text(row.reviewStateKey as FeedbackReviewTextKey) }}</ElTag></td><td class="version">v{{ row.feedbackVersion }} / v{{ row.reviewVersion }}</td>
              <td v-if="reviewer"><ArtTableActionGroup>
                <ElButton :icon="Check" size="small" data-testid="feedback-review-approve" :disabled="busy || refreshRequired || row.feedbackVersion >= 2147483647 || row.reviewVersion >= 2147483647" @click="decide(row, 'approved')">{{ text('approve') }}</ElButton>
                <ElButton :icon="Close" size="small" data-testid="feedback-review-reject" :disabled="busy || refreshRequired || row.feedbackVersion >= 2147483647 || row.reviewVersion >= 2147483647" @click="decide(row, 'rejected')">{{ text('reject') }}</ElButton>
              </ArtTableActionGroup></td>
            </tr></tbody>
          </table><p v-if="!rows.length" class="empty" role="status">{{ text('empty') }}</p>
        </div>
        <div class="pager"><ElButton data-testid="feedback-review-previous" :disabled="busy || refreshRequired || page === 0" @click="move(-1)">{{ text('previous') }}</ElButton><span>{{ text('page') }} {{ page + 1 }}</span><ElButton data-testid="feedback-review-next" :disabled="busy || refreshRequired || !next" @click="move(1)">{{ text('next') }}</ElButton></div>
      </section>
      <section v-if="exportable" :aria-label="text('labels')">
        <div class="section-heading"><h3>{{ text('labels') }}</h3><div class="statistics-actions"><ElButton data-testid="feedback-labels-preview" :disabled="busy" @click="statistics()">{{ text('preview') }}</ElButton><ElButton data-testid="feedback-labels-download" :disabled="busy" @click="statistics(true)">{{ text('download') }}</ElButton></div></div>
        <p class="privacy">{{ text('privacy') }}</p>
        <p v-if="!stats" class="empty">{{ text('notLoaded') }}</p>
        <template v-else><ElTag type="info">{{ text('partial') }}</ElTag><div class="table-scroll"><table :aria-label="text('labels')"><thead><tr><th>{{ text('rating') }}</th><th>{{ text('reason') }}</th><th>{{ text('citations') }}</th><th>{{ text('count') }}</th></tr></thead><tbody><tr v-for="cell in stats.cells" :key="`${cell.ratingKey}:${cell.reasonKey}:${cell.citationCount}`"><td>{{ text(cell.ratingKey as FeedbackReviewTextKey) }}</td><td>{{ text(cell.reasonKey as FeedbackReviewTextKey) }}</td><td>{{ cell.citationCount }}</td><td>{{ cell.feedbackCount }}</td></tr></tbody></table><p v-if="!stats.cells.length" class="empty" role="status">{{ text('emptyStats') }}</p></div></template>
      </section>
    </div>
    <template #footer><ElButton data-testid="feedback-review-close" @click="reset">{{ text('close') }}</ElButton></template>
  </ElDialog>
</template>

<style scoped>
.review-panel { display: grid; gap: 24px; }
section { min-width: 0; }
.section-heading { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 12px; flex-wrap: wrap; }
h3 { margin: 0; font-size: 16px; font-weight: 600; }
.table-scroll { overflow: auto; max-height: 320px; margin-top: 12px; border: 1px solid var(--el-border-color-lighter); border-radius: 8px; }
table { width: 100%; border-collapse: collapse; white-space: nowrap; font-size: 13px; }
th { text-align: start; position: sticky; top: 0; background: var(--el-fill-color-light); font-weight: 500; color: var(--el-text-color-secondary); }
th, td { padding: 12px; border-bottom: 1px solid var(--el-border-color-lighter); }
tbody tr:last-child td { border-bottom: 0; }
.version { font-variant-numeric: tabular-nums; color: var(--el-text-color-secondary); }
.pager { display: flex; align-items: center; justify-content: flex-end; gap: 12px; margin-top: 12px; }
.empty { padding: 16px; margin: 0; color: var(--el-text-color-secondary); }
.privacy { color: var(--el-text-color-secondary); line-height: 1.6; margin: 0; }
.statistics-actions { display: flex; flex-wrap: wrap; gap: 8px; }
.statistics-actions :deep(.el-button + .el-button) { margin-left: 0; }
</style>
