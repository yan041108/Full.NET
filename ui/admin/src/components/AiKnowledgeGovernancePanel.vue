<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElAlert, ElButton, ElEmpty, ElInput, ElMessageBox, ElOption, ElPagination, ElSelect, ElTable, ElTableColumn, ElTag } from 'element-plus';
import { isGuid, type AiKnowledgeDirectoryResponse, type AiKnowledgeFolderResponse, type AiKnowledgeDirectoryItemResult, type SetAiKnowledgeDirectoryItem } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import AiKnowledgePublicationPanel from './AiKnowledgePublicationPanel.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeGovernanceText } from '../i18n/ai-knowledge-governance';
import * as api from '../api/ai-knowledge-governance';
const props = defineProps<{ open: boolean; knowledgeBaseId: string }>();
const emit = defineEmits<{ 'update:open': [value: boolean] }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof knowledgeGovernanceText>[1]) => knowledgeGovernanceText(locale.value, key);
const readable = computed(() => !session.switching && ['ai.knowledge_bases.read', 'ai.knowledge_documents.read'].every(code => session.can(code)));
const writable = computed(() => readable.value && session.can('ai.knowledge_directories.manage'));
const publicationReadable = computed(() => ['ai.knowledge_publications.read', 'ai.knowledge_documents.source_read', 'ai.knowledge_documents.index_read'].every(code => session.can(code)));
const folders = ref<AiKnowledgeFolderResponse[]>([]), rows = ref<AiKnowledgeDirectoryResponse[]>([]), selected = ref<AiKnowledgeDirectoryResponse[]>([]);
const total = ref(0), page = ref(1), busy = ref(false), failed = ref(false);
const folderFilter = ref(''), tagFilter = ref(''), responsibleFilter = ref(''), stateFilter = ref('');
const folderName = ref(''), folderParent = ref(''), editingFolder = ref('');
const targetFolder = ref(''), tags = ref(''), responsible = ref('');
const results = ref<AiKnowledgeDirectoryItemResult[]>([]);
const publicationOpen = ref(false), document = ref<AiKnowledgeDirectoryResponse>();
let epoch = 0, controller = new AbortController(), confirming = false;
let originalBatch: { fingerprint: string; items: SetAiKnowledgeDirectoryItem[] } | undefined;
function reset() {
  ++epoch; controller.abort(); controller = new AbortController(); if (confirming) ElMessageBox.close(); confirming = false;
  folders.value = []; rows.value = []; selected.value = []; total.value = 0; page.value = 1; busy.value = false; failed.value = false;
  folderFilter.value = ''; tagFilter.value = ''; responsibleFilter.value = ''; stateFilter.value = '';
  folderName.value = ''; folderParent.value = ''; editingFolder.value = ''; targetFolder.value = ''; tags.value = ''; responsible.value = '';
  results.value = []; originalBatch = undefined; publicationOpen.value = false; document.value = undefined;
}
watch(() => [props.open, props.knowledgeBaseId, session.currentUser, session.currentContextName, session.switching], () => {
  reset(); if (props.open && readable.value && isGuid(props.knowledgeBaseId)) void act(load);
}, { immediate: true, deep: true, flush: 'sync' });
onBeforeUnmount(reset);
async function act(action: (current: number, signal: AbortSignal) => Promise<void>) {
  if (busy.value || !props.open || !readable.value) return;
  const current = epoch; busy.value = true; failed.value = false;
  try { await action(current, controller.signal); }
  catch (error) { if (current === epoch && error !== 'cancel' && error !== 'close') failed.value = true; }
  finally { if (current === epoch) busy.value = false; }
}
async function load(current: number, signal: AbortSignal) {
  rows.value = []; folders.value = []; selected.value = []; total.value = 0;
  if (responsibleFilter.value && !isGuid(responsibleFilter.value)) throw new Error('invalid');
  const values = await api.listFolders(props.knowledgeBaseId, signal);
  if (current !== epoch) return; folders.value = values;
  const result = await api.listDirectory(props.knowledgeBaseId, { page: page.value, ...(folderFilter.value ? { folderId: folderFilter.value } : {}),
    ...(tagFilter.value.trim() ? { tag: tagFilter.value.trim() } : {}), ...(responsibleFilter.value ? { responsibleUserId: responsibleFilter.value } : {}),
    ...(stateFilter.value ? { state: stateFilter.value } : {}) }, signal);
  if (current === epoch) { rows.value = result.items; total.value = result.total; }
}
function chooseFolder() {
  const folder = folders.value.find(item => item.id === editingFolder.value);
  folderName.value = folder?.name ?? ''; folderParent.value = folder?.parentId ?? '';
}
async function saveFolder(current: number, signal: AbortSignal) {
  if (!writable.value || !folderName.value.trim()) return;
  const existing = folders.value.find(item => item.id === editingFolder.value);
  const body = { name: folderName.value.trim(), parentId: folderParent.value || null, version: existing?.version ?? 0 };
  if (existing) await api.updateFolder(props.knowledgeBaseId, existing.id, body, signal); else await api.createFolder(props.knowledgeBaseId, body, signal);
  if (current === epoch) { editingFolder.value = ''; folderName.value = ''; folderParent.value = ''; await load(current, signal); }
}
async function deleteFolder(current: number, signal: AbortSignal) {
  const folder = folders.value.find(item => item.id === editingFolder.value); if (!folder || !writable.value) return;
  confirming = true;
  try { await ElMessageBox.confirm(text('confirmDelete'), text('folder'), { type: 'warning' }); }
  finally { confirming = false; }
  if (current !== epoch || signal.aborted || !writable.value) return;
  await api.deleteFolder(props.knowledgeBaseId, folder.id, folder.version, signal);
  if (current === epoch) { editingFolder.value = ''; folderName.value = ''; folderParent.value = ''; await load(current, signal); }
}
async function batch(current: number, signal: AbortSignal, retry = false) {
  if (!writable.value) return;
  let items: SetAiKnowledgeDirectoryItem[];
  if (retry && originalBatch) {
    const failures = new Set(results.value.filter(result => !result.succeeded).map(result => result.documentId));
    items = originalBatch.items.filter(item => failures.has(item.documentId));
  } else {
    if (selected.value.length < 1 || selected.value.length > 20 || responsible.value && !isGuid(responsible.value)) throw new Error('invalid');
    const draft = selected.value.map(row => ({ documentId: row.id, version: row.version, folderId: targetFolder.value || null,
      tags: tags.value.split(/[,，]/u).map(tag => tag.trim()).filter(Boolean), responsibleUserId: responsible.value || null }));
    if (draft.some(item => item.tags.length > 10)) throw new Error('invalid');
    const fingerprint = JSON.stringify(draft);
    if (originalBatch?.fingerprint !== fingerprint) originalBatch = { fingerprint, items: draft.map(item => ({ requestKey: crypto.randomUUID(), ...item })) };
    items = originalBatch.items;
  }
  if (!items.length) return;
  try {
    const value = await api.setDirectoryBatch(props.knowledgeBaseId, { items }, signal);
    if (current !== epoch) return;
    const prior = results.value.filter(item => !value.some(result => result.documentId === item.documentId));
    results.value = [...prior, ...value]; await load(current, signal);
  } catch (error) {
    if (current === epoch) {
      const pending = new Set(items.map(item => item.documentId));
      results.value = [...results.value.filter(item => !pending.has(item.documentId)), ...items.map(item => ({ documentId: item.documentId, requestKey: item.requestKey, succeeded: false, version: null, errorCode: 'client.outcome_unknown' }))];
    }
    throw error;
  }
}
function openPublication(id: string) { const row = rows.value.find(item => item.id === id); if (row) { document.value = row; publicationOpen.value = true; } }
const hasFailures = computed(() => results.value.some(item => !item.succeeded));
</script>
<template>
  <ArtFormDialog :open="open && readable" :title="text('title')" width="1160px" :show-confirm="false" :cancel-label="text('close')" :trap-focus="true" @update:open="emit('update:open', $event)">
    <section class="knowledge-governance" data-testid="knowledge-governance">
      <p>{{ text('organizationHint') }}</p><p>{{ text('publicationHint') }}</p>
      <ElAlert v-if="failed" :title="text('unavailable')" type="error" :closable="false" />
      <form class="knowledge-governance__toolbar" @submit.prevent="page = 1; act(load)">
        <ElSelect v-model="folderFilter" clearable :aria-label="text('folder')" :placeholder="text('folder')" :disabled="busy"><ElOption v-for="folder in folders" :key="folder.id" :label="folder.name" :value="folder.id" /></ElSelect>
        <ElInput v-model="tagFilter" :aria-label="text('tag')" :placeholder="text('tag')" :disabled="busy" />
        <ElInput v-model="responsibleFilter" :aria-label="text('responsible')" :placeholder="text('responsible')" :disabled="busy" />
        <ElSelect v-model="stateFilter" clearable :aria-label="text('state')" :placeholder="text('state')" :disabled="busy"><ElOption v-for="state in (['draft','published','scheduled','expired'] as const)" :key="state" :label="text(state)" :value="state" /></ElSelect>
        <ElButton native-type="submit" :disabled="busy" data-testid="knowledge-governance-filter">{{ text('filter') }}</ElButton>
      </form>
      <fieldset v-if="writable" :disabled="busy"><legend>{{ text('folder') }}</legend>
        <ElSelect v-model="editingFolder" clearable :aria-label="text('renameFolder')" :placeholder="text('renameFolder')" @change="chooseFolder"><ElOption v-for="folder in folders" :key="folder.id" :label="folder.name" :value="folder.id" /></ElSelect>
        <ElInput v-model="folderName" :maxlength="100" :aria-label="text('name')" :placeholder="text('name')" />
        <ElSelect v-model="folderParent" clearable :aria-label="text('parent')" :placeholder="text('parent')"><ElOption v-for="folder in folders.filter(item => item.id !== editingFolder)" :key="folder.id" :label="folder.name" :value="folder.id" /></ElSelect>
        <ElButton :disabled="busy || !folderName.trim()" @click="act(saveFolder)">{{ text(editingFolder ? 'saveFolder' : 'createFolder') }}</ElButton>
        <ElButton v-if="editingFolder" type="danger" :disabled="busy" @click="act(deleteFolder)">{{ text('deleteFolder') }}</ElButton>
      </fieldset>
      <fieldset v-if="writable" :disabled="busy"><legend>{{ text('organization') }}</legend>
        <ElSelect v-model="targetFolder" clearable :aria-label="text('folder')" :placeholder="text('root')"><ElOption v-for="folder in folders" :key="folder.id" :label="folder.name" :value="folder.id" /></ElSelect>
        <ElInput v-model="tags" :maxlength="1024" :aria-label="text('tags')" :placeholder="text('tags')" />
        <ElInput v-model="responsible" :aria-label="text('responsible')" :placeholder="text('responsible')" />
        <ElButton :disabled="busy || !selected.length" data-testid="knowledge-governance-batch" @click="act((current, signal) => batch(current, signal))">{{ text('apply') }} ({{ selected.length }})</ElButton>
        <ElButton v-if="hasFailures" :disabled="busy" data-testid="knowledge-governance-retry" @click="act((current, signal) => batch(current, signal, true))">{{ text('retry') }}</ElButton>
      </fieldset>
      <ElTable v-if="rows.length" :data="rows" row-key="id" max-height="460" @selection-change="selected = $event">
        <ElTableColumn v-if="writable" type="selection" width="48" />
        <ElTableColumn prop="title" :label="text('name')" min-width="170" />
        <ElTableColumn :label="text('folder')" min-width="130"><template #default="{ row }">{{ folders.find(item => item.id === row.folderId)?.name ?? text('root') }}</template></ElTableColumn>
        <ElTableColumn :label="text('tags')" min-width="180"><template #default="{ row }"><ElTag v-for="tag in row.tags" :key="tag">{{ tag }}</ElTag></template></ElTableColumn>
        <ElTableColumn :label="text('state')" min-width="130"><template #default="{ row }">{{ text((['draft','published','scheduled','expired'] as const).find(key => key === row.publicationState) ?? 'draft') }}</template></ElTableColumn>
        <ElTableColumn v-if="publicationReadable" :label="text('publication')" min-width="150"><template #default="{ row }"><ElButton :disabled="busy" data-testid="knowledge-governance-publication" @click="openPublication(row.id)">{{ text('publication') }}</ElButton></template></ElTableColumn>
      </ElTable><ElEmpty v-else :description="text('empty')" />
      <ElPagination :current-page="page" :page-size="20" :total="total" :disabled="busy" layout="total, prev, pager, next" @current-change="value => { page = value; act(load); }" />
      <ul v-if="results.length" data-testid="knowledge-governance-results"><li v-for="result in results" :key="result.documentId">{{ result.documentId }} · {{ text(result.succeeded ? 'success' : 'failed') }}<span v-if="result.errorCode"> ({{ result.errorCode }})</span></li></ul>
    </section>
  </ArtFormDialog>
  <AiKnowledgePublicationPanel v-model:open="publicationOpen" :knowledge-base-id="knowledgeBaseId" :document="document" @changed="act(load)" />
</template>
<style scoped>
.knowledge-governance { min-width: 0; overflow-wrap: anywhere; }
.knowledge-governance__toolbar, fieldset { display: flex; flex-wrap: wrap; gap: 12px; margin: 16px 0; min-width: 0; }
fieldset { padding: 16px; }
.knowledge-governance__toolbar .el-input, .knowledge-governance__toolbar .el-select, fieldset .el-input, fieldset .el-select { flex: 1 1 180px; min-width: 0; }
.el-tag { margin: 2px; }
</style>
