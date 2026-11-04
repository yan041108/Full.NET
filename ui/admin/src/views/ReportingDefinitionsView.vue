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
import { sameReportingId, sameReportingParameters, sameReportingLayout, validReportingVersion, validReportingGroup, validReportingDefinition, matchesReportingDraft } from '../guards/reporting-management-guards';
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
const deletedDefinitions = new Set<string>(), deletedGroups = new Set<string>();
const groupFloors = new Map<string, number>();
type Confirmation = { token: number; message: string; confirm: () => void; update: (open: boolean) => void };
const confirmation = ref<Confirmation>();
let confirmationToken = 0, cancelConfirmation: (() => void) | undefined;
let publishEpoch = 0, publicationRequest: { controller: AbortController; receipt: PublicationReceipt } | undefined;
const text = (key: Parameters<typeof reportingConfigurationText>[1]) => reportingConfigurationText(locale.value, key);
let generation = 0, groupEpoch = 0, definitionEpoch = 0, requests = new AbortController();
const catalogEpoch = ref(0);
const catalogProblems = reactive<{ groups?: FullNetProblemDetails; ports?: FullNetProblemDetails; sources?: FullNetProblemDetails; definitions?: FullNetProblemDetails }>({});
const reads = new Map<string, AbortController>();
const canRead = () => active.value && !session.switching && (!route || route.name === 'reporting-definitions')
  && session.currentUser?.scope === 'host' && session.currentUser.actorScope === 'host' && !session.currentUser.tenantId && session.can('reporting.definitions.read');
const can = (permission: string) => canRead() && session.can(permission);
function current(scope: number) { return scope === generation && canRead(); }
/** 确认只属于发起它的页面与目标，关闭后的旧回调不能批准下一次操作。 */
function askConfirmation(message: string): Promise<boolean> {
  cancelConfirmation?.();
  const scope = generation, token = ++confirmationToken;
  return new Promise(resolve => {
    const finish = (accepted: boolean) => {
      if (confirmation.value?.token !== token) return;
      confirmation.value = undefined; cancelConfirmation = undefined;
      resolve(accepted && current(scope));
    };
    cancelConfirmation = () => finish(false);
    confirmation.value = { token, message, confirm: () => finish(true), update: open => { if (!open) finish(false); } };
  });
}
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
const draftLoading = ref(false), groupDraftLoading = ref(false), draftTargetId = ref('');
const versionsLoading = ref(false), versionsProblem = ref<FullNetProblemDetails>(), versionDefinitionName = ref(''), historyEpoch = ref(0);
const observedVersionIds = new Map<string, Map<number, string>>();
const historyContent = ref<HTMLElement>();
const historyLandmarks = useArtDialogLandmarks(() => historyContent.value?.closest('.el-drawer'));
type DraftResource = 'group' | 'definition';
type DraftReceipt = { resource: DraftResource; kind: 'create' | 'update'; outcome: 'processing' | 'confirmed' | 'unknown' | 'rejected'; name: string; id?: string; version?: number };
type DraftSnapshot = { resource: 'group'; form: typeof groupForm } | { resource: 'definition'; form: typeof definitionForm; groupId?: string };
const draftReceipt = ref<DraftReceipt>(), creationBlocks = reactive(new Set<DraftResource>());
const unconfirmedDrafts = new Map<DraftResource, DraftSnapshot>();
let draftRequest: { controller: AbortController; receipt: DraftReceipt; snapshot: DraftSnapshot } | undefined;
const groupSignature = () => JSON.stringify([groupEditorMode.value, editingGroup.value, groupForm]);
const definitionSignature = () => JSON.stringify([definitionEditorMode.value, editingDefinition.value, definitionGroupId.value, definitionForm]);
function snapshotDraft(resource: DraftResource): DraftSnapshot {
  return resource === 'group' ? { resource, form: { ...groupForm } }
    : { resource, groupId: definitionGroupId.value, form: { ...definitionForm, parameterSchema: definitionForm.parameterSchema.map(row => ({ ...row })) } };
}
function markDraftUnknown(request: NonNullable<typeof draftRequest>) {
  draftReceipt.value = { ...request.receipt, outcome: 'unknown' }; writeUnconfirmed.value = true;
  if (request.receipt.kind === 'create') { creationBlocks.add(request.receipt.resource); unconfirmedDrafts.set(request.receipt.resource, request.snapshot); }
}
/** 关闭或改输入只停止客户端接入，不承诺撤销服务端已发出的写入。 */
function cancelDraftWrite(resource: DraftResource) {
  const request = draftRequest; if (!request || request.receipt.resource !== resource) return;
  draftRequest = undefined; request.controller.abort(); acting.value = false; markDraftUnknown(request);
}
async function saveDraft(receipt: Omit<DraftReceipt, 'outcome'>, snapshot: DraftSnapshot, action: (signal: AbortSignal) => Promise<ReportingGroup | ReportingDefinition>,
  accept: (value: ReportingGroup | ReportingDefinition) => void) {
  const scope = generation, request = { controller: new AbortController(), receipt: { ...receipt, outcome: 'processing' as const }, snapshot };
  draftRequest = request; draftReceipt.value = { ...request.receipt }; stopReads(); acting.value = true; loading.value = false; problem.value = undefined;
  try {
    const result = await action(request.controller.signal);
    if (draftRequest !== request || !current(scope)) return;
    accept(result);
    // 成功归属核对完成后先解除在途所有权，再关闭表单与补读，避免自己的关闭产生未知回执。
    draftRequest = undefined; acting.value = false;
    draftReceipt.value = { ...receipt, outcome: 'confirmed', id: result.id, version: result.version };
    creationBlocks.delete(receipt.resource); unconfirmedDrafts.delete(receipt.resource);
    if (receipt.resource === 'group') groupEditorOpen.value = false; else definitionEditorOpen.value = false;
    ElMessage.success(t(receipt.resource === 'group' ? receipt.kind === 'create' ? 'reportingDefinitions.groupCreateSuccess' : 'reportingDefinitions.groupUpdateSuccess'
      : receipt.kind === 'create' ? 'reportingDefinitions.createSuccess' : 'reportingDefinitions.updateSuccess'));
    await loadDefinitions();
  } catch (error) {
    if (draftRequest === request && current(scope)) {
      const uncertain = !isFullNetProblemDetails(error) || error.status < 400 || error.status === 408 || error.status >= 500;
      if (uncertain) markDraftUnknown(request); else {
        draftReceipt.value = { ...receipt, outcome: 'rejected' }; writeUnconfirmed.value = error.status === 409;
      }
      problem.value = toProblem(error, 'reportingDefinitions.saveFailed');
    }
  } finally { if (draftRequest === request) { draftRequest = undefined; acting.value = false; } }
}
async function acknowledgeRecovery(resource: DraftResource) {
  const scope = generation, epoch = resource === 'group' ? groupEpoch : definitionEpoch;
  const signature = resource === 'group' ? groupSignature() : definitionSignature();
  const owned = () => current(scope) && !acting.value && can(`reporting.${resource === 'group' ? 'groups' : 'definitions'}.create`) && creationBlocks.has(resource)
    && (resource === 'group' ? groupEditorOpen.value && groupEditorMode.value === 'create' && epoch === groupEpoch && signature === groupSignature()
      : definitionEditorOpen.value && definitionEditorMode.value === 'create' && epoch === definitionEpoch && signature === definitionSignature());
  if (!owned() || !await askConfirmation(text('recoveryConfirm')) || !owned()) return;
  creationBlocks.delete(resource); unconfirmedDrafts.delete(resource);
  if (draftReceipt.value?.resource === resource && draftReceipt.value.kind === 'create' && draftReceipt.value.outcome === 'unknown') writeUnconfirmed.value = false;
}

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
const definitionCatalogReady = computed(() => !loading.value && can('reporting.groups.read') && can('reporting.query_ports.read') && can('reporting.data_sources.read')
  && !catalogProblems.groups && !catalogProblems.ports && !catalogProblems.sources);
const definitionCatalogCompatible = computed(() => {
  const port = selectedQueryPort.value, source = dataSourceOptions.value.find(row => sameReportingId(row.value, definitionForm.dataSourceId));
  if (!definitionCatalogReady.value || !groups.value.some(row => sameReportingId(row.id, definitionGroupId.value) && row.isEnabled)
    || !port || !source || !port.supportedProviderKeys.includes(source.providerKey)) return false;
  const schema = definitionForm.parameterSchema;
  return schema.length === port.parameters.length && new Set(schema.map(row => row.parameterKey)).size === schema.length
    && schema.every(row => { const parameter = port.parameters.find(item => item.parameterKey === row.parameterKey);
      return parameter && row.dataTypeKey === parameter.dataTypeKey && (!parameter.isRequired || row.isRequired) && row.displayName.trim().length > 0; });
});
const groupDraftFresh = computed(() => groupEditorMode.value === 'create' || !!editingGroup.value
  && groups.value.some(row => sameReportingId(row.id, editingGroup.value!.id) && row.version <= editingGroup.value!.version)
  && editingGroup.value.version >= (groupFloors.get(editingGroup.value.id.toLowerCase()) ?? 0));
const definitionDraftFresh = computed(() => {
  // 分页目录可能不含当前编辑目标，已观察修订仍是有效下界，不能因翻页而解除。
  const rows = definitions.value, target = editingDefinition.value;
  if (definitionEditorMode.value === 'create') return true;
  if (!target || deletedDefinitions.has(target.id.toLowerCase())) return false;
  const row = rows.find(item => sameReportingId(item.id, target.id));
  return target.version >= Math.max(row?.version ?? 0, definitionFloors.get(target.id.toLowerCase())?.definitionVersion ?? 0);
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
  draftLoading.value = false; groupDraftLoading.value = false; draftTargetId.value = '';
  observedVersionIds.clear(); versionsProblem.value = undefined; versionsLoading.value = false; versionDefinitionName.value = ''; historyEpoch.value++;
  draftRequest?.controller.abort(); draftRequest = undefined; draftReceipt.value = undefined; creationBlocks.clear(); unconfirmedDrafts.clear(); groupFloors.clear();
  cancelConfirmation?.(); deletedDefinitions.clear(); deletedGroups.clear();
  publicationRequest?.controller.abort(); publicationRequest = undefined; publicationReceipt.value = undefined; definitionFloors.clear();
  generation++; catalogEpoch.value++; for (const key of ['groups', 'ports', 'sources', 'definitions'] as const) catalogProblems[key] = undefined; requests.abort(); requests = new AbortController(); stopReads();
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
watch(versionsDrawerOpen, value => { historyEpoch.value++; if (!value) { reads.get('history')?.abort(); versions.value = []; versionTotal.value = 0; versionDefinitionId.value = ''; versionDefinitionName.value = ''; versionsProblem.value = undefined; versionsLoading.value = false; } }, { flush: 'sync' });
watch(groupEditorOpen, value => { groupEpoch++; if (!value) { reads.get('group-draft')?.abort(); groupDraftLoading.value = false; } }, { flush: 'sync' });
watch(definitionEditorOpen, value => { definitionEpoch++; if (!value) { reads.get('draft')?.abort(); draftLoading.value = false; } }, { flush: 'sync' });
watch(() => JSON.stringify([groupEditorOpen.value, groupSignature()]), () => cancelDraftWrite('group'), { flush: 'sync' });
watch(() => JSON.stringify([definitionEditorOpen.value, definitionSignature()]), () => cancelDraftWrite('definition'), { flush: 'sync' });
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
      if (ids.has(id) || !row.isEnabled) throw Error('client.invalid_reporting_data_source_catalog');
      ids.add(id); items.push(row);
    }
    if (items.length === expectedTotal) return { items };
  }
  throw Error('client.invalid_reporting_data_source_catalog');
}

async function loadPage(): Promise<void> {
  if (!canRead() || acting.value) return;
  cancelConfirmation?.();
  const scope = generation, controller = reading('catalogs');
  catalogEpoch.value++; catalogProblems.groups = undefined; catalogProblems.ports = undefined; catalogProblems.sources = undefined;
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
    if (groupResult.status === 'rejected') catalogProblems.groups = toProblem(groupResult.reason, 'reportingDefinitions.loadFailed');
    if (portResult.status === 'rejected') catalogProblems.ports = toProblem(portResult.reason, 'reportingDefinitions.loadFailed');
    if (sourceResult.status === 'rejected') catalogProblems.sources = toProblem(sourceResult.reason, 'reportingDefinitions.loadFailed');
    const groupRows = groupResult.status === 'fulfilled' ? groupResult.value : [];
    const portRows = portResult.status === 'fulfilled' ? portResult.value : [];
    const dataSourcePage = sourceResult.status === 'fulfilled' ? sourceResult.value : { items: [] };
    if (catalogs.some(result => result.status === 'rejected')) problem.value = toProblem(catalogs.find(result => result.status === 'rejected')?.reason, 'reportingDefinitions.loadFailed');
    const groupsTrusted = !groupRows.some(row => deletedGroups.has(row.id.toLowerCase()) || row.version < (groupFloors.get(row.id.toLowerCase()) ?? 0));
    if (groupsTrusted) { groups.value = groupRows; for (const row of groupRows) groupFloors.set(row.id.toLowerCase(), row.version); }
    else { catalogProblems.groups = toProblem(Error('client.stale_reporting_group_catalog'), 'reportingDefinitions.loadFailed'); problem.value = catalogProblems.groups; }
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
    if (loaded && groupsTrusted && catalogs.every(result => result.status === 'fulfilled') && !definitionEditorOpen.value && !groupEditorOpen.value && current(scope) && !controller.signal.aborted) writeUnconfirmed.value = false;
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
    if (result.items.some(row => deletedDefinitions.has(row.id.toLowerCase()) || deletedGroups.has(row.groupId.toLowerCase()))) throw Error('client.stale_reporting_deleted_catalog');
    // 已确认发布的修订是本视图下界，迟到或陈旧目录不能撤销这一事实。
    if (result.items.some(row => { const floor = definitionFloors.get(row.id.toLowerCase());
      return floor && (row.version < floor.definitionVersion || row.latestPublishedVersionNumber < floor.publishedVersion); })) throw Error('client.stale_reporting_publication_catalog');
    for (const row of result.items) definitionFloors.set(row.id.toLowerCase(), { definitionVersion: row.version, publishedVersion: row.latestPublishedVersionNumber });
    definitions.value = result.items; definitionTotal.value = result.total; catalogProblems.definitions = undefined;
    return true;
  } catch (error) { if (current(scope) && !controller.signal.aborted) { catalogProblems.definitions = toProblem(error, 'reportingDefinitions.loadFailed'); problem.value = catalogProblems.definitions; } return false; }
}

async function loadVersions(): Promise<void> {
  if (!canRead() || !versionsDrawerOpen.value || !versionDefinitionId.value) return;
  const scope = generation, id = versionDefinitionId.value, page = versionPage.value, epoch = historyEpoch.value, controller = reading('history');
  const owned = () => current(scope) && !controller.signal.aborted && versionsDrawerOpen.value && id === versionDefinitionId.value && page === versionPage.value && epoch === historyEpoch.value;
  versionsLoading.value = true; versionsProblem.value = undefined; versions.value = [];
  try { const result = await listReportingDefinitionVersions(id, controller.signal, page, 20);
    if (!owned()) return;
    const known = observedVersionIds.get(id.toLowerCase()) ?? new Map<number, string>();
    if (result.total < (definitionFloors.get(id.toLowerCase())?.publishedVersion ?? 0)
      || result.items.some(row => !sameReportingId(row.definitionId, id) || known.has(row.versionNumber) && !sameReportingId(known.get(row.versionNumber), row.id))
      || [...known].some(([number, key]) => result.total < number || Math.floor((result.total - number) / 20) + 1 === page
        && !result.items.some(row => row.versionNumber === number && sameReportingId(row.id, key)))) throw Error('client.stale_reporting_version_history');
    for (const row of result.items) known.set(row.versionNumber, row.id);
    observedVersionIds.set(id.toLowerCase(), known); versions.value = result.items; versionTotal.value = result.total;
  } catch (error) { if (owned()) versionsProblem.value = toProblem(error, 'reportingDefinitions.loadFailed'); }
  finally { if (owned()) versionsLoading.value = false; }
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
  if (!can('reporting.groups.create') || acting.value || writeUnconfirmed.value && !creationBlocks.has('group')) return;
  invalidateDetails(); definitionEditorOpen.value = false; versionsDrawerOpen.value = false;
  groupEpoch++; groupEditorMode.value = 'create';
  editingGroup.value = null;
  groupForm.name = '';
  groupForm.sortOrder = '0';
  groupForm.isEnabled = true;
  const saved = unconfirmedDrafts.get('group'); if (saved?.resource === 'group') Object.assign(groupForm, saved.form);
  groupEditorOpen.value = true;
}

function openEditGroup(group: ReportingGroup): void {
  if (!can('reporting.groups.update') || acting.value || writeUnconfirmed.value || !groups.value.some(row => sameReportingId(row.id, group.id) && row.version === group.version)) return;
  invalidateDetails(); definitionEditorOpen.value = false; versionsDrawerOpen.value = false;
  groupEpoch++; groupEditorMode.value = 'edit';
  editingGroup.value = group;
  groupForm.name = group.name;
  groupForm.sortOrder = String(group.sortOrder);
  groupForm.isEnabled = group.isEnabled;
  groupEditorOpen.value = true;
}

async function submitGroup(): Promise<void> {
  const permission = `reporting.groups.${groupEditorMode.value === 'create' ? 'create' : 'update'}`, scope = generation, editor = groupEpoch;
  if (!groupFormRef.value || acting.value || groupDraftLoading.value || !groupDraftFresh.value || writeUnconfirmed.value || groupEditorMode.value === 'create' && creationBlocks.has('group') || !can(permission)) {
    return;
  }
  // 校验等待期间只能确认原编辑目标和原草稿；隐藏的父组关系也属于本次写入快照。
  const target = editingGroup.value ? { ...editingGroup.value } : undefined;
  const mode = groupEditorMode.value, signature = groupSignature();
  const payload = { name: groupForm.name.trim(), sortOrder: Number(groupForm.sortOrder), isEnabled: groupForm.isEnabled };
  if (!Number.isSafeInteger(payload.sortOrder)) return;
  const valid = await groupFormRef.value.validate().catch(() => false);
  if (!valid || !current(scope) || editor !== groupEpoch || signature !== groupSignature() || !groupEditorOpen.value || groupDraftLoading.value || !groupDraftFresh.value || acting.value || !can(permission)) {
    return;
  }
  if (writeUnconfirmed.value || mode === 'create' && creationBlocks.has('group') || mode === 'edit' && !target) return;
  await saveDraft({ resource: 'group', kind: mode === 'create' ? 'create' : 'update', name: payload.name, id: target?.id }, snapshotDraft('group'),
    signal => mode === 'create' ? createReportingGroup(payload, signal) : updateReportingGroup(target!.id, { ...payload, parentId: target!.parentId, version: target!.version }, signal), result => {
    if (!validReportingGroup(result) || result.version !== (target ? target.version + 1 : 1) || target && !sameReportingId(result.id, target.id)
      || mode === 'create' && groupFloors.has(result.id.toLowerCase())
      || !sameReportingId(result.parentId, target?.parentId) || result.name !== payload.name || result.sortOrder !== payload.sortOrder || result.isEnabled !== payload.isEnabled) throw Error('client.invalid_reporting_group_save');
    groupFloors.set(result.id.toLowerCase(), result.version);
    groups.value = mode === 'create' ? [...groups.value, result] : groups.value.map(item => sameReportingId(item.id, result.id) ? result : item);
    if (mode === 'create') selectedGroupId.value = result.id;
  });
}

async function confirmDeleteGroup(group: ReportingGroup): Promise<void> {
  const scope = generation; if (!can('reporting.groups.delete') || acting.value || writeUnconfirmed.value) return;
  const target = { ...group };
  if (!await askConfirmation(t('reportingDefinitions.confirmDeleteGroup', { name: target.name }))) return;
  if (!current(scope) || !groups.value.some(row => sameReportingId(row.id, target.id) && row.version === target.version)) return;
  await write('reporting.groups.delete', async signal => {
    const deleted = await deleteReportingGroup(target.id, signal); if (!current(scope)) return;
    if (!deleted) throw { status: 409, code: 'client.reporting_delete_not_applied', title: text('deleteNotApplied') };
    deletedGroups.add(target.id.toLowerCase());
    groups.value = groups.value.filter(item => !sameReportingId(item.id, target.id));
    definitions.value = definitions.value.filter(item => !sameReportingId(item.groupId, target.id));
    if (selectedGroupId.value === target.id) {
      selectedGroupId.value = groups.value[0]?.id;
    }
    ElMessage.success(t('reportingDefinitions.groupDeleteSuccess'));
  });
}

function openCreateDefinition(): void {
  if (!can('reporting.definitions.create') || acting.value || loading.value || writeUnconfirmed.value && !creationBlocks.has('definition')) return;
  if (!definitionCatalogReady.value) { ElMessage.warning(text('catalogMissing')); return; }
  if (!selectedGroupId.value) {
    ElMessage.warning(t('reportingDefinitions.selectGroupFirst'));
    return;
  }
  invalidateDetails(); groupEditorOpen.value = false; versionsDrawerOpen.value = false;
  definitionEpoch++;
  definitionEditorMode.value = 'create';
  editingDefinition.value = null;
  definitionGroupId.value = selectedGroupId.value;
  definitionForm.definitionKey = '';
  definitionForm.name = '';
  definitionForm.description = '';
  definitionForm.queryPortKey = queryPorts.value.find(port => dataSourceOptions.value.some(source => port.supportedProviderKeys.includes(source.providerKey)))?.queryPortKey ?? '';
  definitionForm.parameterSchema = (selectedQueryPort.value?.parameters ?? []).map(parameter => ({ parameterKey: parameter.parameterKey, displayName: parameter.displayName,
    dataTypeKey: parameter.dataTypeKey, isRequired: parameter.isRequired, defaultValue: parameter.defaultValue }));
  definitionForm.dataSourceId = compatibleDataSources.value[0]?.value ?? '';
  definitionForm.layoutConfigJson = '{}';
  definitionForm.isEnabled = true;
  const saved = unconfirmedDrafts.get('definition'); if (saved?.resource === 'definition') {
    definitionGroupId.value = saved.groupId; Object.assign(definitionForm, saved.form, { parameterSchema: saved.form.parameterSchema.map(row => ({ ...row })) });
  }
  definitionEditorOpen.value = true;
}

async function openEditDefinition(summary: ReportingDefinitionSummary): Promise<void> {
  if (!can('reporting.definitions.update') || acting.value || writeUnconfirmed.value || !definitions.value.some(row => sameReportingId(row.id, summary.id) && row.version === summary.version)) return;
  invalidateDetails(); groupEditorOpen.value = false; versionsDrawerOpen.value = false;
  definitionEpoch++; definitionEditorMode.value = 'edit'; editingDefinition.value = null; draftTargetId.value = summary.id; definitionGroupId.value = undefined;
  Object.assign(definitionForm, { definitionKey: '', name: '', description: '', dataSourceId: '', queryPortKey: '', parameterSchema: [], layoutConfigJson: '{}', isEnabled: true });
  definitionEditorOpen.value = true;
  await readDraft(summary.id);
}
async function readDraft(id: string, owned: () => boolean = () => true) {
  const scope = generation, controller = reading('draft');
  draftLoading.value = true;
  try {
  const definition = await getReportingDefinition(id, controller.signal);
  if (!current(scope) || !can('reporting.definitions.update') || controller.signal.aborted || !owned()) return;
  if (!sameReportingId(definition.id, id) || definition.version < (definitionFloors.get(id.toLowerCase())?.definitionVersion ?? 0)) throw Error('client.invalid_reporting_draft');
  definitionEpoch++;
  definitionEditorMode.value = 'edit';
  editingDefinition.value = definition;
  draftTargetId.value = definition.id;
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
  } catch (error) { if (current(scope) && !controller.signal.aborted && owned()) problem.value = toProblem(error, 'reportingDefinitions.loadFailed'); }
  finally { if (current(scope) && reads.get('draft') === controller && !controller.signal.aborted) draftLoading.value = false; }
}
async function reloadDraft() {
  const scope = generation, id = draftTargetId.value; if (!id || !can('reporting.definitions.update') || acting.value || draftLoading.value) return;
  const epoch = definitionEpoch, signature = JSON.stringify([definitionGroupId.value, definitionForm, editingDefinition.value?.version]);
  const owned = () => current(scope) && can('reporting.definitions.update') && definitionEditorOpen.value && definitionEpoch === epoch
    && draftTargetId.value === id && signature === JSON.stringify([definitionGroupId.value, definitionForm, editingDefinition.value?.version]);
  if (await askConfirmation(text('discard')) && owned()) await readDraft(id, owned);
}
async function resetParameters() {
  const scope = generation, epoch = definitionEpoch, catalog = catalogEpoch.value, signature = definitionSignature();
  const owned = () => current(scope) && can(`reporting.definitions.${definitionEditorMode.value === 'create' ? 'create' : 'update'}`) && !acting.value
    && definitionEditorOpen.value && definitionCatalogReady.value && epoch === definitionEpoch && catalog === catalogEpoch.value && signature === definitionSignature();
  if (!owned() || !selectedQueryPort.value || !await askConfirmation(text('resetParametersConfirm')) || !owned()) return;
  definitionForm.parameterSchema = selectedQueryPort.value!.parameters.map(parameter => ({ parameterKey: parameter.parameterKey, displayName: parameter.displayName,
    dataTypeKey: parameter.dataTypeKey, isRequired: parameter.isRequired, defaultValue: parameter.defaultValue }));
}
async function reloadGroupDraft() {
  const scope = generation, id = editingGroup.value?.id; if (!id || !can('reporting.groups.update') || acting.value) return;
  const epoch = groupEpoch, signature = JSON.stringify([groupForm, editingGroup.value?.version]);
  const owned = () => current(scope) && can('reporting.groups.update') && groupEditorOpen.value && groupEpoch === epoch
    && editingGroup.value?.id === id && signature === JSON.stringify([groupForm, editingGroup.value?.version]);
  if (!await askConfirmation(text('discard')) || !owned()) return;
  const controller = reading('group-draft');
  groupDraftLoading.value = true;
  try { const rows = await listReportingGroups(controller.signal); const group = rows.find(item => item.id === id);
    if (!controller.signal.aborted && owned() && group) {
      if (rows.some(row => deletedGroups.has(row.id.toLowerCase()) || row.version < (groupFloors.get(row.id.toLowerCase()) ?? 0))) throw Error('client.stale_reporting_group_catalog');
      for (const row of rows) groupFloors.set(row.id.toLowerCase(), row.version);
      groups.value = rows; writeUnconfirmed.value = false; openEditGroup(group);
    }
  } catch (error) { if (current(scope) && !controller.signal.aborted && owned()) problem.value = toProblem(error, 'reportingDefinitions.loadFailed'); }
  finally { if (current(scope) && reads.get('group-draft') === controller && !controller.signal.aborted) groupDraftLoading.value = false; }
}

async function submitDefinition(): Promise<void> {
  const scope = generation, editor = definitionEpoch, catalog = catalogEpoch.value, permission = `reporting.definitions.${definitionEditorMode.value === 'create' ? 'create' : 'update'}`;
  if (!definitionFormRef.value || acting.value || draftLoading.value || !definitionDraftFresh.value || writeUnconfirmed.value || definitionEditorMode.value === 'create' && creationBlocks.has('definition') || !definitionCatalogCompatible.value || !definitionGroupId.value || !can(permission)) {
    return;
  }
  const mode = definitionEditorMode.value, target = editingDefinition.value ? { ...editingDefinition.value } : undefined;
  const signature = definitionSignature(), snapshot = snapshotDraft('definition'), key = definitionForm.definitionKey.trim();
  const payload = { groupId: definitionGroupId.value, dataSourceId: definitionForm.dataSourceId, name: definitionForm.name.trim(),
    description: definitionForm.description.trim() || null, queryPortKey: definitionForm.queryPortKey,
    parameterSchema: definitionForm.parameterSchema.map(item => ({ ...item })), layoutConfigJson: definitionForm.layoutConfigJson, isEnabled: definitionForm.isEnabled };
  const valid = await definitionFormRef.value.validate().catch(() => false);
  if (!valid || !current(scope) || editor !== definitionEpoch || signature !== definitionSignature() || catalog !== catalogEpoch.value || draftLoading.value || !definitionDraftFresh.value || !definitionCatalogCompatible.value || !definitionEditorOpen.value || acting.value || writeUnconfirmed.value || !can(permission)) {
    return;
  }
  if (mode === 'create' && creationBlocks.has('definition') || mode === 'edit' && !target) return;
  await saveDraft({ resource: 'definition', kind: mode === 'create' ? 'create' : 'update', name: payload.name, id: target?.id }, snapshot,
    signal => mode === 'create' ? createReportingDefinition({ ...payload, definitionKey: key }, signal) : updateReportingDefinition(target!.id, { ...payload, version: target!.version }, signal), result => {
    if (!validReportingDefinition(result) || !matchesReportingDraft(result, payload) || result.definitionKey !== (target?.definitionKey ?? key)
      || mode === 'create' && definitionFloors.has(result.id.toLowerCase())
      || result.version !== (target ? target.version + 1 : 1) || target && !sameReportingId(result.id, target.id)
      || result.latestPublishedVersionNumber !== (target?.latestPublishedVersionNumber ?? 0)) throw Error('client.invalid_reporting_definition_save');
    definitionFloors.set(result.id.toLowerCase(), { definitionVersion: result.version, publishedVersion: result.latestPublishedVersionNumber });
    definitions.value = mode === 'create' ? [...definitions.value, result] : definitions.value.map(item => sameReportingId(item.id, result.id) ? result : item);
  });
}

async function confirmDeleteDefinition(definition: ReportingDefinitionSummary): Promise<void> {
  const scope = generation; if (!can('reporting.definitions.delete') || acting.value || writeUnconfirmed.value) return;
  const target = { ...definition };
  if (!await askConfirmation(t('reportingDefinitions.confirmDeleteDefinition', { name: target.name }))) return;
  if (!current(scope) || !definitions.value.some(row => sameReportingId(row.id, target.id) && row.version === target.version)) return;
  await write('reporting.definitions.delete', async signal => {
    const deleted = await deleteReportingDefinition(target.id, signal); if (!current(scope)) return;
    if (!deleted) throw { status: 409, code: 'client.reporting_delete_not_applied', title: text('deleteNotApplied') };
    deletedDefinitions.add(target.id.toLowerCase());
    definitions.value = definitions.value.filter(item => !sameReportingId(item.id, target.id));
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
  publicationRequest = request; publicationReceipt.value = { ...request.receipt }; draftReceipt.value = undefined; stopReads(); acting.value = true; loading.value = false; problem.value = undefined;
  const owned = () => publicationRequest === request && current(scope) && owner === publishEpoch && publishOpen.value && can('reporting.definitions.publish');
  try {
    const result = await publishReportingDefinition(target.id, { version: target.version, changeNote: note }, request.controller.signal);
    if (!owned()) return;
    if (!validReportingVersion(result) || !sameReportingId(result.definitionId, target.id) || result.versionNumber !== target.latestPublishedVersionNumber + 1
      || !sameReportingId(result.dataSourceId, target.dataSourceId) || result.queryPortKey !== target.queryPortKey
      || !sameReportingParameters(result.parameterSchema, target.parameterSchema) || !sameReportingLayout(result.layoutConfigJson, target.layoutConfigJson)
      || !sameReportingId(result.publishedByUserId, session.currentUser?.id) || result.changeNote !== note) throw Error('client.invalid_reporting_publication');
    publicationReceipt.value = { ...request.receipt, outcome: 'confirmed', versionNumber: result.versionNumber };
    const known = observedVersionIds.get(target.id.toLowerCase()) ?? new Map<number, string>(); known.set(result.versionNumber, result.id); observedVersionIds.set(target.id.toLowerCase(), known);
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
  const scope = generation; draftReceipt.value = undefined; stopReads(); acting.value = true; loading.value = false; problem.value = undefined;
  try { await action(requests.signal); if (current(scope)) await loadDefinitions(true); }
  catch (error) { if (current(scope)) { writeUnconfirmed.value = unknown(error); problem.value = toProblem(error, 'reportingDefinitions.saveFailed'); } }
  finally { if (current(scope)) acting.value = false; }
}

async function openVersions(definition: ReportingDefinitionSummary): Promise<void> {
  if (!canRead() || acting.value || !definitions.value.some(row => sameReportingId(row.id, definition.id) && row.version === definition.version)) return;
  invalidateDetails(); groupEditorOpen.value = false; definitionEditorOpen.value = false;
  historyEpoch.value++;
  versionDefinitionId.value = definition.id; versionPage.value = 1; versions.value = []; versionTotal.value = 0; versionsDrawerOpen.value = true;
  versionDefinitionName.value = definition.name;
  await loadVersions();
}

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { status: 500, code: 'client.unexpected_error', title: t(fallbackKey) };
}
/** 事件捕获当次窗口及输入；Vue 复用组件后保留的回调不能借用后来的表单。 */
const groupControls = computed(() => {
  const scope = generation, epoch = groupEpoch, catalog = catalogEpoch.value, open = groupEditorOpen.value, signature = groupSignature();
  const owned = () => current(scope) && catalog === catalogEpoch.value && open && groupEditorOpen.value && epoch === groupEpoch && signature === groupSignature();
  return { update: (value: boolean) => { if (owned()) groupEditorOpen.value = value; },
    confirm: () => { if (owned()) void submitGroup(); }, reload: () => { if (owned()) void reloadGroupDraft(); }, recover: () => { if (owned()) void acknowledgeRecovery('group'); },
    refreshCatalog: () => { if (owned()) void loadPage(); },
    name: (value: string) => { if (owned() && !acting.value) groupForm.name = value; },
    sortOrder: (value: string) => { if (owned() && !acting.value) groupForm.sortOrder = value; },
    enabled: (value: boolean) => { if (owned() && !acting.value) groupForm.isEnabled = value; } };
});
const definitionControls = computed(() => {
  const scope = generation, epoch = definitionEpoch, catalog = catalogEpoch.value, open = definitionEditorOpen.value, signature = definitionSignature();
  const owned = () => current(scope) && catalog === catalogEpoch.value && open && definitionEditorOpen.value && epoch === definitionEpoch && signature === definitionSignature();
  const input = (key: 'definitionKey' | 'name' | 'description' | 'dataSourceId' | 'queryPortKey' | 'layoutConfigJson') => (value: string) => { if (owned() && !acting.value) definitionForm[key] = value; };
  return { update: (value: boolean) => { if (owned()) definitionEditorOpen.value = value; },
    confirm: () => { if (owned()) void submitDefinition(); }, reload: () => { if (owned()) void reloadDraft(); }, recover: () => { if (owned()) void acknowledgeRecovery('definition'); },
    parametersReset: () => { if (owned()) void resetParameters(); },
    refreshCatalog: () => { if (owned()) void loadPage(); },
    key: input('definitionKey'), name: input('name'), description: input('description'), source: input('dataSourceId'), port: input('queryPortKey'), layout: input('layoutConfigJson'),
    enabled: (value: boolean) => { if (owned() && !acting.value) definitionForm.isEnabled = value; },
    parameters: definitionForm.parameterSchema.map((_, index) => ({
      name: (value: string) => { if (owned() && !acting.value) definitionForm.parameterSchema[index]!.displayName = value; },
      defaultValue: (value: string) => { if (owned() && !acting.value) definitionForm.parameterSchema[index]!.defaultValue = value; }
    })) };
});
const historyControls = computed(() => {
  const scope = generation, epoch = historyEpoch.value, id = versionDefinitionId.value, page = versionPage.value, open = versionsDrawerOpen.value;
  const owned = () => current(scope) && open && versionsDrawerOpen.value && epoch === historyEpoch.value && id === versionDefinitionId.value && page === versionPage.value;
  return { update: (value: boolean) => { if (owned()) versionsDrawerOpen.value = value; }, retry: () => { if (owned()) void loadVersions(); },
    page: (value: number) => { if (owned() && Number.isSafeInteger(value) && value >= 1 && value <= Math.max(1, Math.ceil(versionTotal.value / 20))) { versionPage.value = value; void loadVersions(); } } };
});
const pageControls = computed(() => {
  const scope = generation, catalog = catalogEpoch.value;
  return { refresh: () => { if (current(scope) && catalog === catalogEpoch.value) void loadPage(); } };
});
function definitionAction(row: ReportingDefinitionSummary, action: (target: ReportingDefinitionSummary) => unknown) {
  const scope = generation, catalog = catalogEpoch.value, target = { ...row };
  return () => { if (current(scope) && catalog === catalogEpoch.value && definitions.value.some(item => sameReportingId(item.id, target.id) && item.version === target.version)) void action(target); };
}
</script>

<template>
  <div class="reporting-definitions-view">
    <ElAlert v-if="!canRead()" type="info" :closable="false" :title="text('hostOnly')" />
    <template v-if="canRead()">
    <div v-if="draftReceipt" role="status" data-testid="reporting-draft-receipt"><p>{{ draftReceipt.name }} <code>{{ draftReceipt.id }}</code></p>
      <p>{{ text(draftReceipt.outcome === 'confirmed' ? 'draftConfirmed' : draftReceipt.outcome === 'unknown' ? 'draftUnknown' : draftReceipt.outcome === 'rejected' ? 'draftRejected' : 'draftProcessing') }} <span v-if="draftReceipt.version">{{ draftReceipt.version }}</span></p></div>
    <div v-if="publicationReceipt" role="status" data-testid="reporting-publication-receipt"><p>{{ publicationReceipt.definitionKey }} · <code>{{ publicationReceipt.definitionId }}</code></p>
      <p>{{ publicationReceipt.outcome === 'confirmed' ? t('reportingDefinitions.publishSuccess') : text(publicationReceipt.outcome === 'unknown' ? 'publishUnknown' : publicationReceipt.outcome === 'rejected' ? 'publishRejected' : 'publishProcessing') }} <span v-if="publicationReceipt.versionNumber">{{ publicationReceipt.versionNumber }}</span></p></div>
    <ElAlert v-if="problem" type="error" :title="problem.title" show-icon class="mb-4" />
    <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="text('unconfirmed')" data-testid="reporting-config-unconfirmed" />
    <ElButton :disabled="acting" :loading="loading" data-testid="reporting-config-refresh" v-on="{ click: pageControls.refresh }">{{ text('refresh') }}</ElButton>
    <ElAlert v-if="can('reporting.definitions.create') && !definitionCatalogReady && !loading" type="warning" :closable="false" :title="text('catalogMissing')" />

    <div class="layout-grid" :class="{ 'layout-grid--single': !can('reporting.groups.read') }">
      <ElCard v-if="can('reporting.groups.read')" :header="t('reportingDefinitions.groupsTitle')" class="groups-card">
        <PermissionGate code="reporting.groups.create">
          <ElButton
            type="primary"
            :icon="Plus"
            class="mb-3"
            data-testid="reporting-group-create"
            :disabled="acting || writeUnconfirmed && !creationBlocks.has('group')"
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
        <ArtTableHeader :title="t('reportingDefinitions.title')" :loading="loading" v-on="{ refresh: pageControls.refresh }">
          <template #left>
            <PermissionGate code="reporting.definitions.create">
              <ElButton
                type="primary"
                :icon="Plus"
                data-testid="reporting-definition-create"
                :disabled="acting || writeUnconfirmed && !creationBlocks.has('definition') || !definitionCatalogReady"
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
                    <ArtTableActionButton type="edit" :title="t('reportingDefinitions.actionEdit')" :disabled="acting || writeUnconfirmed" test-id="reporting-config-edit" v-on="{ click: definitionAction(row, openEditDefinition) }">
                      {{ t('reportingDefinitions.actionEdit') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <PermissionGate code="reporting.definitions.publish">
                    <ArtTableActionButton type="view" :title="t('reportingDefinitions.actionPublish')" :disabled="acting || writeUnconfirmed" test-id="reporting-config-publish" v-on="{ click: definitionAction(row, publishCurrentDefinition) }">
                      {{ t('reportingDefinitions.actionPublish') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <ArtTableActionButton type="view" :title="t('reportingDefinitions.actionVersions')" :disabled="acting" test-id="reporting-config-history" v-on="{ click: definitionAction(row, openVersions) }">
                    {{ t('reportingDefinitions.actionVersions') }}
                  </ArtTableActionButton>
                  <PermissionGate code="reporting.definitions.delete">
                    <ArtTableActionButton type="delete" :title="t('reportingDefinitions.actionDelete')" :disabled="acting || writeUnconfirmed" test-id="reporting-config-delete" v-on="{ click: definitionAction(row, confirmDeleteDefinition) }">
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
      :open="groupEditorOpen"
      width="min(520px, calc(100vw - 32px))"
      :title="groupEditorMode === 'create' ? t('reportingDefinitions.createGroupTitle') : t('reportingDefinitions.editGroupTitle')"
      :saving="acting"
      :show-confirm="!groupDraftLoading && groupDraftFresh && !writeUnconfirmed && (groupEditorMode !== 'create' || !creationBlocks.has('group')) && can(`reporting.groups.${groupEditorMode === 'create' ? 'create' : 'update'}`)"
      confirm-test-id="reporting-config-group-save"
      v-on="{ confirm: groupControls.confirm, 'update:open': groupControls.update }"
    >
      <ElForm ref="groupFormRef" :model="groupForm" label-position="top" :disabled="acting">
        <ElAlert v-if="!groupDraftFresh" type="warning" :closable="false" :title="text('draftStale')" />
        <ElAlert v-if="problem" type="error" :closable="false" :title="problem.detail ?? problem.title" />
        <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="text('unconfirmed')" />
        <ElAlert v-if="draftRequest?.receipt.resource === 'group'" type="info" :closable="false" :title="text('closeSaving')" />
        <ElAlert v-if="groupEditorMode === 'create' && creationBlocks.has('group')" type="warning" :closable="false" :title="text('creationBlocked')" />
        <ElButton v-if="groupEditorMode === 'create' && creationBlocks.has('group')" :disabled="acting" data-testid="reporting-group-recovery" v-on="{ click: groupControls.recover }">{{ text('recovery') }}</ElButton>
        <ElButton v-if="editingGroup" :disabled="acting || groupDraftLoading" :loading="groupDraftLoading" data-testid="reporting-config-group-reload" v-on="{ click: groupControls.reload }">{{ text('reload') }}</ElButton>
        <ElButton :disabled="acting" :loading="loading" data-testid="reporting-group-catalog-refresh" v-on="{ click: groupControls.refreshCatalog }">{{ text('refreshCatalog') }}</ElButton>
        <ElFormItem :label="t('reportingDefinitions.fieldGroupName')" prop="name" required>
          <ElInput :model-value="groupForm.name" v-on="{ 'update:modelValue': groupControls.name }" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldSortOrder')" prop="sortOrder" required>
          <ElInput :model-value="groupForm.sortOrder" v-on="{ 'update:modelValue': groupControls.sortOrder }" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldEnabled')">
          <ElSwitch :model-value="groupForm.isEnabled" v-on="{ 'update:modelValue': groupControls.enabled }" />
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>

    <ArtFormDialog
      :open="definitionEditorOpen"
      :title="definitionEditorMode === 'create' ? t('reportingDefinitions.createTitle') : t('reportingDefinitions.editTitle')"
      :saving="acting"
      :show-confirm="!draftLoading && definitionDraftFresh && definitionCatalogCompatible && !writeUnconfirmed && (definitionEditorMode !== 'create' || !creationBlocks.has('definition')) && can(`reporting.definitions.${definitionEditorMode === 'create' ? 'create' : 'update'}`)"
      confirm-test-id="reporting-config-draft-save"
      width="min(760px, calc(100vw - 32px))"
      v-on="{ confirm: definitionControls.confirm, 'update:open': definitionControls.update }"
    >
      <ElForm ref="definitionFormRef" :model="definitionForm" label-position="top" :disabled="acting || draftLoading && !editingDefinition">
        <ElAlert v-if="draftLoading" type="info" :closable="false" :title="text('draftLoading')" data-testid="reporting-draft-loading" />
        <ElAlert v-else-if="definitionEditorMode === 'edit' && !definitionDraftFresh" type="warning" :closable="false" :title="text('draftStale')" />
        <ElAlert v-if="!definitionCatalogReady" type="warning" :closable="false" :title="text('catalogMissing')" />
        <ElAlert v-else-if="!definitionCatalogCompatible" type="warning" :closable="false" :title="text('draftIncompatible')" />
        <ElAlert v-if="problem" type="error" :closable="false" :title="problem.detail ?? problem.title" />
        <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="text('unconfirmed')" />
        <ElAlert v-if="draftRequest?.receipt.resource === 'definition'" type="info" :closable="false" :title="text('closeSaving')" />
        <ElAlert v-if="definitionEditorMode === 'create' && creationBlocks.has('definition')" type="warning" :closable="false" :title="text('creationBlocked')" />
        <ElButton v-if="definitionEditorMode === 'create' && creationBlocks.has('definition')" :disabled="acting" data-testid="reporting-definition-recovery" v-on="{ click: definitionControls.recover }">{{ text('recovery') }}</ElButton>
        <ElButton v-if="definitionEditorMode === 'edit' && draftTargetId" :disabled="acting || draftLoading" :loading="draftLoading" data-testid="reporting-config-draft-reload" v-on="{ click: definitionControls.reload }">{{ text('reload') }}</ElButton>
        <ElButton :disabled="acting" :loading="loading" data-testid="reporting-editor-catalog-refresh" v-on="{ click: definitionControls.refreshCatalog }">{{ text('refreshCatalog') }}</ElButton>
        <ElButton v-if="selectedQueryPort && definitionCatalogReady" :disabled="acting" data-testid="reporting-parameters-reset" v-on="{ click: definitionControls.parametersReset }">{{ text('resetParameters') }}</ElButton>
        <ElFormItem
          v-if="definitionEditorMode === 'create'"
          :label="t('reportingDefinitions.fieldDefinitionKey')"
          prop="definitionKey"
          required
        >
          <ElInput :model-value="definitionForm.definitionKey" v-on="{ 'update:modelValue': definitionControls.key }" data-testid="reporting-config-key" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldName')" prop="name" required>
          <ElInput :model-value="definitionForm.name" v-on="{ 'update:modelValue': definitionControls.name }" data-testid="reporting-config-name" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldDescription')">
          <ElInput :model-value="definitionForm.description" v-on="{ 'update:modelValue': definitionControls.description }" type="textarea" :rows="2" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldQueryPort')" prop="queryPortKey" required>
          <ElSelect class="reporting-draft-select" :model-value="definitionForm.queryPortKey" v-on="{ 'update:modelValue': definitionControls.port }" filterable>
            <ElOption
              v-for="port in queryPorts"
              :key="port.queryPortKey"
              :label="port.displayName"
              :value="port.queryPortKey"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldDataSource')" prop="dataSourceId" required>
          <ElSelect class="reporting-draft-select" :model-value="definitionForm.dataSourceId" v-on="{ 'update:modelValue': definitionControls.source }" filterable>
            <ElOption
              v-for="option in compatibleDataSources"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldLayoutConfig')">
          <ElInput :model-value="definitionForm.layoutConfigJson" v-on="{ 'update:modelValue': definitionControls.layout }" type="textarea" :rows="3" data-testid="reporting-config-layout" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldEnabled')">
          <ElSwitch :model-value="definitionForm.isEnabled" v-on="{ 'update:modelValue': definitionControls.enabled }" />
        </ElFormItem>
        <ElFormItem v-if="definitionForm.parameterSchema.length > 0" :label="t('reportingDefinitions.fieldParameterSchema')">
          <ElTable :data="definitionForm.parameterSchema" size="small">
            <ElTableColumn prop="parameterKey" :label="t('reportingDefinitions.fieldParameterKey')" />
            <ElTableColumn :label="t('reportingDefinitions.fieldParameterDisplayName')">
              <template #default="{ row, $index }">
                <ElInput :model-value="row.displayName" v-on="{ 'update:modelValue': definitionControls.parameters[$index]?.name }" />
              </template>
            </ElTableColumn>
            <ElTableColumn :label="t('reportingDefinitions.fieldParameterDefault')">
              <template #default="{ row, $index }">
                <ElInput :model-value="row.defaultValue" v-on="{ 'update:modelValue': definitionControls.parameters[$index]?.defaultValue }" />
              </template>
            </ElTableColumn>
          </ElTable>
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>

    <ArtFormDialog v-if="confirmation" :key="confirmation.token" :open="true" :title="t('common.confirm')" :show-confirm="true"
    :confirm-label="t('common.confirm')" :cancel-label="text('cancel')" width="min(520px, calc(100vw - 32px))" :trap-focus="true" confirm-test-id="reporting-management-confirm"
      v-on="{ confirm: confirmation.confirm, 'update:open': confirmation.update }">
      <p data-testid="reporting-management-confirm-message">{{ confirmation.message }}</p>
    </ArtFormDialog>
    <ElDrawer :model-value="versionsDrawerOpen" v-on="{ 'update:modelValue': historyControls.update }" @open="historyLandmarks" @opened="historyLandmarks"
      :title="t('reportingDefinitions.versionsTitle')" size="min(760px, calc(100vw - 32px))">
      <div ref="historyContent">
      <p>{{ versionDefinitionName }} <code>{{ versionDefinitionId }}</code></p>
      <ElAlert v-if="versionsProblem" type="error" :closable="false" :title="versionsProblem.title" data-testid="reporting-history-problem" />
      <ElButton :loading="versionsLoading" data-testid="reporting-history-retry" v-on="{ click: historyControls.retry }">{{ text('historyRetry') }}</ElButton>
      <ElTable :data="versions" size="small" v-loading="versionsLoading" :scrollbar-tabindex="0">
        <ElTableColumn prop="versionNumber" :label="t('reportingDefinitions.fieldVersionNumber')" width="100" />
        <ElTableColumn prop="queryPortKey" :label="t('reportingDefinitions.fieldQueryPort')" />
        <ElTableColumn prop="publishedAtUtc" :label="t('reportingDefinitions.fieldPublishedAt')" min-width="180" />
        <ElTableColumn prop="changeNote" :label="t('reportingDefinitions.fieldChangeNote')" />
      </ElTable>
      <ElPagination :current-page="versionPage" :total="versionTotal" :page-size="20" :disabled="versionsLoading"
        layout="prev, pager, next, total" v-on="{ 'update:currentPage': historyControls.page }" />
      </div>
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
/* 选中占位文字使用负层级，选择区显式沿用控件底色，避免透明背景穿透到弹窗遮罩。 */
.reporting-draft-select :deep(.el-select__placeholder) { background-color: var(--el-fill-color-blank); }
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
