<script setup lang="ts">
import { knowledgeFaqCitation } from '@fullnet/client-contracts';
import { onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElOption, ElSelect } from 'element-plus';
import { isFullNetProblemDetails, type AiKnowledgeCitation, type AiKnowledgeSearchContextResult, type ReadAiKnowledgeSearchContextRequest } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import AiKnowledgeCitationPosition from './AiKnowledgeCitationPosition.vue';
import AiKnowledgeSourceDialog from './AiKnowledgeSourceDialog.vue';
import { readKnowledgeSearchContext } from '../api/ai-knowledge-search';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeSearchText } from '../i18n/ai-knowledge-search';

const props = defineProps<{ open: boolean; target: Omit<ReadAiKnowledgeSearchContextRequest, 'radius'> }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; changed: []; unavailable: [] }>();
const { locale, t } = useAdminI18n(); const tr = (key: Parameters<typeof knowledgeSearchText>[1]) => knowledgeSearchText(locale.value, key);
const radius = ref(1); const busy = ref(false); const result = ref<AiKnowledgeSearchContextResult>();
const preview = ref<Omit<AiKnowledgeCitation, 'id'>>(); const sourceOpen = ref(false);
let epoch = 0; let controller: AbortController | undefined;
function clear() { ++epoch; controller?.abort(); busy.value = false; result.value = undefined; preview.value = undefined; sourceOpen.value = false; }
async function load() {
  clear(); if (!props.open) return; const current = ++epoch; controller = new AbortController(); busy.value = true;
  try {
    const response = await readKnowledgeSearchContext({ body: { ...props.target, source: { ...props.target.source }, radius: radius.value } }, controller.signal);
    if (current === epoch && props.open) result.value = response;
  } catch (error) {
    if (current !== epoch || !props.open) return;
    clear();
    if (isFullNetProblemDetails(error) && error.code === 'ai.knowledge.search_source_changed') emit('changed');
    else emit('unavailable');
  } finally { if (current === epoch) busy.value = false; }
}
function citation(block: AiKnowledgeSearchContextResult['blocks'][number]): Omit<AiKnowledgeCitation, 'id'> {
  return { documentId: props.target.source.documentId, versionId: props.target.source.versionId, sequence: block.sequence,
    pageNumber: block.pageNumber, offset: block.offset, length: block.length, sourceLocation: block.sourceLocation, docxLocation: block.docxLocation, correction: block.correction, faqSource: knowledgeFaqCitation(props.target.source, props.target.embeddingModelConfigId, props.target.embeddingModelVersion) };
}
watch(() => [props.open, props.target], () => { radius.value = 1; void load(); }, { immediate: true, deep: true });
watch(radius, () => void load()); onUnmounted(clear);
</script>

<template>
  <ArtFormDialog :open="open" :title="tr('contextTitle')" :show-confirm="false" :cancel-label="t('common.cancel')" width="800px"
    class="knowledge-context-dialog" data-testid="knowledge-context-dialog" @update:open="value => { clear(); emit('update:open', value); }">
    <ElAlert :title="tr('contextHint')" type="info" :closable="false" />
    <label for="knowledge-context-radius">{{ tr('contextRadius') }}</label>
    <ElSelect id="knowledge-context-radius" v-model="radius" :teleported="false" :disabled="busy" :aria-label="tr('contextRadius')" data-testid="context-radius">
      <ElOption v-for="value in [0, 1, 2]" :key="value" :value="value" :label="tr(value === 0 ? 'contextZero' : value === 1 ? 'contextOne' : 'contextTwo')" />
    </ElSelect>
    <ElButton :disabled="busy" data-testid="context-refresh" @click="load">{{ tr('contextRefresh') }}</ElButton>
    <p v-if="busy" role="status">{{ tr('loading') }}</p>
    <template v-if="result">
      <h3>{{ result.documentTitle }}</h3>
      <p>{{ result.hasPreviousBlocks ? tr('contextBefore') : tr('contextStart') }} · {{ result.hasNextBlocks ? tr('contextAfter') : tr('contextEnd') }}</p>
      <article v-for="block in result.blocks" :key="block.sequence" class="context-block" data-testid="context-block">
        <h4>{{ tr('contextBlock') }} {{ block.sequence + 1 }} <span v-if="block.sequence === target.anchorSequence">{{ tr('contextAnchor') }}</span></h4>
        <pre>{{ block.text }}</pre><AiKnowledgeCitationPosition :citation="citation(block)" />
        <ElButton data-testid="context-preview" @click="preview = citation(block); sourceOpen = true">{{ tr('preview') }}</ElButton>
      </article>
    </template>
  </ArtFormDialog>
  <AiKnowledgeSourceDialog v-if="preview" v-model:open="sourceOpen" :knowledge-base-id="target.source.knowledgeBaseId" :document-id="target.source.documentId"
    :version-id="target.source.versionId" :citation="preview" @unavailable="clear(); emit('unavailable')" />
</template>

<style scoped>
.context-block { border: 1px solid var(--el-border-color); border-radius: 6px; padding: 12px; margin-block: 12px; min-width: 0; }
.context-block pre { white-space: pre-wrap; overflow-wrap: anywhere; font: inherit; max-height: 32vh; overflow-y: auto; }
.el-select { max-width: 100%; margin-block: 12px; }
/* 弹窗内的绝对定位选值文本须有实体背景，避免被遮罩层参与文字对比度合成。 */
:global(.knowledge-context-dialog .el-select__placeholder > span) { background-color: var(--el-fill-color-blank); }
</style>
