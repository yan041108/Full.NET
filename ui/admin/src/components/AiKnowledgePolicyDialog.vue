<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElForm, ElFormItem, ElOption, ElSelect } from 'element-plus';
import type { AiKnowledgeBaseResponse, AiModelConfigListItem } from '@fullnet/client-contracts';
import { getKnowledgeBase, updateKnowledgePolicy } from '../api/ai-knowledge';
import { listAiModelConfigs } from '../api/ai-model-configs';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
const props = defineProps<{ open: boolean; knowledgeBaseId: string }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; saved: [] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const allowed = computed(() => !session.switching && session.can('ai.knowledge_bases.read') && session.can('ai.knowledge_bases.policy_update'));
const modelRead = computed(() => allowed.value && session.can('ai.models.read'));
const base = ref<AiKnowledgeBaseResponse>(); const classification = ref('internal'); const embedding = ref(''); const generation = ref('');
const models = ref<AiModelConfigListItem[]>([]); const modelPage = ref(0); const modelTotal = ref(0);
const loading = ref(false); const modelLoading = ref(false); const saving = ref(false);
let epoch = 0; let controller: AbortController | undefined;
const embeddingIds = new Set(['text-embedding-3-small', 'text-embedding-3-large']);
const key = (id: string | null, version: number | null) => id && version ? `${id}:${version}` : '';
function options(isEmbedding: boolean) {
  const result = models.value.filter(model => embeddingIds.has(model.modelId) === isEmbedding)
    .map(model => ({ value: key(model.id, model.version), label: `${model.name} · ${model.modelId} · v${model.version}` }));
  const original = base.value; const id = isEmbedding ? original?.embeddingModelConfigId : original?.generationModelConfigId;
  const version = isEmbedding ? original?.embeddingModelVersion : original?.generationModelVersion;
  if (id && version && !result.some(item => item.value === key(id, version))) result.unshift({ value: key(id, version), label: `${t('knowledge.originalApproval')} · ${id} · v${version}` });
  return result;
}
const embeddingOptions = computed(() => options(true)); const generationOptions = computed(() => options(false));
function reset() {
  ++epoch; controller?.abort(); base.value = undefined; models.value = []; modelPage.value = 0; modelTotal.value = 0;
  classification.value = 'internal'; embedding.value = ''; generation.value = ''; loading.value = false; modelLoading.value = false; saving.value = false;
}
async function loadModels(scope = epoch) {
  if (!props.open || !modelRead.value || !base.value || modelLoading.value || modelPage.value >= 10) return;
  const next = modelPage.value + 1; modelLoading.value = true;
  try {
    const result = await listAiModelConfigs({ page: next, pageSize: 100, isEnabled: true }, controller?.signal);
    if (scope !== epoch) return;
    models.value = [...models.value, ...result.items.filter(model => model.isEnabled && !models.value.some(existing => existing.id === model.id))];
    modelPage.value = next; modelTotal.value = result.total;
  } catch (error) { if (scope === epoch) showProblem(error, t('common.loadFailed')); }
  finally { if (scope === epoch) modelLoading.value = false; }
}
async function load() {
  reset(); if (!props.open || !allowed.value) return;
  const scope = epoch; controller = new AbortController(); loading.value = true;
  try {
    const result = await getKnowledgeBase({ knowledgeBaseId: props.knowledgeBaseId }, controller.signal);
    if (scope !== epoch) return;
    if (result.id !== props.knowledgeBaseId) throw new Error('client.invalid_knowledge_resource');
    base.value = result; classification.value = result.dataClassification;
    embedding.value = key(result.embeddingModelConfigId, result.embeddingModelVersion); generation.value = key(result.generationModelConfigId, result.generationModelVersion);
    await loadModels(scope);
  } catch (error) { if (scope === epoch) showProblem(error, t('common.loadFailed')); }
  finally { if (scope === epoch) loading.value = false; }
}
function approval(value: string, isEmbedding: boolean) {
  if (!value) return { id: null, version: null };
  if (!options(isEmbedding).some(item => item.value === value)) return undefined;
  const split = value.lastIndexOf(':'); return { id: value.slice(0, split), version: Number(value.slice(split + 1)) };
}
async function save() {
  if (!props.open || !allowed.value || !base.value || loading.value || saving.value) return;
  const approvedEmbedding = approval(embedding.value, true); const approvedGeneration = approval(generation.value, false);
  if (!['public', 'internal', 'restricted'].includes(classification.value) || !approvedEmbedding || !approvedGeneration) { showWarning(t('knowledge.invalidPolicy')); return; }
  // 保留已审批的精确版本；目录中出现新版本不等于用户批准升级。
  const parameters = { knowledgeBaseId: props.knowledgeBaseId, body: { dataClassification: classification.value, version: base.value.version,
    embeddingModelConfigId: approvedEmbedding.id, embeddingModelVersion: approvedEmbedding.version,
    generationModelConfigId: approvedGeneration.id, generationModelVersion: approvedGeneration.version } };
  const scope = epoch; saving.value = true;
  try { await updateKnowledgePolicy(parameters); if (scope !== epoch) return; showSuccess(t('common.saved')); emit('saved'); emit('update:open', false); }
  catch (error) { if (scope === epoch) showProblem(error, t('common.saveFailed')); }
  finally { if (scope === epoch) saving.value = false; }
}
watch(() => [props.open, props.knowledgeBaseId, session.currentUser, session.switching], () => void load(), { deep: true, immediate: true });
onUnmounted(reset);
</script>
<template>
  <ArtFormDialog :open="open" :title="t('knowledge.policy')" :saving="saving" :show-confirm="allowed && !!base && !loading" :confirm-label="t('knowledge.save')" :cancel-label="t('common.cancel')" confirm-test-id="knowledge-policy-save" @confirm="save" @update:open="emit('update:open', $event)">
    <el-alert v-if="!allowed" :title="t('knowledge.noPermission')" type="warning" :closable="false" />
    <template v-else>
      <p>{{ t('knowledge.policyHint') }}</p>
      <el-form label-position="top" :disabled="loading || saving || !base" @submit.prevent="save">
        <el-form-item :label="t('knowledge.classification')"><el-select v-model="classification"><el-option v-for="value in ['public', 'internal', 'restricted']" :key="value" :value="value" :label="t(`knowledge.${value}`)" /></el-select></el-form-item>
        <el-form-item v-for="isEmbedding in [true, false]" :key="String(isEmbedding)" :label="t(isEmbedding ? 'knowledge.embeddingApproval' : 'knowledge.generationApproval')">
          <el-select :model-value="isEmbedding ? embedding : generation" @update:model-value="value => { if (isEmbedding) embedding = String(value); else generation = String(value); }">
            <el-option value="" :label="t('knowledge.clearApproval')" /><el-option v-for="item in isEmbedding ? embeddingOptions : generationOptions" :key="item.value" :value="item.value" :label="item.label" />
          </el-select>
        </el-form-item>
      </el-form>
      <template v-if="modelRead && base">
        <p>{{ t('knowledge.candidateCount') }} {{ models.length }} / {{ modelTotal }}</p>
        <el-button v-if="modelPage === 0 || (models.length < modelTotal && modelPage < 10)" :loading="modelLoading" :disabled="loading || saving || modelLoading" data-testid="knowledge-models-more" @click="loadModels()">{{ t('knowledge.loadMore') }}</el-button>
        <p v-if="modelPage >= 10 && models.length < modelTotal">{{ t('knowledge.candidateLimit') }}</p>
      </template>
    </template>
  </ArtFormDialog>
</template>
