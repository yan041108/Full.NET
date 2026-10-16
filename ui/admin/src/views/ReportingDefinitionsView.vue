<script setup lang="ts">
import { computed, defineAsyncComponent, inject, nextTick, onActivated, onBeforeUnmount, onDeactivated, onMounted, reactive, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDrawer,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type {
  FullNetProblemDetails,
  ReportingDefinition,
  ReportingDefinitionVersion,
  ReportingGroup,
  ReportingParameterSchemaEntry,
  ReportingQueryPortDefinition
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { listReportingDataSources } from '../api/reporting-data-sources';
import { validReportingDefinition, validReportingGroup, validReportingPort, validReportingVersion, sameReportingId as sameId, sameReportingLayout, sameReportingParameters, matchesReportingDraft } from '../guards/reporting-management-guards';
import {
  createReportingDefinition,
  createReportingGroup,
  deleteReportingDefinition,
  deleteReportingGroup,
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

const { t } = useAdminI18n();
const session = useSessionStore(), route = inject(routeLocationKey, undefined);
const ReportingTenantGrantsDialog = defineAsyncComponent(() => import('../components/reporting/ReportingTenantGrantsDialog.vue'));
const grantDefinition = ref<ReportingDefinition>();

function openGrants(definition: ReportingDefinition): void {
  if (!canRead.value || !currentDefinition(definition) || acting.value || !session.can('reporting.definitions.grant_tenants')) return;
  grantDefinition.value = { ...definition };
}
const groups = ref<ReportingGroup[]>([]);
const definitions = ref<ReportingDefinition[]>([]);
const queryPorts = ref<ReportingQueryPortDefinition[]>([]);
const dataSourceOptions = ref<Array<{ value: string; label: string; providerKey: string }>>([]);
const selectedGroupId = ref<string>();
const versions = ref<ReportingDefinitionVersion[]>([]);
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
const definitionGroupId = ref<string>();
const versionTarget = ref<ReportingDefinition>();

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
} = useArtCrudTableLayout({ bottomOffset: 8 });

watchLoading(loading);

const searchItems = computed<ArtSearchBarItem[]>(() => [
  { key: 'name', label: t('reportingDefinitions.fieldName'), type: 'input' }
]);

const filteredDefinitions = computed(() => {
  const keyword = appliedFilters.value.name.trim().toLowerCase();
  let rows = definitions.value;
  if (selectedGroupId.value) {
    rows = rows.filter(item => sameId(item.groupId, selectedGroupId.value));
  }
  if (keyword) {
    rows = rows.filter(item =>
      item.name.toLowerCase().includes(keyword)
      || item.definitionKey.toLowerCase().includes(keyword));
  }
  return rows;
});

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

// 端口默认值必须同步写入，避免打开已有定义后异步覆盖随后恢复的自定义参数。
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




type Resource = 'group' | 'definition';
type Target = { id: string; version: number };
type Receipt = { sequence: number; resource: Resource; kind: 'create' | 'update' | 'delete' | 'publish'; id?: string; version?: number; versionNumber?: number; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed' };
const receipt = ref<Receipt>(), creationBlocks = ref(new Set<Resource>()), rowBlocks = ref(new Map<string, number>());
const catalogProblems = reactive<{ groups?: FullNetProblemDetails; definitions?: FullNetProblemDetails; ports?: FullNetProblemDetails; sources?: FullNetProblemDetails }>({});
const catalogReady = reactive({ groups: false, definitions: false, ports: false, sources: false });
const groupEditorProblem = ref<FullNetProblemDetails>(), definitionEditorProblem = ref<FullNetProblemDetails>(), versionsProblem = ref<FullNetProblemDetails>();
const floors = new Map<string, number>(), deleted = new Set<string>(), publishedNumbers = new Map<string, number>(), publishedIds = new Map<string, string>();
const keyOf = (resource: Resource, id: string) => resource + ':' + id.toLowerCase();
const groupOwner = ref(0), definitionOwner = ref(0), versionOwner = ref(0);
let controlSequence = 0, receiptSequence = 0;
const confirmation = ref<{ title: string; message: string; resolve: (confirmed: boolean) => void }>();
const confirmationHandlers = computed(() => {
  const pending = confirmation.value, finish = (confirmed: boolean) => { if (pending && confirmation.value === pending) finishConfirmation(confirmed); };
  return { confirm: () => finish(true), update: (open: boolean) => { if (!open) finish(false); } };
});
function finishConfirmation(confirmed: boolean): void { const pending = confirmation.value; confirmation.value = undefined; pending?.resolve(confirmed); }
function ask(title: string, message: string): Promise<boolean> { finishConfirmation(false); return new Promise(resolve => { confirmation.value = { title, message, resolve }; }); }
const viewActive = ref(false);
onMounted(() => { viewActive.value = true; }); onActivated(() => { viewActive.value = true; });
onDeactivated(() => { viewActive.value = false; }); onBeforeUnmount(() => { viewActive.value = false; });
const host = () => viewActive.value && (!route || route.name === 'reporting-definitions') && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null;
const canRead = computed(() => host() && session.can('reporting.definitions.read'));
const scope = useAuthorizedViewScope(session, resetScope, loadPage);
type ViewRequest = NonNullable<ReturnType<typeof scope.begin>>;
let listRequest: ViewRequest | undefined, groupRequest: ViewRequest | undefined, definitionRequest: ViewRequest | undefined,
  versionRequest: ViewRequest | undefined, actionRequest: ViewRequest | undefined;
let pendingMutation: { request: ViewRequest; receipt: Receipt } | undefined;
const begin = (permission: string) => canRead.value ? scope.begin(permission) : undefined;
function canEnter(permission: string): boolean { const request = begin(permission); request?.finish(); return !!request; }
function currentGroup(target: Target): boolean { return groups.value.some(row => sameId(row.id, target.id) && row.version === target.version); }
function currentDefinition(target: Target): boolean { return definitions.value.some(row => sameId(row.id, target.id) && row.version === target.version); }
function allowed(resource: Resource, target: Target): boolean {
  return (resource === 'group' ? currentGroup(target) && catalogReady.groups : currentDefinition(target) && catalogReady.definitions)
    && !rowBlocks.value.has(keyOf(resource, target.id)) && target.version >= (floors.get(keyOf(resource, target.id)) ?? 0);
}
function resetScope(): void {
  pendingMutation = undefined; finishConfirmation(false);
  groups.value = []; definitions.value = []; queryPorts.value = []; dataSourceOptions.value = [];
  selectedGroupId.value = undefined; versions.value = []; grantDefinition.value = undefined; versionTarget.value = undefined;
  groupEditorOpen.value = false; definitionEditorOpen.value = false; versionsDrawerOpen.value = false;
  editingGroup.value = null; editingDefinition.value = null; definitionGroupId.value = undefined;
  resetGroup(); resetDefinition(); searchForm.value = {}; appliedFilters.value = { name: '' };
  loading.value = false; acting.value = false; problem.value = undefined; groupEditorProblem.value = undefined; definitionEditorProblem.value = undefined; versionsProblem.value = undefined;
  for (const key of ['groups', 'definitions', 'ports', 'sources'] as const) { catalogReady[key] = false; catalogProblems[key] = undefined; }
  receipt.value = undefined; creationBlocks.value = new Set(); rowBlocks.value = new Map(); floors.clear(); deleted.clear(); publishedNumbers.clear(); publishedIds.clear();
  listRequest = undefined; actionRequest = undefined; groupRequest = undefined; definitionRequest = undefined; versionRequest = undefined;
  listControls.value = makeListControls();
}
watch(() => route?.name, () => { scope.invalidate(); if (canRead.value) void loadPage(); }, { flush: 'sync' });
function stopList(): void { listRequest?.cancel(); listRequest = undefined; loading.value = false; }
function markUnconfirmed(pending: NonNullable<typeof pendingMutation>): void {
  stopList(); receipt.value = { ...pending.receipt, outcome: 'unconfirmed' };
  if (pending.receipt.kind === 'create') creationBlocks.value = new Set(creationBlocks.value).add(pending.receipt.resource);
  else if (pending.receipt.id) rowBlocks.value = new Map(rowBlocks.value).set(keyOf(pending.receipt.resource, pending.receipt.id), pending.receipt.version ?? 0);
}
function cancelAction(request: ViewRequest | undefined): void {
  if (request && pendingMutation?.request === request) { markUnconfirmed(pendingMutation); pendingMutation = undefined; }
  request?.cancel(); if (request && actionRequest === request) { actionRequest = undefined; acting.value = false; }
}
function detach(request: ViewRequest): void {
  if (pendingMutation?.request === request) pendingMutation = undefined;
  if (groupRequest === request) groupRequest = undefined;
  if (definitionRequest === request) definitionRequest = undefined;
  if (versionRequest === request) versionRequest = undefined;
  if (actionRequest === request) { actionRequest = undefined; acting.value = false; }
}
function resetGroup(): void { Object.assign(groupForm, { name: '', sortOrder: '0', isEnabled: true }); }
function resetDefinition(): void {
  Object.assign(definitionForm, { definitionKey: '', name: '', description: '', dataSourceId: '', queryPortKey: '', layoutConfigJson: '{}', isEnabled: true, parameterSchema: [] }); publishNote.value = '';
}
watch(groupEditorOpen, open => { groupOwner.value++; if (!open) { cancelAction(groupRequest); groupRequest = undefined; editingGroup.value = null; groupEditorProblem.value = undefined; resetGroup(); } }, { flush: 'sync' });
watch(definitionEditorOpen, open => { definitionOwner.value++; if (!open) { cancelAction(definitionRequest); definitionRequest = undefined; editingDefinition.value = null; definitionGroupId.value = undefined; definitionEditorProblem.value = undefined; resetDefinition(); } }, { flush: 'sync' });
watch(groupForm, () => { groupOwner.value++; cancelAction(groupRequest); groupRequest = undefined; }, { flush: 'sync', deep: true });
watch(definitionForm, () => { definitionOwner.value++; cancelAction(definitionRequest); definitionRequest = undefined; }, { flush: 'sync', deep: true });
watch(versionsDrawerOpen, open => { versionOwner.value++; if (!open) { cancelAction(versionRequest); versionRequest = undefined; versions.value = []; versionsProblem.value = undefined; versionTarget.value = undefined; } }, { flush: 'sync' });
watch(selectedGroupId, () => { definitionEditorOpen.value = false; versionsDrawerOpen.value = false; grantDefinition.value = undefined; listControls.value = makeListControls(); }, { flush: 'sync' });
watch(groups, () => { if (editingGroup.value && !currentGroup(editingGroup.value)) groupEditorOpen.value = false; }, { flush: 'sync', deep: true });
watch(definitions, () => {
  if (editingDefinition.value && !currentDefinition(editingDefinition.value)) definitionEditorOpen.value = false;
  if (versionTarget.value && !currentDefinition(versionTarget.value)) versionsDrawerOpen.value = false;
  if (grantDefinition.value && !currentDefinition(grantDefinition.value)) grantDefinition.value = undefined;
}, { flush: 'sync', deep: true });
function sent(request: ViewRequest, resource: Resource, kind: Receipt['kind'], target?: Target): void {
  stopList(); receipt.value = { sequence: ++receiptSequence, resource, kind, id: target?.id, version: target?.version, outcome: 'processing' };
  pendingMutation = { request, receipt: { ...receipt.value } };
}
function confirmed(request: ViewRequest, id?: string, version?: number, versionNumber?: number): void {
  if (pendingMutation?.request === request) receipt.value = { ...pendingMutation.receipt, id: id ?? pendingMutation.receipt.id, version: version ?? pendingMutation.receipt.version, versionNumber, outcome: 'succeeded' };
  detach(request);
}
async function withAction(permission: string, action: (request: ViewRequest) => Promise<void>, editor?: 'group' | 'definition' | 'versions', fallback: Parameters<typeof t>[0] = 'reportingDefinitions.saveFailed'): Promise<void> {
  if (acting.value) return; const request = begin(permission); if (!request) return;
  if (editor === 'group') { groupRequest = request; groupEditorProblem.value = undefined; }
  if (editor === 'definition') { definitionRequest = request; definitionEditorProblem.value = undefined; }
  if (editor === 'versions') { versionRequest = request; versionsProblem.value = undefined; }
  actionRequest = request; acting.value = true; problem.value = undefined;
  try { await action(request); }
  catch (error) {
    if (request.current()) {
      const failure = toProblem(error, fallback);
      if (pendingMutation?.request === request) {
        if (!isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408) markUnconfirmed(pendingMutation);
        else receipt.value = { ...pendingMutation.receipt, outcome: 'failed' };
      }
      if (editor === 'group') groupEditorProblem.value = failure;
      else if (editor === 'definition') definitionEditorProblem.value = failure;
      else if (editor === 'versions') versionsProblem.value = failure;
      else problem.value = failure;
    }
  } finally { detach(request); request.finish(); }
}
function reconcile(resource: Resource, rows: Target[]): void {
  const blocks = new Map(rowBlocks.value);
  for (const row of rows) { const key = keyOf(resource, row.id), minimum = blocks.get(key); if (minimum !== undefined && row.version > minimum) blocks.delete(key); }
  rowBlocks.value = blocks;
}
function validRows(resource: Resource, rows: Target[]): boolean {
  return new Set(rows.map(row => row.id.toLowerCase())).size === rows.length
    && rows.every(row => !deleted.has(keyOf(resource, row.id)) && row.version >= (floors.get(keyOf(resource, row.id)) ?? 0));
}
async function loadSourceOptions(request: ViewRequest): Promise<Array<{ value: string; label: string; providerKey: string }>> {
  const rows: Array<{ value: string; label: string; providerKey: string }> = [], seen = new Set<string>(); let page = 1, pages = 1, expectedTotal: number | undefined;
  do {
    const result = await listReportingDataSources({ page, pageSize: 100, isEnabled: true }, request.signal);
    if (!request.current()) return [];
    if (result.page !== page || result.pageSize !== 100 || !Number.isSafeInteger(result.total) || result.total < 0 || result.total > 10000
      || expectedTotal !== undefined && result.total !== expectedTotal
      || result.items.length !== Math.min(100, Math.max(0, result.total - (page - 1) * 100))) throw Error('client.invalid_reporting_data_source_catalog');
    expectedTotal = result.total; pages = Math.max(1, Math.ceil(result.total / 100));
    for (const row of result.items) {
      if (!row.isEnabled || seen.has(row.id.toLowerCase())) throw Error('client.invalid_reporting_data_source_catalog');
      seen.add(row.id.toLowerCase()); rows.push({ value: row.id, label: row.name, providerKey: row.providerKey });
    }
    page++;
  } while (page <= pages);
  if (rows.length !== expectedTotal) throw Error('client.invalid_reporting_data_source_catalog');
  return rows;
}
async function loadPage(): Promise<void> {
  stopList();
  // 就绪状态即使恢复为相同目录，也属于新的读取代次；旧校验和旧按钮不能借用它继续写入。
  groupOwner.value++; definitionOwner.value++;
  if (pendingMutation) cancelAction(pendingMutation.request);
  cancelAction(groupRequest); groupRequest = undefined; cancelAction(definitionRequest); definitionRequest = undefined;
  const request = begin('reporting.definitions.read'); if (!request) return; listRequest = request;
  loading.value = true; listControls.value = makeListControls();
  for (const key of ['groups', 'definitions', 'ports', 'sources'] as const) { catalogReady[key] = false; catalogProblems[key] = undefined; }
  const read = async (key: keyof typeof catalogReady, permission: string, action: () => Promise<void>) => {
    if (!session.can(permission)) return;
    try { await action(); if (request.current()) catalogReady[key] = true; }
    catch (error) { if (request.current()) catalogProblems[key] = toProblem(error, 'reportingDefinitions.loadFailed'); }
  };
  try {
    await Promise.all([
      read('groups', 'reporting.groups.read', async () => {
        const rows = await listReportingGroups(request.signal); if (!request.current()) return;
        if (!rows.every(validReportingGroup) || !validRows('group', rows)) throw Error('client.invalid_reporting_group_list');
        for (const row of rows) floors.set(keyOf('group', row.id), row.version);
        reconcile('group', rows); groups.value = rows;
        if (!groups.value.some(row => sameId(row.id, selectedGroupId.value))) selectedGroupId.value = groups.value[0]?.id;
      }),
      read('definitions', 'reporting.definitions.read', async () => {
        const rows = await listReportingDefinitions({}, request.signal); if (!request.current()) return;
        if (!rows.every(validReportingDefinition) || !validRows('definition', rows) || rows.some(row => row.latestPublishedVersionNumber < (publishedNumbers.get(row.id.toLowerCase()) ?? 0))) throw Error('client.invalid_reporting_definition_list');
        for (const row of rows) { floors.set(keyOf('definition', row.id), row.version); publishedNumbers.set(row.id.toLowerCase(), row.latestPublishedVersionNumber); }
        reconcile('definition', rows); definitions.value = rows;
      }),
      read('ports', 'reporting.query_ports.read', async () => {
        const rows = await listReportingQueryPorts(request.signal); if (!request.current()) return;
        if (!rows.every(validReportingPort) || new Set(rows.map(row => row.queryPortKey)).size !== rows.length) throw Error('client.invalid_reporting_query_port_list');
        if (JSON.stringify(rows) !== JSON.stringify(queryPorts.value)) { definitionOwner.value++; cancelAction(definitionRequest); definitionRequest = undefined; }
        if (request.current()) queryPorts.value = rows;
      }),
      read('sources', 'reporting.data_sources.read', async () => {
        const rows = await loadSourceOptions(request); if (!request.current()) return;
        if (JSON.stringify(rows) !== JSON.stringify(dataSourceOptions.value)) { definitionOwner.value++; cancelAction(definitionRequest); definitionRequest = undefined; }
        if (request.current()) dataSourceOptions.value = rows;
      })
    ]);
  } finally {
    if (listRequest === request) { loading.value = false; listRequest = undefined; listControls.value = makeListControls(); void nextTick(updateTableHeight); } request.finish();
  }
}
function selectGroup(id: string): void { if (canRead.value && groups.value.some(row => sameId(row.id, id))) selectedGroupId.value = id; }
function handleSearch(params: Record<string, string | undefined>): void { appliedFilters.value = { name: params.name?.trim() ?? '' }; listControls.value = makeListControls(); }
function resetSearch(): void { appliedFilters.value = { name: '' }; listControls.value = makeListControls(); }
interface ListControls { token: number; refresh: () => void; createGroup: () => void; createDefinition: () => void; search: (value: Record<string, string | undefined>) => void; reset: () => void; input: (value: Record<string, string | undefined>) => void; select: (row: ReportingGroup) => void }
const listControls = ref<ListControls>(makeListControls());
function makeListControls(): ListControls {
  searchForm.value = { ...searchForm.value };
  const token = ++controlSequence, owned = () => canRead.value && token === listControls.value.token;
  return { token, refresh: () => { if (owned()) void loadPage(); }, createGroup: () => { if (owned()) openCreateGroup(); }, createDefinition: () => { if (owned()) openCreateDefinition(); },
    search: value => { if (owned()) handleSearch(value); }, reset: () => { if (owned()) resetSearch(); }, input: value => { if (owned()) searchForm.value = { ...value }; }, select: row => { if (owned() && currentGroup(row)) selectGroup(row.id); } };
}
const groupRows = computed(() => {
  const token = listControls.value.token;
  return groups.value.map(row => { const target = { ...row }, owned = () => canRead.value && token === listControls.value.token && currentGroup(target);
    return { ...target, blocked: rowBlocks.value.has(keyOf('group', target.id)), edit: () => { if (owned()) openEditGroup(target); }, remove: () => { if (owned()) void confirmDeleteGroup(target); }, recover: () => { if (owned()) void acknowledgeRecovery('group', target.id); } };
  });
});
const definitionRows = computed(() => {
  const token = listControls.value.token;
  return filteredDefinitions.value.map(row => { const target = { ...row, parameterSchema: row.parameterSchema.map(p => ({ ...p })) }, owned = () => canRead.value && token === listControls.value.token && currentDefinition(target);
    return { ...target, blocked: rowBlocks.value.has(keyOf('definition', target.id)), edit: () => { if (owned()) openEditDefinition(target); }, remove: () => { if (owned()) void confirmDeleteDefinition(target); },
      publish: () => { if (owned()) void publishCurrentDefinition(target); }, versions: () => { if (owned()) void openVersions(target); }, grants: () => { if (owned()) openGrants(target); }, recover: () => { if (owned()) void acknowledgeRecovery('definition', target.id); } };
  });
});
type GroupRow = (typeof groupRows.value)[number];
type DefinitionRow = (typeof definitionRows.value)[number];
function openCreateGroup(): void {
  if (acting.value || !canEnter('reporting.groups.create')) return;
  definitionEditorOpen.value = false; versionsDrawerOpen.value = false; groupEditorOpen.value = false;
  groupEditorMode.value = 'create'; editingGroup.value = null; resetGroup(); groupEditorProblem.value = undefined; groupEditorOpen.value = true;
}
function openEditGroup(group: ReportingGroup): void {
  if (acting.value || !allowed('group', group) || !canEnter('reporting.groups.update')) return;
  definitionEditorOpen.value = false; versionsDrawerOpen.value = false; groupEditorOpen.value = false;
  groupEditorMode.value = 'edit'; editingGroup.value = { ...group }; Object.assign(groupForm, { name: group.name, sortOrder: String(group.sortOrder), isEnabled: group.isEnabled }); groupEditorOpen.value = true;
}
function groupBlocked(): boolean { return groupEditorMode.value === 'create' ? creationBlocks.value.has('group') : !!editingGroup.value && rowBlocks.value.has(keyOf('group', editingGroup.value.id)); }
async function submitGroup(): Promise<void> {
  if (!groupEditorOpen.value || !groupFormRef.value || groupBlocked()) return;
  const mode = groupEditorMode.value, target = editingGroup.value ? { ...editingGroup.value } : undefined, input = { ...groupForm };
  if (mode === 'edit' && (!target || !allowed('group', target))) return;
  await withAction(mode === 'create' ? 'reporting.groups.create' : 'reporting.groups.update', async request => {
    const valid = await groupFormRef.value!.validate().catch(() => false); if (!valid || !request.current() || target && !allowed('group', target)) return;
    const payload = { parentId: target?.parentId ?? null, name: input.name.trim(), sortOrder: Number(input.sortOrder), isEnabled: input.isEnabled };
    if (!Number.isSafeInteger(payload.sortOrder)) throw Error('client.invalid_reporting_group');
    sent(request, 'group', mode === 'create' ? 'create' : 'update', target);
    const result = mode === 'create' ? await createReportingGroup(payload, request.signal) : await updateReportingGroup(target!.id, { ...payload, version: target!.version }, request.signal);
    if (!request.current()) return;
    if (!validReportingGroup(result) || result.version !== (target ? target.version + 1 : 1) || target && !sameId(result.id, target.id)
      || !sameId(result.parentId, payload.parentId) || result.name !== payload.name || result.sortOrder !== payload.sortOrder || result.isEnabled !== payload.isEnabled) throw Error('client.invalid_reporting_group');
    confirmed(request, result.id, result.version); floors.set(keyOf('group', result.id), result.version); groupEditorOpen.value = false;
    groups.value = mode === 'create' ? [...groups.value.filter(row => !sameId(row.id, result.id)), result] : groups.value.map(row => sameId(row.id, result.id) ? result : row);
    if (mode === 'create') selectedGroupId.value = result.id;
    ElMessage.success(t(mode === 'create' ? 'reportingDefinitions.groupCreateSuccess' : 'reportingDefinitions.groupUpdateSuccess')); await loadPage();
  }, 'group');
}
async function confirmDeleteGroup(row: ReportingGroup): Promise<void> {
  const target = { ...row }; if (!allowed('group', target)) return;
  await withAction('reporting.groups.delete', async request => {
    if (!await ask(t('reportingDefinitions.fieldGroupName'), t('reportingDefinitions.confirmDeleteGroup', { name: target.name })) || !request.current() || !allowed('group', target)) return;
    sent(request, 'group', 'delete', target); const removed = await deleteReportingGroup(target.id, request.signal); if (!request.current()) return;
    if (!removed) { receipt.value = { ...receipt.value!, outcome: 'failed' }; pendingMutation = undefined; throw Error('client.reporting_group_delete_failed'); }
    confirmed(request); deleted.add(keyOf('group', target.id)); groups.value = groups.value.filter(item => !sameId(item.id, target.id));
    if (sameId(selectedGroupId.value, target.id)) selectedGroupId.value = groups.value[0]?.id;
    ElMessage.success(t('reportingDefinitions.groupDeleteSuccess')); await loadPage();
  });
}
function openCreateDefinition(): void {
  if (acting.value || !canEnter('reporting.definitions.create')) return;
  if (!selectedGroupId.value) { ElMessage.warning(t('reportingDefinitions.selectGroupFirst')); return; }
  groupEditorOpen.value = false; versionsDrawerOpen.value = false; definitionEditorOpen.value = false;
  definitionGroupId.value = selectedGroupId.value; definitionEditorMode.value = 'create'; editingDefinition.value = null; resetDefinition();
  definitionForm.queryPortKey = queryPorts.value[0]?.queryPortKey ?? ''; definitionForm.dataSourceId = compatibleDataSources.value[0]?.value ?? '';
  definitionEditorOpen.value = true;
}
function openEditDefinition(definition: ReportingDefinition): void {
  if (acting.value || !allowed('definition', definition) || !canEnter('reporting.definitions.update')) return;
  groupEditorOpen.value = false; versionsDrawerOpen.value = false; definitionEditorOpen.value = false;
  definitionGroupId.value = definition.groupId; definitionEditorMode.value = 'edit';
  editingDefinition.value = { ...definition, parameterSchema: definition.parameterSchema.map(p => ({ ...p })) };
  Object.assign(definitionForm, { definitionKey: definition.definitionKey, name: definition.name, description: definition.description ?? '',
    queryPortKey: definition.queryPortKey, dataSourceId: definition.dataSourceId, layoutConfigJson: definition.layoutConfigJson, isEnabled: definition.isEnabled });
  definitionForm.parameterSchema = definition.parameterSchema.map(p => ({ ...p })); definitionEditorOpen.value = true;
}
const draftAvailable = computed(() => catalogReady.groups && catalogReady.definitions && catalogReady.ports && catalogReady.sources
  && groups.value.some(group => sameId(group.id, definitionGroupId.value) && group.isEnabled)
  && !!selectedQueryPort.value && compatibleDataSources.value.some(source => sameId(source.value, definitionForm.dataSourceId))
  && definitionForm.parameterSchema.length === selectedQueryPort.value.parameters.length
  && new Set(definitionForm.parameterSchema.map(row => row.parameterKey)).size === definitionForm.parameterSchema.length
  && definitionForm.parameterSchema.every(row => selectedQueryPort.value!.parameters.some(p => p.parameterKey === row.parameterKey && p.dataTypeKey === row.dataTypeKey && (!p.isRequired || row.isRequired))));
function definitionBlocked(): boolean { return definitionEditorMode.value === 'create' ? creationBlocks.value.has('definition') : !!editingDefinition.value && rowBlocks.value.has(keyOf('definition', editingDefinition.value.id)); }
async function submitDefinition(): Promise<void> {
  if (!definitionEditorOpen.value || !definitionFormRef.value || !definitionGroupId.value || !draftAvailable.value || definitionBlocked()) return;
  const mode = definitionEditorMode.value, groupId = definitionGroupId.value, target = editingDefinition.value ? { ...editingDefinition.value } : undefined;
  const input = { ...definitionForm, parameterSchema: definitionForm.parameterSchema.map(p => ({ ...p })) };
  if (mode === 'edit' && (!target || !allowed('definition', target))) return;
  await withAction(mode === 'create' ? 'reporting.definitions.create' : 'reporting.definitions.update', async request => {
    const valid = await definitionFormRef.value!.validate().catch(() => false); if (!valid || !request.current() || !draftAvailable.value) return;
    const payload = { groupId, dataSourceId: input.dataSourceId, name: input.name.trim(), description: input.description.trim() || null, queryPortKey: input.queryPortKey,
      parameterSchema: input.parameterSchema, layoutConfigJson: input.layoutConfigJson, isEnabled: input.isEnabled };
    sent(request, 'definition', mode === 'create' ? 'create' : 'update', target);
    const result = mode === 'create' ? await createReportingDefinition({ ...payload, definitionKey: input.definitionKey.trim() }, request.signal)
      : await updateReportingDefinition(target!.id, { ...payload, version: target!.version }, request.signal);
    if (!request.current()) return;
    if (!validReportingDefinition(result) || !matchesReportingDraft(result, payload) || result.version !== (target ? target.version + 1 : 1)
      || result.definitionKey !== (target?.definitionKey ?? input.definitionKey.trim()) || target && (!sameId(result.id, target.id) || result.latestPublishedVersionNumber !== target.latestPublishedVersionNumber)
      || !target && result.latestPublishedVersionNumber !== 0) throw Error('client.invalid_reporting_definition');
    confirmed(request, result.id, result.version); floors.set(keyOf('definition', result.id), result.version); definitionEditorOpen.value = false;
    definitions.value = [...definitions.value.filter(row => !sameId(row.id, result.id)), result];
    ElMessage.success(t(mode === 'create' ? 'reportingDefinitions.createSuccess' : 'reportingDefinitions.updateSuccess')); await loadPage();
  }, 'definition');
}
async function confirmDeleteDefinition(row: ReportingDefinition): Promise<void> {
  const target = { ...row }; if (!allowed('definition', target)) return;
  await withAction('reporting.definitions.delete', async request => {
    if (!await ask(t('reportingDefinitions.actionDelete'), t('reportingDefinitions.confirmDeleteDefinition', { name: target.name })) || !request.current() || !allowed('definition', target)) return;
    sent(request, 'definition', 'delete', target); const removed = await deleteReportingDefinition(target.id, request.signal); if (!request.current()) return;
    if (!removed) { receipt.value = { ...receipt.value!, outcome: 'failed' }; pendingMutation = undefined; throw Error('client.reporting_definition_delete_failed'); }
    confirmed(request); deleted.add(keyOf('definition', target.id)); definitions.value = definitions.value.filter(item => !sameId(item.id, target.id));
    ElMessage.success(t('reportingDefinitions.deleteSuccess')); await loadPage();
  });
}
async function publishCurrentDefinition(definition: ReportingDefinition): Promise<void> {
  const target = { ...definition, parameterSchema: definition.parameterSchema.map(p => ({ ...p })) }; if (!allowed('definition', target)) return;
  const changeNote = sameId(editingDefinition.value?.id, target.id) ? publishNote.value.trim() || null : null;
  await withAction('reporting.definitions.publish', async request => {
    sent(request, 'definition', 'publish', target); const result = await publishReportingDefinition(target.id, { changeNote, version: target.version }, request.signal);
    if (!request.current()) return;
    if (!validReportingVersion(result) || !sameId(result.definitionId, target.id) || result.versionNumber !== target.latestPublishedVersionNumber + 1
      || !sameId(result.dataSourceId, target.dataSourceId) || result.queryPortKey !== target.queryPortKey || !sameReportingParameters(result.parameterSchema, target.parameterSchema)
      || !sameReportingLayout(result.layoutConfigJson, target.layoutConfigJson) || result.changeNote !== changeNote || !sameId(result.publishedByUserId, session.currentUser?.id)) throw Error('client.invalid_reporting_definition_version');
    confirmed(request, target.id, target.version + 1, result.versionNumber);
    floors.set(keyOf('definition', target.id), target.version + 1); publishedNumbers.set(target.id.toLowerCase(), result.versionNumber); publishedIds.set(target.id.toLowerCase() + ':' + result.versionNumber, result.id.toLowerCase());
    definitions.value = definitions.value.map(row => sameId(row.id, target.id) ? { ...row, version: target.version + 1, latestPublishedVersionNumber: result.versionNumber, updatedAtUtc: result.publishedAtUtc } : row);
    publishNote.value = ''; ElMessage.success(t('reportingDefinitions.publishSuccess')); await loadPage();
  });
}
async function openVersions(definition: ReportingDefinition): Promise<void> {
  if (acting.value || !currentDefinition(definition) || !canRead.value) return;
  const target = { ...definition }; groupEditorOpen.value = false; definitionEditorOpen.value = false; versionsDrawerOpen.value = false;
  versions.value = []; versionTarget.value = target; versionsDrawerOpen.value = true; await retryVersions();
}
async function retryVersions(): Promise<void> {
  const target = versionTarget.value ? { ...versionTarget.value } : undefined;
  if (acting.value || !versionsDrawerOpen.value || !target || !currentDefinition(target)) return;
  versionOwner.value++;
  await withAction('reporting.definitions.read', async request => {
    const rows = await listReportingDefinitionVersions(target.id, request.signal); if (!request.current()) return;
    const identity = target.id.toLowerCase(), knownLatest = Math.max(target.latestPublishedVersionNumber, publishedNumbers.get(identity) ?? 0);
    if (!rows.every(validReportingVersion) || rows.some(row => !sameId(row.definitionId, target.id) || publishedIds.has(identity + ':' + row.versionNumber) && publishedIds.get(identity + ':' + row.versionNumber) !== row.id.toLowerCase())
      || new Set(rows.map(row => row.id.toLowerCase())).size !== rows.length || new Set(rows.map(row => row.versionNumber)).size !== rows.length
      || knownLatest > 0 && !rows.some(row => row.versionNumber === knownLatest)
      || [...publishedIds].some(([key, id]) => key.startsWith(identity + ':') && !rows.some(row => identity + ':' + row.versionNumber === key && row.id.toLowerCase() === id))) throw Error('client.invalid_reporting_definition_version');
    for (const row of rows) publishedIds.set(identity + ':' + row.versionNumber, row.id.toLowerCase());
    publishedNumbers.set(identity, rows.reduce((latest, row) => Math.max(latest, row.versionNumber), knownLatest));
    versions.value = rows;
  }, 'versions', 'reportingDefinitions.loadFailed');
}
async function acknowledgeRecovery(resource: Resource, id?: string): Promise<void> {
  if (acting.value || !canRead.value) return;
  const target = id ? keyOf(resource, id) : undefined, minimum = target ? rowBlocks.value.get(target) : undefined;
  const open = resource === 'group' ? groupEditorOpen : definitionEditorOpen, owner = resource === 'group' ? groupOwner : definitionOwner, generation = owner.value;
  if (target ? minimum === undefined : !open.value || !creationBlocks.value.has(resource)) return;
  await withAction('reporting.definitions.read', async request => {
    if (!await ask(t('reportingDefinitions.recoveryTitle'), t('reportingDefinitions.recoveryHint')) || !request.current() || !target && (!open.value || owner.value !== generation)) return;
    // 仅解除当前组件的人工重试门槛，服务端没有创建或发布幂等键，不能自动重放。
    if (target && rowBlocks.value.get(target) === minimum) { const blocks = new Map(rowBlocks.value); blocks.delete(target); rowBlocks.value = blocks; }
    else if (!target) { const blocks = new Set(creationBlocks.value); blocks.delete(resource); creationBlocks.value = blocks; }
  });
}
const groupControls = computed(() => {
  const owner = groupOwner.value, open = groupEditorOpen.value, target = editingGroup.value, mode = groupEditorMode.value;
  const owned = () => open && groupEditorOpen.value && canRead.value && owner === groupOwner.value && target === editingGroup.value && mode === groupEditorMode.value;
  const input = (key: 'name' | 'sortOrder' | 'isEnabled', value: unknown) => { if (owned() && !acting.value && typeof value === typeof groupForm[key]) Object.assign(groupForm, { [key]: value }); };
  return { submit: () => { if (owned()) void submitGroup(); }, close: (value: boolean) => { if (!value && owned()) groupEditorOpen.value = false; },
    name: (value: string) => input('name', value), order: (value: string) => input('sortOrder', value), enabled: (value: unknown) => input('isEnabled', value),
    recover: () => { if (owned()) void acknowledgeRecovery('group', target?.id); }, refresh: () => { if (owned() && !acting.value) void loadPage(); } };
});
const definitionControls = computed(() => {
  const owner = definitionOwner.value, open = definitionEditorOpen.value, target = editingDefinition.value, mode = definitionEditorMode.value;
  const owned = () => open && definitionEditorOpen.value && canRead.value && owner === definitionOwner.value && target === editingDefinition.value && mode === definitionEditorMode.value;
  const input = (key: keyof typeof definitionForm, value: unknown) => { if (owned() && !acting.value && typeof value === typeof definitionForm[key]) Object.assign(definitionForm, { [key]: value }); };
  return { submit: () => { if (owned()) void submitDefinition(); }, close: (value: boolean) => { if (!value && owned()) definitionEditorOpen.value = false; },
    key: (value: string) => input('definitionKey', value), name: (value: string) => input('name', value), description: (value: string) => input('description', value),
    port: (value: unknown) => { if (!owned() || acting.value || typeof value !== 'string') return; definitionForm.queryPortKey = value;
      if (!compatibleDataSources.value.some(row => sameId(row.value, definitionForm.dataSourceId))) definitionForm.dataSourceId = compatibleDataSources.value[0]?.value ?? ''; },
    source: (value: unknown) => input('dataSourceId', value), layout: (value: string) => input('layoutConfigJson', value), enabled: (value: unknown) => input('isEnabled', value),
    note: (value: string) => { if (owned() && !acting.value) publishNote.value = value; },
    recover: () => { if (owned()) void acknowledgeRecovery('definition', target?.id); }, refresh: () => { if (owned() && !acting.value) void loadPage(); },
    parameters: definitionForm.parameterSchema.map((parameter, index) => ({ ...parameter,
      name: (value: string) => { if (owned() && !acting.value && definitionForm.parameterSchema[index]?.parameterKey === parameter.parameterKey) definitionForm.parameterSchema[index]!.displayName = value; },
      value: (value: string) => { if (owned() && !acting.value && definitionForm.parameterSchema[index]?.parameterKey === parameter.parameterKey) definitionForm.parameterSchema[index]!.defaultValue = value; } })) };
});
type ParameterRow = (typeof definitionControls.value.parameters)[number];
const versionControls = computed(() => {
  const owner = versionOwner.value, target = versionTarget.value, open = versionsDrawerOpen.value;
  const owned = () => canRead.value && open && versionsDrawerOpen.value && owner === versionOwner.value && target === versionTarget.value;
  return { close: (value: boolean) => { if (!value && owned()) versionsDrawerOpen.value = false; }, retry: () => { if (owned()) void retryVersions(); } };
});
const grantControls = computed(() => { const target = grantDefinition.value; return { close: () => { if (target && target === grantDefinition.value) grantDefinition.value = undefined; } }; });
function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  return isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.unexpected_error', title: t(fallbackKey) };
}
</script>

<template>
  <div v-if="canRead" class="reporting-definitions-view art-page-stack art-full-height">
    <div v-if="receipt" role="status" data-testid="reporting-management-receipt">
      <p>{{ t(receipt.resource === 'group' ? 'reportingDefinitions.groupsTitle' : 'reportingDefinitions.title') }} · {{ t(receipt.kind === 'create' ? 'reportingDefinitions.operationCreate' : receipt.kind === 'update' ? 'reportingDefinitions.actionEdit' : receipt.kind === 'delete' ? 'reportingDefinitions.actionDelete' : 'reportingDefinitions.actionPublish') }} · {{ t(receipt.outcome === 'succeeded' ? 'reportingDefinitions.resultSucceeded' : receipt.outcome === 'failed' ? 'reportingDefinitions.resultFailed' : receipt.outcome === 'processing' ? 'reportingDefinitions.resultProcessing' : 'reportingDefinitions.resultUnconfirmed') }}</p>
      <code v-if="receipt.id" translate="no">{{ receipt.id }}</code>
      <p v-if="receipt.versionNumber">{{ t('reportingDefinitions.fieldVersionNumber') }}: {{ receipt.versionNumber }}</p>
      <p v-if="receipt.outcome === 'unconfirmed'">{{ t('reportingDefinitions.recoveryHint') }}</p>
    </div>
    <template v-for="(error, key) in catalogProblems" :key="key">
      <ElAlert v-if="error" type="error" :title="t(key === 'groups' ? 'reportingDefinitions.groupsTitle' : key === 'definitions' ? 'reportingDefinitions.title' : key === 'ports' ? 'reportingDefinitions.fieldQueryPort' : 'reportingDefinitions.fieldDataSource')" :description="error.title" :closable="false" />
    </template>
    <ElAlert v-if="problem" type="error" :title="problem.title" show-icon class="mb-4" />

    <div class="layout-grid art-split-layout">
      <ElCard :header="t('reportingDefinitions.groupsTitle')" class="groups-card art-table-card">
        <PermissionGate code="reporting.groups.create">
          <ElButton
            type="primary"
            :icon="Plus"
            class="mb-3"
            data-testid="reporting-group-create"
            v-on="{ click: listControls.createGroup }"
          >
            {{ t('reportingDefinitions.addGroup') }}
          </ElButton>
        </PermissionGate>
        <ElTable :data="groupRows" size="small" highlight-current-row v-on="{ 'row-click': listControls.select }">
          <ElTableColumn prop="name" :label="t('reportingDefinitions.fieldGroupName')" />
          <ElTableColumn prop="sortOrder" :label="t('reportingDefinitions.fieldSortOrder')" width="90" />
          <ElTableColumn :label="t('reportingDefinitions.fieldEnabled')" width="90">
            <template #default="{ row }">
              <ElTag :type="row.isEnabled ? 'success' : 'info'">
                {{ row.isEnabled ? t('reportingDefinitions.statusEnabled') : t('reportingDefinitions.statusDisabled') }}
              </ElTag>
            </template>
          </ElTableColumn>
          <!-- @vue-generic {GroupRow} -->
          <ElTableColumn :label="t('reportingDefinitions.actions')" width="140">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="reporting.groups.update">
                  <ArtTableActionButton type="edit" test-id="reporting-group-edit" :disabled="acting || loading || row.blocked" :title="t('reportingDefinitions.actionEdit')" v-on="{ click: row.edit }">
                    {{ t('reportingDefinitions.actionEdit') }}
                  </ArtTableActionButton>
                </PermissionGate>
                <PermissionGate code="reporting.groups.delete">
                  <ArtTableActionButton type="delete" test-id="reporting-group-delete" :disabled="acting || loading || row.blocked" :title="t('reportingDefinitions.actionDelete')" v-on="{ click: row.remove }">
                    {{ t('reportingDefinitions.actionDelete') }}
                  </ArtTableActionButton>
                </PermissionGate>
                <ElButton v-if="row.blocked" :disabled="acting" v-on="{ click: row.recover }">{{ t('reportingDefinitions.recoveryTitle') }}</ElButton>
              </ArtTableActionGroup>
            </template>
          </ElTableColumn>
        </ElTable>
      </ElCard>

      <ElCard class="definitions-card art-table-card">
        <ArtTableHeader v-on="{ refresh: listControls.refresh }">
          <template #left>
            <PermissionGate code="reporting.definitions.create">
              <ElButton
                type="primary"
                :icon="Plus"
                data-testid="reporting-definition-create"
                v-on="{ click: listControls.createDefinition }"
              >
                {{ t('reportingDefinitions.addDefinition') }}
              </ElButton>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <ArtSearchBar
          :key="listControls.token"
          :model-value="searchForm"
          :items="searchItems"
          v-on="{ 'update:modelValue': listControls.input, search: listControls.search, reset: listControls.reset }"
        />

        <div ref="tableMainRef" class="art-crud-table-main">
          <ElTable
            v-loading="loading"
            :data="definitionRows"
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
            <!-- @vue-generic {DefinitionRow} -->
          <ElTableColumn :label="t('reportingDefinitions.actions')" width="260" fixed="right">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate v-if="session.currentUser?.scope === 'host' && row.latestPublishedVersionNumber > 0" code="reporting.definitions.grant_tenants">
                    <ArtTableActionButton type="view" test-id="reporting-tenant-grants-open"
                      :title="t('reportingGrants.manage')" :aria-label="t('reportingGrants.manage')" v-on="{ click: row.grants }" />
                  </PermissionGate>
                  <PermissionGate code="reporting.definitions.update">
                    <ArtTableActionButton type="edit" test-id="reporting-definition-edit" :disabled="acting || loading || row.blocked" :title="t('reportingDefinitions.actionEdit')" v-on="{ click: row.edit }">
                      {{ t('reportingDefinitions.actionEdit') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <PermissionGate code="reporting.definitions.publish">
                    <ArtTableActionButton type="view" test-id="reporting-definition-publish" :disabled="acting || loading || row.blocked" :title="t('reportingDefinitions.actionPublish')" v-on="{ click: row.publish }">
                      {{ t('reportingDefinitions.actionPublish') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <ArtTableActionButton type="view" test-id="reporting-definition-versions" :disabled="acting" :title="t('reportingDefinitions.actionVersions')" v-on="{ click: row.versions }">
                    {{ t('reportingDefinitions.actionVersions') }}
                  </ArtTableActionButton>
                  <PermissionGate code="reporting.definitions.delete">
                    <ArtTableActionButton type="delete" test-id="reporting-definition-delete" :disabled="acting || loading || row.blocked" :title="t('reportingDefinitions.actionDelete')" v-on="{ click: row.remove }">
                      {{ t('reportingDefinitions.actionDelete') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <ElButton v-if="row.blocked" :disabled="acting" v-on="{ click: row.recover }">{{ t('reportingDefinitions.recoveryTitle') }}</ElButton>
                </ArtTableActionGroup>
              </template>
            </ElTableColumn>
          </ElTable>
        </div>
      </ElCard>
    </div>


    <ArtFormDialog v-if="confirmation" :open="true" :title="confirmation.title"
      :confirm-label="t('users.confirm')" :cancel-label="t('common.cancel')" confirm-test-id="reporting-management-confirm"
      v-on="{ confirm: confirmationHandlers.confirm, 'update:open': confirmationHandlers.update }">
      <p>{{ confirmation.message }}</p>
    </ArtFormDialog>

    <ArtFormDialog
      :open="groupEditorOpen"
      :title="groupEditorMode === 'create' ? t('reportingDefinitions.createGroupTitle') : t('reportingDefinitions.editGroupTitle')"
      :saving="acting"
      :show-confirm="!groupBlocked()"
      confirm-test-id="reporting-group-editor-submit"
      v-on="{ confirm: groupControls.submit, 'update:open': groupControls.close }"
    >
      <ElAlert v-if="groupEditorProblem" type="error" :title="groupEditorProblem.title" :closable="false" />
      <ElAlert v-if="groupBlocked()" type="warning" :title="t('reportingDefinitions.recoveryHint')" :closable="false" />
      <ElButton v-if="groupBlocked()" :disabled="acting" v-on="{ click: groupControls.recover }">{{ t('reportingDefinitions.recoveryTitle') }}</ElButton>
      <ElForm ref="groupFormRef" :model="groupForm" :disabled="acting" label-width="120px">
        <ElFormItem :label="t('reportingDefinitions.fieldGroupName')" prop="name" required>
          <ElInput :model-value="groupForm.name" v-on="{ 'update:modelValue': groupControls.name }" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldSortOrder')" prop="sortOrder" required>
          <ElInput :model-value="groupForm.sortOrder" v-on="{ 'update:modelValue': groupControls.order }" />
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
      width="760px"
      :show-confirm="!definitionBlocked() && draftAvailable"
      confirm-test-id="reporting-definition-editor-submit"
      v-on="{ confirm: definitionControls.submit, 'update:open': definitionControls.close }"
    >
      <ElAlert v-if="definitionEditorProblem" type="error" :title="definitionEditorProblem.title" :closable="false" />
      <ElAlert v-if="!draftAvailable" type="warning" :title="t('reportingDefinitions.catalogRequired')" :closable="false" />
      <ElButton v-if="!draftAvailable" :disabled="acting" v-on="{ click: definitionControls.refresh }">{{ t('reportingDefinitions.retryCatalog') }}</ElButton>
      <ElAlert v-if="definitionBlocked()" type="warning" :title="t('reportingDefinitions.recoveryHint')" :closable="false" />
      <ElButton v-if="definitionBlocked()" :disabled="acting" v-on="{ click: definitionControls.recover }">{{ t('reportingDefinitions.recoveryTitle') }}</ElButton>
      <ElForm ref="definitionFormRef" :model="definitionForm" :disabled="acting" label-width="140px">
        <ElFormItem
          v-if="definitionEditorMode === 'create'"
          :label="t('reportingDefinitions.fieldDefinitionKey')"
          prop="definitionKey"
          required
        >
          <ElInput :model-value="definitionForm.definitionKey" v-on="{ 'update:modelValue': definitionControls.key }" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldName')" prop="name" required>
          <ElInput :model-value="definitionForm.name" v-on="{ 'update:modelValue': definitionControls.name }" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldDescription')">
          <ElInput :model-value="definitionForm.description" type="textarea" :rows="2" v-on="{ 'update:modelValue': definitionControls.description }" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldQueryPort')" prop="queryPortKey" required>
          <ElSelect :model-value="definitionForm.queryPortKey" filterable v-on="{ 'update:modelValue': definitionControls.port }">
            <ElOption
              v-for="port in queryPorts"
              :key="port.queryPortKey"
              :label="port.displayName"
              :value="port.queryPortKey"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldDataSource')" prop="dataSourceId" required>
          <ElSelect :model-value="definitionForm.dataSourceId" filterable v-on="{ 'update:modelValue': definitionControls.source }">
            <ElOption
              v-for="option in compatibleDataSources"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldLayoutConfig')">
          <ElInput :model-value="definitionForm.layoutConfigJson" type="textarea" :rows="3" v-on="{ 'update:modelValue': definitionControls.layout }" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldEnabled')">
          <ElSwitch :model-value="definitionForm.isEnabled" v-on="{ 'update:modelValue': definitionControls.enabled }" />
        </ElFormItem>
        <ElFormItem v-if="definitionForm.parameterSchema.length > 0" :label="t('reportingDefinitions.fieldParameterSchema')">
          <ElTable :data="definitionControls.parameters" size="small">
            <ElTableColumn prop="parameterKey" :label="t('reportingDefinitions.fieldParameterKey')" />
            <!-- @vue-generic {ParameterRow} -->
            <ElTableColumn :label="t('reportingDefinitions.fieldParameterDisplayName')">
              <template #default="{ row }">
                <ElInput :model-value="row.displayName" v-on="{ 'update:modelValue': row.name }" />
              </template>
            </ElTableColumn>
            <!-- @vue-generic {ParameterRow} -->
            <ElTableColumn :label="t('reportingDefinitions.fieldParameterDefault')">
              <template #default="{ row }">
                <ElInput :model-value="row.defaultValue ?? ''" v-on="{ 'update:modelValue': row.value }" />
              </template>
            </ElTableColumn>
          </ElTable>
        </ElFormItem>
        <ElFormItem v-if="definitionEditorMode === 'edit'" :label="t('reportingDefinitions.fieldChangeNote')">
          <ElInput :model-value="publishNote" data-testid="reporting-publish-note" type="textarea" :rows="2" v-on="{ 'update:modelValue': definitionControls.note }" />
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>

    <ReportingTenantGrantsDialog v-if="grantDefinition" :definition="grantDefinition" v-on="{ close: grantControls.close }" />
    <ElDrawer :model-value="versionsDrawerOpen" :title="t('reportingDefinitions.versionsTitle')" size="40%" v-on="{ 'update:modelValue': versionControls.close }">
      <ElAlert v-if="versionsProblem" type="error" :title="versionsProblem.title" :closable="false" />
      <ElButton :disabled="acting" data-testid="reporting-versions-retry" v-on="{ click: versionControls.retry }">{{ t('reportingDefinitions.retryVersions') }}</ElButton>
      <ElTable v-loading="acting" :data="versions" size="small">
        <ElTableColumn prop="versionNumber" :label="t('reportingDefinitions.fieldVersionNumber')" width="100" />
        <ElTableColumn prop="queryPortKey" :label="t('reportingDefinitions.fieldQueryPort')" />
        <ElTableColumn prop="publishedAtUtc" :label="t('reportingDefinitions.fieldPublishedAt')" min-width="180" />
        <ElTableColumn prop="changeNote" :label="t('reportingDefinitions.fieldChangeNote')" />
      </ElTable>
    </ElDrawer>
  </div>
</template>

<style scoped>
.layout-grid {
  display: flex;
  flex: 1;
  min-height: 0;
  gap: 16px;
}

.groups-card { width: 320px; flex: 0 0 320px; overflow: auto; }
.definitions-card {
  flex: 1;
  min-width: 0;
}

.mb-3 {
  margin-bottom: 12px;
}

.mb-4 {
  margin-bottom: 16px;
}
</style>
