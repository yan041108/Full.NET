<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElInput, ElOption, ElSelect } from 'element-plus';
import type { AiModelConfigListItem, AiPersonalScheduleSourcePreview, AskAiKnowledgeRequest } from '@fullnet/client-contracts';
import { listAiModelConfigs } from '../api/ai-model-configs';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { personalScheduleAssistantText } from '../i18n/personal-schedule-assistant';
import AiKnowledgeBaseMultiSelect from './AiKnowledgeBaseMultiSelect.vue';
const AiKnowledgeSearchAnswerDialog = defineAsyncComponent(() => import('./AiKnowledgeSearchAnswerDialog.vue'));
const props = defineProps<{ sources: AiPersonalScheduleSourcePreview['sources'] }>();
const emit = defineEmits<{ unavailable: [] }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const tr = (key: Parameters<typeof personalScheduleAssistantText>[1]) => personalScheduleAssistantText(locale.value, key);
const allowed = computed(() => !session.switching && session.currentUser?.tenantId === null && ['ai.chat.messages.send', 'ai.chat.sessions.read', 'ai.models.read',
  'ai.knowledge_bases.read', 'ai.knowledge_documents.read', 'ai.knowledge_documents.source_read', 'ai.knowledge_documents.index_read',
  'calendar.personal_schedules.read', 'calendar.personal_schedules.assistant_read'].every(code => session.can(code)));
const models = ref<AiModelConfigListItem[]>([]); const embedding = ref(''); const bases = ref<string[]>([]); const question = ref('');
const target = ref<AskAiKnowledgeRequest>(); const open = ref(false); const failed = ref(false); let epoch = 0;
const ready = computed(() => allowed.value && bases.value.length > 0 && bases.value.length <= 8 && !!embedding.value
  && question.value.trim().length > 0 && question.value.trim().length <= 4096 && props.sources.length > 0 && props.sources.length < 8);
function clear() { ++epoch; open.value = false; target.value = undefined; models.value = []; embedding.value = ''; bases.value = []; question.value = ''; }
async function load() {
  if (!allowed.value) return;
  const current = ++epoch;
  try {
    const reply = await listAiModelConfigs({ page: 1, pageSize: 100, isEnabled: true });
    if (current === epoch && allowed.value) models.value = reply.items.filter(model => model.isEnabled
      && ['text-embedding-3-small', 'text-embedding-3-large'].includes(model.modelId));
  } catch { if (current === epoch) failed.value = true; }
}
function prepare() {
  if (!ready.value) return;
  // 仅冻结预览期望值，服务器必须重读全部业务对象；页面不携带正文、主体或租户授权。
  target.value = { knowledgeBaseIds: [...bases.value], embeddingModelConfigId: embedding.value, question: question.value.trim(),
    personalSchedules: props.sources.map(source => ({ id: source.id, version: source.version, projectionHash: source.projectionHash })) };
  open.value = true;
}
watch(() => [props.sources, allowed.value, session.currentUser, session.currentContextName], () => { clear(); void load(); }, { deep: true });
onMounted(load); onUnmounted(clear);
</script>
<template>
  <section v-if="allowed" class="schedule-ask" data-testid="schedule-ask-panel">
    <h3>{{ tr('ask') }}</h3><p>{{ tr('askHint') }}</p>
    <ElAlert v-if="failed" type="error" :title="tr('invalid')" :closable="false" />
    <AiKnowledgeBaseMultiSelect v-model="bases" />
    <ElSelect v-model="embedding" :aria-label="tr('embedding')" :placeholder="tr('embedding')" :teleported="false" data-testid="schedule-ask-model">
      <ElOption v-for="model in models" :key="model.id" :value="model.id" :label="model.name" />
    </ElSelect>
    <ElInput v-model="question" type="textarea" :rows="3" :maxlength="4096" :aria-label="tr('question')" :placeholder="tr('question')" data-testid="schedule-ask-question" />
    <ElButton :disabled="!ready" data-testid="schedule-ask-prepare" @click="prepare">{{ tr('ask') }}</ElButton>
    <AiKnowledgeSearchAnswerDialog v-if="target" v-model:open="open" :target="target" @unavailable="clear(); emit('unavailable')" @changed="clear(); emit('unavailable')" />
  </section>
</template>
<style scoped>
.schedule-ask { display: grid; gap: 12px; min-width: 0; }
.schedule-ask p { white-space: normal; overflow-wrap: anywhere; }
/* 私人来源叠层保留实体选值表面，标签文字采用正文对比度。 */
.schedule-ask :deep(.el-select__tags-text) { color: var(--el-text-color-primary); }
.schedule-ask :deep(.el-select__placeholder > span) { background-color: var(--el-fill-color-blank); }
</style>
