<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { Refresh, Search } from '@element-plus/icons-vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElPagination,
  ElTable,
  ElTableColumn
} from 'element-plus';
import type {
  FullNetProblemDetails,
  HostDocumentItemResponse,
  HostDocumentPermissionResponse,
  SetHostDocumentPermissionsRequest
} from '@fullnet/client-contracts';
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
import {
  getDocumentPermissionSnapshot,
  setDocumentPermissions
} from '../api/document-permissions';

defineOptions({ name: 'DocumentPermissionsView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostDocumentItemResponse[]>([]);
const loading = ref(false);
const saving = ref(false);
const permissionsLoading = ref(false);
const problem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const searchKeyword = ref('');
const appliedKeyword = ref('');
const permissionDialogOpen = ref(false);
const activeDocument = ref<HostDocumentItemResponse | null>(null);
const permissions = ref<HostDocumentPermissionResponse[]>([]);
const permissionVersion = ref(0);
const userId = ref('');
const permissionLevel = ref('read');

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  syncTableLayout
} = useArtPagedTableInCard(loading);

const viewActive = ref(false);
onMounted(() => { viewActive.value = true; }); onActivated(() => { viewActive.value = true; });
onDeactivated(() => { viewActive.value = false; }); onBeforeUnmount(() => { viewActive.value = false; });
const canReadDocuments = computed(() => viewActive.value && session.currentUser?.scope === 'host' && session.currentUser.tenantId === null
  && session.can('document.host_documents.read') && session.can('document.host_permissions.read'));
const canReadPermissions = () => canReadDocuments.value && session.can('document.host_permissions.read');
const canSet = () => canReadPermissions() && session.can('document.host_permissions.set');
const permissionsReady = ref(false);
const removalConfirmation = ref<{ userId: string; documentTitle: string; resolve: (confirmed: boolean) => void }>();
function finishRemoval(confirmed: boolean): void {
  const pending = removalConfirmation.value; removalConfirmation.value = undefined; pending?.resolve(confirmed);
}
const dialogModel = computed({ get: () => permissionDialogOpen.value, set: open => { if (!open) closePermissionDialog(); } });
const scope = useAuthorizedViewScope(session, () => {
  closePermissionDialog(); items.value = []; total.value = 0; loading.value = false;
  page.value = 1; pageSize.value = 20; searchKeyword.value = ''; appliedKeyword.value = ''; problem.value = undefined;
}, () => { if (canReadDocuments.value) void loadDocuments(); });
let listRequest: ReturnType<typeof scope.begin>;
let permissionRequest: ReturnType<typeof scope.begin>;
let saveRequest: ReturnType<typeof scope.begin>;
function currentDocument(row: Pick<HostDocumentItemResponse, 'id' | 'version'>): boolean {
  return items.value.some(item => item.id === row.id && item.version === row.version);
}
// 编辑持有打开时目录身份；新目录修订不能静默更新旧表单的保存依据。
watch(items, () => { if (activeDocument.value && !currentDocument(activeDocument.value)) closePermissionDialog(); }, { flush: 'sync', deep: true });

const displayItems = computed(() => {
  const keyword = appliedKeyword.value.trim().toLowerCase();
  if (!keyword) {
    return items.value;
  }
  return items.value.filter(
    item =>
      item.title.toLowerCase().includes(keyword) ||
      item.documentNo.toLowerCase().includes(keyword) ||
      item.id.toLowerCase().includes(keyword)
  );
});

const rowIndex = computed(() => (index: number) => (page.value - 1) * pageSize.value + index + 1);

async function loadDocuments() {
  listRequest?.cancel();
  if (!canReadDocuments.value) return;
  const request = scope.begin('document.host_documents.read'); if (!request) return;
  listRequest = request; loading.value = true; problem.value = undefined;
  try {
    const result = await listDocumentItems(page.value, pageSize.value, {}, request.signal);
    if (!request.current()) return;
    items.value = result.items; page.value = result.page; pageSize.value = result.pageSize; total.value = result.total;
  } catch (error) { if (request.current()) problem.value = toProblem(error); }
  finally { if (request.current()) { loading.value = false; void syncTableLayout(); } request.finish(); }
}

function handleQuery() {
  appliedKeyword.value = searchKeyword.value;
  void syncTableLayout();
}

function resetQuery() {
  searchKeyword.value = '';
  appliedKeyword.value = '';
  page.value = 1;
  void loadDocuments();
}

async function openPermissionDialog(row: HostDocumentItemResponse) {
  if (!canReadPermissions() || !currentDocument(row)) return;
  closePermissionDialog();
  const request = scope.begin('document.host_permissions.read'); if (!request) return;
  const target = { ...row };
  permissionRequest = request; activeDocument.value = target; permissionDialogOpen.value = true;
  permissionsLoading.value = true; problem.value = undefined;
  try {
    const result = await getDocumentPermissionSnapshot(target.id, request.signal);
    if (!request.current()) return;
    permissions.value = result.permissions.map(entry => ({ ...entry })); permissionVersion.value = result.version; permissionsReady.value = true;
  } catch (error) {
    if (request.current()) { permissions.value = []; permissionsReady.value = false; problem.value = toProblem(error); }
  } finally { if (request.current()) permissionsLoading.value = false; request.finish(); }
}

function closePermissionDialog() {
  finishRemoval(false);
  permissionRequest?.cancel(); saveRequest?.cancel(); permissionRequest = undefined; saveRequest = undefined;
  permissionDialogOpen.value = false; activeDocument.value = null; permissions.value = [];
  permissionsReady.value = false; permissionVersion.value = 0; permissionsLoading.value = false; saving.value = false;
  userId.value = ''; permissionLevel.value = 'read';
}

async function savePermission() {
  const document = activeDocument.value;
  if (!canSet() || !permissionDialogOpen.value || !permissionsReady.value || permissionsLoading.value || saving.value || !document) return;
  const targetUser = userId.value.trim(), level = permissionLevel.value.trim();
  // API 是整份替换，单用户表单必须保留已成功读取的其他授权，不能把读取失败当作空目录。
  const payload = { documentId: document.id, version: permissionVersion.value, permissions: [
    ...permissions.value.filter(entry => entry.userId.toLowerCase() !== targetUser.toLowerCase()).map(entry => ({ userId: entry.userId, permissionLevel: entry.permissionLevel })),
    { userId: targetUser, permissionLevel: level }
  ] };
  if (!level || !isSetHostDocumentPermissionsRequest(payload)) { showWarning(t('documentPermissions.operationFailed')); return; }
  const request = scope.begin('document.host_permissions.set'); if (!request) return;
  saveRequest = request; saving.value = true; problem.value = undefined;
  try { await persistPermissions(payload, request); }
  finally { finishSave(request); }
}

/** 移除仍使用权威快照的整份替换，不能借单行操作覆盖未读取或冲突后的其他配置。 */
async function removePermission(row: HostDocumentPermissionResponse): Promise<void> {
  const document = activeDocument.value;
  if (!canSet() || !permissionDialogOpen.value || !permissionsReady.value || permissionsLoading.value || saving.value || !document) return;
  const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();
  const target = permissions.value.find(entry => sameId(entry.id, row.id) && sameId(entry.userId, row.userId)
    && sameId(entry.documentId, document.id) && entry.permissionLevel === row.permissionLevel);
  if (!target) return;
  // 在确认前冻结用户、修订及完整剩余集合，确认期间输入和调用者对象不改变意图。
  const targetUser = target.userId;
  const payload = { documentId: document.id, version: permissionVersion.value,
    permissions: permissions.value.filter(entry => !sameId(entry.userId, targetUser))
      .map(entry => ({ userId: entry.userId, permissionLevel: entry.permissionLevel })) };
  if (!isSetHostDocumentPermissionsRequest(payload)) return;
  const request = scope.begin('document.host_permissions.set'); if (!request) return;
  saveRequest = request; saving.value = true; problem.value = undefined;
  try {
    const confirmed = await new Promise<boolean>(resolve => { removalConfirmation.value = { userId: targetUser, documentTitle: document.title, resolve }; });
    if (!confirmed || !request.current() || !canSet() || !permissionDialogOpen.value || activeDocument.value?.id !== payload.documentId
      || !permissionsReady.value || permissionVersion.value !== payload.version) return;
    await persistPermissions(payload, request);
  } finally { finishSave(request); }
}

function finishSave(request: NonNullable<ReturnType<typeof scope.begin>>): void {
  if (request.current() && saveRequest === request) { saving.value = false; saveRequest = undefined; }
  request.finish();
}

async function persistPermissions(payload: SetHostDocumentPermissionsRequest, request: NonNullable<ReturnType<typeof scope.begin>>): Promise<void> {
  try {
    const result = await setDocumentPermissions(payload, request.signal);
    if (!request.current()) return;
    // 保存成功对应提交修订号加一；后续并发更新会在下一次保存时返回冲突，不能复用列表页旧版本。
    permissions.value = result; permissionVersion.value = payload.version + 1; userId.value = ''; permissionLevel.value = 'read';
    showSuccess(t('documentPermissions.saveSuccess'));
  } catch (error) {
    if (request.current()) {
      // 写入可能已经提交但响应丢失；旧快照不能再用于整份替换，须先重新成功读取。
      permissionsReady.value = false; permissionVersion.value = 0; permissions.value = [];
      showProblem(toProblem(error, 'documentPermissions.operationFailed'), t('documentPermissions.operationFailed'));
    }
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'documentPermissions.loadFailed' | 'documentPermissions.operationFailed' = 'documentPermissions.loadFailed'
): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t(fallbackKey), status: 500, code: fallbackKey };
}

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

    <el-card class="art-table-card art-full-height" shadow="never">
      <template #header>
        <div class="document-module-card-header">
          <span>{{ t('documentPermissions.title') }}</span>
          <el-button type="primary" :icon="Refresh" :loading="loading" @click="loadDocuments">
            {{ t('documentPermissions.refresh') }}
          </el-button>
        </div>
      </template>

      <div ref="tableMainRef" class="art-crud-table-main">
        <el-form class="document-module-inline-query" :inline="true" @submit.prevent="handleQuery">
          <el-form-item :label="t('documentPermissions.keyword')">
            <el-input
              v-model="searchKeyword"
              :placeholder="t('documentPermissions.keywordPlaceholder')"
              clearable
              data-testid="document-permissions-keyword"
              @keyup.enter="handleQuery"
            />
          </el-form-item>
          <el-form-item>
            <el-button type="primary" :icon="Search" data-testid="document-permissions-query" @click="handleQuery">
              {{ t('documentPermissions.query') }}
            </el-button>
            <el-button @click="resetQuery">{{ t('documentPermissions.reset') }}</el-button>
          </el-form-item>
        </el-form>

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
                      @click="openPermissionDialog(row as HostDocumentItemResponse)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
            <template #empty>{{ t('documentPermissions.emptyDirectory') }}</template>
          </el-table>
        </div>

        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          class="art-table-pagination center custom-pagination"
          :total="total"
          background
          layout="total, sizes, prev, pager, next, jumper"
          :page-sizes="[10, 20, 50, 100]"
          @current-change="loadDocuments"
          @size-change="loadDocuments"
        />
      </div>
    </el-card>

    <ArtFormDialog
      v-model:open="dialogModel"
      :title="t('documentPermissions.dialogTitle')"
      :saving="saving"
      :confirm-label="t('documentPermissions.save')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="document-permissions-save"
      :show-confirm="canSet() && permissionsReady"
      @confirm="savePermission"
    >
      <p v-if="activeDocument" class="document-permissions-view__active-doc" translate="no">
        {{ activeDocument.title }} · {{ activeDocument.documentNo }}
      </p>
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
                :disabled="saving || permissionsLoading || !permissionsReady" @click="removePermission(row as HostDocumentPermissionResponse)" />
            </ArtTableActionGroup>
          </template>
        </el-table-column>
        <template #empty>{{ t('documentPermissions.emptyDirectory') }}</template>
      </el-table>
      <el-form
        v-if="canSet()"
        :disabled="saving || permissionsLoading || !permissionsReady"
        label-width="120px"
        data-testid="document-permissions-set-form"
        class="document-permissions-view__set-form"
      >
        <el-form-item :label="t('documentPermissions.userId')" required>
          <el-input v-model="userId" autocomplete="off" data-testid="document-permissions-user-id" />
        </el-form-item>
        <el-form-item :label="t('documentPermissions.permissionLevel')" required>
          <el-input v-model="permissionLevel" autocomplete="off" data-testid="document-permissions-level" />
        </el-form-item>
      </el-form>
      <PermissionGate code="document.host_permissions.read">
        <el-button
          v-if="activeDocument && canReadPermissions()"
          type="primary"
          plain
          data-testid="document-permissions-load"
          :loading="permissionsLoading"
          @click="openPermissionDialog(activeDocument)"
        >
          {{ t('documentPermissions.load') }}
        </el-button>
      </PermissionGate>
    </ArtFormDialog>
    <ArtFormDialog v-if="removalConfirmation && canSet()" :open="true" :title="t('documentPermissions.remove')"
      :confirm-label="t('documentPermissions.remove')" :cancel-label="t('users.cancel')"
      confirm-test-id="document-permissions-remove-confirm" @confirm="finishRemoval(true)"
      @update:open="open => { if (!open) finishRemoval(false); }">
      <p>{{ t('documentPermissions.removeConfirm', { user: removalConfirmation.userId, document: removalConfirmation.documentTitle }) }}</p>
    </ArtFormDialog>
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
