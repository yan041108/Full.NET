<script setup lang="ts">
import AiKnowledgeCitationPosition from './AiKnowledgeCitationPosition.vue';
import { computed, defineAsyncComponent, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton } from 'element-plus';
import type { AiKnowledgeCitation, AiKnowledgeVersionResponse } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import AiKnowledgeBaseSelect from './AiKnowledgeBaseSelect.vue';
const AiKnowledgePdfPreview = defineAsyncComponent(() => import('./AiKnowledgePdfPreview.vue'));
import { downloadKnowledgeSource, listKnowledgeVersions } from '../api/ai-knowledge';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
const props = defineProps<{ open: boolean; knowledgeBaseId?: string; candidateBaseIds?: readonly string[]; documentId: string; versionId: string; citation?: AiKnowledgeCitation; version?: AiKnowledgeVersionResponse }>();
const emit = defineEmits<{ 'update:open': [value: boolean] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const selectedBase = ref(''); const text = ref(''); const binary = ref(false); const failed = ref(false); const loading = ref(false);
const pdfData = ref<Uint8Array>();
const canRead = computed(() => !session.switching && ['ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read'].every(code => session.can(code)));
let epoch = 0; let controller: AbortController | undefined;
function clear() { ++epoch; controller?.abort(); pdfData.value = undefined; text.value = ''; binary.value = false; failed.value = false; loading.value = false; }
watch(() => [props.open, props.knowledgeBaseId, props.candidateBaseIds, props.documentId, props.versionId, props.version], () => { clear(); selectedBase.value = props.knowledgeBaseId ?? ''; }, { immediate: true, deep: true });
watch(selectedBase, clear);
watch(() => [session.currentUser, session.switching], () => { clear(); selectedBase.value = ''; emit('update:open', false); }, { deep: true });
onUnmounted(clear);
async function versionMetadata(current: number) {
  if (props.version?.id === props.versionId && props.version.documentId === props.documentId) return props.version;
  if (!session.can('ai.knowledge_documents.versions_read')) return undefined;
  // 历史引用缺少知识库绑定和文件类型，按当前授权版本分页查找；查询有界，缺失时只提供二进制下载。
  for (let page = 1; page <= 10; page++) {
    const result = await listKnowledgeVersions({ knowledgeBaseId: selectedBase.value, documentId: props.documentId, page, pageSize: 100 }, controller?.signal);
    if (current !== epoch || !props.open || !canRead.value) return undefined;
    const version = result.items.find(row => row.id === props.versionId && row.documentId === props.documentId);
    if (version) return version;
    if (page * 100 >= result.total) return undefined;
  }
  return undefined;
}
async function read(download = false) {
  if (!props.open || !canRead.value || !selectedBase.value || !props.documentId || !props.versionId || loading.value) return;
  if (!props.knowledgeBaseId && props.candidateBaseIds && !props.candidateBaseIds.includes(selectedBase.value)) return;
  clear(); const current = epoch; loading.value = true; controller = new AbortController();
  try {
    // 每次预览或下载都重新请求授权来源，不复用失效的下载链接或模型 URL。
    const blob = await downloadKnowledgeSource({ knowledgeBaseId: selectedBase.value, documentId: props.documentId, versionId: props.versionId }, controller.signal);
    if (current !== epoch || !props.open || !canRead.value) return;
    if (blob.size > 8 * 1024 * 1024) throw new Error('client.source_too_large');
    const version = await versionMetadata(current);
    if (current !== epoch || !props.open || !canRead.value) return;
    const mime = (version?.contentType ?? blob.type).split(';')[0]?.trim().toLowerCase();
    if (download) {
      const url = URL.createObjectURL(blob); const anchor = document.createElement('a');
      anchor.href = url; anchor.download = `${props.versionId}.${mime === 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' ? 'docx' : mime === 'application/pdf' ? 'pdf' : mime === 'text/markdown' ? 'md' : mime === 'text/plain' ? 'txt' : 'bin'}`;
      document.body.append(anchor);
      try { anchor.click(); } finally { anchor.remove(); URL.revokeObjectURL(url); }
    } else if (mime === 'text/plain' || mime === 'text/markdown') {
      const buffer = await blob.arrayBuffer();
      if (current !== epoch || !props.open || !canRead.value) return;
      text.value = new TextDecoder('utf-8', { fatal: true }).decode(buffer).slice(0, 100000);
    } else if (mime === 'application/pdf') {
      const buffer = await blob.arrayBuffer();
      if (current !== epoch || !props.open || !canRead.value) return;
      pdfData.value = new Uint8Array(buffer);
    } else { binary.value = true; }
  } catch { if (current === epoch) { text.value = ''; binary.value = false; failed.value = true; } }
  finally { if (current === epoch) loading.value = false; }
}
</script>
<template>
  <ArtFormDialog :open="open" :title="t('knowledge.source')" width="760px" :show-confirm="false" :cancel-label="t('common.cancel')" @update:open="emit('update:open', $event)">
    <p>{{ t('knowledge.sourceHint') }}</p>
    <p v-if="citation"><AiKnowledgeCitationPosition :citation="citation" /></p>
    <AiKnowledgeBaseSelect v-if="!knowledgeBaseId" v-model="selectedBase" :disabled="loading" :candidate-ids="candidateBaseIds" />
    <p v-if="!knowledgeBaseId && candidateBaseIds">{{ t('knowledge.sourceBaseChoice') }}</p>
    <el-alert v-if="!canRead" :title="t('knowledge.noPermission')" type="warning" :closable="false" />
    <el-alert v-if="failed" :title="t('knowledge.sourceUnavailable')" type="warning" :closable="false" data-testid="knowledge-source-error" />
    <div v-if="canRead" class="source-actions">
      <el-button :disabled="!selectedBase || loading" :loading="loading" data-testid="knowledge-source-read" @click="read()">{{ t('knowledge.readSource') }}</el-button>
      <el-button :disabled="!selectedBase || loading" data-testid="knowledge-source-download" @click="read(true)">{{ t('knowledge.download') }}</el-button>
    </div>
    <pre v-if="text" data-testid="knowledge-source-text">{{ text }}</pre>
    <AiKnowledgePdfPreview v-if="pdfData && open && canRead" :data="pdfData" :initial-page="citation?.pageNumber ?? undefined" />
    <p v-if="binary">{{ t('knowledge.sourceBinary') }}</p>
  </ArtFormDialog>
</template>
<style scoped>.source-actions { display: flex; gap: 8px; margin: 16px 0; } pre { max-height: 50vh; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; padding: 16px; background: var(--el-fill-color-light); border-radius: 8px; }</style>
