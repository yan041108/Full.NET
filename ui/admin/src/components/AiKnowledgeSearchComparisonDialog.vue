<script setup lang="ts">
import { knowledgeFaqCitation } from '@fullnet/client-contracts';
import { onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton } from 'element-plus';
import { isFullNetProblemDetails, type AiKnowledgeCitation, type AiKnowledgeSearchComparisonResult, type CompareAiKnowledgeSearchRequest } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import AiKnowledgeCitationPosition from './AiKnowledgeCitationPosition.vue';
import AiKnowledgeSourceDialog from './AiKnowledgeSourceDialog.vue';
import { compareKnowledgeSearch } from '../api/ai-knowledge-search';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeSearchText } from '../i18n/ai-knowledge-search';

const props = defineProps<{ open: boolean; target: CompareAiKnowledgeSearchRequest }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; changed: []; unavailable: [] }>();
const session = useSessionStore(); const { locale, t } = useAdminI18n(); const tr = (key: Parameters<typeof knowledgeSearchText>[1]) => knowledgeSearchText(locale.value, key);
const busy = ref(false); const attempted = ref(false); const failed = ref(false); const result = ref<AiKnowledgeSearchComparisonResult>();
const preview = ref<Omit<AiKnowledgeCitation, 'id'>>(); const previewBase = ref(''); const sourceOpen = ref(false);
let epoch = 0; let controller: AbortController | undefined;
function clear() { ++epoch; controller?.abort(); busy.value = false; result.value = undefined; preview.value = undefined; sourceOpen.value = false; failed.value = false; }
async function compare() {
  if (!props.open || busy.value || attempted.value || session.switching || !session.can('ai.knowledge_search.compare') || !session.can('ai.knowledge_search.read')) return;
  clear(); attempted.value = true; const current = ++epoch; controller = new AbortController(); busy.value = true;
  try { const response = await compareKnowledgeSearch({ body: props.target }, controller.signal); if (current === epoch && props.open) result.value = response; }
  catch (error) {
    if (current !== epoch || !props.open) return; clear(); failed.value = true;
    if (isFullNetProblemDetails(error) && error.code === 'ai.knowledge.search_source_changed') emit('changed');
    else if (isFullNetProblemDetails(error) && (error.status === 403 || error.code === 'ai.knowledge.search_unavailable')) emit('unavailable');
  } finally { if (current === epoch) busy.value = false; }
}
function citation(hit: AiKnowledgeSearchComparisonResult['vector']['hits'][number]): Omit<AiKnowledgeCitation, 'id'> {
  return { documentId: hit.source.documentId, versionId: hit.source.versionId, sequence: hit.sequence, pageNumber: hit.pageNumber,
    offset: hit.offset, length: hit.length, sourceLocation: hit.sourceLocation, docxLocation: hit.docxLocation, correction: hit.correction, faqSource: knowledgeFaqCitation(hit.source, props.target.embeddingModelConfigId, props.target.searchScope.embeddingModelVersion) };
}
watch(() => [props.open, props.target], () => { clear(); attempted.value = false; }, { immediate: true, deep: true });
watch(() => [session.currentUser, session.switching], () => { clear(); emit('update:open', false); }, { deep: true }); onUnmounted(clear);
</script>

<template>
  <ArtFormDialog :open="open" :title="tr('compareTitle')" :show-confirm="false" :cancel-label="t('common.cancel')" width="1000px"
    class="knowledge-comparison-dialog" data-testid="knowledge-comparison-dialog" @update:open="value => { clear(); emit('update:open', value); }">
    <ElAlert :title="tr('compareHint')" type="warning" :closable="false" /><p class="query">{{ target.query }}</p>
    <ElButton :disabled="busy || attempted" data-testid="compare-confirm" @click="compare">{{ tr('compareConfirm') }}</ElButton>
    <p v-if="busy" role="status">{{ tr('loading') }}</p><ElAlert v-if="failed" :title="tr('compareFailed')" type="error" :closable="false" />
    <template v-if="result">
      <p data-testid="comparison-usage">{{ tr(result.usage.status === 'reported' ? 'compareReported' : 'compareUnknown') }} · {{ result.usage.operationId }}</p>
      <p v-if="result.diagnostics" data-testid="comparison-diagnostics">{{ tr('candidateCount') }} {{ result.diagnostics.candidateCount }}</p>
      <div class="rankings" role="region" tabindex="0" :aria-label="tr('compareTitle')"><section v-for="key in (['keyword', 'vector'] as const)" :key="key" :data-testid="'comparison-' + key">
        <h3>{{ tr(key === 'keyword' ? 'compareKeyword' : 'compareVector') }}</h3>
        <p v-if="!result[key].hits.length">{{ tr('empty') }}</p>
        <article v-for="hit in result[key].hits" :key="hit.source.documentId + ':' + hit.sequence" class="comparison-hit" data-testid="comparison-hit">
          <h4>{{ hit.documentTitle }}</h4><p class="source-identity">{{ tr('version') }} {{ hit.source.versionId }} · {{ tr('publication') }} {{ hit.source.publicationId }}</p>
          <pre>{{ hit.text }}</pre><p>{{ tr(key === 'keyword' ? 'score' : 'compareCosine') }} {{ hit.score }}</p>
          <AiKnowledgeCitationPosition :citation="citation(hit)" />
          <ElButton data-testid="comparison-preview" @click="preview = citation(hit); previewBase = hit.source.knowledgeBaseId; sourceOpen = true">{{ tr('preview') }}</ElButton>
        </article><p>{{ tr(result[key].hasMore ? 'compareMore' : 'compareEnd') }}</p>
      </section></div>
    </template>
  </ArtFormDialog>
  <AiKnowledgeSourceDialog v-if="preview" v-model:open="sourceOpen" :knowledge-base-id="previewBase" :document-id="preview.documentId" :version-id="preview.versionId" :citation="preview" @unavailable="clear(); emit('unavailable')" />
</template>

<style scoped>
.rankings { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 16px; max-height: 48vh; overflow-y: auto; }
.rankings section { min-width: 0; }
.comparison-hit { padding: 12px; border: 1px solid var(--el-border-color); border-radius: 6px; margin-block: 12px; }
.query, pre, .source-identity, [data-testid="comparison-usage"] { white-space: pre-wrap; overflow-wrap: anywhere; font: inherit; }
pre { max-height: 28vh; overflow-y: auto; }
:global(.knowledge-comparison-dialog) { max-height: calc(100vh - 24px); display: flex; flex-direction: column; }
:global(.knowledge-comparison-dialog .el-dialog__body) { min-height: 0; overflow-y: auto; }
@media (max-width: 700px) { .rankings { grid-template-columns: minmax(0, 1fr); } }
</style>
