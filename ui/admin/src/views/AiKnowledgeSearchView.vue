<script setup lang="ts">
import { knowledgeFaqCitation } from '@fullnet/client-contracts';
import { computed, nextTick, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElCard, ElCheckbox, ElForm, ElFormItem, ElInput, ElOption, ElSelect } from 'element-plus';
import { isFullNetProblemDetails, type AiKnowledgeBaseResponse, type AiKnowledgeCitation, type AiKnowledgeDocumentResponse, type AiKnowledgeSearchResult } from '@fullnet/client-contracts';
import AiKnowledgeBaseSelect from '../components/AiKnowledgeBaseSelect.vue';
import AiKnowledgeSourceDialog from '../components/AiKnowledgeSourceDialog.vue';
import AiKnowledgeCitationPosition from '../components/AiKnowledgeCitationPosition.vue';
import AiKnowledgeSearchContextDialog from '../components/AiKnowledgeSearchContextDialog.vue';
import AiKnowledgeSearchRelationsDialog from '../components/AiKnowledgeSearchRelationsDialog.vue';
import AiKnowledgeSearchRefinementDialog from '../components/AiKnowledgeSearchRefinementDialog.vue';
import AiKnowledgeSearchAnswerDialog from '../components/AiKnowledgeSearchAnswerDialog.vue';
import AiKnowledgeSearchComparisonDialog from '../components/AiKnowledgeSearchComparisonDialog.vue';
import AiKnowledgeFaqDraftDialog from '../components/AiKnowledgeFaqDraftDialog.vue';
import type { AiKnowledgeFaqDocumentSelection } from '@fullnet/client-contracts';
import { knowledgeFaqText } from '../i18n/ai-knowledge-faqs';
import type { AskAiKnowledgeRequest, CompareAiKnowledgeSearchRequest, ReadAiKnowledgeSearchContextRequest, ReadAiKnowledgeSearchRelationsRequest, RefineAiKnowledgeSearchRequest } from '@fullnet/client-contracts';
import { getKnowledgeBase, listKnowledgeDocuments } from '../api/ai-knowledge';
import { searchKnowledge } from '../api/ai-knowledge-search';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeSearchText } from '../i18n/ai-knowledge-search';
import { knowledgeFaqSourceText } from '../i18n/ai-knowledge-faq-source';

defineOptions({ name: 'AiKnowledgeSearchView' });
const session = useSessionStore(); const { locale } = useAdminI18n();
const tr = (key: Parameters<typeof knowledgeSearchText>[1]) => knowledgeSearchText(locale.value, key);
const required = ['ai.knowledge_search.read', 'ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read', 'ai.knowledge_documents.index_read'];
const canRead = computed(() => !session.switching && required.every(code => session.can(code)));
const canDiagnose = computed(() => canRead.value && session.can('ai.knowledge_search.diagnostics'));
const canAsk = computed(() => canRead.value && session.can('ai.chat.messages.send') && session.can('ai.chat.sessions.read'));
const answerOpen = ref(false); const answerTarget = ref<AskAiKnowledgeRequest>();
const compareOpen = ref(false); const compareTarget = ref<CompareAiKnowledgeSearchRequest>();
const canCompare = computed(() => canRead.value && session.can('ai.knowledge_search.compare'));
const baseId = ref(''); const metadata = ref<AiKnowledgeBaseResponse>(); const query = ref(''); const selected = ref<string[]>([]);
const options = ref<AiKnowledgeDocumentResponse[]>([]); const documentPage = ref(1); const documentTotal = ref(0); const metadataBusy = ref(false);
// FAQ 不伪装普通文档目录项；保留用户选中的身份，标题不沿用已失效的命中正文。
const selectionOptions = computed(() => [...options.value, ...selected.value.filter(id => !options.value.some(value => value.id === id))
  .map(id => ({ id, title: `${knowledgeFaqSourceText(locale.value, 'selected')} ${id}` }))]);
const diagnostics = ref(false); const result = ref<AiKnowledgeSearchResult>(); const busy = ref(false); const failed = ref(false); const changed = ref(false);
const sourceOpen = ref(false); const preview = ref<AiKnowledgeSearchResult['hits'][number]>();
const contextOpen = ref(false); const contextTarget = ref<Omit<ReadAiKnowledgeSearchContextRequest, 'radius'>>();
const relationsOpen = ref(false); const relationsTarget = ref<ReadAiKnowledgeSearchRelationsRequest>();
const refineOpen = ref(false); const refineTarget = ref<Omit<RefineAiKnowledgeSearchRequest, 'mode'>>();
const canRefine = computed(() => canCompare.value && session.can('ai.knowledge_search.refine') && !!metadata.value?.generationModelConfigId && !!metadata.value.generationModelVersion);
let metadataEpoch = 0; let searchEpoch = 0; let metadataAbort: AbortController | undefined; let searchAbort: AbortController | undefined;
const sourceCitation = computed<Omit<AiKnowledgeCitation, 'id'> | undefined>(() => preview.value ? citation(preview.value) : undefined);
function citation(hit: AiKnowledgeSearchResult['hits'][number]): Omit<AiKnowledgeCitation, 'id'> {
  return { documentId: hit.source.documentId, versionId: hit.source.versionId, sequence: hit.sequence, pageNumber: hit.pageNumber,
    offset: hit.offset, length: hit.length, sourceLocation: hit.sourceLocation, docxLocation: hit.docxLocation, correction: hit.correction, faqSource: knowledgeFaqCitation(hit.source, metadata.value?.embeddingModelConfigId, metadata.value?.embeddingModelVersion) };
}
const faqOpen=ref(false);const faqSelection=ref<AiKnowledgeFaqDocumentSelection>();
const canFaq=computed(()=>!session.switching&&session.can('ai.knowledge_faqs.read')&&session.can('ai.knowledge_faqs.write')&&!!result.value);
function openFaq(){if(!canFaq.value||!result.value||!metadata.value?.embeddingModelConfigId||!metadata.value.embeddingModelVersion)return;
  faqSelection.value={embeddingModelConfigId:metadata.value.embeddingModelConfigId,embeddingModelVersion:metadata.value.embeddingModelVersion,sources:result.value.sources.map(s=>({...s}))};faqOpen.value=true;}
function clearResult() { ++searchEpoch; searchAbort?.abort(); busy.value = false; result.value = undefined; changed.value = false; failed.value = false; sourceOpen.value = false; preview.value = undefined; contextOpen.value = false; contextTarget.value = undefined; relationsOpen.value = false; relationsTarget.value = undefined; refineOpen.value = false; refineTarget.value = undefined; answerOpen.value = false; answerTarget.value = undefined; compareOpen.value = false; compareTarget.value = undefined; faqOpen.value=false;faqSelection.value=undefined; }
function openRefinement() {
  if (!canRefine.value || !result.value || !metadata.value?.embeddingModelConfigId || !metadata.value.embeddingModelVersion || !metadata.value.generationModelConfigId || !metadata.value.generationModelVersion) return;
  refineTarget.value = { knowledgeBaseIds: [baseId.value], embeddingModelConfigId: metadata.value.embeddingModelConfigId, processingModelConfigId: metadata.value.generationModelConfigId,
    processingModelVersion: metadata.value.generationModelVersion, query: query.value, limit: 5, includeDiagnostics: diagnostics.value,
    searchScope: { documentIds: [...selected.value], embeddingModelVersion: metadata.value.embeddingModelVersion, sources: result.value.sources.map(source => ({ ...source })) } }; refineOpen.value = true;
}
function openComparison() {
  if (!canCompare.value || !result.value || !metadata.value?.embeddingModelConfigId || !metadata.value.embeddingModelVersion) return;
  compareTarget.value = { knowledgeBaseIds: [baseId.value], embeddingModelConfigId: metadata.value.embeddingModelConfigId, query: query.value, limit: 5, includeDiagnostics: diagnostics.value,
    searchScope: { documentIds: [...selected.value], embeddingModelVersion: metadata.value.embeddingModelVersion, sources: result.value.sources.map(source => ({ ...source })) } };
  compareOpen.value = true;
}
function openAnswer() {
  if (!canAsk.value || !result.value || !metadata.value?.embeddingModelConfigId || !metadata.value.embeddingModelVersion) return;
  answerTarget.value = { knowledgeBaseIds: [baseId.value], embeddingModelConfigId: metadata.value.embeddingModelConfigId, question: query.value,
    searchScope: { documentIds: [...selected.value], embeddingModelVersion: metadata.value.embeddingModelVersion, sources: result.value.sources.map(source => ({ ...source })) } };
  answerOpen.value = true;
}
function openContext(hit: AiKnowledgeSearchResult['hits'][number]) {
  if (!metadata.value?.embeddingModelConfigId || !metadata.value.embeddingModelVersion) return;
  contextTarget.value = { source: { ...hit.source }, embeddingModelConfigId: metadata.value.embeddingModelConfigId,
    embeddingModelVersion: metadata.value.embeddingModelVersion, anchorSequence: hit.sequence }; contextOpen.value = true;
}
function contextChanged() { clearResult(); changed.value = true; }
function openRelations(hit: AiKnowledgeSearchResult['hits'][number]) {
  if (!metadata.value?.embeddingModelConfigId || !metadata.value.embeddingModelVersion) return;
  relationsTarget.value = { source: { ...hit.source }, embeddingModelConfigId: metadata.value.embeddingModelConfigId,
    embeddingModelVersion: metadata.value.embeddingModelVersion, anchorSequence: hit.sequence }; relationsOpen.value = true;
}
function contextUnavailable() { clearResult(); failed.value = true; }
function stop() { ++metadataEpoch; metadataAbort?.abort(); metadataBusy.value = false; metadata.value = undefined; options.value = []; selected.value = []; diagnostics.value = false; clearResult(); }
async function loadDocuments(page = 1) {
  metadataAbort?.abort(); const current = ++metadataEpoch; metadata.value = undefined; metadataBusy.value = true;
  if (!baseId.value || !canRead.value) { options.value = []; metadataBusy.value = false; return; }
  metadataAbort = new AbortController(); const base = baseId.value;
  try {
    const [parent, documents] = await Promise.all([getKnowledgeBase({ knowledgeBaseId: base }, metadataAbort.signal),
      listKnowledgeDocuments({ knowledgeBaseId: base, page, pageSize: 20 }, metadataAbort.signal)]);
    if (current !== metadataEpoch || base !== baseId.value) return;
    metadata.value = parent; documentPage.value = page; documentTotal.value = documents.total;
    options.value = [...options.value.filter(row => selected.value.includes(row.id) && !documents.items.some(next => next.id === row.id)), ...documents.items];
  } catch { if (current === metadataEpoch) { stop(); failed.value = true; } }
  finally { if (current === metadataEpoch) metadataBusy.value = false; }
}
async function selectFaq(hit: AiKnowledgeSearchResult['hits'][number]) {
  if (hit.source.sourceKind !== 'faq-revision-v1') return;
  const frozen = { ...hit.source }; selected.value = [frozen.documentId]; await nextTick(); await search(false, [frozen]);
}
async function search(adopt = false, frozen?: AiKnowledgeSearchResult['sources']) {
  if (!canRead.value || busy.value || metadataBusy.value || !metadata.value?.embeddingModelConfigId || !metadata.value.embeddingModelVersion || changed.value && !adopt) return;
  const expectedSources = frozen ?? (adopt ? null : result.value?.sources ?? null);
  clearResult(); const current = ++searchEpoch; busy.value = true; searchAbort = new AbortController();
  try {
    const response = await searchKnowledge({ body: { knowledgeBaseIds: [baseId.value], documentIds: [...selected.value],
      embeddingModelConfigId: metadata.value.embeddingModelConfigId, embeddingModelVersion: metadata.value.embeddingModelVersion,
      query: query.value, limit: 20, includeDiagnostics: diagnostics.value, expectedSources } }, searchAbort.signal);
    if (current === searchEpoch && canRead.value) result.value = response;
  } catch (error) {
    if (current !== searchEpoch) return;
    changed.value = isFullNetProblemDetails(error) && error.code === 'ai.knowledge.search_source_changed'; failed.value = !changed.value;
  } finally { if (current === searchEpoch) busy.value = false; }
}
watch(baseId, () => { stop(); documentPage.value = 1; documentTotal.value = 0; void loadDocuments(); });
watch([query, selected, diagnostics], clearResult, { deep: true });
watch(() => [session.currentUser, session.switching, canRead.value, canDiagnose.value], () => { stop(); baseId.value = ''; query.value = ''; }, { deep: true });
onUnmounted(stop);
</script>

<template>
  <ElCard class="knowledge-search" data-testid="knowledge-search">
    <h1 class="knowledge-search-title">{{ tr('title') }}</h1>
    <ElAlert :title="tr('hint')" type="info" :closable="false" /><p>{{ tr('bounds') }}</p>
    <ElAlert v-if="!canRead || failed" :title="tr('unavailable')" type="error" :closable="false" data-testid="search-error" />
    <template v-if="canRead">
      <AiKnowledgeBaseSelect v-model="baseId" :disabled="busy" />
      <ElForm label-position="top" @submit.prevent="search()">
        <ElFormItem :label="tr('documents')">
          <ElSelect v-model="selected" multiple :multiple-limit="8" :disabled="busy || metadataBusy || !baseId" :aria-label="tr('documents')" data-testid="search-documents">
            <ElOption v-for="document in selectionOptions" :key="document.id" :value="document.id" :label="document.title" />
          </ElSelect>
          <div class="search-actions"><ElButton :disabled="busy || metadataBusy || documentPage <= 1" @click="loadDocuments(documentPage - 1)">{{ tr('previous') }}</ElButton>
            <ElButton :disabled="busy || metadataBusy || documentPage * 20 >= documentTotal" @click="loadDocuments(documentPage + 1)">{{ tr('next') }}</ElButton></div>
        </ElFormItem>
        <ElFormItem :label="tr('query')"><ElInput v-model="query" :maxlength="512" :aria-label="tr('query')" data-testid="search-query" /></ElFormItem>
        <p>{{ tr('queryHint') }}</p>
        <ElCheckbox v-if="canDiagnose" v-model="diagnostics" :disabled="busy" data-testid="search-diagnostics">{{ tr('diagnostics') }}</ElCheckbox>
        <div class="search-actions"><ElButton native-type="submit" :disabled="busy || metadataBusy || changed || !query.trim() || !metadata?.embeddingModelConfigId" data-testid="search-submit">{{ tr('search') }}</ElButton>
          <ElButton v-if="busy" data-testid="search-cancel" @click="clearResult">{{ tr('cancel') }}</ElButton></div>
      </ElForm>
      <p v-if="busy" role="status">{{ tr('loading') }}</p>
      <ElAlert v-if="changed" :title="tr('changed')" type="warning" :closable="false" data-testid="search-changed" />
      <ElButton v-if="changed" data-testid="search-adopt" @click="search(true)">{{ tr('adopt') }}</ElButton>
      <template v-if="result">
        <ElButton v-if="canFaq" data-testid="search-faq" @click="openFaq">{{ knowledgeFaqText(locale, 'fromDocuments') }}</ElButton>
        <ElButton v-if="canAsk" data-testid="search-answer" @click="openAnswer">{{ tr('answerTitle') }}</ElButton>
        <ElButton v-if="canCompare" data-testid="search-compare" @click="openComparison">{{ tr('compareTitle') }}</ElButton>
        <ElButton v-if="canRefine" data-testid="search-refine" @click="openRefinement">{{ tr('refineTitle') }}</ElButton>
        <p role="status" data-testid="search-count">{{ tr('total') }} {{ result.totalMatches }} · {{ tr('shown') }} {{ result.hits.length }}</p>
        <p v-if="result.diagnostics && canDiagnose" data-testid="search-diagnostic-result">{{ tr('sourceCount') }} {{ result.diagnostics.sourceCount }} · {{ tr('candidateCount') }} {{ result.diagnostics.candidateCount }}</p>
        <p v-if="!result.hits.length" data-testid="search-empty">{{ tr('empty') }}</p>
        <article v-for="hit in result.hits" :key="hit.source.generationId + ':' + hit.sequence" class="search-hit" data-testid="search-hit">
          <h2 class="search-hit-title">{{ hit.documentTitle }}</h2><p>{{ tr('metadataVersion') }} {{ hit.documentMetadataVersion }}</p><p class="search-version">{{ tr('version') }} {{ hit.source.versionId }} · {{ tr('publication') }} {{ hit.source.publicationId }}</p>
          <pre>{{ hit.text }}</pre><p>{{ tr('score') }} {{ hit.score }}</p>
          <AiKnowledgeCitationPosition :citation="citation(hit)" />
          <ElButton data-testid="search-preview" @click="preview = hit; sourceOpen = true">{{ tr('preview') }}</ElButton>
          <ElButton v-if="hit.source.sourceKind === 'faq-revision-v1'" data-testid="search-only-faq" @click="selectFaq(hit)">{{ knowledgeFaqSourceText(locale, 'only') }}</ElButton>
          <ElButton data-testid="search-context" @click="openContext(hit)">{{ tr('contextTitle') }}</ElButton>
          <ElButton data-testid="search-relations" @click="openRelations(hit)">{{ tr('relationsTitle') }}</ElButton>
        </article>
      </template>
      <p v-else-if="!busy && !changed && !failed">{{ tr('ready') }}</p>
    </template>
  </ElCard>
  <AiKnowledgeSourceDialog v-if="preview" v-model:open="sourceOpen" :knowledge-base-id="preview.source.knowledgeBaseId" :document-id="preview.source.documentId" :version-id="preview.source.versionId" :citation="sourceCitation" @unavailable="contextUnavailable" />
  <AiKnowledgeSearchContextDialog v-if="contextTarget" v-model:open="contextOpen" :target="contextTarget" @changed="contextChanged" @unavailable="contextUnavailable" />
  <AiKnowledgeSearchRelationsDialog v-if="relationsTarget" v-model:open="relationsOpen" :target="relationsTarget" @changed="contextChanged" @unavailable="contextUnavailable" />
  <AiKnowledgeSearchRefinementDialog v-if="refineTarget" v-model:open="refineOpen" :target="refineTarget" @changed="contextChanged" @unavailable="contextUnavailable" />
  <AiKnowledgeSearchAnswerDialog v-if="answerTarget" v-model:open="answerOpen" :target="answerTarget" @changed="contextChanged" @unavailable="contextUnavailable" />
  <AiKnowledgeSearchComparisonDialog v-if="compareTarget" v-model:open="compareOpen" :target="compareTarget" @changed="contextChanged" @unavailable="contextUnavailable" />
  <AiKnowledgeFaqDraftDialog v-if="faqSelection" v-model:open="faqOpen" :knowledge-base-id="baseId" :selection="faqSelection" :initial-question="query" @unavailable="contextUnavailable" />
</template>

<style scoped>
.knowledge-search { min-width: 0; }
.knowledge-search-title { font-size: 1.5rem; line-height: 1.4; margin-block: 0 1rem; }
.search-actions { display: flex; flex-wrap: wrap; gap: 10px; margin-block: 12px; }
.search-actions :deep(.el-button + .el-button) { margin-left: 0; }
.search-hit { border: 1px solid var(--el-border-color); padding: 14px; margin-block: 14px; border-radius: 6px; min-width: 0; }
.search-hit-title { font-size: 1.17em; margin-block: 1em; }
.search-hit pre { white-space: pre-wrap; overflow-wrap: anywhere; font: inherit; max-height: 40vh; overflow-y: auto; }
.search-version { overflow-wrap: anywhere; }
</style>
