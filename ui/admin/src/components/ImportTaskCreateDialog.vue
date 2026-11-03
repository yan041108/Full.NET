<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElButton, ElDialog, ElForm, ElFormItem, ElOption, ElSelect } from 'element-plus';
import type { ImportColumnMappingRequest, ImportExportTaskDetailResponse, StaticImportSchemaDefinition } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { createImportExportTask, downloadStaticImportTemplate, listStaticImportSchemas } from '../api/import-export-tasks';
import ImportProfileEditor from './ImportProfileEditor.vue';
import { validImportMapping } from '../api/import-profiles';

const open = defineModel<boolean>({ default: false });
const emit = defineEmits<{ created: [task: ImportExportTaskDetailResponse] }>();
const session = useSessionStore();
const { t } = useAdminI18n();
const allowed = computed(() => !session.switching && !!session.currentUser?.tenantId
  && session.can('import_export.import_tasks.create') && session.can('import_export.static_schemas.read'));
const schemas = ref<StaticImportSchemaDefinition[]>([]);
const schemaKey = ref(''), worksheetKey = ref('');
const file = ref<File>(), fileInput = ref<HTMLInputElement>();
const busy = ref(false);
const profileBusy = ref(false), mapping = ref<ImportColumnMappingRequest>();
const availableSchemas = computed(() => schemas.value.filter(schema => schema.scopeKey === 'tenant' && session.can(schema.requiredPermission)));
const schema = computed(() => availableSchemas.value.find(item => item.schemaKey === schemaKey.value));
const worksheet = computed(() => schema.value?.worksheets.find(item => item.worksheetKey === worksheetKey.value));
let generation = 0, cancellation = new AbortController();

/** 弹窗或会话变化立即作废上传及迟到响应，防止把旧租户结果带入新上下文。 */
function reset() {
  generation++; cancellation.abort(); cancellation = new AbortController();
  schemas.value = []; schemaKey.value = ''; worksheetKey.value = ''; file.value = undefined; busy.value = false;
  mapping.value = undefined; profileBusy.value = false;
  if (fileInput.value) fileInput.value.value = '';
}
async function act(action: () => Promise<void>) {
  if (busy.value || profileBusy.value || !allowed.value || !open.value) return;
  const current = generation; busy.value = true;
  try { await action(); }
  catch (error) { if (current === generation) showProblem(error, t('importExportTasks.actionFailed')); }
  finally { if (current === generation) busy.value = false; }
}
watch(open, value => {
  reset();
  if (value && allowed.value) {
    const current = generation;
    void act(async () => {
      const result = await listStaticImportSchemas(cancellation.signal);
      if (current !== generation) return;
      schemas.value = result;
      schemaKey.value = availableSchemas.value[0]?.schemaKey ?? '';
    });
  }
}, { immediate: true, flush: 'sync' });
watch(schemaKey, () => {
  worksheetKey.value = schema.value?.worksheets[0]?.worksheetKey ?? '';
}, { flush: 'sync' });
watch([schemaKey, worksheetKey], () => {
  generation++; cancellation.abort(); cancellation = new AbortController();
  busy.value = false;
  mapping.value = undefined;
  clearFile();
}, { flush: 'sync' });
function clearFile() {
  file.value = undefined;
  if (fileInput.value) fileInput.value.value = '';
}
watch(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId, session.switching,
  session.currentUser?.permissions.join('|')], () => { open.value = false; reset(); }, { flush: 'sync' });
onBeforeUnmount(reset);

function choose(event: Event) {
  file.value = (event.target as HTMLInputElement).files?.[0];
}
async function download() {
  if (!schema.value || !worksheet.value) return;
  const current = generation, name = `${schemaKey.value}-${worksheetKey.value}.xlsx`;
  const blob = await downloadStaticImportTemplate(schemaKey.value, worksheetKey.value, cancellation.signal);
  if (current !== generation) return;
  const url = URL.createObjectURL(blob), link = document.createElement('a');
  link.href = url; link.download = name; link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
}
async function submit() {
  const selected = file.value;
  if (!schema.value || !worksheet.value || !selected || !/\.xlsx$/i.test(selected.name)
    || selected.size < 1 || selected.size > 1024 * 1024) {
    showWarning(t('importExportTasks.invalidWorkbook')); return;
  }
  if (mapping.value && !validImportMapping(mapping.value, worksheet.value.headerColumns)) {
    showWarning(t('importProfiles.invalid')); return;
  }
  const current = generation;
  const task = mapping.value
    ? await createImportExportTask(schemaKey.value, worksheetKey.value, selected, cancellation.signal, mapping.value)
    : await createImportExportTask(schemaKey.value, worksheetKey.value, selected, cancellation.signal);
  if (current !== generation) return;
  showSuccess(t('importExportTasks.previewCreated')); emit('created', task); open.value = false;
}
</script>

<template>
  <el-dialog v-model="open" :title="t('importExportTasks.addTask')" width="min(560px, calc(100vw - 32px))"
    :close-on-click-modal="false" append-to-body destroy-on-close>
    <el-form v-if="allowed" class="import-task-form" label-position="top" :aria-busy="busy" @submit.prevent="act(submit)">
      <el-form-item :label="t('importExportTasks.schema')" required>
        <el-select v-model="schemaKey" :disabled="busy || profileBusy" :aria-label="t('importExportTasks.schema')" data-testid="import-task-schema">
          <el-option v-for="item in availableSchemas" :key="item.schemaKey" :label="item.displayName" :value="item.schemaKey" />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('importExportTasks.worksheet')" required>
        <el-select v-model="worksheetKey" :disabled="busy || profileBusy" :aria-label="t('importExportTasks.worksheet')" data-testid="import-task-worksheet">
          <el-option v-for="item in schema?.worksheets ?? []" :key="item.worksheetKey" :label="item.displayName" :value="item.worksheetKey" />
        </el-select>
      </el-form-item>
      <el-button :disabled="busy || !worksheet" data-testid="import-task-template" @click="act(download)">{{ t('importExportTasks.downloadTemplate') }}</el-button>
      <import-profile-editor v-if="worksheet" v-model="mapping" :schema-key="schemaKey" :worksheet-key="worksheetKey"
        :headers="worksheet.headerColumns" :active="open && allowed" :disabled="busy" @busy="profileBusy = $event" @changed="clearFile" />
      <p v-if="!busy && availableSchemas.length === 0" role="status">{{ t('importExportTasks.noAvailableSchemas') }}</p>
      <p id="import-task-file-help">{{ t('importExportTasks.workbookHelp') }}</p>
      <el-form-item :label="t('importExportTasks.workbook')" required>
        <input ref="fileInput" type="file" accept=".xlsx" :aria-label="t('importExportTasks.workbook')"
          aria-describedby="import-task-file-help" :disabled="busy || profileBusy || !worksheet" data-testid="import-task-file" @change="choose" />
      </el-form-item>
      <el-button type="primary" :loading="busy" :disabled="profileBusy || !worksheet || !file" data-testid="import-task-submit" @click="act(submit)">{{ t('importExportTasks.preview') }}</el-button>
    </el-form>
  </el-dialog>
</template>

<style scoped>
/* 选中项采用与输入面相同的显式底色，保证弹层叠加时的文字对比度。 */
.import-task-form :deep(.el-select__selected-item) {
  background-color: var(--el-fill-color-blank);
}
.import-task-form input[type='file'] { max-width: 100%; }
</style>
