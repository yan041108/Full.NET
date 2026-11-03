<script setup lang="ts">
import { knowledgeFaqCitation } from '@fullnet/client-contracts';
import { computed, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElOption, ElSelect } from 'element-plus';
import { isFullNetProblemDetails, type AiKnowledgeCitation, type AiKnowledgeSearchRefinementResult, type RefineAiKnowledgeSearchRequest } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import AiKnowledgeCitationPosition from './AiKnowledgeCitationPosition.vue';
import AiKnowledgeSourceDialog from './AiKnowledgeSourceDialog.vue';
import { refineKnowledgeSearch } from '../api/ai-knowledge-search';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeSearchText } from '../i18n/ai-knowledge-search';
const props = defineProps<{ open: boolean; target: Omit<RefineAiKnowledgeSearchRequest, 'mode'> }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; changed: []; unavailable: [] }>();
const session = useSessionStore(); const { locale, t } = useAdminI18n(); const tr = (key: Parameters<typeof knowledgeSearchText>[1]) => knowledgeSearchText(locale.value, key);
const mode = ref('rewrite_rerank'); const busy = ref(false); const attempted = ref(false); const failed = ref(false); const result = ref<AiKnowledgeSearchRefinementResult>();
const preview = ref<Omit<AiKnowledgeCitation, 'id'>>(); const previewBase = ref(''); const sourceOpen = ref(false);
type Ranking = AiKnowledgeSearchRefinementResult['originalVector'];
const groups = computed(() => result.value ? [
  { key: 'original-keyword', label: tr('refineOriginalKeyword'), ranking: result.value.originalKeyword },
  { key: 'original-vector', label: tr('refineOriginalVector'), ranking: result.value.originalVector },
  { key: 'rewritten-keyword', label: tr('refineRewrittenKeyword'), ranking: result.value.rewrittenKeyword },
  { key: 'rewritten-vector', label: tr('refineRewrittenVector'), ranking: result.value.rewrittenVector },
  { key: 'reranked', label: tr('refineRerank'), ranking: result.value.reranked }
].filter((group): group is { key: string; label: string; ranking: Ranking } => group.ranking !== null) : []);
const callCount = computed(() => mode.value === 'rerank' ? 2 : mode.value === 'rewrite' ? 3 : 4);
let epoch = 0; let controller: AbortController | undefined;
function clear() { ++epoch; controller?.abort(); busy.value = false; failed.value = false; result.value = undefined; preview.value = undefined; sourceOpen.value = false; }
async function run() {
  if (!props.open || busy.value || attempted.value || session.switching || !session.can('ai.knowledge_search.compare') || !session.can('ai.knowledge_search.refine') || !session.can('ai.knowledge_search.read')) return;
  clear(); attempted.value = true; const current = ++epoch; controller = new AbortController(); busy.value = true;
  try { const response = await refineKnowledgeSearch({ body: { ...props.target, mode: mode.value } }, controller.signal); if (current === epoch && props.open) result.value = response; }
  catch (error) { if (current !== epoch || !props.open) return; clear(); failed.value = true;
    if (isFullNetProblemDetails(error) && error.code === 'ai.knowledge.search_source_changed') emit('changed');
    else if (isFullNetProblemDetails(error) && (error.status === 403 || error.code === 'ai.knowledge.search_unavailable')) emit('unavailable'); }
  finally { if (current === epoch) busy.value = false; }
}
function citation(hit: Ranking['hits'][number]): Omit<AiKnowledgeCitation, 'id'> {
  return { documentId: hit.source.documentId, versionId: hit.source.versionId, sequence: hit.sequence, pageNumber: hit.pageNumber,
    offset: hit.offset, length: hit.length, sourceLocation: hit.sourceLocation, docxLocation: hit.docxLocation, correction: hit.correction, faqSource: knowledgeFaqCitation(hit.source, props.target.embeddingModelConfigId, props.target.searchScope.embeddingModelVersion) };
}
function stageLabel(stage: string) { return tr(stage === 'original_embedding' ? 'refineOriginalEmbedding' : stage === 'rewritten_embedding' ? 'refineRewrittenEmbedding' : stage === 'rewrite' ? 'refineRewrite' : 'refineRerank'); }
watch(() => [props.open, props.target], () => { clear(); attempted.value = false; }, { immediate: true, deep: true });
watch(mode, () => { clear(); attempted.value = false; });
watch(() => [session.currentUser, session.switching], () => { clear(); emit('update:open', false); }, { deep: true }); onUnmounted(clear);
</script>
<template>
  <ArtFormDialog :open="open" :title="tr('refineTitle')" :show-confirm="false" :cancel-label="t('common.cancel')" width="1100px"
    class="knowledge-refinement-dialog" data-testid="knowledge-refinement-dialog" @update:open="value => { clear(); emit('update:open', value); }">
    <ElAlert :title="tr('refineHint')" type="warning" :closable="false" />
    <p class="identity">{{ tr('refineModel') }} {{ target.processingModelConfigId }} · v{{ target.processingModelVersion }}</p>
    <label for="knowledge-refinement-mode">{{ tr('refineMode') }}</label>
    <ElSelect id="knowledge-refinement-mode" v-model="mode" :disabled="busy" :teleported="false" :aria-label="tr('refineMode')" data-testid="refine-mode">
      <ElOption value="rewrite" :label="tr('refineRewriteMode')" /><ElOption value="rerank" :label="tr('refineRerankMode')" /><ElOption value="rewrite_rerank" :label="tr('refineCombinedMode')" />
    </ElSelect>
    <p>{{ tr('refineCalls') }} {{ callCount }}</p><p class="identity">{{ tr('refineOriginalQuery') }} {{ target.query }}</p>
    <ElButton :disabled="busy || attempted" data-testid="refine-confirm" @click="run">{{ tr('refineConfirm') }}</ElButton>
    <p v-if="busy" role="status">{{ tr('loading') }}</p><ElAlert v-if="failed" :title="tr('refineFailed')" type="error" :closable="false" />
    <template v-if="result">
      <p class="identity" data-testid="refine-effective-query">{{ tr('refineEffectiveQuery') }} {{ result.effectiveQuery }}</p>
      <ElAlert v-if="result.refusedStage" :title="tr('refineRefused') + ' ' + stageLabel(result.refusedStage)" type="info" :closable="false" data-testid="refine-refused" />
      <ul :aria-label="tr('refineUsage')"><li v-for="operation in result.operations" :key="operation.operationId" class="identity" data-testid="refine-operation">
        {{ stageLabel(operation.stage) }} · {{ tr(operation.status === 'reported' ? 'compareReported' : 'compareUnknown') }} · {{ operation.operationId }}
      </li></ul>
      <p v-if="result.rerankCandidateCount !== null">{{ tr('refinePrefix') }} {{ result.rerankCandidateCount }}</p>
      <p v-if="result.diagnostics" data-testid="refine-diagnostics">{{ tr('candidateCount') }} {{ result.diagnostics.candidateCount }}</p>
      <div class="rankings" role="region" tabindex="0" :aria-label="tr('refineTitle')">
        <section v-for="group in groups" :key="group.key" :data-testid="'refine-' + group.key"><h3>{{ group.label }}</h3>
          <p v-if="!group.ranking.hits.length">{{ tr('empty') }}</p>
          <article v-for="hit in group.ranking.hits" :key="hit.source.documentId + ':' + hit.sequence" class="refine-hit" data-testid="refine-hit">
            <h4>{{ hit.documentTitle }}</h4><p class="identity">{{ tr('publication') }} {{ hit.source.publicationId }}</p><pre>{{ hit.text }}</pre>
            <p>{{ tr(group.key === 'reranked' ? 'refineOrdinal' : group.key.endsWith('vector') ? 'compareCosine' : 'score') }} {{ hit.score }}</p>
            <AiKnowledgeCitationPosition :citation="citation(hit)" /><ElButton data-testid="refine-preview" @click="preview = citation(hit); previewBase = hit.source.knowledgeBaseId; sourceOpen = true">{{ tr('preview') }}</ElButton>
          </article><p>{{ tr(group.ranking.hasMore ? 'compareMore' : 'compareEnd') }}</p>
        </section>
      </div>
      <p>{{ tr('refineAnswerBoundary') }}</p>
    </template>
  </ArtFormDialog>
  <AiKnowledgeSourceDialog v-if="preview" v-model:open="sourceOpen" :knowledge-base-id="previewBase" :document-id="preview.documentId" :version-id="preview.versionId" :citation="preview" @unavailable="clear(); emit('unavailable')" />
</template>
<style scoped>
.identity, pre { white-space: pre-wrap; overflow-wrap: anywhere; font: inherit; }
.rankings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; max-height: 44vh; overflow-y: auto; }
.rankings section { min-width: 0; }
.refine-hit { border: 1px solid var(--el-border-color); border-radius: 6px; padding: 12px; margin-block: 12px; }
pre { max-height: 24vh; overflow-y: auto; }
.el-select { max-width: 100%; margin-block: 8px; }
:global(.knowledge-refinement-dialog) { max-height: calc(100vh - 24px); display: flex; flex-direction: column; }
:global(.knowledge-refinement-dialog .el-dialog__body) { min-height: 0; overflow-y: auto; }
:global(.knowledge-refinement-dialog .el-select__placeholder > span) { background-color: var(--el-fill-color-blank); }
@media (max-width: 700px) { .rankings { grid-template-columns: minmax(0, 1fr); } }
</style>
