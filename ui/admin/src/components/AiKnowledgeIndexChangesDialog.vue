<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElTable, ElTableColumn } from 'element-plus';
import { translateKnowledgeIndexChanges } from '@fullnet/admin-i18n';
import type { AiKnowledgeIndexChangeItem } from '@fullnet/client-contracts';
import * as api from '../api/ai-knowledge';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem } from '../feedback/fullNetMessage';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import { ART_TABLE_ACTION_COLUMN_WIDTH } from '../framework/art-design/components/artTableActions';
import AiKnowledgeIndexDialog from './AiKnowledgeIndexDialog.vue';

const props = defineProps<{ open: boolean; knowledgeBaseId: string }>();
const emit = defineEmits<{ 'update:open': [value: boolean] }>();
const session = useSessionStore(); const { t, locale } = useAdminI18n();
const label = (key: Parameters<typeof translateKnowledgeIndexChanges>[1]) => translateKnowledgeIndexChanges(locale.value, key);
const canRead = computed(() => !session.switching && ['ai.knowledge_bases.read', 'ai.knowledge_documents.read',
  'ai.knowledge_documents.versions_read', 'ai.knowledge_documents.index_read', 'ai.knowledge_documents.index_changes_read'].every(code => session.can(code)));
const rows = ref<AiKnowledgeIndexChangeItem[]>([]); const loading = ref(false);
const cursors = ref<(string | undefined)[]>([undefined]); const page = ref(0); const next = ref<string | null>();
const selected = ref<AiKnowledgeIndexChangeItem>(); const indexOpen = ref(false);
let epoch = 0; let controller: AbortController | undefined;
const guid = /^(?!00000000-0000-0000-0000-000000000000$)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function reset() {
  ++epoch; controller?.abort(); rows.value = []; next.value = undefined; loading.value = false;
  cursors.value = [undefined]; page.value = 0; selected.value = undefined; indexOpen.value = false;
}
async function load(target = 0) {
  if (!props.open || !canRead.value || !guid.test(props.knowledgeBaseId) || loading.value) return;
  const cursor = target === 0 ? undefined : target === page.value + 1 ? next.value ?? undefined : cursors.value[target];
  if (target < 0 || (target > 0 && !cursor)) return;
  controller?.abort(); controller = new AbortController(); const scope = ++epoch;
  rows.value = []; next.value = undefined; selected.value = undefined; indexOpen.value = false; loading.value = true;
  try {
    const result = await api.listKnowledgeIndexChanges({ knowledgeBaseId: props.knowledgeBaseId, afterDocumentId: cursor, pageSize: 20 }, controller.signal);
    if (scope !== epoch) return;
    // 模拟层或后续适配也不能把重复游标交给分页，避免无限重复请求。
    if (result.nextDocumentId && (result.nextDocumentId === cursor || cursors.value.slice(1, target + 1).includes(result.nextDocumentId))) throw new Error('client.invalid_knowledge_resource');
    rows.value = result.items; next.value = result.nextDocumentId;
    cursors.value = target === 0 ? [undefined] : [...cursors.value.slice(0, target), cursor]; page.value = target;
  } catch (error) {
    if (scope === epoch) { cursors.value = [undefined]; page.value = 0; showProblem(error, t('common.loadFailed')); }
  } finally { if (scope === epoch) loading.value = false; }
}
function openIndex(value: unknown) {
  const row = rows.value.find(item => item === value);
  if (!props.open || !canRead.value || loading.value || !row) return;
  selected.value = row; indexOpen.value = true;
}
watch(() => [props.open, props.knowledgeBaseId, session.currentUser, session.switching, canRead.value], () => { reset(); void load(); }, { deep: true, immediate: true });
onUnmounted(reset);
</script>
<template>
  <ArtFormDialog :open="open" :title="label('title')" width="860px" :show-confirm="false" :cancel-label="t('common.cancel')" @update:open="emit('update:open', $event)">
    <el-alert v-if="!canRead" :title="t('knowledge.noPermission')" type="warning" :closable="false" />
    <section v-else :aria-busy="loading">
      <el-alert :title="label('hint')" type="info" :closable="false" />
      <div class="source-navigation">
        <el-button data-testid="knowledge-changes-refresh" :disabled="loading" @click="load()">{{ label('refresh') }}</el-button>
        <span aria-live="polite">{{ label('page').replace('{page}', String(page + 1)) }}</span>
      </div>
      <el-table :data="rows" row-key="documentId" max-height="360" :empty-text="label('empty')" border stripe>
        <el-table-column prop="title" :label="label('name')" min-width="200" show-overflow-tooltip />
        <el-table-column prop="versionNumber" :label="label('version')" width="110" />
        <el-table-column prop="updatedAtUtc" :label="label('updated')" min-width="220" />
        <el-table-column :label="t('knowledge.actions')" :width="ART_TABLE_ACTION_COLUMN_WIDTH"><template #default="{ row }"><ArtTableActionGroup>
          <ArtTableActionButton type="view" :title="label('view')" test-id="knowledge-changes-index" @click="openIndex(row)" />
        </ArtTableActionGroup></template></el-table-column>
      </el-table>
      <nav class="source-navigation" :aria-label="label('title')">
        <el-button data-testid="knowledge-changes-previous" :disabled="loading || page === 0" @click="load(page - 1)">{{ label('previous') }}</el-button>
        <el-button data-testid="knowledge-changes-next" :disabled="loading || !next" @click="load(page + 1)">{{ label('next') }}</el-button>
      </nav>
    </section>
    <AiKnowledgeIndexDialog v-if="selected && canRead && open" v-model:open="indexOpen" :knowledge-base-id="knowledgeBaseId" :document-id="selected.documentId" :version-id="selected.versionId" />
  </ArtFormDialog>
</template>
<style scoped>
.source-navigation { display: flex; align-items: center; gap: 12px; margin-block: 16px; }
</style>
