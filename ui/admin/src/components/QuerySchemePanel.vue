<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, reactive, ref, watch } from 'vue';
import { ElAlert, ElButton, ElCard, ElCheckbox, ElCheckboxGroup, ElForm, ElFormItem, ElInput, ElOption, ElOptionGroup, ElPopconfirm, ElSelect, ElSwitch } from 'element-plus';
import { isFullNetProblemDetails, validateSavedQueryConfiguration, type FullNetProblemDetails, type SavedQueryConfiguration,
  type SavedQueryDefinition, type SavedQueryDetail, type SavedQuerySummary } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showSuccess } from '../feedback/fullNetMessage';
import { getQuerySchemeSchema, listQuerySchemes, getQueryScheme, saveQueryScheme, deleteQueryScheme,
  shareQueryScheme, getDefaultQueryScheme, setDefaultQueryScheme } from '../api/query-schemes';

const props = defineProps<{ gridKey: string; businessPermission: string; active: boolean; disabled?: boolean;
  defaults: () => SavedQueryConfiguration; fieldLabel: (field: string) => string }>();
const emit = defineEmits<{ apply: [configuration: SavedQueryConfiguration]; ready: [] }>();
const session = useSessionStore(), { t } = useAdminI18n();
const active = ref(true);
const schema = ref<SavedQueryDefinition>(), selectedId = ref<string>(), selected = ref<SavedQuerySummary>();
const name = ref(''), sortBy = ref('id'), descending = ref(false), visibleColumns = ref<string[]>([]);
const filterValues = reactive<Record<string, string>>(Object.create(null) as Record<string, string>);
const problem = ref<FullNetProblemDetails>(), unavailable = ref(false), reloadRequired = ref(false), loading = ref(false), busy = ref(false);
const defaultVersion = ref(0), hasDefault = ref(false), defaultUnavailable = ref(false);
const mine = reactive({ items: [] as SavedQuerySummary[], cursors: [''], index: 0, next: null as string | null });
const shared = reactive({ items: [] as SavedQuerySummary[], cursors: [''], index: 0, next: null as string | null });
let template = props.defaults(), generation = 0, selectionVersion = 0, requests = new AbortController();
const identity = () => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId, session.currentUser?.permissions.join('|')].join('|');
const eligible = () => active.value && props.active && !session.switching && session.can(props.businessPermission)
  && !!session.currentUser?.permissions.includes('settings.query_schemes.read');
const owner = computed(() => selected.value?.ownerUserId === session.currentUser?.id);
const blocked = computed(() => !!props.disabled || loading.value || busy.value || unavailable.value || !eligible());
const canChange = (code: string) => !blocked.value && !reloadRequired.value && session.can(`settings.query_schemes.${code}`);
const selectedOutsidePage = computed(() => selected.value && ![...mine.items, ...shared.items].some(item => item.id === selected.value?.id) ? selected.value : undefined);

function resetDraft(configuration = props.defaults()) {
  template = structuredClone(configuration); sortBy.value = configuration.sortBy; descending.value = configuration.descending;
  visibleColumns.value = configuration.columns.filter(column => column.visible).map(column => column.columnKey);
  for (const key of Object.keys(filterValues)) delete filterValues[key];
  for (const filter of configuration.filters) filterValues[filter.fieldKey] = filter.value;
}
function configuration(): SavedQueryConfiguration {
  const definition = schema.value!;
  const existing = new Map(template.columns.map(column => [column.columnKey, column]));
  const ordered = [...template.columns.map(column => column.columnKey), ...definition.columnKeys.filter(key => !existing.has(key))];
  return validateSavedQueryConfiguration(definition, {
    sortBy: sortBy.value, descending: descending.value,
    filters: definition.filters.filter(field => filterValues[field.fieldKey]?.trim()).map(field => ({ fieldKey: field.fieldKey, operatorKey: 'contains', value: filterValues[field.fieldKey]!.trim() })),
    columns: ordered.map((columnKey, order) => ({ columnKey, order, width: existing.get(columnKey)?.width ?? null,
      fixed: existing.get(columnKey)?.fixed ?? null, visible: visibleColumns.value.includes(columnKey) }))
  });
}
function fail(error: unknown) {
  problem.value = isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.query_scheme_failed', title: t('querySchemes.failed') };
}
function reset() {
  generation++; selectionVersion++; requests.abort(); requests = new AbortController(); schema.value = undefined;
  selected.value = undefined; selectedId.value = undefined; name.value = ''; problem.value = undefined; reloadRequired.value = false;
  loading.value = false; busy.value = false; unavailable.value = false; defaultVersion.value = 0; hasDefault.value = false; defaultUnavailable.value = false;
  for (const source of [mine, shared]) { source.items = []; source.cursors = ['']; source.index = 0; source.next = null; }
  resetDraft();
}
async function readList(isShared: boolean) {
  const source = isShared ? shared : mine, current = generation;
  const page = await listQuerySchemes(props.gridKey, isShared, source.cursors[source.index] || undefined, requests.signal);
  if (current !== generation || !eligible()) return;
  if (!isShared && page.items.some(item => item.ownerUserId !== session.currentUser?.id)
    || isShared && page.items.some(item => !item.isShared || item.ownerUserId === session.currentUser?.id)) throw new TypeError('client.query_scheme_owner_mismatch');
  source.items = page.items; source.next = page.nextCursor;
}
async function readDefault() {
  const current = generation, value = await getDefaultQueryScheme(props.gridKey, requests.signal);
  if (current !== generation || !eligible()) return undefined;
  defaultVersion.value = value.version; hasDefault.value = value.scheme !== null; defaultUnavailable.value = value.isUnavailable;
  return value;
}
async function initialize() {
  if (!eligible()) return;
  const current = generation; loading.value = true;
  try {
    const definition = await getQuerySchemeSchema(props.gridKey, requests.signal);
    if (current !== generation || !eligible()) return;
    if (definition.gridKey !== props.gridKey || definition.readPermission !== props.businessPermission
      || (session.currentUser?.tenantId ? !definition.supportsTenant : !definition.supportsHost)) throw new TypeError('client.query_scheme_context_mismatch');
    schema.value = definition;
    const [value] = await Promise.all([readDefault(), readList(false), session.currentUser?.tenantId ? readList(true) : Promise.resolve()]);
    if (current !== generation || !eligible()) return;
    if (value?.scheme?.configuration) acceptDetail(value.scheme);
    else { resetDraft(); emit('apply', configuration()); }
  } catch (error) { if (current === generation) { unavailable.value = true; fail(error); } }
  finally { if (current === generation) { loading.value = false; emit('ready'); } }
}
function acceptDetail(detail: SavedQueryDetail) {
  if (detail.summary.isCompatible && detail.summary.schemaVersion !== schema.value?.schemaVersion) throw new TypeError('client.query_scheme_schema_mismatch');
  selected.value = detail.summary; selectedId.value = detail.summary.id; name.value = detail.summary.name; reloadRequired.value = false;
  resetDraft(detail.configuration ?? props.defaults());
  if (detail.summary.isCompatible && detail.configuration) emit('apply', configuration());
}
async function choose(id?: string) {
  if (blocked.value) return;
  const current = generation, version = ++selectionVersion; loading.value = true; problem.value = undefined;
  try {
    if (!id) { selected.value = undefined; selectedId.value = undefined; name.value = ''; reloadRequired.value = false; resetDraft(); emit('apply', configuration()); return; }
    const detail = await getQueryScheme(props.gridKey, id, requests.signal);
    if (current === generation && version === selectionVersion && eligible()) {
      if (detail.summary.id !== id) throw new TypeError('client.query_scheme_target_mismatch');
      acceptDetail(detail);
    }
  } catch (error) { if (current === generation && version === selectionVersion) { selectedId.value = selected.value?.id; fail(error); } }
  finally { if (current === generation && version === selectionVersion) loading.value = false; }
}
function apply() {
  if (blocked.value || reloadRequired.value || selected.value?.isCompatible === false) return;
  try { problem.value = undefined; emit('apply', configuration()); } catch (error) { fail(error); }
}
async function mutation(action: () => Promise<void>) {
  if (blocked.value || reloadRequired.value) return;
  const current = generation; busy.value = true; problem.value = undefined;
  try {
    await action(); if (current !== generation || !eligible()) return;
    await Promise.all([readList(false), session.currentUser?.tenantId ? readList(true) : Promise.resolve(), readDefault()]);
    if (current === generation && eligible()) showSuccess(t('querySchemes.saved'));
  } catch (error) {
    if (current !== generation) return; fail(error);
    // 超时/未知结果或版本冲突必须先显式重读，保留输入但不能直接重放写入。
    reloadRequired.value = !isFullNetProblemDetails(error) || error.status === 409 || (error.status ?? 500) >= 500;
  } finally { if (current === generation) busy.value = false; }
}
async function save(copy: boolean) {
  if (!canChange(copy ? 'create' : 'update') || !copy && !owner.value) return;
  let value: SavedQueryConfiguration; try { value = configuration(); } catch (error) { fail(error); return; }
  if (!name.value.trim()) { problem.value = { status: 400, code: 'client.query_scheme_name_required', title: t('querySchemes.nameRequired') }; return; }
  const current = generation;
  await mutation(async () => {
    const detail = await saveQueryScheme(props.gridKey, copy ? undefined : selected.value!.id,
      { name: name.value.trim(), schemaVersion: schema.value!.schemaVersion, configuration: value, version: copy ? 0 : selected.value!.version }, requests.signal);
    if (current === generation && eligible()) acceptDetail(detail);
  });
}
async function sharing() {
  if (!canChange('share') || !owner.value || !session.currentUser?.tenantId) return;
  const current = generation;
  await mutation(async () => {
    const detail = await shareQueryScheme(props.gridKey, selected.value!.id, !selected.value!.isShared, selected.value!.version, requests.signal);
    if (current === generation && eligible()) { selected.value = detail.summary; selectedId.value = detail.summary.id; }
  });
}
async function remove() {
  if (!canChange('delete') || !owner.value) return;
  const current = generation;
  await mutation(async () => {
    await deleteQueryScheme(props.gridKey, selected.value!.id, selected.value!.version, requests.signal);
    if (current === generation && eligible()) { selected.value = undefined; selectedId.value = undefined; name.value = ''; resetDraft(); emit('apply', configuration()); }
  });
}
async function setDefault(clear: boolean) {
  if (!canChange('set_default') || !clear && (!selected.value || !selected.value.isCompatible)) return;
  const current = generation;
  await mutation(async () => {
    const value = await setDefaultQueryScheme(props.gridKey, clear ? null : selected.value!.id, defaultVersion.value, requests.signal);
    if (current !== generation || !eligible()) return;
    defaultVersion.value = value.version; defaultUnavailable.value = value.isUnavailable; hasDefault.value = value.scheme !== null;
    if (value.scheme) acceptDetail(value.scheme);
  });
}
async function turn(isShared: boolean, forward: boolean) {
  if (blocked.value) return;
  const source = isShared ? shared : mine;
  if (forward ? !source.next : source.index === 0) return;
  if (forward) { source.cursors = [...source.cursors.slice(0, source.index + 1), source.next!]; source.index++; } else source.index--;
  const current = generation; loading.value = true;
  try { await readList(isShared); } catch (error) { if (current === generation) fail(error); }
  finally { if (current === generation) loading.value = false; }
}
function reload() { if (props.disabled || busy.value) return; reset(); void initialize(); }
watch(() => [identity(), props.gridKey, eligible()], () => { reset(); if (eligible()) void initialize(); }, { immediate: true, flush: 'sync' });
onBeforeUnmount(reset);
onActivated(() => { active.value = true; });
onDeactivated(() => { active.value = false; });
</script>

<template>
  <ElCard v-if="eligible()" shadow="never" class="query-scheme-panel" data-testid="query-scheme-panel">
    <template #header><div class="scheme-heading"><span>{{ t('querySchemes.title') }}</span><ElButton :disabled="disabled || busy" :loading="loading" data-testid="query-scheme-refresh" @click="reload">{{ t('common.refresh') }}</ElButton></div></template>
    <ElAlert v-if="problem" type="error" :closable="false" :title="problem.detail ?? problem.title ?? t('querySchemes.failed')" />
    <ElAlert v-if="reloadRequired" type="warning" :closable="false" :title="t('querySchemes.reloadRequired')" />
    <ElAlert v-if="defaultUnavailable" type="warning" :closable="false" :title="t('querySchemes.defaultUnavailable')" />
    <ElAlert v-if="selected?.isCompatible === false" type="warning" :closable="false" :title="t('querySchemes.incompatible')" />
    <ElForm v-if="schema" label-position="top" @submit.prevent="apply">
      <div class="scheme-grid">
        <ElFormItem :label="t('querySchemes.choose')"><ElSelect v-model="selectedId" clearable :disabled="blocked" :aria-label="t('querySchemes.choose')" data-testid="query-scheme-select" @change="choose">
          <ElOption v-if="selectedOutsidePage" :value="selectedOutsidePage.id" :label="selectedOutsidePage.name" />
          <ElOptionGroup :label="t('querySchemes.mine')"><ElOption v-for="item in mine.items" :key="item.id" :value="item.id" :label="item.name + (item.isCompatible ? '' : t('querySchemes.invalidSuffix'))" /></ElOptionGroup>
          <ElOptionGroup v-if="session.currentUser?.tenantId" :label="t('querySchemes.shared')"><ElOption v-for="item in shared.items" :key="item.id" :value="item.id" :label="item.name + (item.isCompatible ? '' : t('querySchemes.invalidSuffix'))" /></ElOptionGroup>
        </ElSelect></ElFormItem>
        <ElFormItem :label="t('querySchemes.name')"><ElInput v-model="name" :disabled="blocked" maxlength="64" data-testid="query-scheme-name" /></ElFormItem>
        <ElFormItem v-for="field in schema.filters" :key="field.fieldKey" :label="fieldLabel(field.fieldKey) + t('querySchemes.contains')">
          <ElInput v-model="filterValues[field.fieldKey]" :maxlength="field.maxLength" clearable :disabled="blocked" :data-testid="`query-scheme-filter-${field.fieldKey}`" />
        </ElFormItem>
        <ElFormItem :label="t('querySchemes.sort')"><ElSelect v-model="sortBy" :disabled="blocked" :aria-label="t('querySchemes.sort')" data-testid="query-scheme-sort"><ElOption v-for="key in schema.sortKeys" :key="key" :value="key" :label="fieldLabel(key)" /></ElSelect></ElFormItem>
        <ElFormItem :label="t('querySchemes.descending')"><ElSwitch v-model="descending" :disabled="blocked" /></ElFormItem>
      </div>
      <ElFormItem :label="t('querySchemes.columns')"><ElCheckboxGroup v-model="visibleColumns" :disabled="blocked" :aria-label="t('querySchemes.columns')"><ElCheckbox v-for="key in schema.columnKeys" :key="key" :value="key">{{ fieldLabel(key) }}</ElCheckbox></ElCheckboxGroup></ElFormItem>
      <div class="scheme-actions">
        <ElButton type="primary" :disabled="blocked || reloadRequired || selected?.isCompatible === false" data-testid="query-scheme-apply" @click="apply">{{ t('querySchemes.apply') }}</ElButton>
        <ElButton v-if="session.can('settings.query_schemes.create')" :disabled="!canChange('create')" data-testid="query-scheme-save-new" @click="save(true)">{{ t('querySchemes.saveNew') }}</ElButton>
        <ElButton v-if="owner && session.can('settings.query_schemes.update')" :disabled="!canChange('update')" data-testid="query-scheme-update" @click="save(false)">{{ t(selected?.isCompatible === false ? 'querySchemes.repair' : 'querySchemes.update') }}</ElButton>
        <ElButton v-if="selected && session.can('settings.query_schemes.set_default')" :disabled="!canChange('set_default') || !selected.isCompatible" data-testid="query-scheme-set-default" @click="setDefault(false)">{{ t('querySchemes.setDefault') }}</ElButton>
        <ElButton v-if="(hasDefault || defaultUnavailable) && session.can('settings.query_schemes.set_default')" :disabled="!canChange('set_default')" data-testid="query-scheme-clear-default" @click="setDefault(true)">{{ t('querySchemes.clearDefault') }}</ElButton>
        <ElButton v-if="owner && session.currentUser?.tenantId && session.can('settings.query_schemes.share')" :disabled="!canChange('share') || !selected?.isCompatible && !selected?.isShared" data-testid="query-scheme-share" @click="sharing">{{ t(selected?.isShared ? 'querySchemes.unshare' : 'querySchemes.share') }}</ElButton>
        <ElPopconfirm v-if="owner && session.can('settings.query_schemes.delete')" :title="t('querySchemes.confirmDelete')" @confirm="remove"><template #reference><ElButton type="danger" plain class="scheme-delete" :disabled="!canChange('delete')" data-testid="query-scheme-delete">{{ t('querySchemes.delete') }}</ElButton></template></ElPopconfirm>
      </div>
      <div class="scheme-pages">
        <span>{{ t('querySchemes.mine') }}</span><ElButton :disabled="blocked || mine.index === 0" data-testid="query-scheme-mine-previous" @click="turn(false, false)">{{ t('taskCenter.previous') }}</ElButton><ElButton :disabled="blocked || !mine.next" data-testid="query-scheme-mine-next" @click="turn(false, true)">{{ t('taskCenter.next') }}</ElButton>
        <template v-if="session.currentUser?.tenantId"><span>{{ t('querySchemes.shared') }}</span><ElButton :disabled="blocked || shared.index === 0" data-testid="query-scheme-shared-previous" @click="turn(true, false)">{{ t('taskCenter.previous') }}</ElButton><ElButton :disabled="blocked || !shared.next" data-testid="query-scheme-shared-next" @click="turn(true, true)">{{ t('taskCenter.next') }}</ElButton></template>
      </div>
    </ElForm>
  </ElCard>
</template>
<style scoped>
.query-scheme-panel { margin-bottom: 16px; min-width: 0; }
.scheme-heading, .scheme-actions, .scheme-pages { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
.scheme-heading { justify-content: space-between; }
.scheme-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 220px), 1fr)); gap: 12px; }
.scheme-grid :deep(.el-select) { width: 100%; }
.scheme-pages { margin-top: 16px; }
.scheme-actions :deep(.el-button + .el-button), .scheme-pages :deep(.el-button + .el-button) { margin-left: 0; }
.query-scheme-panel :deep(.el-alert) { margin-bottom: 12px; }
/* 危险动作保留语义底色，正文使用当前主题文字色以保持普通和悬停状态的对比度。 */
.query-scheme-panel :deep(.scheme-delete:not(.is-disabled)) {
  --el-button-text-color: var(--el-text-color-primary);
  --el-button-hover-text-color: var(--el-text-color-primary);
  --el-button-hover-bg-color: var(--el-color-danger-light-9);
  --el-button-active-text-color: var(--el-text-color-primary);
  --el-button-active-bg-color: var(--el-color-danger-light-8);
}
</style>
