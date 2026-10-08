<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElForm, ElFormItem, ElOption, ElSelect } from 'element-plus';
import type { ImportExportTaskDetailResponse, StaticImportSchemaDefinition } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import PermissionGate from './PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { isTenantSessionContext } from '../auth/tenant-session-context';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { createImportExportTask, downloadStaticImportTemplate, listStaticImportSchemas } from '../api/import-export-tasks';

defineOptions({ name: 'ImportTaskCreateDialog' });
const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; created: [task: ImportExportTaskDetailResponse] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const schemas = ref<StaticImportSchemaDefinition[]>([]); const schemaKey = ref(''); const worksheetKey = ref('');
const selectedFile = ref<File>(); const fileInput = ref<HTMLInputElement>();
const loading = ref(false); const saving = ref(false); const downloading = ref(false);
const selectedSchema = computed(() => schemas.value.find(value => value.schemaKey === schemaKey.value));
const selectedWorksheet = computed(() => selectedSchema.value?.worksheets.find(value => value.worksheetKey === worksheetKey.value));
const canCreate = () => session.can('import_export.import_tasks.create') && session.can('import_export.static_schemas.read')
  && isTenantSessionContext(session.currentUser);
const scope = useAuthorizedViewScope(session, () => { reset(); emit('update:open', false); }, () => { if (props.open) return loadSchemas(); });
let schemaRequest: ReturnType<typeof scope.begin>; let createRequest: ReturnType<typeof scope.begin>; let downloadRequest: ReturnType<typeof scope.begin>;

function clearFile(): void { selectedFile.value = undefined; if (fileInput.value) fileInput.value.value = ''; }
function reset(): void {
  schemaRequest?.cancel(); createRequest?.cancel(); downloadRequest?.cancel();
  schemas.value = []; schemaKey.value = ''; worksheetKey.value = ''; clearFile();
  loading.value = false; saving.value = false; downloading.value = false;
}
watch(() => props.open, open => { reset(); if (open) void loadSchemas(); }, { flush: 'sync' });
function schemaChanged(): void {
  downloadRequest?.cancel(); downloading.value = false; clearFile();
  worksheetKey.value = selectedSchema.value?.worksheets[0]?.worksheetKey ?? '';
}
function worksheetChanged(): void { downloadRequest?.cancel(); downloading.value = false; clearFile(); }
async function loadSchemas(): Promise<void> {
  if (!props.open || !canCreate()) return;
  schemaRequest?.cancel(); const request = scope.begin('import_export.static_schemas.read'); schemaRequest = request;
  if (!request) return; loading.value = true;
  try {
    const values = await listStaticImportSchemas(request.signal);
    if (!request.current()) return;
    // 目录只是元数据；选择仍须满足可信作用域与该 Schema 的业务权限。
    schemas.value = values.filter(value => value.scopeKey === 'tenant' && session.can(value.requiredPermission) && value.worksheets.length > 0);
    schemaKey.value = schemas.value[0]?.schemaKey ?? ''; schemaChanged();
  } catch (error) {
    if (request.current()) showProblem(error, t('common.loadFailed'));
  } finally { if (request.current()) loading.value = false; request.finish(); }
}
function selectFile(event: Event): void {
  const input = event.target as HTMLInputElement; const file = input.files?.[0]; selectedFile.value = undefined;
  if (!file) return;
  // 客户端只做快速反馈，工作簿结构、解压预算与权限仍由服务端最终验证。
  if (!/\.xlsx$/i.test(file.name) || file.size === 0 || file.size > 1024 * 1024) {
    clearFile(); showWarning(t('importCreate.invalidFile')); return;
  }
  selectedFile.value = file;
}
async function submit(): Promise<void> {
  const schema = selectedSchema.value; const worksheet = selectedWorksheet.value; const file = selectedFile.value;
  if (!props.open || saving.value || downloading.value || !canCreate() || !schema || !session.can(schema.requiredPermission)) return;
  if (!worksheet || !file) { showWarning(t('importCreate.required')); return; }
  const request = scope.begin('import_export.import_tasks.create'); if (!request) return; createRequest = request; saving.value = true;
  try {
    const task = await createImportExportTask(schema.schemaKey, worksheet.worksheetKey, file, request.signal);
    if (!request.current()) return;
    showSuccess(t('importExportTasks.actionSuccess'));
    emit('created', task); emit('update:open', false);
  } catch (error) {
    if (request.current()) showProblem(error, t('common.requestFailed'));
  } finally { if (request.current()) saving.value = false; request.finish(); }
}
async function downloadTemplate(): Promise<void> {
  const schema = selectedSchema.value; const worksheet = selectedWorksheet.value;
  if (!props.open || saving.value || downloading.value || !canCreate() || !schema || !worksheet || !session.can(schema.requiredPermission)) return;
  const request = scope.begin('import_export.static_schemas.read'); if (!request) return; downloadRequest = request; downloading.value = true;
  try {
    const blob = await downloadStaticImportTemplate(schema.schemaKey, worksheet.worksheetKey, request.signal);
    if (!request.current()) return;
    const url = URL.createObjectURL(blob);
    try {
      const anchor = document.createElement('a'); anchor.href = url;
      anchor.download = `${schema.schemaKey.replaceAll('.', '-')}-${worksheet.worksheetKey}-template.xlsx`; anchor.click();
    } finally { URL.revokeObjectURL(url); }
  } catch (error) {
    if (request.current()) showProblem(error, t('common.requestFailed'));
  } finally { if (request.current()) downloading.value = false; request.finish(); }
}
</script>

<template>
  <ArtFormDialog :open="open" :title="t('importExportTasks.addTask')" :saving="saving" :show-confirm="canCreate()"
    :confirm-label="t('importCreate.submit')" :cancel-label="t('common.cancel')" confirm-test-id="import-create-submit"
    @update:open="emit('update:open', $event)" @confirm="submit">
    <ElForm label-width="120px">
      <ElFormItem :label="t('importExportTasks.schema')" required>
        <ElSelect v-model="schemaKey" data-testid="import-create-schema" :loading="loading" :disabled="saving || downloading" @change="schemaChanged">
          <ElOption v-for="schema in schemas" :key="schema.schemaKey" :label="schema.displayName" :value="schema.schemaKey" />
        </ElSelect>
      </ElFormItem>
      <ElFormItem :label="t('importExportTasks.worksheet')" required>
        <ElSelect v-model="worksheetKey" data-testid="import-create-worksheet" :disabled="saving || downloading" @change="worksheetChanged">
          <ElOption v-for="worksheet in selectedSchema?.worksheets ?? []" :key="worksheet.worksheetKey" :label="worksheet.displayName" :value="worksheet.worksheetKey" />
        </ElSelect>
      </ElFormItem>
      <ElFormItem v-if="selectedWorksheet" :label="t('importCreate.columns')">
        <span>{{ selectedWorksheet.headerColumns.join('、') }}</span>
      </ElFormItem>
      <ElFormItem :label="t('importCreate.file')" required>
        <input ref="fileInput" type="file" accept=".xlsx" data-testid="import-create-file"
          :aria-label="t('importCreate.file')" :disabled="saving || downloading" @change="selectFile">
      </ElFormItem>
      <ElAlert :title="t('importCreate.fileHint')" type="info" :closable="false" />
      <ElAlert v-if="!loading && schemas.length === 0" :title="t('importCreate.noSchemas')" type="warning" :closable="false" />
      <PermissionGate code="import_export.static_schemas.read">
        <ElButton data-testid="import-create-template" :loading="downloading" :disabled="!selectedWorksheet || saving" @click="downloadTemplate">
          {{ t('users.importTemplate') }}
        </ElButton>
      </PermissionGate>
    </ElForm>
  </ArtFormDialog>
</template>
