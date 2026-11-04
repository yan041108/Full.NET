<script setup lang="ts">
import { computed, inject, onActivated, onDeactivated, onBeforeUnmount, reactive, ref, watch } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDrawer,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElSelect,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { routeLocationKey } from 'vue-router';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type {
  FullNetProblemDetails,
  ReportingDefinition,
  ReportingDefinitionSummary,
  ReportingDataSourceListItem,
  ReportingDefinitionVersionSummary,
  ReportingGroup,
  ReportingParameterSchemaEntry,
  ReportingQueryPortDefinition
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { sameReportingId, sameReportingParameters, sameReportingLayout, validReportingVersion } from '../guards/reporting-management-guards';
import { useArtDialogLandmarks } from '../framework/art-design/composables/useArtDialogLandmarks';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import ReportingDefinitionGrantsDialog from '../components/ReportingDefinitionGrantsDialog.vue';
import ReportingTenantGrantsDialog from '../components/reporting/ReportingTenantGrantsDialog.vue';
const versionGrantDefinition = ref<ReportingDefinitionSummary>();
import { useSessionStore } from '../auth/session';
import { reportingConfigurationText } from '../i18n/reporting-configuration';
import { useAdminI18n } from '../i18n/adminI18n';
import { listReportingDataSources } from '../api/reporting-data-sources';
import {
  createReportingDefinition,
  createReportingGroup,
  deleteReportingDefinition,
  deleteReportingGroup,
  getReportingDefinition,
  listReportingDefinitions,
  listReportingDefinitionVersions,
  listReportingGroups,
  listReportingQueryPorts,
  publishReportingDefinition,
  updateReportingDefinition,
  updateReportingGroup
} from '../api/reporting-definitions';

defineOptions({ name: 'ReportingDefinitionsView' });

type GroupEditorMode = 'create' | 'edit';
type DefinitionEditorMode = 'create' | 'edit';

const { t, locale } = useAdminI18n();
const route = inject(routeLocationKey, undefined);
const session = useSessionStore(), active = ref(true), writeUnconfirmed = ref(false), publishOpen = ref(false), publishTarget = ref<ReportingDefinition>();
type PublicationReceipt = { definitionId: string; definitionKey: string; outcome: 'processing' | 'confirmed' | 'unknown' | 'rejected'; versionNumber?: number };
const publicationReceipt = ref<PublicationReceipt>();
const publicationDialog = ref<InstanceType<typeof ElDialog>>();
const publicationLandmarks = useArtDialogLandmarks(() => publicationDialog.value?.dialogContentRef?.$el);
const definitionFloors = new Map<string, { definitionVersion: number; publishedVersion: number }>();
let publishEpoch = 0, publicationRequest: { controller: AbortController; receipt: PublicationReceipt } | undefined;
const text = (key: Parameters<typeof reportingConfigurationText>[1]) => reportingConfigurationText(locale.value, key);
let generation = 0, groupEpoch = 0, definitionEpoch = 0, catalogEpoch = 0, requests = new AbortController();
const catalogProblems = reactive<{ sources?: FullNetProblemDetails }>({});
const reads = new Map<string, AbortController>();
const canRead = () => active.value && !session.switching && (!route || route.name === 'reporting-definitions')
  && session.currentUser?.scope === 'host' && session.currentUser.actorScope === 'host' && !session.currentUser.tenantId && session.can('reporting.definitions.read');
const can = (permission: string) => canRead() && session.can(permission);
function current(scope: number) { return scope === generation && canRead(); }
function reading(key: string) { reads.get(key)?.abort(); const controller = new AbortController(); reads.set(key, controller); return controller; }
function stopReads() { for (const controller of reads.values()) controller.abort(); reads.clear(); }
function invalidateDetails() { reads.get('draft')?.abort(); reads.get('publish')?.abort(); publishOpen.value = false; publishTarget.value = undefined; }
function unknown(error: unknown) { return !isFullNetProblemDetails(error) || error.status === 409 || (error.status ?? 500) >= 500; }
const grantsOpen = ref(false), grantsDefinitionId = ref('');
const groups = ref<ReportingGroup[]>([]);
const definitions = ref<ReportingDefinitionSummary[]>([]);
const queryPorts = ref<ReportingQueryPortDefinition[]>([]);
const dataSourceOptions = ref<Array<{ value: string; label: string; providerKey: string }>>([]);
const selectedGroupId = ref<string>();
const versions = ref<ReportingDefinitionVersionSummary[]>([]);
const definitionPage = ref(1);
const definitionTotal = ref(0);
const versionPage = ref(1);
const versionTotal = ref(0);
const versionDefinitionId = ref('');
const loading = ref(false);
const acting = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref({ name: '' });
const groupEditorOpen = ref(false);
const groupEditorMode = ref<GroupEditorMode>('create');
const editingGroup = ref<ReportingGroup | null>(null);
const groupFormRef = ref<FormInstance>();
const groupForm = reactive({
  name: '',
  sortOrder: '0',
  isEnabled: true
});
const definitionEditorOpen = ref(false);
const definitionEditorMode = ref<DefinitionEditorMode>('create');
const editingDefinition = ref<ReportingDefinition | null>(null);
const definitionGroupId = ref<string>();
const definitionFormRef = ref<FormInstance>();
const definitionForm = reactive({
  definitionKey: '',
  name: '',
  description: '',
  dataSourceId: '',
  queryPortKey: '',
  layoutConfigJson: '{}',
  isEnabled: true,
  parameterSchema: [] as ReportingParameterSchemaEntry[]
});
const publishNote = ref('');
const versionsDrawerOpen = ref(false);

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  updateTableHeight,
  watchLoading
} = useArtCrudTableLayout();

watchLoading(loading);

function cancelPublication() {
  const request = publicationRequest; if (!request) return;
  publicationRequest = undefined; request.controller.abort(); acting.value = false; writeUnconfirmed.value = true;
  publicationReceipt.value = { ...request.receipt, outcome: 'unknown' };
}
watch(() => [publishOpen.value, publishTarget.value, publishNote.value], () => {
  publishEpoch++; cancelPublication();
}, { flush: 'sync' });
const publicationControls = computed(() => {
  const owner = publishEpoch, open = publishOpen.value, target = publishTarget.value, note = publishNote.value;
  const owned = () => owner === publishEpoch && open && publishOpen.value && target === publishTarget.value && note === publishNote.value && can('reporting.definitions.publish');
  return { close: () => { if (owned()) publishOpen.value = false; }, update: (value: boolean) => { if (!value && owned()) publishOpen.value = false; },
    note: (value: string) => { if (owned() && !acting.value) publishNote.value = value; }, confirm: () => { if (owned()) void confirmPublish(); } };
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  { key: 'name', label: t('reportingDefinitions.fieldName'), type: 'input' }
]);

const filteredDefinitions = computed(() => definitions.value);

const selectedQueryPort = computed(() =>
  queryPorts.value.find(port => port.queryPortKey === definitionForm.queryPortKey));

const compatibleDataSources = computed(() => {
  const port = selectedQueryPort.value;
  if (!port) {
    return dataSourceOptions.value;
  }
  return dataSourceOptions.value.filter(option =>
    port.supportedProviderKeys.includes(option.providerKey));
});

watch(() => definitionForm.queryPortKey, queryPortKey => {
  const port = queryPorts.value.find(item => item.queryPortKey === queryPortKey);
  if (!port) {
    definitionForm.parameterSchema = [];
    return;
  }
  definitionForm.parameterSchema = port.parameters.map(parameter => ({
    parameterKey: parameter.parameterKey,
    displayName: parameter.displayName,
    dataTypeKey: parameter.dataTypeKey,
    isRequired: parameter.isRequired,
    defaultValue: parameter.defaultValue
  }));
}, { flush: 'sync' });

/** 权限或会话变更销毁草稿与在途结果，Host 配置不能残留到租户上下文。 */
function reset() {
  publicationRequest?.controller.abort(); publicationRequest = undefined; publicationReceipt.value = undefined; definitionFloors.clear();
  generation++; catalogEpoch++; catalogProblems.sources = undefined; requests.abort(); requests = new AbortController(); stopReads();
  groups.value = []; definitions.value = []; queryPorts.value = []; dataSourceOptions.value = []; versions.value = [];
  selectedGroupId.value = undefined; definitionPage.value = 1; definitionTotal.value = 0; versionPage.value = 1; versionTotal.value = 0; versionDefinitionId.value = '';
  groupEditorOpen.value = false; definitionEditorOpen.value = false; versionsDrawerOpen.value = false; grantsOpen.value = false; versionGrantDefinition.value = undefined; grantsDefinitionId.value = '';
  editingGroup.value = null; editingDefinition.value = null; publishOpen.value = false; publishTarget.value = undefined; publishNote.value = '';
  definitionGroupId.value = undefined;
  Object.assign(groupForm, { name: '', sortOrder: '0', isEnabled: true });
  Object.assign(definitionForm, { definitionKey: '', name: '', description: '', dataSourceId: '', queryPortKey: '', layoutConfigJson: '{}', isEnabled: true, parameterSchema: [] });
  searchForm.value = {}; appliedFilters.value = { name: '' }; problem.value = undefined; loading.value = false; acting.value = false; writeUnconfirmed.value = false;
}
watch(() => [session.state, route?.name, session.currentUser?.scope, session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.actorScope, session.currentUser?.sessionId, session.currentUser?.permissions.join('|'), session.switching, active.value], () => {
  reset(); if (canRead()) void loadPage();
}, { immediate: true, flush: 'sync' });
watch(versionsDrawerOpen, value => { if (!value) { reads.get('history')?.abort(); versions.value = []; } });
watch(groupEditorOpen, () => { groupEpoch++; }, { flush: 'sync' });
watch(definitionEditorOpen, value => { definitionEpoch++; if (!value) reads.get('draft')?.abort(); }, { flush: 'sync' });
onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; reset(); });

/** 辅助选择必须读取完整目录；数量变化、欠量或重复页不能冒充可提交的候选集。 */
async function loadDataSourceCatalog(signal: AbortSignal): Promise<{ items: ReportingDataSourceListItem[] }> {
  const items: ReportingDataSourceListItem[] = [], ids = new Set<string>();
  let expectedTotal: number | undefined;
  for (let page = 1; page <= 100; page++) {
    signal.throwIfAborted();
    const result = await listReportingDataSources({ page, pageSize: 100, isEnabled: true }, signal);
    signal.throwIfAborted();
    if (result.page !== page || result.pageSize !== 100 || !Number.isSafeInteger(result.total) || result.total < 0 || result.total > 10000
      || expectedTotal !== undefined && result.total !== expectedTotal
      || result.items.length !== Math.min(100, Math.max(0, result.total - (page - 1) * 100))) throw Error('client.invalid_reporting_data_source_catalog');
    expectedTotal = result.total;
    for (const row of result.items) {
      const id = row.id.toLowerCase();
      if (ids.has(id)) throw Error('client.invalid_reporting_data_source_catalog');
      ids.add(id); items.push(row);
    }
    if (items.length === expectedTotal) return { items };
  }
  throw Error('client.invalid_reporting_data_source_catalog');
}

async function loadPage(): Promise<void> {
  if (!canRead() || acting.value) return;
  const scope = generation, controller = reading('catalogs');
  catalogEpoch++; catalogProblems.sources = undefined;
  reads.get('publish')?.abort(); publishOpen.value = false; publishTarget.value = undefined; publishNote.value = '';
  if (!definitionEditorOpen.value) reads.get('draft')?.abort();
  loading.value = true;
  problem.value = undefined;
  try {
    const catalogs = await Promise.allSettled([
      can('reporting.groups.read') ? listReportingGroups(controller.signal) : Promise.resolve([]),
      can('reporting.query_ports.read') ? listReportingQueryPorts(controller.signal) : Promise.resolve([]),
      can('reporting.data_sources.read') ? loadDataSourceCatalog(controller.signal) : Promise.resolve({ items: [] })
    ]);
    if (!current(scope) || controller.signal.aborted) return;
    const [groupResult, portResult, sourceResult] = catalogs;
    if (sourceResult.status === 'rejected') catalogProblems.sources = toProblem(sourceResult.reason, 'reportingDefinitions.loadFailed');
    const groupRows = groupResult.status === 'fulfilled' ? groupResult.value : [];
    const portRows = portResult.status === 'fulfilled' ? portResult.value : [];
    const dataSourcePage = sourceResult.status === 'fulfilled' ? sourceResult.value : { items: [] };
    if (catalogs.some(result => result.status === 'rejected')) problem.value = toProblem(catalogs.find(result => result.status === 'rejected')?.reason, 'reportingDefinitions.loadFailed');
    groups.value = groupRows;
    queryPorts.value = portRows;
    dataSourceOptions.value = dataSourcePage.items.map(item => ({
      value: item.id,
      label: item.name,
      providerKey: item.providerKey
    }));
    if (!groups.value.some(group => group.id === selectedGroupId.value)) {
      selectedGroupId.value = groups.value[0]?.id;
    }
    const loaded = await loadDefinitions();
    if (loaded && catalogs.every(result => result.status === 'fulfilled') && !definitionEditorOpen.value && !groupEditorOpen.value && current(scope) && !controller.signal.aborted) writeUnconfirmed.value = false;
    if (current(scope) && !controller.signal.aborted) updateTableHeight();
  } catch (error: unknown) {
    if (current(scope) && !controller.signal.aborted) problem.value = toProblem(error, 'reportingDefinitions.loadFailed');
  } finally {
    if (current(scope) && !controller.signal.aborted) loading.value = false;
  }
}

async function loadDefinitions(afterWrite = false): Promise<boolean> {
  if (!canRead() || (acting.value && !afterWrite)) return false;
  const scope = generation, controller = reading('definitions');
  try {
    const result = await listReportingDefinitions({ groupId: selectedGroupId.value,
      nameContains: appliedFilters.value.name, page: definitionPage.value, pageSize: 20 }, controller.signal);
    if (!current(scope) || controller.signal.aborted) return false;
    // 已确认发布的修订是本视图下界，迟到或陈旧目录不能撤销这一事实。
    if (result.items.some(row => { const floor = definitionFloors.get(row.id.toLowerCase());
      return floor && (row.version < floor.definitionVersion || row.latestPublishedVersionNumber < floor.publishedVersion); })) throw Error('client.stale_reporting_publication_catalog');
    for (const row of result.items) definitionFloors.set(row.id.toLowerCase(), { definitionVersion: row.version, publishedVersion: row.latestPublishedVersionNumber });
    definitions.value = result.items; definitionTotal.value = result.total;
    return true;
  } catch (error) { if (current(scope) && !controller.signal.aborted) problem.value = toProblem(error, 'reportingDefinitions.loadFailed'); return false; }
}

async function loadVersions(): Promise<void> {
  if (!canRead() || !versionDefinitionId.value) return;
  const scope = generation, id = versionDefinitionId.value, controller = reading('history');
  try { const result = await listReportingDefinitionVersions(id, controller.signal, versionPage.value, 20);
    if (current(scope) && !controller.signal.aborted && id === versionDefinitionId.value) { versions.value = result.items; versionTotal.value = result.total; }
  } catch (error) { if (current(scope) && !controller.signal.aborted) problem.value = toProblem(error, 'reportingDefinitions.loadFailed'); }
}

function selectGroup(groupId: string): void {
  if (!canRead() || acting.value || definitionEditorOpen.value) return;
  invalidateDetails();
  selectedGroupId.value = groupId;
  definitionPage.value = 1; void loadDefinitions();
}

function handleSearch(params: Record<string, string | undefined>): void {
  if (!canRead() || acting.value) return;
  invalidateDetails();
  appliedFilters.value = { name: params.name ?? '' };
  definitionPage.value = 1; void loadDefinitions();
}

function resetSearch(): void {
  if (!canRead() || acting.value) return;
  invalidateDetails();
  appliedFilters.value = { name: '' };
  definitionPage.value = 1; void loadDefinitions();
}

function openCreateGroup(): void {
  if (!can('reporting.groups.create') || acting.value || writeUnconfirmed.value) return;
  groupEpoch++; groupEditorMode.value = 'create';
  editingGroup.value = null;
  groupForm.name = '';
  groupForm.sortOrder = '0';
  groupForm.isEnabled = true;
  groupEditorOpen.value = true;
}

function openEditGroup(group: ReportingGroup): void {
  if (!can('reporting.groups.update') || acting.value || writeUnconfirmed.value) return;
  groupEpoch++; groupEditorMode.value = 'edit';
  editingGroup.value = group;
  groupForm.name = group.name;
  groupForm.sortOrder = String(group.sortOrder);
  groupForm.isEnabled = group.isEnabled;
  groupEditorOpen.value = true;
}

async function submitGroup(): Promise<void> {
  const permission = `reporting.groups.${groupEditorMode.value === 'create' ? 'create' : 'update'}`, scope = generation, editor = groupEpoch;
  if (!groupFormRef.value || acting.value || writeUnconfirmed.value || !can(permission)) {
    return;
  }
  // 校验等待期间只能确认原编辑目标和原草稿；隐藏的父组关系也属于本次写入快照。
  const target = editingGroup.value ? { ...editingGroup.value } : undefined;
  const mode = groupEditorMode.value, signature = JSON.stringify(groupForm);
  const payload = { name: groupForm.name.trim(), sortOrder: Number(groupForm.sortOrder), isEnabled: groupForm.isEnabled };
  if (!Number.isSafeInteger(payload.sortOrder)) return;
  const valid = await groupFormRef.value.validate().catch(() => false);
  if (!valid || !current(scope) || editor !== groupEpoch || signature !== JSON.stringify(groupForm) || !groupEditorOpen.value || acting.value || !can(permission)) {
    return;
  }
  await write(permission, async signal => {
    if (mode === 'create') {
      const created = await createReportingGroup(payload, signal);
      if (!current(scope)) return;
      groups.value = [...groups.value, created];
      selectedGroupId.value = created.id;
      ElMessage.success(t('reportingDefinitions.groupCreateSuccess'));
    } else if (target) {
      const updated = await updateReportingGroup(target.id, { ...payload, parentId: target.parentId, version: target.version }, signal);
      if (!current(scope)) return;
      groups.value = groups.value.map(item => item.id === updated.id ? updated : item);
      ElMessage.success(t('reportingDefinitions.groupUpdateSuccess'));
    }
    groupEditorOpen.value = false;
  });
}

async function confirmDeleteGroup(group: ReportingGroup): Promise<void> {
  const scope = generation; if (!can('reporting.groups.delete') || acting.value || writeUnconfirmed.value) return;
  try {
    await ElMessageBox.confirm(
      t('reportingDefinitions.confirmDeleteGroup', { name: group.name }),
      { type: 'warning' }
    );
  } catch {
    return;
  }
  if (!current(scope)) return;
  await write('reporting.groups.delete', async signal => {
    await deleteReportingGroup(group.id, signal); if (!current(scope)) return;
    groups.value = groups.value.filter(item => item.id !== group.id);
    definitions.value = definitions.value.filter(item => item.groupId !== group.id);
    if (selectedGroupId.value === group.id) {
      selectedGroupId.value = groups.value[0]?.id;
    }
    ElMessage.success(t('reportingDefinitions.groupDeleteSuccess'));
  });
}

function openCreateDefinition(): void {
  if (!can('reporting.definitions.create') || acting.value || loading.value || writeUnconfirmed.value) return;
  if (catalogProblems.sources || !can('reporting.groups.read') || !can('reporting.query_ports.read') || !can('reporting.data_sources.read')) { ElMessage.warning(text('catalogMissing')); return; }
  if (!selectedGroupId.value) {
    ElMessage.warning(t('reportingDefinitions.selectGroupFirst'));
    return;
  }
  definitionEditorMode.value = 'create';
  editingDefinition.value = null;
  definitionGroupId.value = selectedGroupId.value;
  definitionForm.definitionKey = '';
  definitionForm.name = '';
  definitionForm.description = '';
  definitionForm.queryPortKey = queryPorts.value[0]?.queryPortKey ?? '';
  definitionForm.dataSourceId = compatibleDataSources.value[0]?.value ?? '';
  definitionForm.layoutConfigJson = '{}';
  definitionForm.isEnabled = true;
  definitionEditorOpen.value = true;
}

async function openEditDefinition(summary: ReportingDefinitionSummary): Promise<void> {
  if (!can('reporting.definitions.update') || acting.value || writeUnconfirmed.value) return;
  reads.get('publish')?.abort(); publishOpen.value = false;
  await readDraft(summary.id);
}
async function readDraft(id: string) {
  const scope = generation, controller = reading('draft');
  try {
  const definition = await getReportingDefinition(id, controller.signal);
  if (!current(scope) || !can('reporting.definitions.update') || controller.signal.aborted) return;
  definitionEditorMode.value = 'edit';
  editingDefinition.value = definition;
  definitionGroupId.value = definition.groupId;
  definitionForm.definitionKey = definition.definitionKey;
  definitionForm.name = definition.name;
  definitionForm.description = definition.description ?? '';
  definitionForm.dataSourceId = definition.dataSourceId;
  definitionForm.queryPortKey = definition.queryPortKey;
  definitionForm.layoutConfigJson = definition.layoutConfigJson;
  definitionForm.isEnabled = definition.isEnabled;
  definitionForm.parameterSchema = definition.parameterSchema.map(item => ({ ...item }));
  definitionEditorOpen.value = true;
  writeUnconfirmed.value = false;
  } catch (error) { if (current(scope) && !controller.signal.aborted) problem.value = toProblem(error, 'reportingDefinitions.loadFailed'); }
}
async function reloadDraft() {
  const scope = generation, id = editingDefinition.value?.id; if (!id || !can('reporting.definitions.update') || acting.value) return;
  try { await ElMessageBox.confirm(text('discard'), { type: 'warning' }); } catch { return; }
  if (current(scope) && definitionEditorOpen.value && editingDefinition.value?.id === id) await readDraft(id);
}
async function reloadGroupDraft() {
  const scope = generation, id = editingGroup.value?.id; if (!id || !can('reporting.groups.update') || acting.value) return;
  try { await ElMessageBox.confirm(text('discard'), { type: 'warning' }); } catch { return; }
  if (!current(scope) || !groupEditorOpen.value || editingGroup.value?.id !== id) return;
  const controller = reading('group-draft');
  try { const rows = await listReportingGroups(controller.signal); const group = rows.find(item => item.id === id);
    if (current(scope) && !controller.signal.aborted && group) { groups.value = rows; writeUnconfirmed.value = false; openEditGroup(group); }
  } catch (error) { if (current(scope) && !controller.signal.aborted) problem.value = toProblem(error, 'reportingDefinitions.loadFailed'); }
}

async function submitDefinition(): Promise<void> {
  const scope = generation, editor = definitionEpoch, catalog = catalogEpoch, permission = `reporting.definitions.${definitionEditorMode.value === 'create' ? 'create' : 'update'}`;
  if (!definitionFormRef.value || acting.value || loading.value || writeUnconfirmed.value || catalogProblems.sources || !definitionGroupId.value || !can(permission)) {
    return;
  }
  const valid = await definitionFormRef.value.validate().catch(() => false);
  if (!valid || !current(scope) || editor !== definitionEpoch || catalog !== catalogEpoch || loading.value || catalogProblems.sources || !definitionEditorOpen.value || acting.value || !can(permission)) {
    return;
  }
  await write(permission, async signal => {
    const payload = {
      groupId: definitionGroupId.value!,
      dataSourceId: definitionForm.dataSourceId,
      name: definitionForm.name.trim(),
      description: definitionForm.description.trim() || null,
      queryPortKey: definitionForm.queryPortKey,
      parameterSchema: definitionForm.parameterSchema.map(item => ({ ...item })),
      layoutConfigJson: definitionForm.layoutConfigJson,
      isEnabled: definitionForm.isEnabled
    };
    if (definitionEditorMode.value === 'create') {
      const created = await createReportingDefinition({
        ...payload,
        definitionKey: definitionForm.definitionKey.trim()
      }, signal);
      if (!current(scope)) return;
      definitions.value = [...definitions.value, created];
      ElMessage.success(t('reportingDefinitions.createSuccess'));
    } else if (editingDefinition.value) {
      const updated = await updateReportingDefinition(editingDefinition.value.id, {
        ...payload,
        version: editingDefinition.value.version
      }, signal);
      if (!current(scope)) return;
      definitions.value = definitions.value.map(item => item.id === updated.id ? updated : item);
      editingDefinition.value = updated;
      ElMessage.success(t('reportingDefinitions.updateSuccess'));
    }
    definitionEditorOpen.value = false;
  });
}

async function confirmDeleteDefinition(definition: ReportingDefinitionSummary): Promise<void> {
  const scope = generation; if (!can('reporting.definitions.delete') || acting.value || writeUnconfirmed.value) return;
  try {
    await ElMessageBox.confirm(
      t('reportingDefinitions.confirmDeleteDefinition', { name: definition.name }),
      { type: 'warning' }
    );
  } catch {
    return;
  }
  if (!current(scope)) return;
  await write('reporting.definitions.delete', async signal => {
    await deleteReportingDefinition(definition.id, signal); if (!current(scope)) return;
    definitions.value = definitions.value.filter(item => item.id !== definition.id);
    ElMessage.success(t('reportingDefinitions.deleteSuccess'));
  });
}

async function publishCurrentDefinition(definition: ReportingDefinitionSummary): Promise<void> {
  if (!can('reporting.definitions.publish') || acting.value || writeUnconfirmed.value || publishOpen.value || definitionEditorOpen.value || groupEditorOpen.value
    || !definitions.value.some(row => sameReportingId(row.id, definition.id) && row.version === definition.version)) return;
  reads.get('draft')?.abort();
  const scope = generation, controller = reading('publish');
  try { const target = await getReportingDefinition(definition.id, controller.signal);
    if (current(scope) && can('reporting.definitions.publish') && !controller.signal.aborted) {
      if (!sameReportingId(target.id, definition.id) || target.version < definition.version || target.latestPublishedVersionNumber < definition.latestPublishedVersionNumber) throw Error('client.invalid_reporting_publication_target');
      publishTarget.value = target; publishNote.value = ''; publishOpen.value = true; }
  } catch (error) { if (current(scope) && !controller.signal.aborted) problem.value = toProblem(error, 'reportingDefinitions.loadFailed'); }
}
async function confirmPublish() {
  const target = publishTarget.value ? { ...publishTarget.value, parameterSchema: publishTarget.value.parameterSchema.map(parameter => ({ ...parameter })) } : undefined;
  if (!publishOpen.value || !target || !can('reporting.definitions.publish') || acting.value || writeUnconfirmed.value) return;
  const scope = generation, owner = publishEpoch, note = publishNote.value.trim() || null;
  const request = { controller: new AbortController(), receipt: { definitionId: target.id, definitionKey: target.definitionKey, outcome: 'processing' as const } };
  publicationRequest = request; publicationReceipt.value = { ...request.receipt }; stopReads(); acting.value = true; loading.value = false; problem.value = undefined;
  const owned = () => publicationRequest === request && current(scope) && owner === publishEpoch && publishOpen.value && can('reporting.definitions.publish');
  try {
    const result = await publishReportingDefinition(target.id, { version: target.version, changeNote: note }, request.controller.signal);
    if (!owned()) return;
    if (!validReportingVersion(result) || !sameReportingId(result.definitionId, target.id) || result.versionNumber !== target.latestPublishedVersionNumber + 1
      || !sameReportingId(result.dataSourceId, target.dataSourceId) || result.queryPortKey !== target.queryPortKey
      || !sameReportingParameters(result.parameterSchema, target.parameterSchema) || !sameReportingLayout(result.layoutConfigJson, target.layoutConfigJson)
      || !sameReportingId(result.publishedByUserId, session.currentUser?.id) || result.changeNote !== note) throw Error('client.invalid_reporting_publication');
    publicationReceipt.value = { ...request.receipt, outcome: 'confirmed', versionNumber: result.versionNumber };
    definitionFloors.set(target.id.toLowerCase(), { definitionVersion: target.version + 1, publishedVersion: result.versionNumber });
    definitions.value = definitions.value.map(row => sameReportingId(row.id, target.id)
      ? { ...row, version: Math.max(row.version, target.version + 1), latestPublishedVersionNumber: Math.max(row.latestPublishedVersionNumber, result.versionNumber) } : row);
    // 先记录权威版本再关闭；后续补读失败只影响目录，不降级已确认结果。
    publicationRequest = undefined; acting.value = false; publishOpen.value = false; publishNote.value = '';
    ElMessage.success(t('reportingDefinitions.publishSuccess')); await loadDefinitions();
  } catch (error) {
    if (owned()) {
      const uncertain = !isFullNetProblemDetails(error) || error.status < 400 || error.status === 408 || error.status >= 500;
      publicationReceipt.value = { ...request.receipt, outcome: uncertain ? 'unknown' : 'rejected' };
      writeUnconfirmed.value = uncertain || isFullNetProblemDetails(error) && error.status === 409;
      problem.value = toProblem(error, 'reportingDefinitions.saveFailed');
    }
  } finally { if (publicationRequest === request) { publicationRequest = undefined; acting.value = false; } }
}
/** 同一批写入只使用当前作用域；写前读取不能解除未知写入的锁定。 */
async function write(permission: string, action: (signal: AbortSignal) => Promise<void>) {
  if (!can(permission) || acting.value || writeUnconfirmed.value) return;
  const scope = generation; stopReads(); acting.value = true; loading.value = false; problem.value = undefined;
  try { await action(requests.signal); if (current(scope)) await loadDefinitions(true); }
  catch (error) { if (current(scope)) { writeUnconfirmed.value = unknown(error); problem.value = toProblem(error, 'reportingDefinitions.saveFailed'); } }
  finally { if (current(scope)) acting.value = false; }
}

async function openVersions(definition: ReportingDefinitionSummary): Promise<void> {
  if (!canRead() || acting.value) return;
  versionDefinitionId.value = definition.id; versionPage.value = 1; versions.value = []; versionTotal.value = 0; versionsDrawerOpen.value = true;
  await loadVersions();
}

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { status: 500, code: 'client.unexpected_error', title: t(fallbackKey) };
}
</script>

<template>
  <div class="reporting-definitions-view">
    <ElAlert v-if="!canRead()" type="info" :closable="false" :title="text('hostOnly')" />
    <template v-if="canRead()">
    <div v-if="publicationReceipt" role="status" data-testid="reporting-publication-receipt"><p>{{ publicationReceipt.definitionKey }} · <code>{{ publicationReceipt.definitionId }}</code></p>
      <p>{{ publicationReceipt.outcome === 'confirmed' ? t('reportingDefinitions.publishSuccess') : text(publicationReceipt.outcome === 'unknown' ? 'publishUnknown' : publicationReceipt.outcome === 'rejected' ? 'publishRejected' : 'publishProcessing') }} <span v-if="publicationReceipt.versionNumber">{{ publicationReceipt.versionNumber }}</span></p></div>
    <ElAlert v-if="problem" type="error" :title="problem.title" show-icon class="mb-4" />
    <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="text('unconfirmed')" data-testid="reporting-config-unconfirmed" />
    <ElButton :disabled="acting" :loading="loading" data-testid="reporting-config-refresh" @click="loadPage">{{ text('refresh') }}</ElButton>

    <div class="layout-grid" :class="{ 'layout-grid--single': !can('reporting.groups.read') }">
      <ElCard v-if="can('reporting.groups.read')" :header="t('reportingDefinitions.groupsTitle')" class="groups-card">
        <PermissionGate code="reporting.groups.create">
          <ElButton
            type="primary"
            :icon="Plus"
            class="mb-3"
            data-testid="reporting-group-create"
            :disabled="acting || writeUnconfirmed"
            @click="openCreateGroup"
          >
            {{ t('reportingDefinitions.addGroup') }}
          </ElButton>
        </PermissionGate>
        <ElTable :data="groups" size="small" highlight-current-row @row-click="row => selectGroup(row.id)">
          <ElTableColumn prop="name" :label="t('reportingDefinitions.fieldGroupName')" />
          <ElTableColumn prop="sortOrder" :label="t('reportingDefinitions.fieldSortOrder')" width="90" />
          <ElTableColumn :label="t('reportingDefinitions.fieldEnabled')" width="90">
            <template #default="{ row }">
              <ElTag :type="row.isEnabled ? 'success' : 'info'">
                {{ row.isEnabled ? t('reportingDefinitions.statusEnabled') : t('reportingDefinitions.statusDisabled') }}
              </ElTag>
            </template>
          </ElTableColumn>
          <!-- @vue-generic {ReportingGroup} -->
          <ElTableColumn :label="t('reportingDefinitions.actions')" width="140">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="reporting.groups.update">
                  <ArtTableActionButton type="edit" :title="t('reportingDefinitions.actionEdit')" :disabled="acting || writeUnconfirmed" @click="openEditGroup(row)">
                    {{ t('reportingDefinitions.actionEdit') }}
                  </ArtTableActionButton>
                </PermissionGate>
                <PermissionGate code="reporting.groups.delete">
                  <ArtTableActionButton type="delete" :title="t('reportingDefinitions.actionDelete')" :disabled="acting || writeUnconfirmed" @click="confirmDeleteGroup(row)">
                    {{ t('reportingDefinitions.actionDelete') }}
                  </ArtTableActionButton>
                </PermissionGate>
              </ArtTableActionGroup>
            </template>
          </ElTableColumn>
        </ElTable>
      </ElCard>

      <ElCard class="definitions-card">
        <ArtTableHeader :title="t('reportingDefinitions.title')">
          <template #left>
            <PermissionGate code="reporting.definitions.create">
              <ElButton
                type="primary"
                :icon="Plus"
                data-testid="reporting-definition-create"
                :disabled="acting || loading || writeUnconfirmed || !!catalogProblems.sources"
                @click="openCreateDefinition"
              >
                {{ t('reportingDefinitions.addDefinition') }}
              </ElButton>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <ArtSearchBar
          v-model="searchForm"
          :items="searchItems"
          @search="handleSearch"
          @reset="resetSearch"
        />

        <div ref="tableMainRef" class="table-main">
          <ElTable
            v-loading="loading"
            :data="filteredDefinitions"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            :header-cell-class-name="() => (tableHeaderBackground ? 'is-background' : '')"
          >
            <ElTableColumn prop="definitionKey" :label="t('reportingDefinitions.fieldDefinitionKey')" min-width="180" />
            <ElTableColumn prop="name" :label="t('reportingDefinitions.fieldName')" min-width="160" />
            <ElTableColumn prop="queryPortKey" :label="t('reportingDefinitions.fieldQueryPort')" min-width="200" />
            <ElTableColumn :label="t('reportingDefinitions.fieldPublishedVersion')" width="120">
              <template #default="{ row }">
                {{ row.latestPublishedVersionNumber || '-' }}
              </template>
            </ElTableColumn>
            <!-- @vue-generic {ReportingDefinition} -->
          <ElTableColumn :label="t('reportingDefinitions.actions')" width="260" fixed="right">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate v-if="row.latestPublishedVersionNumber > 0" code="reporting.definitions.grant_tenants">
                    <ArtTableActionButton type="view" test-id="reporting-tenant-grants-open" :disabled="acting"
                      :title="t('reportingGrants.manage')" @click="versionGrantDefinition = row">{{ t('reportingGrants.manage') }}</ArtTableActionButton>
                  </PermissionGate>
                  <PermissionGate code="reporting.definition_grants.read">
                    <ArtTableActionButton v-if="row.latestPublishedVersionNumber > 0" type="view"
                      :title="t('reportingGrants.title')" :disabled="acting"
                      data-testid="reporting-definition-grants" @click="grantsDefinitionId = row.id; grantsOpen = true">
                      {{ t('reportingGrants.title') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <PermissionGate code="reporting.definitions.update">
                    <ArtTableActionButton type="edit" :title="t('reportingDefinitions.actionEdit')" :disabled="acting || writeUnconfirmed" test-id="reporting-config-edit" @click="openEditDefinition(row)">
                      {{ t('reportingDefinitions.actionEdit') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <PermissionGate code="reporting.definitions.publish">
                    <ArtTableActionButton type="view" :title="t('reportingDefinitions.actionPublish')" :disabled="acting || writeUnconfirmed" test-id="reporting-config-publish" @click="publishCurrentDefinition(row)">
                      {{ t('reportingDefinitions.actionPublish') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <ArtTableActionButton type="view" :title="t('reportingDefinitions.actionVersions')" :disabled="acting" test-id="reporting-config-history" @click="openVersions(row)">
                    {{ t('reportingDefinitions.actionVersions') }}
                  </ArtTableActionButton>
                  <PermissionGate code="reporting.definitions.delete">
                    <ArtTableActionButton type="delete" :title="t('reportingDefinitions.actionDelete')" :disabled="acting || writeUnconfirmed" test-id="reporting-config-delete" @click="confirmDeleteDefinition(row)">
                      {{ t('reportingDefinitions.actionDelete') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </ElTableColumn>
          </ElTable>
          <ElPagination v-model:current-page="definitionPage" :total="definitionTotal" :page-size="20" :disabled="acting"
            layout="prev, pager, next, total" @current-change="() => { invalidateDetails(); void loadDefinitions(); }" />

        </div>
      </ElCard>
    </div>

    <ArtFormDialog
      v-model:open="groupEditorOpen"
      width="min(520px, calc(100vw - 32px))"
      :title="groupEditorMode === 'create' ? t('reportingDefinitions.createGroupTitle') : t('reportingDefinitions.editGroupTitle')"
      :saving="acting"
      :show-confirm="!writeUnconfirmed && can(`reporting.groups.${groupEditorMode === 'create' ? 'create' : 'update'}`)"
      confirm-test-id="reporting-config-group-save"
      @confirm="submitGroup"
    >
      <ElForm ref="groupFormRef" :model="groupForm" label-position="top" :disabled="acting">
        <ElAlert v-if="problem" type="error" :closable="false" :title="problem.detail ?? problem.title" />
        <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="text('unconfirmed')" />
        <ElButton v-if="editingGroup" :disabled="acting" data-testid="reporting-config-group-reload" @click="reloadGroupDraft">{{ text('reload') }}</ElButton>
        <ElFormItem :label="t('reportingDefinitions.fieldGroupName')" prop="name" required>
          <ElInput v-model="groupForm.name" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldSortOrder')" prop="sortOrder" required>
          <ElInput v-model="groupForm.sortOrder" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldEnabled')">
          <ElSwitch v-model="groupForm.isEnabled" />
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="definitionEditorOpen"
      :title="definitionEditorMode === 'create' ? t('reportingDefinitions.createTitle') : t('reportingDefinitions.editTitle')"
      :saving="acting"
      :show-confirm="!loading && !catalogProblems.sources && !writeUnconfirmed && can(`reporting.definitions.${definitionEditorMode === 'create' ? 'create' : 'update'}`)"
      confirm-test-id="reporting-config-draft-save"
      width="min(760px, calc(100vw - 32px))"
      @confirm="submitDefinition"
    >
      <ElForm ref="definitionFormRef" :model="definitionForm" label-position="top" :disabled="acting">
        <ElAlert v-if="problem" type="error" :closable="false" :title="problem.detail ?? problem.title" />
        <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="text('unconfirmed')" />
        <ElButton v-if="editingDefinition" :disabled="acting" data-testid="reporting-config-draft-reload" @click="reloadDraft">{{ text('reload') }}</ElButton>
        <ElFormItem
          v-if="definitionEditorMode === 'create'"
          :label="t('reportingDefinitions.fieldDefinitionKey')"
          prop="definitionKey"
          required
        >
          <ElInput v-model="definitionForm.definitionKey" data-testid="reporting-config-key" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldName')" prop="name" required>
          <ElInput v-model="definitionForm.name" data-testid="reporting-config-name" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldDescription')">
          <ElInput v-model="definitionForm.description" type="textarea" :rows="2" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldQueryPort')" prop="queryPortKey" required>
          <ElSelect v-model="definitionForm.queryPortKey" filterable>
            <ElOption
              v-for="port in queryPorts"
              :key="port.queryPortKey"
              :label="port.displayName"
              :value="port.queryPortKey"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldDataSource')" prop="dataSourceId" required>
          <ElSelect v-model="definitionForm.dataSourceId" filterable>
            <ElOption
              v-for="option in compatibleDataSources"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldLayoutConfig')">
          <ElInput v-model="definitionForm.layoutConfigJson" type="textarea" :rows="3" data-testid="reporting-config-layout" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldEnabled')">
          <ElSwitch v-model="definitionForm.isEnabled" />
        </ElFormItem>
        <ElFormItem v-if="definitionForm.parameterSchema.length > 0" :label="t('reportingDefinitions.fieldParameterSchema')">
          <ElTable :data="definitionForm.parameterSchema" size="small">
            <ElTableColumn prop="parameterKey" :label="t('reportingDefinitions.fieldParameterKey')" />
            <ElTableColumn :label="t('reportingDefinitions.fieldParameterDisplayName')">
              <template #default="{ row }">
                <ElInput v-model="row.displayName" />
              </template>
            </ElTableColumn>
            <ElTableColumn :label="t('reportingDefinitions.fieldParameterDefault')">
              <template #default="{ row }">
                <ElInput v-model="row.defaultValue" />
              </template>
            </ElTableColumn>
          </ElTable>
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>

    <ElDrawer v-model="versionsDrawerOpen" :title="t('reportingDefinitions.versionsTitle')" size="40%">
      <ElTable :data="versions" size="small">
        <ElTableColumn prop="versionNumber" :label="t('reportingDefinitions.fieldVersionNumber')" width="100" />
        <ElTableColumn prop="queryPortKey" :label="t('reportingDefinitions.fieldQueryPort')" />
        <ElTableColumn prop="publishedAtUtc" :label="t('reportingDefinitions.fieldPublishedAt')" min-width="180" />
        <ElTableColumn prop="changeNote" :label="t('reportingDefinitions.fieldChangeNote')" />
      </ElTable>
      <ElPagination v-model:current-page="versionPage" :total="versionTotal" :page-size="20"
        layout="prev, pager, next, total" @current-change="loadVersions" />

    </ElDrawer>
    <ReportingTenantGrantsDialog v-if="versionGrantDefinition" :definition="versionGrantDefinition" @close="versionGrantDefinition = undefined" />
    <ReportingDefinitionGrantsDialog v-model="grantsOpen" :definition-id="grantsDefinitionId" />
    <ElDialog ref="publicationDialog" @open="publicationLandmarks" @opened="publicationLandmarks" :model-value="publishOpen" v-on="{ 'update:modelValue': publicationControls.update }" :title="text('publish')" width="min(480px, calc(100vw - 32px))" :close-on-click-modal="false" :close-on-press-escape="true" :show-close="true">
      <p>{{ publishTarget?.name }}</p><p data-testid="reporting-publication-target-version">{{ text('draftVersion') }} {{ publishTarget?.version }} · {{ t('reportingDefinitions.fieldPublishedVersion') }} {{ publishTarget?.latestPublishedVersionNumber }}</p><p>{{ text('publishHelp') }}</p>
      <ElAlert v-if="acting" type="info" :closable="false" :title="text('closePublishing')" />
      <ElAlert v-if="publicationReceipt?.outcome === 'unknown'" type="warning" :closable="false" :title="text('publishUnknown')" />
      <ElAlert v-if="problem" type="error" :closable="false" :title="problem.detail ?? problem.title" />
      <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="text('unconfirmed')" />
      <ElForm label-position="top"><ElFormItem :label="text('note')"><ElInput :model-value="publishNote" v-on="{ 'update:modelValue': publicationControls.note }" :disabled="acting" maxlength="512" data-testid="reporting-config-publish-note" /></ElFormItem></ElForm>
      <template #footer><ElButton data-testid="reporting-publication-close" v-on="{ click: publicationControls.close }">{{ text('close') }}</ElButton><ElButton type="primary" :loading="acting" :disabled="acting || writeUnconfirmed || !can('reporting.definitions.publish')" data-testid="reporting-config-publish-confirm" v-on="{ click: publicationControls.confirm }">{{ text('publish') }}</ElButton></template>
    </ElDialog>
    </template>
  </div>
</template>

<style scoped>
.layout-grid {
  display: grid;
  grid-template-columns: minmax(0, 320px) minmax(0, 1fr);
  gap: 16px;
}
.layout-grid--single { grid-template-columns: minmax(0, 1fr); }

.groups-card,
.definitions-card {
  min-width: 0;
  min-height: 520px;
}

@media (max-width: 1024px) {
  .layout-grid { grid-template-columns: minmax(0, 1fr); }
}

.table-main {
  margin-top: 12px;
}

.mb-3 {
  margin-bottom: 12px;
}

.mb-4 {
  margin-bottom: 16px;
}
</style>
