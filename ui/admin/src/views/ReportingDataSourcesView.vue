<script setup lang="ts">
import { computed, inject, onActivated, onBeforeUnmount, onDeactivated, onMounted, reactive, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import {
  ElAlert,
  ElButton,
  ElCard,
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
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type {
  FullNetProblemDetails,
  ReportingDataSource,
  ReportingDataSourceListItem,
  TestReportingDataSourceResult
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getReportingDataSource,
  createReportingDataSource,
  deleteReportingDataSource,
  disableReportingDataSource,
  listReportingDataSources,
  testReportingDataSource,
  updateReportingDataSource
} from '../api/reporting-data-sources';

defineOptions({ name: 'ReportingDataSourcesView' });

type EditorMode = 'create' | 'edit';
type RowTarget = Pick<ReportingDataSourceListItem, 'id' | 'version'>;
type MutationKind = 'create' | 'update' | 'disable' | 'delete' | 'test';
type Receipt = { sequence: number; kind: MutationKind; id?: string; version?: number; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed' };
const { t } = useAdminI18n();
const session = useSessionStore(), route = inject(routeLocationKey, undefined);
const items = ref<ReportingDataSourceListItem[]>([]), total = ref(0), page = ref(1), pageSize = ref(20);
const loading = ref(false), changing = ref(false), problem = ref<FullNetProblemDetails>(), editorProblem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({}), appliedFilters = ref({ name: '' });
const editorOpen = ref(false), editorMode = ref<EditorMode>('create'), editing = ref<ReportingDataSource | null>(null);
const editingListTarget = ref<RowTarget>(), editorFormRef = ref<FormInstance>(), testResult = ref<TestReportingDataSourceResult | null>(null);
const testResultTarget = ref<string>();
const editorForm = reactive({ tenantId: '', name: '', providerKey: 'sql_server', serverHost: '', port: '1433',
  databaseName: '', username: '', password: '', trustServerCertificate: false, isEnabled: true });
const receipt = ref<Receipt>(), createBlocked = ref(false), rowBlocks = ref(new Map<string, number>());
const editorBlocked = computed(() => editorMode.value === 'create' ? createBlocked.value : !!editingListTarget.value && rowBlocks.value.has(editingListTarget.value.id.toLowerCase()));
// 只保存标识与修订，不把密码、连接端点或表单快照写入恢复记录。
const revisionFloors = new Map<string, number>(), deletedIds = new Set<string>();
let receiptSequence = 0, controlsSequence = 0, editorOwner = 0;
const confirmation = ref<{ title: string; message: string; resolve: (confirmed: boolean) => void }>();
const confirmationHandlers = computed(() => {
  const pending = confirmation.value;
  const finish = (confirmed: boolean) => { if (pending && confirmation.value === pending) finishConfirmation(confirmed); };
  return { confirm: () => finish(true), update: (open: boolean) => { if (!open) finish(false); } };
});
function finishConfirmation(confirmed: boolean): void {
  const pending = confirmation.value; confirmation.value = undefined; pending?.resolve(confirmed);
}
function ask(title: string, message: string): Promise<boolean> {
  finishConfirmation(false); return new Promise(resolve => { confirmation.value = { title, message, resolve }; });
}
const viewActive = ref(false);
onMounted(() => { viewActive.value = true; }); onActivated(() => { viewActive.value = true; });
onDeactivated(() => { viewActive.value = false; }); onBeforeUnmount(() => { viewActive.value = false; });
const host = () => viewActive.value && (!route || route.name === 'reporting-data-sources')
  && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null;
const canRead = computed(() => host() && session.can('reporting.data_sources.read'));
const scope = useAuthorizedViewScope(session, resetScope, load);
type ViewRequest = NonNullable<ReturnType<typeof scope.begin>>;
let listRequest: ViewRequest | undefined, editorRequest: ViewRequest | undefined, actionRequest: ViewRequest | undefined;
let pendingMutation: { request: ViewRequest; receipt: Receipt } | undefined;
let lastQuery: { page: number; pageSize: number; nameContains: string | undefined } | undefined;
const begin = (permission: string) => canRead.value ? scope.begin(permission) : undefined;
const sameId = (left: string | null | undefined, right: string | null | undefined) => (left?.toLowerCase() ?? null) === (right?.toLowerCase() ?? null);
const validId = (value: string) => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
const validVersion = (value: number) => Number.isSafeInteger(value) && value > 0;
function canEnter(permission: string): boolean { const request = begin(permission); request?.finish(); return !!request; }
function currentRow(target: RowTarget): boolean {
  return items.value.some(item => sameId(item.id, target.id) && item.version === target.version);
}
function allowedRow(target: RowTarget): boolean {
  return currentRow(target) && target.version >= (revisionFloors.get(target.id.toLowerCase()) ?? 0) && !rowBlocks.value.has(target.id.toLowerCase());
}
function resetScope(): void {
  finishConfirmation(false); pendingMutation = undefined; editorOpen.value = false; cancelEditor();
  items.value = []; total.value = 0; page.value = 1; problem.value = undefined; editorProblem.value = undefined;
  searchForm.value = {}; appliedFilters.value = { name: '' }; editing.value = null; editingListTarget.value = undefined;
  resetEditor(); loading.value = false; changing.value = false; actionRequest = undefined;
  receipt.value = undefined; createBlocked.value = false; rowBlocks.value = new Map(); revisionFloors.clear(); deletedIds.clear();
  lastQuery = undefined; listControls.value = makeListControls();
}
watch(() => route?.name, () => { scope.invalidate(); if (canRead.value) void load(); }, { flush: 'sync' });
function stopList(): void { listRequest?.cancel(); listRequest = undefined; loading.value = false; }
function markUnconfirmed(pending: NonNullable<typeof pendingMutation>): void {
  stopList(); receipt.value = { ...pending.receipt, outcome: 'unconfirmed' };
  if (pending.receipt.kind === 'create') createBlocked.value = true;
  else if (pending.receipt.id) rowBlocks.value = new Map(rowBlocks.value).set(pending.receipt.id.toLowerCase(), pending.receipt.version ?? 0);
}
function cancelEditor(): void {
  const request = editorRequest;
  if (request && pendingMutation?.request === request) { markUnconfirmed(pendingMutation); pendingMutation = undefined; }
  request?.cancel(); editorRequest = undefined;
  if (request && actionRequest === request) { actionRequest = undefined; changing.value = false; }
}
watch(editorOpen, open => {
  editorOwner++; finishConfirmation(false);
  if (!open) { cancelEditor(); editing.value = null; editingListTarget.value = undefined; editorProblem.value = undefined; resetEditor(); }
}, { flush: 'sync' });
watch(() => ({ ...editorForm }), () => { editorOwner++; cancelEditor(); finishConfirmation(false); }, { flush: 'sync' });
// 列表目标失效就清除凭据；详情允许比打开时的列表更新。
watch(items, () => { if (editingListTarget.value && !currentRow(editingListTarget.value)) editorOpen.value = false; }, { flush: 'sync', deep: true });
function sent(request: ViewRequest, kind: MutationKind, target?: RowTarget): void {
  stopList(); receipt.value = { sequence: ++receiptSequence, kind, id: target?.id, version: target?.version, outcome: 'processing' };
  pendingMutation = { request, receipt: { ...receipt.value } };
}
function detach(request: ViewRequest): void {
  if (pendingMutation?.request === request) pendingMutation = undefined;
  if (editorRequest === request) editorRequest = undefined;
  if (actionRequest === request) { actionRequest = undefined; changing.value = false; }
}
function confirmed(request: ViewRequest, id?: string, version?: number): void {
  if (pendingMutation?.request !== request) return;
  receipt.value = { ...pendingMutation.receipt, id: id ?? pendingMutation.receipt.id, version: version ?? pendingMutation.receipt.version, outcome: 'succeeded' };
  detach(request);
}
async function withAction(permission: string, fallback: Parameters<typeof t>[0],
  action: (request: ViewRequest) => Promise<void>, editor = false): Promise<void> {
  if (changing.value) return;
  const request = begin(permission); if (!request) return;
  if (editor) editorRequest = request;
  actionRequest = request; changing.value = true;
  try { await action(request); }
  catch (error) {
    if (request.current()) {
      const failure = toProblem(error, fallback);
      if (pendingMutation?.request === request) {
        if (!isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408) markUnconfirmed(pendingMutation);
        else receipt.value = { ...pendingMutation.receipt, outcome: 'failed' };
      }
      if (editor) editorProblem.value = failure; else problem.value = failure;
      ElMessage.error(failure.title);
    }
  } finally { detach(request); request.finish(); }
}
const { tableMainRef, tableHeight, tableSize, tableZebra, tableBorder, tableHeaderBackground,
  tableHeaderCellStyle, syncTableLayout } = useArtPagedTableInCard(loading);
const searchItems = computed<ArtSearchBarItem[]>(() => [{ key: 'name', label: t('reportingDataSources.fieldName'), type: 'input' }]);
const providerOptions = computed(() => [{ value: 'sql_server', label: t('reportingDataSources.providerSqlServer') },
  { value: 'mysql', label: t('reportingDataSources.providerMySql') }]);
function rowIndex(index: number) { return (page.value - 1) * pageSize.value + index + 1; }
function providerLabel(key: string) { return t(key === 'mysql' ? 'reportingDataSources.providerMySql' : 'reportingDataSources.providerSqlServer'); }
function testStatusTagType(key: string | null): 'success' | 'danger' | 'info' { return key === 'succeeded' ? 'success' : key === 'failed' ? 'danger' : 'info'; }
interface ListControls { token: number; refresh: () => void; create: () => void; search: () => void; input: (value: Record<string, string | undefined>) => void; page: (value: number) => void; size: (value: number) => void; change: (page: number, size: number) => void }
const listControls = ref<ListControls>(makeListControls());
function makeListControls(): ListControls {
  // 搜索组件会原地更新模型；换代时同时隔离对象和组件实例，避免旧输入事件改写新查询。
  searchForm.value = { ...searchForm.value };
  const token = ++controlsSequence, owned = () => canRead.value && token === listControls.value.token;
  return { token, refresh: () => { if (owned()) void load(); }, create: () => { if (owned()) openCreate(); },
    search: () => { if (owned()) applySearch(); }, input: value => { if (owned()) searchForm.value = { ...value }; },
    page: value => { if (owned() && Number.isSafeInteger(value) && value > 0) page.value = value; },
    size: value => { if (owned() && [10, 20, 50, 100].includes(value)) pageSize.value = value; },
    change: (nextPage, nextSize) => {
      if (!owned() || !Number.isSafeInteger(nextPage) || nextPage < 1 || ![10, 20, 50, 100].includes(nextSize)) return;
      page.value = lastQuery?.pageSize !== nextSize ? 1 : nextPage; pageSize.value = nextSize;
      if (lastQuery?.page !== page.value || lastQuery.pageSize !== pageSize.value) void load();
    } };
}
const tableRows = computed(() => {
  const token = listControls.value.token;
  return items.value.map(row => { const target = { ...row }, owned = () => canRead.value && token === listControls.value.token && currentRow(target);
    return { ...target, blocked: rowBlocks.value.has(target.id.toLowerCase()), edit: () => { if (owned()) void openEdit(target); },
      test: () => { if (owned()) void runTest(target); }, disable: () => { if (owned()) void runDisable(target); },
      remove: () => { if (owned()) void runDelete(target); }, recover: () => { if (owned()) void acknowledgeRecovery(target.id); } };
  });
});
type TableRow = (typeof tableRows.value)[number];
const paginationTotal = computed(() => Math.max(total.value, items.value.length ? (page.value - 1) * pageSize.value + items.value.length : 0));
async function load(fallback = true): Promise<void> {
  stopList(); const request = begin('reporting.data_sources.read'); if (!request) return; listRequest = request;
  const query = { page: page.value, pageSize: pageSize.value, nameContains: appliedFilters.value.name || undefined };
  // 清理旧行但保留分页总数，防止真实分页组件在读取新页期间回跳。
  if (JSON.stringify(lastQuery) !== JSON.stringify(query)) { items.value = []; }
  lastQuery = query; listControls.value = makeListControls(); loading.value = true; problem.value = undefined;
  try {
    const result = await listReportingDataSources({ ...query }, request.signal);
    if (!request.current() || listRequest !== request) return;
    if (result.page !== query.page || result.pageSize !== query.pageSize || !Number.isSafeInteger(result.total) || result.total < 0
      || result.items.some(row => !validId(row.id) || !validVersion(row.version) || deletedIds.has(row.id.toLowerCase()) || row.version < (revisionFloors.get(row.id.toLowerCase()) ?? 0))
      || new Set(result.items.map(row => row.id.toLowerCase())).size !== result.items.length) throw Error('client.invalid_reporting_data_source_page');
    if (fallback && result.items.length === 0 && query.page > 1) {
      const last = Math.max(1, Math.ceil(result.total / query.pageSize));
      if (last < query.page) { page.value = last; await load(false); return; }
    }
    const blocks = new Map(rowBlocks.value);
    for (const row of result.items) { const minimum = blocks.get(row.id.toLowerCase()); if (minimum !== undefined && row.version > minimum) blocks.delete(row.id.toLowerCase()); }
    rowBlocks.value = blocks; items.value = result.items; total.value = result.total; await syncTableLayout();
  } catch (error) { if (request.current() && listRequest === request) problem.value = toProblem(error, 'reportingDataSources.loadFailed'); }
  finally { if (listRequest === request) { loading.value = false; listRequest = undefined; listControls.value = makeListControls(); } request.finish(); }
}
function applySearch(): void { appliedFilters.value = { name: searchForm.value.name?.trim() ?? '' }; page.value = 1; void load(); }
function resetEditor(): void {
  Object.assign(editorForm, { tenantId: '', name: '', providerKey: 'sql_server', serverHost: '', port: '1433', databaseName: '',
    username: '', password: '', trustServerCertificate: false, isEnabled: true }); testResult.value = null; testResultTarget.value = undefined;
}
function openCreate(): void {
  if (changing.value || !canEnter('reporting.data_sources.create')) return;
  editorOpen.value = false; resetEditor(); editorProblem.value = undefined; editorMode.value = 'create'; editing.value = null; editorOpen.value = true;
}
async function openEdit(row: ReportingDataSourceListItem): Promise<void> {
  const target = { id: row.id, version: row.version };
  if (changing.value || !currentRow(target) || rowBlocks.value.has(target.id.toLowerCase()) || !canEnter('reporting.data_sources.update')) return;
  editorOpen.value = false; resetEditor(); editorProblem.value = undefined; editing.value = null;
  editorMode.value = 'edit'; editingListTarget.value = target; editorOpen.value = true; await retryEditor();
}
async function retryEditor(): Promise<void> {
  const target = editingListTarget.value ? { ...editingListTarget.value } : undefined;
  if (changing.value || !editorOpen.value || editorMode.value !== 'edit' || !target || !currentRow(target)) return;
  editorProblem.value = undefined; editing.value = null;
  await withAction('reporting.data_sources.update', 'reportingDataSources.loadFailed', async request => {
    const detail = await getReportingDataSource(target.id, request.signal);
    if (!request.current()) return;
    if (!sameId(detail.id, target.id) || !validVersion(detail.version) || detail.version < Math.max(target.version, revisionFloors.get(target.id.toLowerCase()) ?? 0)) throw Error('client.invalid_reporting_data_source');
    // 填充表单会触发意图失效，必须先释放详情租约，不能让自己的填充中断后续流程。
    detach(request); editing.value = { ...detail };
    revisionFloors.set(detail.id.toLowerCase(), Math.max(detail.version, revisionFloors.get(detail.id.toLowerCase()) ?? 0));
    const minimum = rowBlocks.value.get(detail.id.toLowerCase());
    if (minimum !== undefined && detail.version > minimum) { const blocks = new Map(rowBlocks.value); blocks.delete(detail.id.toLowerCase()); rowBlocks.value = blocks; }
    Object.assign(editorForm, { tenantId: detail.tenantId ?? '', name: detail.name, providerKey: detail.providerKey, serverHost: detail.serverHost,
      port: String(detail.port), databaseName: detail.databaseName, username: detail.username, password: '',
      trustServerCertificate: detail.trustServerCertificate, isEnabled: detail.isEnabled });
  }, true);
}
const editorControls = computed(() => {
  // 响应式依赖让每次输入、开关或目标变化都生成独立回调，旧 DOM 事件不能复用新表单。
  const open = editorOpen.value, mode = editorMode.value, detail = editing.value, owner = editorOwner;
  const fieldValues = Object.values(editorForm);
  const owned = () => open && editorOpen.value && canRead.value && owner === editorOwner && mode === editorMode.value && detail === editing.value;
  void fieldValues;
  const input = (key: string, value: unknown) => { if (owned() && !changing.value && Object.hasOwn(editorForm, key) && typeof value === typeof editorForm[key as keyof typeof editorForm]) Object.assign(editorForm, { [key]: value }); };
  return { submit: () => { if (owned()) void submitEditor(); }, update: (value: boolean) => { if (owned() && !value) editorOpen.value = false; },
    input, name: (value: string) => input('name', value), provider: (value: unknown) => input('providerKey', value),
    server: (value: string) => input('serverHost', value), port: (value: string) => input('port', value),
    database: (value: string) => input('databaseName', value), username: (value: string) => input('username', value), password: (value: string) => input('password', value),
    trust: (value: unknown) => input('trustServerCertificate', value), enabled: (value: unknown) => input('isEnabled', value),
    retry: () => { if (owned()) void retryEditor(); }, recover: () => { if (owned()) void acknowledgeRecovery(mode === 'edit' ? editingListTarget.value?.id : undefined); } };
});
async function submitEditor(): Promise<void> {
  if (!editorOpen.value || !editorFormRef.value) return;
  const mode = editorMode.value, target = editing.value ? { ...editing.value } : undefined, input = { ...editorForm };
  if (mode === 'create' && createBlocked.value || mode === 'edit' && (!target || !editingListTarget.value || !currentRow(editingListTarget.value)
    || rowBlocks.value.has(target.id.toLowerCase()) || target.version < (revisionFloors.get(target.id.toLowerCase()) ?? 0))) return;
  editorProblem.value = undefined;
  await withAction(mode === 'create' ? 'reporting.data_sources.create' : 'reporting.data_sources.update', 'reportingDataSources.saveFailed', async request => {
    const valid = await editorFormRef.value!.validate().catch(() => false);
    if (!valid || !request.current()) return;
    const payload = { name: input.name.trim(), providerKey: input.providerKey.trim(), serverHost: input.serverHost.trim(), port: Number(input.port),
      databaseName: input.databaseName.trim(), username: input.username.trim(), trustServerCertificate: input.trustServerCertificate, isEnabled: input.isEnabled };
    sent(request, mode === 'create' ? 'create' : 'update', target);
    const updated = mode === 'create'
      ? await createReportingDataSource({ ...payload, tenantId: input.tenantId.trim() || null, password: input.password }, request.signal)
      : await updateReportingDataSource(target!.id, { ...payload, password: input.password.trim() ? input.password : null, version: target!.version }, request.signal);
    if (!request.current()) return;
    if (!validId(updated.id) || !validVersion(updated.version) || mode === 'edit' && (!sameId(updated.id, target!.id) || updated.version !== target!.version + 1)) throw Error('client.invalid_reporting_data_source');
    confirmed(request, updated.id, updated.version);
    if (mode === 'edit') { revisionFloors.set(updated.id.toLowerCase(), updated.version); items.value = items.value.filter(row => !sameId(row.id, updated.id)); }
    editorOpen.value = false; ElMessage.success(t(mode === 'create' ? 'reportingDataSources.createSuccess' : 'reportingDataSources.updateSuccess')); await load();
  }, true);
}
async function runTest(row: ReportingDataSourceListItem): Promise<void> {
  const target = { id: row.id, version: row.version }; if (!allowedRow(target)) return;
  await withAction('reporting.data_sources.test', 'reportingDataSources.testFailed', async request => {
    testResult.value = null; testResultTarget.value = undefined; sent(request, 'test', target); const result = await testReportingDataSource(target.id, request.signal);
    if (!request.current()) return;
    confirmed(request); testResult.value = result; testResultTarget.value = target.id;
    if (result.succeeded) ElMessage.success(result.message); else ElMessage.error(result.message);
    await load();
  });
}
async function runDisable(row: ReportingDataSourceListItem): Promise<void> {
  const target = { id: row.id, version: row.version, name: row.name }; if (!allowedRow(target)) return;
  await withAction('reporting.data_sources.update', 'reportingDataSources.saveFailed', async request => {
    if (!await ask(t('reportingDataSources.actionDisable'), t('reportingDataSources.confirmDisable', { name: target.name })) || !request.current() || !allowedRow(target)) return;
    sent(request, 'disable', target); const updated = await disableReportingDataSource(target.id, request.signal);
    if (!request.current()) return;
    if (!sameId(updated.id, target.id) || !validVersion(updated.version) || updated.version <= target.version || updated.isEnabled) throw Error('client.invalid_reporting_data_source');
    confirmed(request, updated.id, updated.version); revisionFloors.set(updated.id.toLowerCase(), updated.version);
    items.value = items.value.filter(item => !sameId(item.id, updated.id)); ElMessage.success(t('reportingDataSources.disableSuccess')); await load();
  });
}
async function runDelete(row: ReportingDataSourceListItem): Promise<void> {
  const target = { id: row.id, version: row.version, name: row.name }; if (!allowedRow(target)) return;
  await withAction('reporting.data_sources.delete', 'reportingDataSources.saveFailed', async request => {
    if (!await ask(t('reportingDataSources.actionDelete'), t('reportingDataSources.confirmDelete', { name: target.name })) || !request.current() || !allowedRow(target)) return;
    sent(request, 'delete', target); const removed = await deleteReportingDataSource(target.id, request.signal);
    if (!request.current()) return;
    if (!removed) { pendingMutation = undefined; receipt.value = { ...receipt.value!, outcome: 'failed' }; throw Error('client.reporting_data_source_delete_failed'); }
    confirmed(request); deletedIds.add(target.id.toLowerCase()); items.value = items.value.filter(item => !sameId(item.id, target.id));
    ElMessage.success(t('reportingDataSources.deleteSuccess')); await load();
  });
}
async function acknowledgeRecovery(id?: string): Promise<void> {
  const target = id?.toLowerCase(), owner = editorOwner;
  const minimum = target ? rowBlocks.value.get(target) : undefined;
  if (changing.value || !canRead.value || (target ? minimum === undefined : !editorOpen.value || !createBlocked.value)) return;
  const permission = target ? 'reporting.data_sources.read' : 'reporting.data_sources.create';
  await withAction(permission, 'reportingDataSources.saveFailed', async request => {
    const confirmed = await ask(t('reportingDataSources.recoveryTitle'), t('reportingDataSources.recoveryHint'));
    if (!confirmed || !request.current() || !target && (!editorOpen.value || owner !== editorOwner)) return;
    // 确认仅解锁下一次人工提交，绝不重放服务端写入；服务端当前没有创建幂等键。
    if (target && rowBlocks.value.get(target) === minimum) { const blocks = new Map(rowBlocks.value); blocks.delete(target); rowBlocks.value = blocks; }
    else if (!target) createBlocked.value = false;
  });
}
function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  return isFullNetProblemDetails(error) ? error : { title: t(fallbackKey), status: 500, code: fallbackKey };
}

</script>

<template>
  <section v-if="canRead" class="reporting-data-sources-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('reportingDataSources.title') }}</h1>

    <div v-if="receipt" role="status" data-testid="reporting-data-source-receipt">
      <p>{{ t(receipt.kind === 'create' ? 'reportingDataSources.addDataSource' : receipt.kind === 'update' ? 'reportingDataSources.actionEdit' : receipt.kind === 'disable' ? 'reportingDataSources.actionDisable' : receipt.kind === 'delete' ? 'reportingDataSources.actionDelete' : 'reportingDataSources.testConnection') }} · {{ t(receipt.outcome === 'succeeded' ? 'reportingDataSources.resultSucceeded' : receipt.outcome === 'failed' ? 'reportingDataSources.resultFailed' : receipt.outcome === 'processing' ? 'reportingDataSources.resultProcessing' : 'reportingDataSources.resultUnconfirmed') }}</p>
      <code v-if="receipt.id" translate="no">{{ receipt.id }}</code>
      <p v-if="receipt.outcome === 'unconfirmed'">{{ t('reportingDataSources.recoveryHint') }}</p>
    </div>
    <el-alert v-if="testResult" :type="testResult.succeeded ? 'success' : 'warning'" :title="testResult.message" :closable="false"><code translate="no">{{ testResultTarget }}</code></el-alert>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

    <el-card class="art-table-card art-full-height" shadow="never">
      <ArtSearchBar
        :key="listControls.token"
        :model-value="searchForm"
        :items="searchItems" v-on="{ 'update:modelValue': listControls.input, 'search': listControls.search, 'reset': listControls.search }"/>
      <ArtTableHeader v-on="{ 'refresh': listControls.refresh }">
        <template #left>
          <PermissionGate code="reporting.data_sources.create">
            <el-button
              type="primary"
              :icon="Plus"
              data-testid="reporting-data-source-create" v-on="{ 'click': listControls.create }">
              {{ t('reportingDataSources.addDataSource') }}
            </el-button>
          </PermissionGate>
        </template>
      </ArtTableHeader>

      <div ref="tableMainRef" class="art-crud-table-main">
        <el-table
          v-loading="loading"
          :data="tableRows"
          :height="tableHeight"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
          :header-cell-class-name="tableHeaderBackground ? 'art-table-header-background' : ''"
        >
          <el-table-column type="index" :index="rowIndex" width="56" />
          <el-table-column prop="name" :label="t('reportingDataSources.fieldName')" min-width="140" />
          <el-table-column :label="t('reportingDataSources.fieldProvider')" min-width="120">
            <template #default="{ row }">{{ providerLabel(row.providerKey) }}</template>
          </el-table-column>
          <el-table-column prop="maskedServerEndpoint" :label="t('reportingDataSources.fieldEndpoint')" min-width="160" />
          <el-table-column prop="maskedDatabaseName" :label="t('reportingDataSources.fieldDatabase')" min-width="120" />
          <el-table-column :label="t('reportingDataSources.fieldTestStatus')" min-width="120">
            <template #default="{ row }">
              <el-tag v-if="row.lastTestStatusKey" :type="testStatusTagType(row.lastTestStatusKey)">
                {{ row.lastTestStatusKey }}
              </el-tag>
              <span v-else>-</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('reportingDataSources.fieldEnabled')" width="90">
            <template #default="{ row }">
              <el-tag :type="row.isEnabled ? 'success' : 'info'">
                {{ row.isEnabled ? t('reportingDataSources.statusEnabled') : t('reportingDataSources.statusDisabled') }}
              </el-tag>
            </template>
          </el-table-column>
          <!-- @vue-generic {TableRow} -->
          <el-table-column :label="t('reportingDataSources.actions')" width="260" fixed="right">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="reporting.data_sources.test">
                  <ArtTableActionButton type="view"
                    :title="t('reportingDataSources.testConnection')"
                    test-id="reporting-data-source-test"
                    :disabled="changing || row.blocked" v-on="{ 'click': row.test }"/>
                </PermissionGate>
                <PermissionGate code="reporting.data_sources.update">
                  <ArtTableActionButton type="edit"
                    :title="t('reportingDataSources.actionEdit')"
                    test-id="reporting-data-source-edit"
                    :disabled="changing || row.blocked" v-on="{ 'click': row.edit }"/>
                </PermissionGate>
                <PermissionGate code="reporting.data_sources.update">
                  <ArtTableActionButton type="delete"
                    :title="t('reportingDataSources.actionDisable')"
                    test-id="reporting-data-source-disable"
                    :disabled="changing || row.blocked" v-on="{ 'click': row.disable }"/>
                </PermissionGate>
                <PermissionGate code="reporting.data_sources.delete">
                  <ArtTableActionButton type="delete"
                    :title="t('reportingDataSources.actionDelete')"
                    test-id="reporting-data-source-delete"
                    :disabled="changing || row.blocked" v-on="{ 'click': row.remove }"/>
                </PermissionGate>
                <el-button v-if="row.blocked" :disabled="changing" data-testid="reporting-data-source-row-recover" v-on="{ 'click': row.recover }">{{ t('reportingDataSources.recoveryTitle') }}</el-button>
              </ArtTableActionGroup>
            </template>
          </el-table-column>
        </el-table>
      <el-pagination
        :current-page="page"
        :page-size="pageSize"
        :page-sizes="[10, 20, 50, 100]"
        :total="paginationTotal"
        layout="total, sizes, prev, pager, next"
        class="art-table-pagination" v-on="{ 'update:currentPage': listControls.page, 'update:pageSize': listControls.size, 'change': listControls.change }"/>
      </div>
    </el-card>


    <ArtFormDialog v-if="confirmation" :open="true" :title="confirmation.title"
      :confirm-label="t('users.confirm')" :cancel-label="t('common.cancel')" confirm-test-id="reporting-data-source-confirm" v-on="{ 'confirm': confirmationHandlers.confirm, 'update:open': confirmationHandlers.update }">
      <p>{{ confirmation.message }}</p>
    </ArtFormDialog>

    <ArtFormDialog
      :open="editorOpen"
      :title="editorMode === 'create' ? t('reportingDataSources.createTitle') : t('reportingDataSources.editTitle')"
      :saving="changing"
      :show-confirm="!editorBlocked && (editorMode === 'create' || !!editing)"
      confirm-test-id="reporting-data-source-editor-submit" v-on="{ 'confirm': editorControls.submit, 'update:open': editorControls.update }">
      <el-alert v-if="editorProblem" type="error" :title="editorProblem.title" :description="editorProblem.detail ?? editorProblem.code" :closable="false" />
      <el-button v-if="editorMode === 'edit' && (!editing || editorBlocked || editorProblem)" :disabled="changing" data-testid="reporting-data-source-detail-retry" v-on="{ 'click': editorControls.retry }">{{ t('reportingDataSources.retryDetail') }}</el-button>
      <el-alert v-if="editorBlocked" type="warning" :title="t('reportingDataSources.recoveryHint')" :closable="false" />
      <el-button v-if="editorBlocked" :disabled="changing" data-testid="reporting-data-source-create-recover" v-on="{ 'click': editorControls.recover }">{{ t('reportingDataSources.recoveryTitle') }}</el-button>
      <el-form ref="editorFormRef" :model="editorForm" :disabled="changing || editorMode === 'edit' && !editing" label-width="140px">
        <el-form-item :label="t('reportingDataSources.fieldName')" prop="name" required>
          <el-input :model-value="editorForm.name" v-on="{ 'update:modelValue': editorControls.name }"/>
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldProvider')" prop="providerKey" required>
          <el-select :model-value="editorForm.providerKey" style="width: 100%" v-on="{ 'update:modelValue': editorControls.provider }">
            <el-option
              v-for="option in providerOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldServerHost')" prop="serverHost" required>
          <el-input :model-value="editorForm.serverHost" v-on="{ 'update:modelValue': editorControls.server }"/>
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldPort')" prop="port" required>
          <el-input :model-value="editorForm.port" v-on="{ 'update:modelValue': editorControls.port }"/>
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldDatabase')" prop="databaseName" required>
          <el-input :model-value="editorForm.databaseName" v-on="{ 'update:modelValue': editorControls.database }"/>
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldUsername')" prop="username" required>
          <el-input :model-value="editorForm.username" v-on="{ 'update:modelValue': editorControls.username }"/>
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldPassword')" prop="password" :required="editorMode === 'create'">
          <el-input :model-value="editorForm.password" type="password" show-password v-on="{ 'update:modelValue': editorControls.password }"/>
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldTrustServerCertificate')">
          <el-switch :model-value="editorForm.trustServerCertificate" v-on="{ 'update:modelValue': editorControls.trust }"/>
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldEnabled')">
          <el-switch :model-value="editorForm.isEnabled" v-on="{ 'update:modelValue': editorControls.enabled }"/>
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>
