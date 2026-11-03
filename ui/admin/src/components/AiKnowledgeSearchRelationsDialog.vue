<script setup lang="ts">
import { knowledgeFaqCitation } from '@fullnet/client-contracts';
import { onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton } from 'element-plus';
import { isFullNetProblemDetails, type AiKnowledgeCitation, type AiKnowledgeSearchRelationsResult, type ReadAiKnowledgeSearchRelationsRequest } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import AiKnowledgeCitationPosition from './AiKnowledgeCitationPosition.vue';
import AiKnowledgeSourceDialog from './AiKnowledgeSourceDialog.vue';
import { readKnowledgeSearchRelations } from '../api/ai-knowledge-search';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeSearchText } from '../i18n/ai-knowledge-search';
const props = defineProps<{ open: boolean; target: ReadAiKnowledgeSearchRelationsRequest }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; changed: []; unavailable: [] }>();
const session = useSessionStore(); const { locale, t } = useAdminI18n(); const tr = (key: Parameters<typeof knowledgeSearchText>[1]) => knowledgeSearchText(locale.value, key);
const busy = ref(false); const result = ref<AiKnowledgeSearchRelationsResult>();
const preview = ref<Omit<AiKnowledgeCitation, 'id'>>(); const sourceOpen = ref(false);
let epoch = 0; let controller: AbortController | undefined;
function clear() { ++epoch; controller?.abort(); busy.value = false; result.value = undefined; preview.value = undefined; sourceOpen.value = false; }
async function load() {
  if (busy.value || !props.open || session.switching || !session.can('ai.knowledge_search.read')) return;
  clear(); const current = ++epoch; controller = new AbortController(); busy.value = true;
  try { const response = await readKnowledgeSearchRelations({ body: { ...props.target, source: { ...props.target.source } } }, controller.signal);
    if (current === epoch && props.open) result.value = response; }
  catch (error) { if (current !== epoch || !props.open) return; clear();
    if (isFullNetProblemDetails(error) && error.code === 'ai.knowledge.search_source_changed') emit('changed'); else emit('unavailable'); }
  finally { if (current === epoch) busy.value = false; }
}
function citation(block: AiKnowledgeSearchRelationsResult['blocks'][number]): Omit<AiKnowledgeCitation, 'id'> {
  return { documentId: props.target.source.documentId, versionId: props.target.source.versionId, sequence: block.sequence,
    pageNumber: block.pageNumber, offset: block.offset, length: block.length, sourceLocation: block.sourceLocation, docxLocation: block.docxLocation, correction: block.correction, faqSource: knowledgeFaqCitation(props.target.source, props.target.embeddingModelConfigId, props.target.embeddingModelVersion) };
}
watch(() => [props.open, props.target], clear, { immediate: true, deep: true });
watch(() => [session.currentUser, session.switching], () => { clear(); emit('update:open', false); }, { deep: true }); onUnmounted(clear);
</script>
<template>
  <ArtFormDialog :open="open" :title="tr('relationsTitle')" :show-confirm="false" :cancel-label="t('common.cancel')" width="850px"
    class="knowledge-relations-dialog" data-testid="knowledge-relations-dialog" @update:open="value => { clear(); emit('update:open', value); }">
    <ElAlert :title="tr('relationsHint')" type="info" :closable="false" />
    <ElButton :disabled="busy" data-testid="relations-load" @click="load">{{ tr('relationsLoad') }}</ElButton>
    <p v-if="busy" role="status">{{ tr('loading') }}</p>
    <template v-if="result">
      <h3>{{ result.documentTitle }}</h3><p class="identity">{{ tr('publication') }} {{ result.source.publicationId }}</p>
      <p role="status" data-testid="relations-state">{{ tr(result.relationState === 'none' ? 'relationsNone' : result.relationState === 'parent_not_indexed' ? 'relationsUnindexed' : 'relationsComplete') }}</p>
      <ul v-if="result.relations.length" :aria-label="tr('relationsEvidence')"><li v-for="(edge, index) in result.relations" :key="index" data-testid="relation-edge">
        {{ tr('relationsParent') }} {{ edge.parentSequence === null ? tr('relationsNoBody') : edge.parentSequence + 1 }} → {{ tr('relationsChild') }} {{ edge.childSequence + 1 }}
        · {{ tr('relationsEvidence') }} {{ edge.parentLocation.blockSequence + 1 }} → {{ edge.childLocation.blockSequence + 1 }}
      </li></ul>
      <div class="relations-body" role="region" tabindex="0" :aria-label="tr('relationsTitle')">
        <article v-for="block in result.blocks" :key="block.sequence" class="relation-block" data-testid="relation-block">
          <h4>{{ tr('contextBlock') }} {{ block.sequence + 1 }} <span v-if="block.sequence === target.anchorSequence">{{ tr('contextAnchor') }}</span></h4>
          <pre>{{ block.text }}</pre><AiKnowledgeCitationPosition :citation="citation(block)" />
          <ElButton data-testid="relations-preview" @click="preview = citation(block); sourceOpen = true">{{ tr('preview') }}</ElButton>
        </article>
      </div>
    </template>
  </ArtFormDialog>
  <AiKnowledgeSourceDialog v-if="preview" v-model:open="sourceOpen" :knowledge-base-id="target.source.knowledgeBaseId" :document-id="target.source.documentId"
    :version-id="target.source.versionId" :citation="preview" @unavailable="clear(); emit('unavailable')" />
</template>
<style scoped>
.relation-block { border: 1px solid var(--el-border-color); border-radius: 6px; padding: 12px; margin-block: 12px; min-width: 0; }
.relation-block pre, .identity { white-space: pre-wrap; overflow-wrap: anywhere; font: inherit; }
.relation-block pre { max-height: 28vh; overflow-y: auto; }
.relations-body { max-height: 48vh; overflow-y: auto; }
:global(.knowledge-relations-dialog) { max-height: calc(100vh - 24px); display: flex; flex-direction: column; }
:global(.knowledge-relations-dialog .el-dialog__body) { min-height: 0; overflow-y: auto; }
</style>
