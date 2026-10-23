<script setup lang="ts">
import { translateKnowledgeFormats, translateKnowledgeIndexChanges, translateKnowledgeDocx, translateKnowledgeProcessing, translateKnowledgeRecovery } from '@fullnet/admin-i18n';
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { ElAlert, ElButton, ElCard, ElEmpty, ElForm, ElFormItem, ElInput, ElMessageBox, ElPagination, ElSwitch, ElTable, ElTableColumn, ElTag } from 'element-plus';
import { knowledgeFileAccept, knowledgeFileContentTypes } from '@fullnet/client-contracts';
import type { AiKnowledgeBaseResponse, AiKnowledgeDocumentResponse, AiKnowledgeImportResponse, AiKnowledgeVersionResponse } from '@fullnet/client-contracts';
import * as api from '../api/ai-knowledge';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import { ART_TABLE_ACTION_COLUMN_WIDTH } from '../framework/art-design/components/artTableActions';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import AiKnowledgeBaseSelect from './AiKnowledgeBaseSelect.vue';
import AiKnowledgeSourceDialog from './AiKnowledgeSourceDialog.vue';
import AiKnowledgePolicyDialog from './AiKnowledgePolicyDialog.vue';
import AiKnowledgeGrantsDialog from './AiKnowledgeGrantsDialog.vue';
import AiKnowledgeIndexDialog from './AiKnowledgeIndexDialog.vue';
import AiKnowledgeIndexChangesDialog from './AiKnowledgeIndexChangesDialog.vue';
import AiKnowledgeIndexGrantPanel from './AiKnowledgeIndexGrantPanel.vue';
const props = defineProps<{ documents?: boolean }>();
const { t, locale } = useAdminI18n(); const session = useSessionStore(); const route = useRoute();
type Row = AiKnowledgeBaseResponse | AiKnowledgeDocumentResponse;
const rows = ref<Row[]>([]); const loading = ref(false); const busy = ref(false); const page = ref(1); const total = ref(0);
const baseId = ref(typeof route.query.knowledgeBaseId === 'string' ? route.query.knowledgeBaseId : '');
const editorOpen = ref(false); const editing = ref<Row>(); const form = reactive({ name: '', description: '', isEnabled: true });
const versionOpen = ref(false); const documentRow = ref<AiKnowledgeDocumentResponse>(); const versions = ref<AiKnowledgeVersionResponse[]>([]);
const versionPage = ref(1); const versionTotal = ref(0); const versionsLoading = ref(false);
const file = ref<File>();
const parseOptions = reactive({ encoding: 'utf8', delimiter: 'comma', header: false, hidden: false });
const fileExtension = computed(() => file.value?.name.split('.').at(-1)?.toLowerCase());
const parsingPolicy = computed(() => fileExtension.value === 'csv' ? `csv_${parseOptions.encoding}_${parseOptions.delimiter}_${parseOptions.header ? 'header' : 'plain'}`
  : ['html', 'htm'].includes(fileExtension.value ?? '') && parseOptions.encoding === 'gb18030' ? 'html_gb18030'
  : ['xls', 'xlsx', 'ppt', 'pptx'].includes(fileExtension.value ?? '') && parseOptions.hidden ? 'include_hidden' : 'default');
watch(parseOptions, () => { uploadKey = ''; uploadVersion = 0; }); let uploadKey = ''; let uploadVersion = 0; const importOperation = ref<AiKnowledgeImportResponse>(); const operationId = ref('');
const sourceOpen = ref(false); const sourceVersion = ref<AiKnowledgeVersionResponse>();
const policyOpen = ref(false); const grantsOpen = ref(false); const accessRow = ref<Row>();
const indexOpen = ref(false); const indexVersion = ref<AiKnowledgeVersionResponse>();
const { tableMainRef, tableHeight, tableHeaderCellStyle, syncTableLayout } = useArtPagedTableInCard(loading);
const prefix = computed(() => props.documents ? 'ai.knowledge_documents' : 'ai.knowledge_bases');
const canRead = computed(() => !session.switching && session.can('ai.knowledge_bases.read') && (!props.documents || session.can('ai.knowledge_documents.read')));
const can = (action: string) => canRead.value && session.can(`${prefix.value}.${action}`);
const canVersion = computed(() => canRead.value && session.can('ai.knowledge_documents.versions_read'));
const changesOpen = ref(false);
const canChanges = computed(() => props.documents && canVersion.value && session.can('ai.knowledge_documents.index_read') && session.can('ai.knowledge_documents.index_changes_read'));
const canRecover = computed(() => props.documents && canVersion.value && session.can('ai.knowledge_documents.index_read') && session.can('ai.knowledge_documents.index_recover'));
const recoveryCursor = ref<string>(); const recoveryReceipt = ref(''); let recoveryController: AbortController | undefined;
const canGrants = computed(() => ['members_read', 'roles_read', 'organizations_read'].some(action => can(action)));
function openAccess(id: string, policy = false) {
  const row = rows.value.find(item => item.id === id);
  if (!row || busy.value || (policy ? props.documents || !can('policy_update') : !canGrants.value)) return;
  accessRow.value = row; if (policy) policyOpen.value = true; else grantsOpen.value = true;
}
let epoch = 0; let listEpoch = 0; let versionEpoch = 0; let controller: AbortController | undefined; let versionController: AbortController | undefined;
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
async function load(next = page.value) {
  controller?.abort(); const current = ++listEpoch; const scope = epoch; rows.value = []; total.value = 0;
  if (!canRead.value || (props.documents && !guid.test(baseId.value))) { loading.value = false; return; }
  controller = new AbortController(); loading.value = true;
  try {
    const result = props.documents ? await api.listKnowledgeDocuments({ knowledgeBaseId: baseId.value, page: next, pageSize: 20 }, controller.signal)
      : await api.listKnowledgeBases({ page: next, pageSize: 20 }, controller.signal);
    if (current !== listEpoch || scope !== epoch) return;
    rows.value = result.items; total.value = result.total; page.value = next;
  } catch (error) { if (current === listEpoch && scope === epoch) showProblem(error, t('common.loadFailed')); }
  finally { if (current === listEpoch && scope === epoch) { loading.value = false; void syncTableLayout(); } }
}
function openEditor(id?: string) {
  const row = id ? rows.value.find(item => item.id === id) : undefined;
  if (id && !row) return;
  if (busy.value || !can(row ? 'update' : 'create')) return;
  editing.value = row; form.name = row ? ('name' in row ? row.name : row.title) : ''; form.description = row?.description ?? '';
  form.isEnabled = row && 'isEnabled' in row ? row.isEnabled : true; editorOpen.value = true;
}
async function save() {
  if (busy.value || !can(editing.value ? 'update' : 'create') || (props.documents && !guid.test(baseId.value))) return;
  if (!form.name.trim()) { showWarning(t('knowledge.required')); return; }
  busy.value = true; const scope = epoch; const row = editing.value; const knowledgeBaseId = baseId.value;
  try {
    const description = form.description.trim() || null;
    if (props.documents) {
      const body = { title: form.name.trim(), description };
      if (row) await api.updateKnowledgeDocument({ knowledgeBaseId, documentId: row.id, body: { ...body, version: row.version } });
      else await api.createKnowledgeDocument({ knowledgeBaseId, body });
    } else {
      const body = { name: form.name.trim(), description };
      if (row) await api.updateKnowledgeBase({ knowledgeBaseId: row.id, body: { ...body, isEnabled: form.isEnabled, version: row.version } });
      else await api.createKnowledgeBase({ body });
    }
    if (scope !== epoch) return;
    editorOpen.value = false; showSuccess(t('common.saved')); await load();
  } catch (error) { if (scope === epoch) showProblem(error, t('common.saveFailed')); }
  finally { if (scope === epoch) busy.value = false; }
}
async function remove(id: string) {
  const row = rows.value.find(item => item.id === id);
  if (!row) return;
  if (busy.value || !props.documents || !can('delete')) return;
  const scope = epoch; busy.value = true;
  try {
    await ElMessageBox.confirm(t('knowledge.deleteConfirm'), { type: 'warning' });
    // 弹窗期间可能发生会话切换或撤权，提交前再检查当前权限和作用域。
    if (scope !== epoch || !can('delete')) return;
    await api.deleteKnowledgeDocument({ knowledgeBaseId: baseId.value, documentId: row.id, body: { version: row.version } });
    if (scope !== epoch) return;
    sourceOpen.value = false; await load();
  } catch (error) { if (scope === epoch && error !== 'cancel' && error !== 'close') showProblem(error, t('common.requestFailed')); }
  finally { if (scope === epoch) busy.value = false; }
}
async function loadVersions(next = 1) {
  versionController?.abort(); const current = ++versionEpoch; const scope = epoch; versions.value = []; versionTotal.value = 0;
  if (!documentRow.value || !canVersion.value || !versionOpen.value) return;
  versionController = new AbortController(); versionsLoading.value = true;
  try {
    const result = await api.listKnowledgeVersions({ knowledgeBaseId: baseId.value, documentId: documentRow.value.id, page: next, pageSize: 20 }, versionController.signal);
    if (current !== versionEpoch || scope !== epoch || !versionOpen.value) return;
    versions.value = result.items; versionPage.value = next; versionTotal.value = result.total;
  } catch (error) { if (current === versionEpoch && scope === epoch) showProblem(error, t('common.loadFailed')); }
  finally { if (current === versionEpoch && scope === epoch) versionsLoading.value = false; }
}
function openVersions(id: string) {
  const row = rows.value.find(item => item.id === id);
  if (!row || !('knowledgeBaseId' in row)) return;
  if (busy.value || !canVersion.value) return;
  documentRow.value = row; file.value = undefined; uploadKey = ''; importOperation.value = undefined; operationId.value = ''; versionOpen.value = true; void loadVersions();
}
function chooseFile(event: Event) {
  if (busy.value) return;
  const target = event.target; file.value = target instanceof HTMLInputElement ? target.files?.[0] : undefined;
  uploadKey = ''; uploadVersion = 0;
}
async function upload() {
  if (busy.value || !documentRow.value || !file.value || !canRead.value || !session.can('ai.knowledge_documents.upload')) return;
  const selected = file.value; const extension = selected.name.split('.').at(-1)?.toLowerCase();
  const contentType = knowledgeFileContentTypes[extension ?? ''];
  if (!contentType || selected.size <= 0 || selected.size > (extension === 'docx' ? 5 : 8) * 1024 * 1024) { showWarning(extension === 'docx' ? translateKnowledgeDocx(locale.value, 'fileInvalid') : t('knowledge.fileInvalid')); return; }
  // 网络结果不明时保留原请求键和原版本；只允许用户显式重试原上传。
  if (!uploadKey) { uploadKey = crypto.randomUUID(); uploadVersion = documentRow.value.version; }
  const scope = epoch; busy.value = true;
  try {
    const result = await api.uploadKnowledgeVersion({ knowledgeBaseId: baseId.value, documentId: documentRow.value.id, version: uploadVersion, requestKey: uploadKey, fileName: selected.name, contentType, parsingPolicy: parsingPolicy.value, body: selected });
    if (scope !== epoch || !versionOpen.value) return;
    importOperation.value = result; operationId.value = result.id; file.value = undefined; uploadKey = '';
    const refreshedDocument = await api.getKnowledgeDocument({ knowledgeBaseId: baseId.value, documentId: documentRow.value.id });
    if (scope !== epoch) return;
    documentRow.value = refreshedDocument;
    await loadVersions(); await load(); showSuccess(t('common.saved'));
  } catch (error) { if (scope === epoch) showProblem(error, t('common.requestFailed')); }
  finally { if (scope === epoch) busy.value = false; }
}
async function refreshImport(retry = false) {
  if (busy.value || !documentRow.value || !guid.test(operationId.value) || !canRead.value
    || !session.can('ai.knowledge_documents.imports_read') || (retry && !session.can('ai.knowledge_documents.imports_retry'))) return;
  const scope = epoch; const parameters = { knowledgeBaseId: baseId.value, documentId: documentRow.value.id, operationId: operationId.value }; busy.value = true;
  importOperation.value = undefined;
  try {
    const result = retry ? await api.retryKnowledgeImport(parameters) : await api.getKnowledgeImport(parameters);
    if (scope !== epoch || !versionOpen.value) return;
    importOperation.value = result; await loadVersions();
  } catch (error) { if (scope === epoch) showProblem(error, t('common.requestFailed')); }
  finally { if (scope === epoch) busy.value = false; }
}
function openSource(id: string) {
  const row = versions.value.find(item => item.id === id);
  if (!row) return;
  if (!session.can('ai.knowledge_documents.source_read') || !canRead.value) return;
  sourceVersion.value = row; sourceOpen.value = true;
}
function openIndex(id: string) {
  const row = versions.value.find(item => item.id === id);
  if (!row || busy.value || !canRead.value || !session.can('ai.knowledge_documents.index_read')) return;
  indexVersion.value = row; indexOpen.value = true;
}
async function recoverSources() {
  if (!canRecover.value || !guid.test(baseId.value) || busy.value) return;
  const scope = epoch; const selectedBase = baseId.value; busy.value = true;
  recoveryController = new AbortController();
  try {
    const receipt = await api.recoverKnowledgeIndexChanges({ knowledgeBaseId: selectedBase,
      body: { afterDocumentId: recoveryCursor.value ?? null, pageSize: 50 } }, recoveryController.signal);
    if (scope !== epoch || selectedBase !== baseId.value || !canRecover.value) return;
    recoveryCursor.value = receipt.nextDocumentId ?? undefined;
    recoveryReceipt.value = recoveryCursor.value ? 'recoveryPageDone' : 'recoveryDone';
    showSuccess(translateKnowledgeRecovery(locale.value, recoveryReceipt.value));
  } catch (error) {
    if (scope === epoch && canRecover.value) {
      // 部分页可能已经提交；失败后从头幂等重扫，不能凭旧游标假定整页成功。
      recoveryCursor.value = undefined;
      showProblem(error, t('common.loadFailed'));
    }
  } finally { if (scope === epoch) busy.value = false; }
}
function reset() {
  changesOpen.value = false;
  ++epoch; ++listEpoch; ++versionEpoch; controller?.abort(); versionController?.abort(); rows.value = []; versions.value = [];
  recoveryController?.abort(); recoveryCursor.value = undefined; recoveryReceipt.value = '';
  editorOpen.value = false; versionOpen.value = false; sourceOpen.value = false; editing.value = undefined; documentRow.value = undefined;
  policyOpen.value = false; grantsOpen.value = false; accessRow.value = undefined;
  indexOpen.value = false; indexVersion.value = undefined;
  sourceVersion.value = undefined; importOperation.value = undefined; file.value = undefined; uploadKey = ''; operationId.value = ''; total.value = 0; loading.value = false; busy.value = false; versionsLoading.value = false;
}
watch(() => route.query.knowledgeBaseId, value => { baseId.value = typeof value === 'string' ? value : ''; });
watch(baseId, () => { reset(); void load(1); });
watch(() => [session.currentUser, session.switching], () => { reset(); baseId.value = ''; void load(1); }, { deep: true });
onMounted(() => void load(1)); onUnmounted(reset);
</script>
<template>
  <section class="art-page-stack art-full-height" :aria-busy="loading || busy">
    <AiKnowledgePolicyDialog v-if="accessRow && !documents" v-model:open="policyOpen" :knowledge-base-id="accessRow.id" @saved="load()" />
    <AiKnowledgeGrantsDialog v-if="accessRow" v-model:open="grantsOpen" :knowledge-base-id="'knowledgeBaseId' in accessRow ? accessRow.knowledgeBaseId : accessRow.id" :document-id="documents ? accessRow.id : undefined" @saved="load()" />
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t(documents ? 'knowledge.documents' : 'knowledge.title') }}</h1>
    <el-alert v-if="!canRead" :title="t('knowledge.noPermission')" type="warning" :closable="false" />
    <el-card v-else shadow="never" class="art-table-card">
      <div ref="tableMainRef" class="art-crud-table-main">
        <div class="knowledge-toolbar">
          <AiKnowledgeBaseSelect v-if="documents" v-model="baseId" :disabled="busy" />
          <el-button :disabled="loading || busy" @click="load()">{{ t('common.refresh') }}</el-button>
          <el-button v-if="canChanges" data-testid="knowledge-changes" :disabled="busy || !guid.test(baseId)" @click="changesOpen = true">{{ translateKnowledgeIndexChanges(locale, 'title') }}</el-button>
          <el-button v-if="canRecover" data-testid="knowledge-recover" :disabled="busy || !baseId" @click="recoverSources">{{ translateKnowledgeRecovery(locale, recoveryCursor ? 'continueRecovery' : 'recover') }}</el-button>
          <el-button v-if="can('create')" type="primary" data-testid="knowledge-create" :disabled="busy || (documents && !baseId)" @click="openEditor()">{{ t('knowledge.create') }}</el-button>
        </div>
        <el-alert v-if="canRecover" :title="translateKnowledgeRecovery(locale, recoveryReceipt || 'recoveryHint')" type="info" :closable="false" />
        <el-table :data="rows" :height="tableHeight" :header-cell-style="tableHeaderCellStyle" :empty-text="t('knowledge.empty')" row-key="id" border stripe>
          <el-table-column :prop="documents ? 'title' : 'name'" :label="t('knowledge.name')" min-width="220" />
          <el-table-column prop="description" :label="t('knowledge.description')" min-width="260" show-overflow-tooltip />
          <el-table-column v-if="!documents" :label="t('knowledge.state')" width="110"><template #default="{ row }"><el-tag :type="row.isEnabled ? 'success' : 'info'">{{ t(row.isEnabled ? 'knowledge.enabled' : 'knowledge.disabled') }}</el-tag></template></el-table-column>
          <el-table-column v-else prop="status" :label="t('knowledge.state')" width="150" />
          <el-table-column :label="t('knowledge.actions')" :width="ART_TABLE_ACTION_COLUMN_WIDTH" fixed="right">
            <template #default="{ row }"><ArtTableActionGroup>
              <ArtTableActionButton v-if="can('update')" type="edit" :title="t('knowledge.edit')" test-id="knowledge-edit" @click="openEditor(row.id)" />
              <ArtTableActionButton v-if="!documents && can('policy_update')" type="edit" :title="t('knowledge.policy')" test-id="knowledge-policy" @click="openAccess(row.id, true)" />
              <ArtTableActionButton v-if="canGrants" type="view" :title="t('knowledge.grants')" test-id="knowledge-grants" @click="openAccess(row.id)" />
              <ArtTableActionButton v-if="documents && canVersion" type="view" :title="t('knowledge.versions')" test-id="knowledge-versions" @click="openVersions(row.id)" />
              <router-link v-if="!documents && session.can('ai.knowledge_documents.read')" :to="{ name: 'ai-knowledge-documents', query: { knowledgeBaseId: row.id } }"><ArtTableActionButton type="view" :title="t('knowledge.documents')" /></router-link>
              <ArtTableActionButton v-if="documents && can('delete')" type="delete" :title="t('knowledge.delete')" test-id="knowledge-delete" @click="remove(row.id)" />
            </ArtTableActionGroup></template>
          </el-table-column>
        </el-table>
        <el-pagination class="art-table-pagination" :current-page="page" :page-size="20" :total="total" :disabled="loading || busy" layout="total, prev, pager, next" @current-change="load($event)" />
      </div>
    </el-card>
    <ArtFormDialog :open="editorOpen" :title="t(editing ? 'knowledge.edit' : 'knowledge.create')" :saving="busy" :confirm-label="t('knowledge.save')" :cancel-label="t('common.cancel')" confirm-test-id="knowledge-save" @update:open="value => { if (!busy) editorOpen = value; }" @confirm="save">
      <el-form label-position="top" @submit.prevent="save">
        <el-form-item :label="t('knowledge.name')" required><el-input v-model="form.name" :maxlength="200" :disabled="busy" data-testid="knowledge-name" /></el-form-item>
        <el-form-item :label="t('knowledge.description')"><el-input v-model="form.description" type="textarea" :maxlength="2000" :disabled="busy" /></el-form-item>
        <el-form-item v-if="editing && !documents" :label="t('knowledge.enabled')"><el-switch v-model="form.isEnabled" :disabled="busy" /></el-form-item>
      </el-form>
    </ArtFormDialog>
    <ArtFormDialog :open="versionOpen" :title="t('knowledge.versions')" width="860px" :show-confirm="false" :cancel-label="t('common.cancel')" @update:open="value => { if (!busy) { versionOpen = value; if (!value) { ++versionEpoch; versionController?.abort(); versions = []; sourceOpen = false; indexOpen = false; } } }">
      <template v-if="documentRow">
        <p>{{ documentRow.title }}</p><p>{{ translateKnowledgeFormats(locale, 'fileHint') }}</p>
        <div v-if="session.can('ai.knowledge_documents.upload')" class="knowledge-toolbar">
          <label>{{ t('knowledge.file') }} <input type="file" :accept="knowledgeFileAccept" :disabled="busy" data-testid="knowledge-file" @change="chooseFile" /></label>
          <el-button :disabled="!file || busy" :loading="busy" data-testid="knowledge-upload" @click="upload">{{ t('knowledge.upload') }}</el-button>
        </div>
        <fieldset v-if="file" :disabled="busy" class="knowledge-parser-options">
          <legend>{{ translateKnowledgeFormats(locale, 'policyHint') }}</legend>
          <label v-if="['csv', 'html', 'htm'].includes(fileExtension ?? '')">{{ translateKnowledgeFormats(locale, 'encoding') }}
            <select v-model="parseOptions.encoding" data-testid="knowledge-encoding"><option value="utf8">UTF-8</option><option value="gb18030">GB 18030</option></select>
          </label>
          <template v-if="fileExtension === 'csv'">
            <label>{{ translateKnowledgeFormats(locale, 'delimiter') }} <select v-model="parseOptions.delimiter" data-testid="knowledge-delimiter">
              <option v-for="value in ['comma', 'tab', 'semicolon'] as const" :key="value" :value="value">{{ translateKnowledgeFormats(locale, value) }}</option>
            </select></label>
            <label><input v-model="parseOptions.header" type="checkbox" data-testid="knowledge-csv-header" /> {{ translateKnowledgeFormats(locale, 'header') }}</label>
          </template>
          <label v-if="['xls', 'xlsx', 'ppt', 'pptx'].includes(fileExtension ?? '')"><input v-model="parseOptions.hidden" type="checkbox" data-testid="knowledge-hidden" /> {{ translateKnowledgeFormats(locale, 'hidden') }}</label>
        </fieldset>
        <el-table :data="versions" max-height="320" :empty-text="t('knowledge.empty')" row-key="id">
          <el-table-column prop="versionNumber" :label="t('knowledge.version')" width="90" />
          <el-table-column prop="originalFileName" :label="t('knowledge.file')" min-width="230" />
          <el-table-column :label="t('knowledge.state')" width="190"><template #default="{ row }">{{ translateKnowledgeProcessing(locale, row.processingStatus) }}</template></el-table-column>
          <el-table-column v-if="session.can('ai.knowledge_documents.source_read') || session.can('ai.knowledge_documents.index_read')" :label="t('knowledge.actions')" :width="ART_TABLE_ACTION_COLUMN_WIDTH"><template #default="{ row }"><ArtTableActionGroup>
            <ArtTableActionButton v-if="session.can('ai.knowledge_documents.source_read')" type="view" :title="t('knowledge.source')" test-id="knowledge-source" @click="openSource(row.id)" />
            <ArtTableActionButton v-if="session.can('ai.knowledge_documents.index_read')" type="view" :title="t('knowledge.index')" test-id="knowledge-index" @click="openIndex(row.id)" />
          </ArtTableActionGroup></template></el-table-column>
        </el-table>
        <el-pagination :current-page="versionPage" :page-size="20" :total="versionTotal" :disabled="versionsLoading || busy" layout="total, prev, pager, next" @current-change="loadVersions($event)" />
        <section v-if="session.can('ai.knowledge_documents.imports_read')" class="knowledge-import">
          <h3>{{ t('knowledge.import') }}</h3><p>{{ t('knowledge.operationHint') }}</p>
          <el-input v-model="operationId" :placeholder="t('knowledge.operationId')" :aria-label="t('knowledge.operationId')" :disabled="busy" maxlength="36" @update:model-value="importOperation = undefined" />
          <div class="knowledge-toolbar">
            <el-button :disabled="busy || !guid.test(operationId)" data-testid="knowledge-import-refresh" @click="refreshImport()">{{ t('common.refresh') }}</el-button>
            <el-button v-if="session.can('ai.knowledge_documents.imports_retry')" :disabled="busy || !importOperation?.failureCode" data-testid="knowledge-import-retry" @click="refreshImport(true)">{{ t('knowledge.retry') }}</el-button>
          </div>
          <dl v-if="importOperation"><dt>{{ t('knowledge.operationId') }}</dt><dd>{{ importOperation.id }}</dd><dt>{{ t('knowledge.state') }}</dt><dd>{{ importOperation.state }}</dd><dt v-if="importOperation.failureCode">{{ t('knowledge.failure') }}</dt><dd v-if="importOperation.failureCode">{{ importOperation.failureCode }}</dd></dl>
        </section>
      </template><el-empty v-else :description="t('knowledge.empty')" />
    </ArtFormDialog>
    <AiKnowledgeIndexChangesDialog v-if="documents && canChanges" v-model:open="changesOpen" :knowledge-base-id="baseId" />
    <AiKnowledgeIndexGrantPanel v-if="documents && baseId" :knowledge-base-id="baseId" />
    <AiKnowledgeSourceDialog v-if="sourceVersion && documentRow" v-model:open="sourceOpen" :knowledge-base-id="baseId" :document-id="documentRow.id" :version-id="sourceVersion.id" :version="sourceVersion" />
    <AiKnowledgeIndexDialog v-if="indexVersion && documentRow" v-model:open="indexOpen" :knowledge-base-id="documentRow.knowledgeBaseId" :document-id="documentRow.id" :version-id="indexVersion.id" />
  </section>
</template>
<style scoped>
.knowledge-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-bottom: 16px; }
.knowledge-import { margin-top: 24px; padding-top: 16px; border-top: 1px solid var(--el-border-color); }
.knowledge-import .knowledge-toolbar { margin-top: 12px; } dd { margin: 4px 0 12px; overflow-wrap: anywhere; }
</style>
