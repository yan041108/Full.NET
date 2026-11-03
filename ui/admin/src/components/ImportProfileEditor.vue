<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElAlert, ElButton, ElCheckbox, ElFormItem, ElInput, ElOption, ElPopconfirm, ElSelect } from 'element-plus';
import { isFullNetProblemDetails, type ImportColumnMappingRequest, type ImportProfileSummary } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import { listImportProfiles, getImportProfile, saveImportProfile, deleteImportProfile, validImportMapping } from '../api/import-profiles';

const props = defineProps<{ schemaKey: string; worksheetKey: string; headers: readonly string[]; active: boolean; disabled?: boolean }>();
const mapping = defineModel<ImportColumnMappingRequest | undefined>();
const emit = defineEmits<{ busy: [value: boolean]; changed: [] }>();
const session = useSessionStore(), { t } = useAdminI18n();
const custom = ref(false), aliases = ref<string[]>([]), name = ref(''), selectedId = ref<string>();
const items = ref<ImportProfileSummary[]>([]), selected = ref<ImportProfileSummary>();
const busy = ref(false), failed = ref(false), reloadRequired = ref(false);
let generation = 0, requests = new AbortController();
const can = (action: string) => session.can(`import_export.import_profiles.${action}`);
const disabled = computed(() => !!props.disabled || busy.value || !props.active || session.switching);
const value = () => ({ headerColumns: [...props.headers], columns: props.headers.map((targetColumn, index) => ({ targetColumn, sourceHeader: aliases.value[index]?.trim() ?? '' })) });
function publish() { mapping.value = custom.value ? value() : undefined; emit('changed'); }
watch([custom, aliases], publish, { deep: true });
watch(busy, loading => emit('busy', loading), { flush: 'sync' });
function reset() {
  generation++; requests.abort(); requests = new AbortController(); busy.value = false;
  items.value = []; selectedId.value = undefined; selected.value = undefined; name.value = '';
  failed.value = false; reloadRequired.value = false; custom.value = false; aliases.value = [...props.headers]; mapping.value = undefined;
}
async function reload() {
  if (!props.active || !props.schemaKey || !props.worksheetKey || busy.value || session.switching || !can('read')) return;
  const current = generation; busy.value = true;
  try {
    const rows = await listImportProfiles(props.schemaKey, props.worksheetKey, requests.signal);
    if (current !== generation) return;
    const target = selectedId.value && rows.some(row => row.id === selectedId.value) ? selectedId.value : undefined;
    const detail = target ? await getImportProfile(props.schemaKey, props.worksheetKey, target, props.headers, requests.signal) : undefined;
    if (current !== generation) return;
    items.value = rows;
    if (detail) accept(detail);
    else if (selectedId.value) { selectedId.value = undefined; selected.value = undefined; }
    failed.value = false; reloadRequired.value = false;
  } catch (error) { if (current === generation) { failed.value = true; showProblem(error, t('importProfiles.failed')); } }
  finally { if (current === generation) busy.value = false; }
}
function accept(detail: Awaited<ReturnType<typeof getImportProfile>>) {
  selected.value = detail.summary; selectedId.value = detail.summary.id; name.value = detail.summary.name;
  aliases.value = props.headers.map(header => detail.configuration?.columns.find(column => column.targetColumn === header)?.sourceHeader ?? header);
  custom.value = true; publish();
}
async function choose(id?: string) {
  if (disabled.value) return;
  if (!id) { selectedId.value = undefined; selected.value = undefined; name.value = ''; aliases.value = [...props.headers]; reloadRequired.value = false; return; }
  const current = generation; busy.value = true;
  try {
    const detail = await getImportProfile(props.schemaKey, props.worksheetKey, id, props.headers, requests.signal);
    if (current === generation) { accept(detail); reloadRequired.value = false; }
  } catch (error) { if (current === generation) { selectedId.value = selected.value?.id; showProblem(error, t('importProfiles.failed')); } }
  finally { if (current === generation) busy.value = false; }
}
async function mutate(action: () => Promise<void>) {
  if (disabled.value || failed.value || reloadRequired.value) return;
  const current = generation; busy.value = true;
  try { await action(); }
  catch (error) {
    if (current !== generation) return;
    reloadRequired.value = !isFullNetProblemDetails(error) || error.status === 409 || (error.status ?? 500) >= 500;
    showProblem(error, t('importProfiles.failed'));
  } finally { if (current === generation) busy.value = false; }
}
async function save(copy: boolean) {
  if (!can(copy ? 'create' : 'update') || !custom.value || !copy && !selected.value) return;
  const configuration = value(), current = generation;
  if (!name.value.trim() || !validImportMapping(configuration, props.headers)) { showProblem({ title: t('importProfiles.invalid') }, t('importProfiles.invalid')); return; }
  await mutate(async () => {
    const result = await saveImportProfile(props.schemaKey, props.worksheetKey, copy ? undefined : selected.value!.id,
      name.value.trim(), configuration, copy ? 0 : selected.value!.version, requests.signal);
    if (current !== generation) return;
    accept(result);
    if (can('read')) {
      const rows = await listImportProfiles(props.schemaKey, props.worksheetKey, requests.signal);
      if (current !== generation) return;
      items.value = rows;
    }
    if (current === generation) showSuccess(t('importProfiles.savedOk'));
  });
}
async function remove() {
  if (!selected.value || !can('delete')) return;
  const current = generation, profile = selected.value;
  await mutate(async () => {
    await deleteImportProfile(props.schemaKey, props.worksheetKey, profile.id, profile.version, requests.signal);
    if (current !== generation) return;
    selectedId.value = undefined; selected.value = undefined; name.value = '';
    if (can('read')) {
      const rows = await listImportProfiles(props.schemaKey, props.worksheetKey, requests.signal);
      if (current !== generation) return;
      items.value = rows;
    }
    if (current === generation) showSuccess(t('importProfiles.deletedOk'));
  });
}
watch(() => [props.active, props.schemaKey, props.worksheetKey, props.headers.join('\0'), session.currentUser?.id,
  session.currentUser?.tenantId, session.currentUser?.sessionId, session.currentUser?.permissions.join('|'), session.switching],
() => { reset(); if (props.active) void reload(); }, { immediate: true, flush: 'sync' });
onBeforeUnmount(reset);
</script>

<template>
  <fieldset class="import-profile-editor" :disabled="disabled" :aria-busy="busy">
    <legend>{{ t('importProfiles.title') }}</legend>
    <el-checkbox v-model="custom" :disabled="disabled" data-testid="import-profile-custom">{{ t('importProfiles.custom') }}</el-checkbox>
    <p>{{ t('importProfiles.help') }}</p>
    <template v-if="custom">
      <el-form-item v-if="can('read')" :label="t('importProfiles.saved')">
        <el-select :model-value="selectedId" :disabled="disabled || failed" clearable :placeholder="t('importProfiles.none')" data-testid="import-profile-select" @change="choose">
          <el-option v-for="item in items" :key="item.id" :value="item.id" :label="item.name" />
        </el-select>
      </el-form-item>
      <el-alert v-if="selected?.isCompatible === false" :title="t('importProfiles.incompatible')" type="warning" :closable="false" />
      <el-form-item v-for="(header, index) in headers" :key="header" :label="`${header} → ${t('importProfiles.source')}`" required>
        <el-input v-model="aliases[index]" :maxlength="128" :disabled="disabled" :aria-label="`${header} ${t('importProfiles.source')}`" :data-testid="`import-profile-header-${index}`" />
      </el-form-item>
      <el-form-item :label="t('importProfiles.name')">
        <el-input v-model="name" :maxlength="64" :disabled="disabled" :aria-label="t('importProfiles.name')" data-testid="import-profile-name" />
      </el-form-item>
      <div class="profile-actions">
        <el-button v-if="can('create')" :disabled="disabled || failed || reloadRequired || items.length >= 32" data-testid="import-profile-save" @click="save(true)">{{ t('importProfiles.saveNew') }}</el-button>
        <el-button v-if="can('update')" :disabled="disabled || !selected || failed || reloadRequired" data-testid="import-profile-update" @click="save(false)">{{ t('importProfiles.update') }}</el-button>
        <el-popconfirm v-if="can('delete')" :title="t('importProfiles.delete')" :disabled="disabled || !selected || failed || reloadRequired" @confirm="remove">
          <template #reference><el-button type="danger" plain :disabled="disabled || !selected || failed || reloadRequired">{{ t('importProfiles.delete') }}</el-button></template>
        </el-popconfirm>
        <el-button v-if="can('read')" :disabled="disabled" data-testid="import-profile-reload" @click="reload">{{ t('importProfiles.reload') }}</el-button>
      </div>
    </template>
    <p v-if="busy" role="status">{{ t('importProfiles.loading') }}</p>
    <el-alert v-if="failed || reloadRequired" :title="t('importProfiles.failed')" type="warning" :closable="false" />
    <p>{{ t('importProfiles.frozen') }}</p>
  </fieldset>
</template>

<style scoped>
.import-profile-editor { min-width: 0; margin: 16px 0; border: 1px solid var(--el-border-color); border-radius: 6px; }
.import-profile-editor p { overflow-wrap: anywhere; }
.profile-actions { display: flex; flex-wrap: wrap; gap: 8px; }
.profile-actions :deep(.el-button + .el-button) { margin-left: 0; }
</style>
