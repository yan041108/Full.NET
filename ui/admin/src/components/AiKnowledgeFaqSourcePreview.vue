<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton } from 'element-plus';
import type { AiKnowledgeCitation, AiKnowledgeFaqPublishedContent, AiKnowledgeSearchSource } from '@fullnet/client-contracts';
import { getPublishedFaq } from '../api/ai-knowledge-faq-lifecycle';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeFaqSourceText } from '../i18n/ai-knowledge-faq-source';
const props = defineProps<{ open: boolean; target?: NonNullable<AiKnowledgeCitation['faqSource']>; source?:AiKnowledgeSearchSource; documentId: string; versionId: string }>();
const emit = defineEmits<{ unavailable: [] }>();
const session = useSessionStore(); const { locale } = useAdminI18n(); const tr = (key: Parameters<typeof knowledgeFaqSourceText>[1]) => knowledgeFaqSourceText(locale.value, key);
const required = ['ai.knowledge_bases.read', 'ai.knowledge_faqs.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read', 'ai.knowledge_documents.index_read'];
const canRead = computed(() => !session.switching && required.every(value => session.can(value)));
const value = ref<AiKnowledgeFaqPublishedContent>(); const failed = ref(false); const busy = ref(false); let epoch = 0; let abort: AbortController | undefined;
function clear() { ++epoch; abort?.abort(); value.value = undefined; failed.value = false; busy.value = false; }
watch(() => [props.open, props.target,props.source, props.documentId, props.versionId, session.currentUser, session.switching, canRead.value], clear, { deep: true });
onUnmounted(clear);
async function read() {
  if (!props.open || !canRead.value || busy.value) return;
  clear(); const request = epoch; busy.value = true; abort = new AbortController();
  try {
    const source = props.source??props.target?.source;
    if (!source || source.sourceKind !== 'faq-revision-v1' || !source.faqId || !source.faqRevisionId || source.documentId !== props.documentId || source.versionId !== props.versionId) throw new Error('client.invalid_faq_source');
    const current = await getPublishedFaq({ knowledgeBaseId: source.knowledgeBaseId, faqId: source.faqId }, abort.signal);
    if (request !== epoch || !props.open || !canRead.value) return;
    if (current.revisionId !== source.faqRevisionId || current.publicationId !== source.publicationId) throw new Error('client.faq_source_changed');
    value.value = current;
  } catch { if (request === epoch) { value.value = undefined; failed.value = true; emit('unavailable'); } }
  finally { if (request === epoch) busy.value = false; }
}
</script>
<template>
  <section data-testid="knowledge-faq-source">
    <p>{{ tr('hint') }}</p>
    <ElAlert v-if="failed || !canRead" :title="tr('unavailable')" type="warning" :closable="false" data-testid="knowledge-faq-source-error" />
    <ElButton :disabled="!canRead || busy" :loading="busy" data-testid="knowledge-faq-source-read" @click="read">{{ tr('read') }}</ElButton>
    <article v-if="value" data-testid="knowledge-faq-source-body">
      <h3>{{ value.content.question }}</h3><p v-for="alias in value.content.aliases" :key="alias">{{ alias }}</p><pre>{{ value.content.answer }}</pre>
      <p>{{ tr('revision') }}: {{ value.revisionId }}</p><p>{{ tr(value.origin === 'feedback_derived'?'feedback_derived':value.origin === 'independent' ? 'independent' : value.origin==='faq_derived'?'faq_derived':'document_derived') }}</p><p v-if="value.basis">{{ value.basis }}</p>
      <p v-if="value.feedback" data-testid="faq-published-feedback">{{ tr('feedback') }} · {{ value.feedback.feedbackId }} · v{{ value.feedback.feedbackVersion }} / v{{ value.feedback.reviewVersion }}</p><h4>{{ tr('dependencies') }}</h4><p v-for="source in value.sources" :key="source.documentId">{{ source.knowledgeBaseId }} · {{ source.documentId }} · {{ source.versionId }} · {{ source.publicationId }}</p>
      <section v-if="value.graph" data-testid="faq-published-parent-graph"><h4>{{tr('parents')}}</h4><p v-for="node in value.graph.nodes" :key="node.source.documentId">{{node.source.faqId}} · {{node.source.faqRevisionId}} · {{node.source.publicationId}} → {{node.parentDocumentIds.join(', ')||tr('none')}}</p></section>
    </article>
  </section>
</template>
<style scoped>section { overflow-wrap: anywhere; } pre { white-space: pre-wrap; overflow-wrap: anywhere; max-height: 50vh; overflow: auto; }</style>
