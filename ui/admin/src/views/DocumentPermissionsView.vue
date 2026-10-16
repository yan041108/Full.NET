<script setup lang="ts">
import { computed, inject, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { routeLocationKey } from 'vue-router';
import { Refresh, Search } from '@element-plus/icons-vue';
import { ElAlert, ElButton, ElCard, ElForm, ElFormItem, ElInput, ElPagination, ElTable, ElTableColumn } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentItemResponse, HostDocumentPermissionResponse, SetHostDocumentPermissionsRequest } from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isSetHostDocumentPermissionsRequest } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { showSuccess, showProblem, showWarning } from '../feedback/fullNetMessage';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listDocumentItems } from '../api/host-document-items';
import { getDocumentPermissionSnapshot, setDocumentPermissions } from '../api/document-permissions';
import { permissionRowsMatch, permissionSignature, trimPermissionText, validPermissionDocumentPage, validPermissionId, validPermissionSnapshot } from '../guards/document-permission-guards';

defineOptions({ name: 'DocumentPermissionsView' });
const session = useSessionStore(), route = inject(routeLocationKey, undefined), { t } = useAdminI18n();
const items = ref<HostDocumentItemResponse[]>([]), loading = ref(false), saving = ref(false), permissionsLoading = ref(false), recovering = ref(false), catalogReady = ref(false);
const problem = ref<FullNetProblemDetails>(), permissionProblem = ref<FullNetProblemDetails>();
const page = ref(1), pageSize = ref(20), total = ref(0), searchKeyword = ref(''), appliedKeyword = ref(''), catalogOwner = ref(0), dialogOwner = ref(0), dialogWindow = ref(0);
const permissionDialogOpen = ref(false), activeDocument = ref<HostDocumentItemResponse | null>(null), permissions = ref<HostDocumentPermissionResponse[]>([]), permissionVersion = ref(0), permissionsReady = ref(false);
const userId = ref(''), permissionLevel = ref('read');
type WriteKind = 'set' | 'remove';
const saveReceipt = ref<{ documentId: string; title: string; action: WriteKind; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed'; version: number; count?: number; errorCode?: string }>();
// 只在当前授权组件代次保存已观察修订及集合签名，不把恢复状态持久化或写入浏览器存储。
const knownSnapshots = new Map<string, { version: number; signature: string }>(), revisionFloors = new Map<string, number>();
const writeBlocks = ref(new Map<string, number>());
const removalConfirmation = ref<{ userId: string; documentTitle: string; resolve: (value: boolean) => void }>();
const recoveryConfirmation = ref<{ resolve: (value: boolean) => void }>();
function finishRemoval(value: boolean): void { const pending = removalConfirmation.value; removalConfirmation.value = undefined; pending?.resolve(value); }
function finishRecovery(value: boolean): void { const pending = recoveryConfirmation.value; recoveryConfirmation.value = undefined; pending?.resolve(value); }
const removalHandlers = computed(() => { const pending = removalConfirmation.value, finish = (value: boolean) => { if (pending && removalConfirmation.value === pending) finishRemoval(value); }; return { confirm: () => finish(true), update: (open: boolean) => { if (!open) finish(false); } }; });
const recoveryHandlers = computed(() => { const pending = recoveryConfirmation.value, finish = (value: boolean) => { if (pending && recoveryConfirmation.value === pending) finishRecovery(value); }; return { confirm: () => finish(true), update: (open: boolean) => { if (!open) finish(false); } }; });
const { tableMainRef, tableHeight, tableSize, tableZebra, tableBorder, tableHeaderBackground, tableHeaderCellStyle, syncTableLayout } = useArtPagedTableInCard(loading);
const viewActive = ref(false);
onMounted(() => { viewActive.value = true; }); onActivated(() => { viewActive.value = true; });
onDeactivated(() => { viewActive.value = false; }); onBeforeUnmount(() => { viewActive.value = false; });
const canReadDocuments = computed(() => viewActive.value && (!route || route.name === 'document-permissions') && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null && session.can('document.host_documents.read') && session.can('document.host_permissions.read'));
const canReadPermissions = () => canReadDocuments.value && session.can('document.host_permissions.read');
const canSet = () => canReadPermissions() && session.can('document.host_permissions.set');
const scope = useAuthorizedViewScope(session, resetScope, () => { if (canReadDocuments.value) void loadDocuments(); });
type ViewRequest = NonNullable<ReturnType<typeof scope.begin>>;
type WriteContext = { request: ViewRequest; document: HostDocumentItemResponse; payload: SetHostDocumentPermissionsRequest; action: WriteKind; sent: boolean };
let listRequest: ViewRequest | undefined, permissionRequest: ViewRequest | undefined, recoveryRequest: ViewRequest | undefined, writeContext: WriteContext | undefined;
let readKey = '', listKey = '', snapshotEpoch = 0, applyingAuthority = false;
function begin(permission: string): ViewRequest | undefined { if (canReadDocuments.value) return scope.begin(permission); }
function resetScope(): void {
  closePermissionDialog(); items.value = []; total.value = 0; loading.value = false; catalogReady.value = false; page.value = 1; pageSize.value = 20;
  searchKeyword.value = ''; appliedKeyword.value = ''; problem.value = undefined; saveReceipt.value = undefined;
  knownSnapshots.clear(); revisionFloors.clear(); writeBlocks.value = new Map(); listRequest = undefined; readKey = ''; listKey = ''; catalogOwner.value++;
}
watch(() => route?.name, () => { scope.invalidate(); if (canReadDocuments.value) void loadDocuments(); }, { flush: 'sync' });
function currentDocument(row: HostDocumentItemResponse): boolean { return items.value.some(item => item.id.toLowerCase() === row.id.toLowerCase() && item.version === row.version && item.title === row.title && item.documentNo === row.documentNo); }
watch(items, () => { catalogOwner.value++; if (activeDocument.value && !currentDocument(activeDocument.value)) closePermissionDialog(); }, { flush: 'sync', deep: true });
watch([permissions, permissionVersion], () => { if (applyingAuthority) return; snapshotEpoch++; dialogOwner.value++; finishRemoval(false); finishRecovery(false); permissionsReady.value = false; cancelWrite(); }, { flush: 'sync', deep: true });
watch([userId, permissionLevel], () => { dialogOwner.value++; finishRecovery(false); if (writeContext?.action === 'set') cancelWrite(); }, { flush: 'sync' });
const displayItems = computed(() => { const term = appliedKeyword.value.trim().toLowerCase(); return term ? items.value.filter(row => row.title.toLowerCase().includes(term) || row.documentNo.toLowerCase().includes(term) || row.id.toLowerCase().includes(term)) : items.value; });
// 在途新页保留页数下界，避免分页器因清空旧内容把受控页码压回第一页。
const displayTotal = computed(() => Math.max(total.value, page.value > 1 ? (page.value - 1) * pageSize.value + Math.max(1, items.value.length) : items.value.length));
const rowIndex = computed(() => (index: number) => (page.value - 1) * pageSize.value + index + 1);
async function loadDocuments(): Promise<void> {
  if (!canReadDocuments.value || saving.value || recovering.value) return;
  listRequest?.cancel(); const request = begin('document.host_documents.read'); if (!request) return;
  let requestedPage = Math.min(2147483647, Math.max(1, Number.isSafeInteger(page.value) ? page.value : 1));
  const requestedSize = Math.min(100, Math.max(1, Number.isSafeInteger(pageSize.value) ? pageSize.value : 20));
  page.value = requestedPage; pageSize.value = requestedSize; listKey = requestedPage + ':' + requestedSize;
  listRequest = request; loading.value = true; catalogReady.value = false; problem.value = undefined; catalogOwner.value++; dialogOwner.value++; finishRemoval(false); finishRecovery(false);
  if (listKey !== readKey) { closePermissionDialog(); items.value = []; total.value = 0; }
  try {
    for (let attempt = 0; attempt < 2; attempt++) {
      const result = await listDocumentItems(requestedPage, requestedSize, {}, request.signal); if (!request.current() || listRequest !== request) return;
      if (!validPermissionDocumentPage(result, requestedPage, requestedSize) || result.items.some(row => row.version < (revisionFloors.get(row.id.toLowerCase()) ?? 0))) throw Error('client.invalid_document_item_page');
      if (attempt === 0 && result.items.length === 0 && requestedPage > 1) { requestedPage = Math.max(1, Math.min(requestedPage - 1, Math.ceil(result.total / requestedSize))); page.value = requestedPage; listKey = requestedPage + ':' + requestedSize; continue; }
      for (const row of result.items) revisionFloors.set(row.id.toLowerCase(), row.version);
      items.value = result.items; total.value = result.total; readKey = listKey; catalogReady.value = true; return;
    }
  } catch (error) { if (request.current() && listRequest === request) problem.value = toProblem(error); }
  finally { if (listRequest === request) { listRequest = undefined; listKey = ''; loading.value = false; catalogOwner.value++; dialogOwner.value++; void syncTableLayout(); } request.finish(); }
}
function changePage(nextPage: number, nextSize: number): void {
  if (!canReadDocuments.value || saving.value || recovering.value || !Number.isSafeInteger(nextPage) || nextPage < 1 || !Number.isSafeInteger(nextSize) || nextSize < 1 || nextSize > 100) return;
  const previousSize = Number((listKey || readKey).split(':')[1] || pageSize.value), destination = nextSize !== previousSize ? 1 : nextPage, key = destination + ':' + nextSize;
  if (key === listKey || key === readKey && catalogReady.value) return; page.value = destination; pageSize.value = nextSize; void loadDocuments();
}
function handleQuery(): void { if (saving.value || recovering.value) return; appliedKeyword.value = searchKeyword.value; catalogOwner.value++; void syncTableLayout(); }
function resetQuery(): void { if (saving.value || recovering.value) return; searchKeyword.value = ''; appliedKeyword.value = ''; page.value = 1; void loadDocuments(); }
function applyAuthority(rows: HostDocumentPermissionResponse[], version: number): void {
  applyingAuthority = true; try { permissions.value = rows.map(row => ({ ...row })); permissionVersion.value = version; } finally { applyingAuthority = false; }
  snapshotEpoch++; dialogOwner.value++;
}
async function openPermissionDialog(row: HostDocumentItemResponse): Promise<void> {
  if (!canReadPermissions() || !catalogReady.value || loading.value || recovering.value || !currentDocument(row)) return;
  closePermissionDialog(); activeDocument.value = { ...row }; permissionDialogOpen.value = true; dialogWindow.value++; dialogOwner.value++; catalogOwner.value++; await reloadPermissions();
}
async function reloadPermissions(): Promise<void> {
  const document = activeDocument.value ? { ...activeDocument.value } : undefined;
  if (!canReadPermissions() || !catalogReady.value || !permissionDialogOpen.value || !document || !currentDocument(document) || saving.value || recovering.value || loading.value) return;
  permissionRequest?.cancel(); const request = begin('document.host_permissions.read'); if (!request) return;
  const window = dialogWindow.value, previousVersion = permissionVersion.value; permissionRequest = request; permissionsLoading.value = true; permissionsReady.value = false; permissionProblem.value = undefined; dialogOwner.value++; finishRemoval(false);
  try {
    const result = await getDocumentPermissionSnapshot(document.id, request.signal);
    if (!request.current() || permissionRequest !== request || !permissionDialogOpen.value || dialogWindow.value !== window || !currentDocument(document)) return;
    const id = document.id.toLowerCase(), known = knownSnapshots.get(id);
    if (!validPermissionSnapshot(result, document.id) || result.version < (revisionFloors.get(id) ?? document.version) || known && (result.version < known.version || result.version === known.version && permissionSignature(result.permissions) !== known.signature)) throw Error('client.invalid_document_permission_snapshot');
    knownSnapshots.set(id, { version: result.version, signature: permissionSignature(result.permissions) }); revisionFloors.set(id, Math.max(revisionFloors.get(id) ?? 0, result.version));
    applyAuthority(result.permissions, result.version); permissionsReady.value = true;
    if (previousVersion > 0 && result.version !== previousVersion) { userId.value = ''; permissionLevel.value = 'read'; }
    const blockedVersion = writeBlocks.value.get(id); if (blockedVersion !== undefined && result.version > blockedVersion) { const blocks = new Map(writeBlocks.value); blocks.delete(id); writeBlocks.value = blocks; }
  } catch (error) { if (request.current() && permissionRequest === request) permissionProblem.value = toProblem(error); }
  finally { if (permissionRequest === request) { permissionRequest = undefined; permissionsLoading.value = false; dialogOwner.value++; } request.finish(); }
}
function markUnconfirmed(context: WriteContext): void {
  const id = context.document.id.toLowerCase(); writeBlocks.value = new Map(writeBlocks.value).set(id, context.payload.version); permissionsReady.value = false;
  saveReceipt.value = { documentId: context.document.id, title: context.document.title, action: context.action, outcome: 'unconfirmed', version: context.payload.version };
}
function cancelWrite(): void {
  const context = writeContext; if (context?.sent && context.request.current()) markUnconfirmed(context);
  context?.request.cancel(); writeContext = undefined; saving.value = false; finishRemoval(false);
}
function closePermissionDialog(): void {
  finishRecovery(false); recoveryRequest?.cancel(); recoveryRequest = undefined; recovering.value = false; cancelWrite();
  permissionRequest?.cancel(); permissionRequest = undefined; permissionDialogOpen.value = false; activeDocument.value = null;
  permissionsReady.value = false; permissionsLoading.value = false; permissionProblem.value = undefined; applyAuthority([], 0); userId.value = ''; permissionLevel.value = 'read'; dialogOwner.value++;
}
function writable(): boolean { return canSet() && catalogReady.value && !loading.value && permissionDialogOpen.value && permissionsReady.value && !permissionsLoading.value && !saving.value && !recovering.value && !!activeDocument.value && currentDocument(activeDocument.value) && !writeBlocks.value.has(activeDocument.value.id.toLowerCase()); }
function startWrite(payload: SetHostDocumentPermissionsRequest, action: WriteKind): WriteContext | undefined {
  const request = begin('document.host_permissions.set'); if (!request || !activeDocument.value) return;
  const context = { request, document: { ...activeDocument.value }, payload, action, sent: false }; writeContext = context; saving.value = true; dialogOwner.value++; return context;
}
async function savePermission(): Promise<void> {
  if (!writable() || !activeDocument.value) return; const targetUser = userId.value.trim(), level = trimPermissionText(permissionLevel.value);
  const payload = { documentId: activeDocument.value.id, version: permissionVersion.value, permissions: [...permissions.value.filter(row => row.userId.toLowerCase() !== targetUser.toLowerCase()).map(row => ({ userId: row.userId, permissionLevel: row.permissionLevel })), { userId: targetUser, permissionLevel: level }] };
  if (!validPermissionId(targetUser) || !level || !isSetHostDocumentPermissionsRequest(payload)) { showWarning(t('documentPermissions.operationFailed')); return; }
  const context = startWrite(payload, 'set'); if (!context) return; try { await persistPermissions(context); } finally { finishSave(context); }
}
async function removePermission(row: HostDocumentPermissionResponse): Promise<void> {
  if (!writable() || !activeDocument.value) return;
  const target = permissions.value.find(value => value.id.toLowerCase() === row.id.toLowerCase() && value.userId.toLowerCase() === row.userId.toLowerCase() && value.permissionLevel === row.permissionLevel && value.documentId.toLowerCase() === activeDocument.value!.id.toLowerCase()); if (!target) return;
  const payload = { documentId: activeDocument.value.id, version: permissionVersion.value, permissions: permissions.value.filter(value => value.userId.toLowerCase() !== target.userId.toLowerCase()).map(value => ({ userId: value.userId, permissionLevel: value.permissionLevel })) };
  const context = startWrite(payload, 'remove'); if (!context) return; const epoch = snapshotEpoch, window = dialogWindow.value;
  try {
    const confirmed = await new Promise<boolean>(resolve => { removalConfirmation.value = { userId: target.userId, documentTitle: context.document.title, resolve }; });
    if (!confirmed || !context.request.current() || writeContext !== context || dialogWindow.value !== window || snapshotEpoch !== epoch || !permissionDialogOpen.value || !permissionsReady.value || permissionVersion.value !== payload.version || !currentDocument(context.document)) return;
    await persistPermissions(context);
  } finally { finishSave(context); }
}
function finishSave(context: WriteContext): void { if (writeContext === context) { writeContext = undefined; saving.value = false; dialogOwner.value++; } context.request.finish(); }
/** 已确认写入先解除自身归属，再应用快照和父修订，避免自身 watcher 把成功标成取消。 */
async function persistPermissions(context: WriteContext): Promise<void> {
  const { request, payload, document, action } = context;
  context.sent = true; saveReceipt.value = { documentId: document.id, title: document.title, action, outcome: 'processing', version: payload.version };
  try {
    const result = await setDocumentPermissions(payload, request.signal); if (!request.current() || writeContext !== context) return;
    if (!permissionRowsMatch(result, payload)) throw Error('client.invalid_document_permission_identity');
    const version = payload.version + 1, id = document.id.toLowerCase(); writeContext = undefined; saving.value = false;
    saveReceipt.value = { documentId: document.id, title: document.title, action, outcome: 'succeeded', version, count: result.length };
    knownSnapshots.set(id, { version, signature: permissionSignature(result) }); revisionFloors.set(id, version);
    applyAuthority(result, version); permissionsReady.value = true; permissionProblem.value = undefined; userId.value = ''; permissionLevel.value = 'read';
    activeDocument.value = { ...document, version }; items.value = items.value.map(row => row.id.toLowerCase() === id ? { ...row, version } : row);
    showSuccess(t('documentPermissions.saveSuccess'));
  } catch (error) {
    if (request.current() && writeContext === context) {
      const failure = toProblem(error, 'documentPermissions.operationFailed');
      if (!isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408) markUnconfirmed(context);
      else { saveReceipt.value = { documentId: document.id, title: document.title, action, outcome: 'failed', version: payload.version, errorCode: failure.code }; permissionsReady.value = false; }
      showProblem(failure, t('documentPermissions.operationFailed'));
    }
  }
}
async function acknowledgeRecovery(): Promise<void> {
  const document = activeDocument.value ? { ...activeDocument.value } : undefined, owner = dialogOwner.value, window = dialogWindow.value;
  if (!canSet() || !document || !catalogReady.value || !permissionsReady.value || loading.value || permissionsLoading.value || saving.value || recovering.value || !writeBlocks.value.has(document.id.toLowerCase())) return;
  const request = begin('document.host_permissions.set'); if (!request) return; recoveryRequest = request; recovering.value = true;
  try {
    const confirmed = await new Promise<boolean>(resolve => { recoveryConfirmation.value = { resolve }; });
    if (confirmed && request.current() && permissionDialogOpen.value && dialogWindow.value === window && dialogOwner.value === owner && currentDocument(document)) { const blocks = new Map(writeBlocks.value); blocks.delete(document.id.toLowerCase()); writeBlocks.value = blocks; }
  } finally { if (recoveryRequest === request) { recoveryRequest = undefined; recovering.value = false; dialogOwner.value++; } request.finish(); }
}
function toProblem(error: unknown, key: 'documentPermissions.loadFailed' | 'documentPermissions.operationFailed' = 'documentPermissions.loadFailed'): FullNetProblemDetails { return isFullNetProblemDetails(error) ? error : { title: t(key), status: 500, code: 'client.request_failed' }; }
const controls = computed(() => {
  const owner = catalogOwner.value, owned = () => canReadDocuments.value && owner === catalogOwner.value;
  const query = () => { if (owned()) handleQuery(); };
  return { refresh: () => { if (owned()) void loadDocuments(); }, query, submit: (event: Event) => { event.preventDefault(); query(); }, enter: (event: KeyboardEvent) => { if (event.key === 'Enter') query(); }, input: (value: string) => { if (owned() && !saving.value && !recovering.value) searchKeyword.value = value; }, reset: () => { if (owned()) resetQuery(); },
    pageModel: (value: number) => { if (owned() && !saving.value && !recovering.value && Number.isSafeInteger(value) && value > 0) page.value = value; }, sizeModel: (value: number) => { if (owned() && !saving.value && !recovering.value && Number.isSafeInteger(value) && value > 0 && value <= 100) pageSize.value = value; }, change: (value: number, size: number) => { if (owned()) changePage(value, size); } };
});
const rowControls = computed(() => { const owner = catalogOwner.value; return new Map(displayItems.value.map(row => { const target = { ...row }; return [row.id.toLowerCase(), () => { if (canReadDocuments.value && catalogOwner.value === owner && currentDocument(target)) void openPermissionDialog(target); }]; })); });
const dialogControls = computed(() => {
  const window = dialogWindow.value, owner = dialogOwner.value, document = activeDocument.value ? { ...activeDocument.value } : undefined;
  const owned = () => canReadDocuments.value && permissionDialogOpen.value && dialogWindow.value === window && dialogOwner.value === owner && !!document && currentDocument(document);
  const close = () => { if (owned()) closePermissionDialog(); };
  return { close, update: (value: boolean) => { if (!value) close(); }, save: () => { if (owned()) void savePermission(); }, reload: () => { if (owned()) void reloadPermissions(); }, user: (value: string) => { if (owned() && !saving.value && !recovering.value && !permissionsLoading.value) userId.value = value; }, level: (value: string) => { if (owned() && !saving.value && !recovering.value && !permissionsLoading.value) permissionLevel.value = value; }, recover: () => { if (owned()) void acknowledgeRecovery(); } };
});
const permissionControls = computed(() => { const owner = dialogOwner.value, window = dialogWindow.value; return new Map(permissions.value.map(row => { const target = { ...row }; return [row.id.toLowerCase(), () => { if (canReadDocuments.value && permissionDialogOpen.value && dialogOwner.value === owner && dialogWindow.value === window) void removePermission(target); }]; })); });
</script>

<template>
  <section v-if="canReadDocuments" class="document-permissions-view document-module-page art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentPermissions.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

    <el-card v-if="saveReceipt" shadow="never" data-testid="document-permissions-receipt">
      <p>{{ saveReceipt.title }} · <code translate="no">{{ saveReceipt.documentId }}</code></p>
      <p>{{ t(saveReceipt.outcome === 'processing' ? 'documentPermissions.receiptProcessing' : saveReceipt.outcome === 'succeeded' ? 'documentPermissions.receiptSucceeded' : saveReceipt.outcome === 'failed' ? 'documentPermissions.receiptFailed' : 'documentPermissions.receiptUnconfirmed') }}</p>
      <p v-if="saveReceipt.outcome === 'succeeded'">{{ t('documentPermissions.receiptVersion', { version: String(saveReceipt.version) }) }}</p>
      <code v-if="saveReceipt.errorCode" translate="no">{{ saveReceipt.errorCode }}</code>
    </el-card>

    <el-card class="art-table-card art-full-height" shadow="never">
      <template #header>
        <div class="document-module-card-header">
          <span>{{ t('documentPermissions.title') }}</span>
          <el-button type="primary" :icon="Refresh" :loading="loading" :disabled="saving || recovering" v-on="{ click: controls.refresh }">
            {{ t('documentPermissions.refresh') }}
          </el-button>
        </div>
      </template>

      <div ref="tableMainRef" class="art-crud-table-main">
        <el-form class="document-module-inline-query" :inline="true" v-on="{ submit: controls.submit }">
          <el-form-item :label="t('documentPermissions.keyword')">
            <el-input
              :model-value="searchKeyword" v-on="{ 'update:modelValue': controls.input, keyup: controls.enter }"
              :placeholder="t('documentPermissions.keywordPlaceholder')"
              clearable
              data-testid="document-permissions-keyword"
            />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :icon="Search" data-testid="document-permissions-query" v-on="{ click: controls.query }">
              {{ t('documentPermissions.query') }}
            </el-button>
            <el-button v-on="{ click: controls.reset }">{{ t('documentPermissions.reset') }}</el-button>
          </el-form-item>
        </el-form>
        <p>{{ t('documentPermissions.pageSearchHint') }}</p>

        <div class="art-table" :class="{ 'is-empty': displayItems.length === 0 }">
          <el-table
            :loading="loading"
            :data="displayItems"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
          >
            <el-table-column :label="t('users.columnIndex')" width="72" align="center">
              <template #default="{ $index }">{{ rowIndex($index) }}</template>
            </el-table-column>
            <el-table-column :label="t('documentPermissions.documentTitle')" prop="title" min-width="200" />
            <el-table-column :label="t('documentPermissions.documentNo')" prop="documentNo" min-width="160" />
            <el-table-column :label="t('documentPermissions.category')" prop="categoryName" min-width="120" />
            <el-table-column :label="t('documentPermissions.createdAt')" prop="createdAtUtc" min-width="180" />
            <el-table-column :label="t('users.columnActions')" width="140" fixed="right" align="center">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate code="document.host_permissions.read">
                    <ArtTableActionButton
                      type="edit"
                      test-id="document-permissions-set"
                      :title="t('documentPermissions.setPermissions')"
                      :disabled="!catalogReady || loading || recovering" v-on="{ click: rowControls.get(row.id.toLowerCase()) }"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
            <template #empty>{{ t('documentPermissions.emptyDirectory') }}</template>
          </el-table>
        </div>

        <el-pagination
          :current-page="page"
          :page-size="pageSize"
          class="art-table-pagination center custom-pagination"
          :total="displayTotal" :disabled="saving || recovering"
          background
          layout="total, sizes, prev, pager, next, jumper"
          :page-sizes="[10, 20, 50, 100]"
          v-on="{ 'update:current-page': controls.pageModel, 'update:page-size': controls.sizeModel, change: controls.change }"
        />
      </div>
    </el-card>

    <ArtFormDialog
      :key="dialogWindow" :open="permissionDialogOpen" v-on="{ 'update:open': dialogControls.update, confirm: dialogControls.save }"
      :title="t('documentPermissions.dialogTitle')"
      :saving="saving"
      :confirm-label="t('documentPermissions.save')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="document-permissions-save"
      :show-confirm="writable()"
    >
      <p v-if="activeDocument" class="document-permissions-view__active-doc" translate="no">
        {{ activeDocument.title }} · {{ activeDocument.documentNo }}
      </p>
      <el-alert v-if="permissionProblem" type="error" :title="permissionProblem.title" :description="permissionProblem.detail ?? permissionProblem.code" :closable="false" />
      <el-alert v-if="activeDocument && writeBlocks.has(activeDocument.id.toLowerCase())" type="warning" :title="t('documentPermissions.recoveryHint')" :closable="false" />
      <PermissionGate code="document.host_permissions.set"><el-button v-if="activeDocument && writeBlocks.has(activeDocument.id.toLowerCase())" data-testid="document-permissions-recover" :disabled="!catalogReady || !permissionsReady || saving || loading || permissionsLoading || recovering" v-on="{ click: dialogControls.recover }">{{ t('documentPermissions.recoveryTitle') }}</el-button></PermissionGate>
      <el-table
        :loading="permissionsLoading"
        :data="permissions"
        size="small"
        class="art-crud-data-table document-permissions-view__perm-table"
      >
        <el-table-column :label="t('documentPermissions.userId')" prop="userId" min-width="240" />
        <el-table-column :label="t('documentPermissions.permissionLevel')" prop="permissionLevel" min-width="120" />
        <el-table-column v-if="canSet()" :label="t('users.columnActions')" width="100" align="center">
          <template #default="{ row }">
            <ArtTableActionGroup>
              <ArtTableActionButton type="delete" test-id="document-permissions-remove" :title="t('documentPermissions.remove')"
                :disabled="!writable()" v-on="{ click: permissionControls.get(row.id.toLowerCase()) }" />
            </ArtTableActionGroup>
          </template>
        </el-table-column>
        <template #empty>{{ t('documentPermissions.emptyDirectory') }}</template>
      </el-table>
      <el-form
        v-if="canSet()"
        :disabled="!writable()"
        label-width="120px"
        data-testid="document-permissions-set-form"
        class="document-permissions-view__set-form"
      >
        <el-form-item :label="t('documentPermissions.userId')" required>
          <el-input :model-value="userId" v-on="{ 'update:modelValue': dialogControls.user }" autocomplete="off" data-testid="document-permissions-user-id" />
        </el-form-item>
        <el-form-item :label="t('documentPermissions.permissionLevel')" required>
          <el-input :model-value="permissionLevel" v-on="{ 'update:modelValue': dialogControls.level }" autocomplete="off" data-testid="document-permissions-level" />
        </el-form-item>
      </el-form>
      <PermissionGate code="document.host_permissions.read">
        <el-button
          v-if="activeDocument && canReadPermissions()"
          type="primary"
          plain
          data-testid="document-permissions-load"
          :loading="permissionsLoading"
          :disabled="loading || saving || recovering || !catalogReady" v-on="{ click: dialogControls.reload }"
        >
          {{ t('documentPermissions.load') }}
        </el-button>
      </PermissionGate>
    </ArtFormDialog>
    <ArtFormDialog v-if="removalConfirmation && canSet()" :open="true" :title="t('documentPermissions.remove')"
      :confirm-label="t('documentPermissions.remove')" :cancel-label="t('users.cancel')"
      confirm-test-id="document-permissions-remove-confirm" v-on="{ confirm: removalHandlers.confirm, 'update:open': removalHandlers.update }">
      <p>{{ t('documentPermissions.removeConfirm', { user: removalConfirmation.userId, document: removalConfirmation.documentTitle }) }}</p>
    </ArtFormDialog>
    <ArtFormDialog v-if="recoveryConfirmation && canSet()" :open="true" :title="t('documentPermissions.recoveryTitle')" :confirm-label="t('users.confirm')" :cancel-label="t('users.cancel')" confirm-test-id="document-permissions-recovery-confirm" v-on="{ confirm: recoveryHandlers.confirm, 'update:open': recoveryHandlers.update }"><p>{{ t('documentPermissions.recoveryHint') }}</p></ArtFormDialog>
  </section>
</template>

<style scoped>
.document-permissions-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.document-permissions-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.document-permissions-view :deep(.art-crud-table-main) {
  flex: 1;
  min-height: 200px;
}

.document-permissions-view__active-doc {
  margin: 0 0 12px;
  color: var(--art-gray-600);
  font-size: 13px;
}

.document-permissions-view__perm-table {
  margin-bottom: 16px;
}

.document-permissions-view__set-form {
  margin-top: 8px;
}
</style>
