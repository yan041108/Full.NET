<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElForm, ElFormItem, ElOption, ElSelect } from 'element-plus';
import { isFullNetProblemDetails, isGuid, type FullNetProblemDetails, type ImportExportTaskDetailResponse, type StaticImportSchemaDefinition } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import PermissionGate from './PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { isTenantSessionContext } from '../auth/tenant-session-context';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { createImportExportTask, downloadStaticImportTemplate, listStaticImportSchemas } from '../api/import-export-tasks';

defineOptions({ name: 'ImportTaskCreateDialog' });
const props = defineProps<{ open: boolean; schemaKey?: string }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; created: [task: ImportExportTaskDetailResponse] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const schemas = ref<StaticImportSchemaDefinition[]>([]); const schemaKey = ref(''); const worksheetKey = ref('');
const selectedFile = ref<File>(); const fileInput = ref<HTMLInputElement>();
const loading = ref(false); const saving = ref(false); const downloading = ref(false);
const active = ref(false), closed = ref(!props.open), catalogProblem = ref<FullNetProblemDetails>();
onMounted(() => { active.value = true; }); onActivated(() => { active.value = true; });
onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; });
const busy = computed(() => loading.value || saving.value || downloading.value);
const selectedSchema = computed(() => schemas.value.find(value => value.schemaKey === schemaKey.value));
const selectedWorksheet = computed(() => selectedSchema.value?.worksheets.find(value => value.worksheetKey === worksheetKey.value));
const canCreate = () => active.value && props.open && !closed.value && session.can('import_export.import_tasks.create') && session.can('import_export.static_schemas.read')
  && isTenantSessionContext(session.currentUser);
let receiptSequence = 0, controlSequence = 0, recoverySequence = 0;
const receipt = ref<{ sequence: number; schemaKey: string; worksheetKey: string; fileName: string; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed'; taskId?: string; errorCode?: string }>();
const acknowledged = ref<number>();
const requiresReview = computed(() => receipt.value?.outcome === 'unconfirmed' && acknowledged.value !== receipt.value.sequence);
let dispatched = false;
const recoveryConfirmation = ref<{ token: number; confirm: () => void; update: (open: boolean) => void }>();
interface Controls { token: number; schema: (value: string) => void; worksheet: (value: string) => void; file: (event: Event) => void; submit: () => void; download: () => void; refresh: () => void; close: (open: boolean) => void; acknowledge: () => void }
const controls = ref<Controls>(makeControls());
function makeControls(): Controls {
  const token = ++controlSequence, owned = () => controls.value.token === token && canCreate();
  return { token, schema: value => { if (owned() && !busy.value) schemaKey.value = value; }, worksheet: value => { if (owned() && !busy.value) worksheetKey.value = value; },
    file: event => { if (owned() && !busy.value) selectFile(event); }, submit: () => { if (owned()) void submit(); }, download: () => { if (owned()) void downloadTemplate(); },
    refresh: () => { if (owned() && !busy.value) void loadSchemas(); }, close: open => { if (!open && owned()) changeOpen(false); }, acknowledge: () => { if (owned()) acknowledgeUnknown(); } };
}
const scope = useAuthorizedViewScope(session, () => { closed.value = true; reset(); receipt.value = undefined; acknowledged.value = undefined; emit('update:open', false); }, () => { if (props.open) return loadSchemas(); });
let schemaRequest: ReturnType<typeof scope.begin>; let createRequest: ReturnType<typeof scope.begin>; let downloadRequest: ReturnType<typeof scope.begin>;

function clearFile(): void { selectedFile.value = undefined; if (fileInput.value) fileInput.value.value = ''; }
function cancelSelection(): void {
  // 浏览器取消不能撤销已到服务端的创建，关闭或换选择后仍须保留未知事实。
  if (dispatched && createRequest?.current() && receipt.value?.outcome === 'processing') receipt.value.outcome = 'unconfirmed';
  createRequest?.cancel(); downloadRequest?.cancel(); dispatched = false; saving.value = false; downloading.value = false;
  recoveryConfirmation.value = undefined; controls.value = makeControls(); clearFile();
}
function reset(): void {
  cancelSelection(); schemaRequest?.cancel();
  schemas.value = []; schemaKey.value = ''; worksheetKey.value = ''; clearFile();
  loading.value = false; catalogProblem.value = undefined;
}
function changeOpen(open: boolean): void {
  // 关闭意图先取消本组件请求，不能等父组件下一轮更新属性后才阻断迟到结果。
  if (!open) { closed.value = true; reset(); }
  emit('update:open', open);
}
// 业务入口切换时同步撤销旧文件、目录、下载和上传，不能把前一业务的完成交给新入口。
watch(() => props.open, open => { reset(); closed.value = !open; if (open) void loadSchemas(); }, { flush: 'sync' });
watch(() => props.schemaKey, () => { reset(); if (canCreate()) void loadSchemas(); }, { flush: 'sync' });
watch(schemaKey, () => { worksheetKey.value = selectedSchema.value?.worksheets[0]?.worksheetKey ?? ''; }, { flush: 'sync' });
const selectionSignature = computed(() => JSON.stringify([schemaKey.value, worksheetKey.value, selectedSchema.value?.scopeKey, selectedSchema.value?.requiredPermission, selectedWorksheet.value]));
watch(selectionSignature, cancelSelection, { flush: 'sync' });
async function loadSchemas(): Promise<void> {
  if (!canCreate() || saving.value || downloading.value) return;
  schemaRequest?.cancel(); const request = scope.begin('import_export.static_schemas.read'); schemaRequest = request;
  if (!request) return; loading.value = true; catalogProblem.value = undefined;
  try {
    const values = await listStaticImportSchemas(request.signal);
    if (!request.current()) return;
    // 目录只是元数据；选择仍须满足可信作用域与该 Schema 的业务权限。
    const previousSchema = schemaKey.value, previousWorksheet = worksheetKey.value;
    schemas.value = values.filter(value => value.scopeKey === 'tenant' && session.can(value.requiredPermission) && value.worksheets.length > 0
      && (props.schemaKey === undefined || value.schemaKey === props.schemaKey));
    schemaKey.value = schemas.value.find(value => value.schemaKey === previousSchema)?.schemaKey ?? schemas.value[0]?.schemaKey ?? '';
    worksheetKey.value = selectedSchema.value?.worksheets.find(value => value.worksheetKey === previousWorksheet)?.worksheetKey ?? selectedSchema.value?.worksheets[0]?.worksheetKey ?? '';
  } catch (error) {
    if (request.current()) catalogProblem.value = toProblem(error);
  } finally { if (request.current()) loading.value = false; request.finish(); }
}
function selectFile(event: Event): void {
  if (!canCreate() || busy.value) return;
  const input = event.target as HTMLInputElement; const file = input.files?.[0]; controls.value = makeControls(); clearFile();
  if (!file) return;
  // 客户端只做快速反馈，工作簿结构、解压预算与权限仍由服务端最终验证。
  if (!/\.xlsx$/i.test(file.name) || file.size === 0 || file.size > 1024 * 1024) {
    clearFile(); showWarning(t('importCreate.invalidFile')); return;
  }
  selectedFile.value = file;
}
async function submit(): Promise<void> {
  const schema = selectedSchema.value; const worksheet = selectedWorksheet.value; const file = selectedFile.value;
  if (busy.value || requiresReview.value || !canCreate() || !schema || schema.scopeKey !== 'tenant' || !session.can(schema.requiredPermission)) return;
  if (!worksheet || !file) { showWarning(t('importCreate.required')); return; }
  const request = scope.begin('import_export.import_tasks.create'); if (!request) return; createRequest = request; saving.value = true;
  const target = { schemaKey: schema.schemaKey, worksheetKey: worksheet.worksheetKey, file }, signature = selectionSignature.value;
  receipt.value = { sequence: ++receiptSequence, schemaKey: target.schemaKey, worksheetKey: target.worksheetKey, fileName: file.name, outcome: 'processing' }; acknowledged.value = undefined; dispatched = true;
  try {
    const task = await createImportExportTask(target.schemaKey, target.worksheetKey, target.file, request.signal);
    if (!request.current()) return;
    if (!canCreate() || signature !== selectionSignature.value || !isGuid(task.id) || task.id === '00000000-0000-0000-0000-000000000000'
      || task.tenantId.toLowerCase() !== session.currentUser?.tenantId?.toLowerCase() || task.requestedByUserId.toLowerCase() !== session.currentUser?.id.toLowerCase()
      || task.schemaKey !== target.schemaKey || task.worksheetKey !== target.worksheetKey) throw new Error('client.invalid_import_export_task_identity');
    dispatched = false; receipt.value = { ...receipt.value!, outcome: 'succeeded', taskId: task.id };
    showSuccess(t('importExportTasks.actionSuccess'));
    // 先交付创建事实，再本地关闭；父组件延迟更新 open 也不能再次提交。
    emit('created', task); changeOpen(false);
  } catch (error) {
    if (request.current()) {
      const failure = toProblem(error), unknown = !isFullNetProblemDetails(error) || failure.status < 400 || failure.status >= 500 || failure.status === 408;
      receipt.value = { ...receipt.value!, outcome: unknown ? 'unconfirmed' : 'failed', errorCode: failure.code }; showProblem(failure, t('common.requestFailed'));
    }
  } finally { if (createRequest === request) { if (request.current()) saving.value = false; dispatched = false; } request.finish(); }
}
async function downloadTemplate(): Promise<void> {
  const schema = selectedSchema.value; const worksheet = selectedWorksheet.value;
  if (busy.value || !canCreate() || !schema || !worksheet || schema.scopeKey !== 'tenant' || !session.can(schema.requiredPermission)) return;
  const request = scope.begin('import_export.static_schemas.read'); if (!request) return; downloadRequest = request; downloading.value = true;
  const target = { schemaKey: schema.schemaKey, worksheetKey: worksheet.worksheetKey }, signature = selectionSignature.value;
  try {
    const blob = await downloadStaticImportTemplate(target.schemaKey, target.worksheetKey, request.signal);
    if (!request.current() || !canCreate() || signature !== selectionSignature.value) return;
    const url = URL.createObjectURL(blob);
    try {
      const anchor = document.createElement('a'); anchor.href = url;
      anchor.download = `${target.schemaKey.replaceAll('.', '-')}-${target.worksheetKey}-template.xlsx`; anchor.click();
    } finally { URL.revokeObjectURL(url); }
  } catch (error) {
    if (request.current()) showProblem(error, t('common.requestFailed'));
  } finally { if (request.current()) downloading.value = false; request.finish(); }
}
function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.unexpected_error', title: t('common.requestFailed') };
}
function acknowledgeUnknown(): void {
  if (!canCreate() || busy.value || !requiresReview.value || !receipt.value) return;
  const sequence = receipt.value.sequence, token = ++recoverySequence;
  const owned = () => recoveryConfirmation.value?.token === token && canCreate() && receipt.value?.sequence === sequence && requiresReview.value;
  recoveryConfirmation.value = { token, confirm: () => { if (owned()) { acknowledged.value = sequence; recoveryConfirmation.value = undefined; } }, update: open => { if (!open && owned()) recoveryConfirmation.value = undefined; } };
}
</script>

<template>
  <ArtFormDialog :open="open && !closed" :title="t('importExportTasks.addTask')" :saving="busy" :show-confirm="canCreate() && !requiresReview"
    :confirm-label="t('importCreate.submit')" :cancel-label="t('common.cancel')" confirm-test-id="import-create-submit"
    v-on="{ 'update:open': controls.close, confirm: controls.submit }">
    <ElAlert v-if="catalogProblem" data-testid="import-create-catalog-problem" type="error" :closable="false" :title="catalogProblem.title" :description="catalogProblem.code" />
    <ElButton data-testid="import-create-refresh" :disabled="busy || !canCreate()" v-on="{ click: controls.refresh }">{{ t('importCreate.refreshSchemas') }}</ElButton>
    <div v-if="receipt" role="status" data-testid="import-create-receipt">
      <p>{{ t('importCreate.receiptTarget', {schema:receipt.schemaKey,worksheet:receipt.worksheetKey,file:receipt.fileName}) }}</p>
      <p>{{ t(receipt.outcome === 'succeeded' ? 'importCreate.resultSucceeded' : receipt.outcome === 'failed' ? 'importCreate.resultFailed' : receipt.outcome === 'processing' ? 'importCreate.resultProcessing' : 'importCreate.resultUnconfirmed') }} <code v-if="receipt.taskId" translate="no">{{ receipt.taskId }}</code> <code v-if="receipt.errorCode" translate="no">{{ receipt.errorCode }}</code></p>
      <p v-if="receipt.outcome === 'unconfirmed'">{{ t('importCreate.recoveryHint') }}</p>
      <p v-if="receipt.outcome === 'unconfirmed' && !requiresReview">{{ t('importCreate.recoveryAcknowledged') }}</p>
      <ElButton v-if="requiresReview" data-testid="import-create-recovery" :disabled="busy || !canCreate()" v-on="{ click: controls.acknowledge }">{{ t('importCreate.reviewExistingTasks') }}</ElButton>
    </div>
    <ElForm label-width="120px">
      <ElFormItem :label="t('importExportTasks.schema')" required>
        <ElSelect :model-value="schemaKey" data-testid="import-create-schema" :loading="loading" :disabled="busy || !canCreate()" v-on="{ 'update:modelValue': controls.schema }">
          <ElOption v-for="schema in schemas" :key="schema.schemaKey" :label="schema.displayName" :value="schema.schemaKey" />
        </ElSelect>
      </ElFormItem>
      <ElFormItem :label="t('importExportTasks.worksheet')" required>
        <ElSelect :model-value="worksheetKey" data-testid="import-create-worksheet" :disabled="busy || !canCreate()" v-on="{ 'update:modelValue': controls.worksheet }">
          <ElOption v-for="worksheet in selectedSchema?.worksheets ?? []" :key="worksheet.worksheetKey" :label="worksheet.displayName" :value="worksheet.worksheetKey" />
        </ElSelect>
      </ElFormItem>
      <ElFormItem v-if="selectedWorksheet" :label="t('importCreate.columns')">
        <span>{{ selectedWorksheet.headerColumns.join('、') }}</span>
      </ElFormItem>
      <ElFormItem :label="t('importCreate.file')" required>
        <input ref="fileInput" type="file" accept=".xlsx" data-testid="import-create-file"
          :aria-label="t('importCreate.file')" :disabled="busy || !canCreate()" v-on="{ change: controls.file }">
        <span v-if="selectedFile" data-testid="import-create-selected-file">{{ t('importCreate.selectedFile', {file:selectedFile.name,size:String(selectedFile.size)}) }}</span>
      </ElFormItem>
      <ElAlert :title="t('importCreate.fileHint')" type="info" :closable="false" />
      <ElAlert v-if="!loading && !catalogProblem && schemas.length === 0" data-testid="import-create-no-schemas" :title="t('importCreate.noSchemas')" type="warning" :closable="false" />
      <PermissionGate code="import_export.static_schemas.read">
        <ElButton data-testid="import-create-template" :loading="downloading" :disabled="!selectedWorksheet || busy || !canCreate()" v-on="{ click: controls.download }">
          {{ t('users.importTemplate') }}
        </ElButton>
      </PermissionGate>
    </ElForm>
  </ArtFormDialog>
  <ArtFormDialog v-if="recoveryConfirmation && canCreate()" :key="recoveryConfirmation.token" :open="true" :title="t('importCreate.reviewExistingTasks')"
    :confirm-label="t('importCreate.allowResubmit')" :cancel-label="t('common.cancel')" confirm-test-id="import-create-recovery-confirm"
    v-on="{ confirm: recoveryConfirmation.confirm, 'update:open': recoveryConfirmation.update }">
    <p>{{ t('importCreate.recoveryConfirm') }}</p>
  </ArtFormDialog>
</template>
