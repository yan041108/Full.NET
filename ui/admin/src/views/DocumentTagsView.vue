<script setup lang="ts">
import { computed, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElPagination,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
// 为避免 barrel 层重复标识符冲突，此处用新版 Response 类型别名旧名
import type { FullNetProblemDetails, HostDocumentTagResponse as HostDocumentTag } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtClientPagination } from '../framework/art-design/composables/useArtCrudTableLayout';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import PermissionGate from '../components/PermissionGate.vue';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { showSuccess, showProblem, showWarning } from '../feedback/fullNetMessage';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createDocumentTag,
  deleteDocumentTag,
  listDocumentTags,
  updateDocumentTag
} from '../api/host-document-tags';

defineOptions({ name: 'DocumentTagsView' });

type EditorMode = 'create' | 'edit';

interface AppliedFilters {
  name: string;
  isHot: string;
  isRecommended: string;
}

const session = useSessionStore();
const { t } = useAdminI18n();
const allTags = ref<HostDocumentTag[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ name: '', isHot: '', isRecommended: '' });
const editorOpen = ref(false);
// 删除确认由本页持有，撤权与离页同步回收，避免独立弹窗泄露旧目录名称。
const confirmation = ref<{ message: string; resolve: (confirmed: boolean) => void }>();
function finishConfirmation(confirmed: boolean): void {
  const pending = confirmation.value;
  confirmation.value = undefined;
  pending?.resolve(confirmed);
}
const editorMode = ref<EditorMode>('create');
const editingTag = ref<HostDocumentTag | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  name: '',
  code: null as string | null,
  icon: null as string | null,
  color: null as string | null,
  description: null as string | null,
  isHot: false,
  isRecommended: false
});
const fieldErrors = reactive({ name: '' });

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

const filteredTags = computed(() => {
  let rows = allTags.value;
  const keyword = appliedFilters.value.name.trim().toLowerCase();
  if (keyword) {
    rows = rows.filter(tag => tag.name.toLowerCase().includes(keyword));
  }
  return rows;
});

const { page, pageSize, total, pagedItems: pagedTags, resetPage } =
  useArtClientPagination(filteredTags);

const triStateOptions = (yes: string, no: string) => [
  { label: t('documentTags.filterAny'), value: '' },
  { label: yes, value: 'true' },
  { label: no, value: 'false' }
];

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'name',
    label: t('documentTags.name'),
    placeholder: t('documentTags.searchNamePlaceholder')
  },
  {
    key: 'isHot',
    label: t('documentTags.isHot'),
    type: 'select',
    options: triStateOptions(t('documentTags.yes'), t('documentTags.no'))
  },
  {
    key: 'isRecommended',
    label: t('documentTags.isRecommended'),
    type: 'select',
    options: triStateOptions(t('documentTags.yes'), t('documentTags.no'))
  }
]);

const canRead = computed(() => session.currentUser?.scope === 'host' && session.can('document.tags.read'));
const canCreate = computed(() => canRead.value && session.can('document.tags.create'));
const canUpdate = computed(() => canRead.value && session.can('document.tags.update'));
const canDelete = computed(() => canRead.value && session.can('document.tags.delete'));
const scope = useAuthorizedViewScope(session, () => {
  finishConfirmation(false); closeEditor(); allTags.value = []; loading.value = false; changing.value = false;
  problem.value = undefined; searchForm.value = {};
  appliedFilters.value = { name: '', isHot: '', isRecommended: '' };
  resetPage(); pageSize.value = 20;
}, () => { if (canRead.value) void load(); });
let listRequest: ReturnType<typeof scope.begin>;
let editorRequest: ReturnType<typeof scope.begin>;
const editorModel = computed({ get: () => editorOpen.value, set: open => { if (!open) closeEditor(); } });
function closeEditor(): void {
  editorRequest?.cancel();
  if (editorRequest) changing.value = false;
  editorRequest = undefined; editorOpen.value = false; editingTag.value = null;
  Object.assign(editorForm, { name: '', code: null, icon: null, color: null, description: null, isHot: false, isRecommended: false }); clearFieldErrors();
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function clearFieldErrors(): void {
  fieldErrors.name = '';
}

function validateName(): string {
  const name = editorForm.name.trim();
  if (!name) {
    return t('documentTags.nameRequired');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.name = validateName();
  return !fieldErrors.name;
}

function parseTriState(value: string): boolean | undefined {
  if (value === 'true') {
    return true;
  }
  if (value === 'false') {
    return false;
  }
  return undefined;
}

async function load(): Promise<void> {
  listRequest?.cancel();
  if (!canRead.value) return;
  const request = scope.begin('document.tags.read');
  if (!request) return;
  listRequest = request; loading.value = true; problem.value = undefined;
  try {
    const rows = await listDocumentTags({
      isHot: parseTriState(appliedFilters.value.isHot), isRecommended: parseTriState(appliedFilters.value.isRecommended)
    }, request.signal);
    if (request.current()) allTags.value = rows;
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'documentTags.loadFailed');
  } finally {
    if (request.current()) { loading.value = false; void syncTableLayout(); }
    request.finish();
  }
}

async function handleSearch(params: Record<string, string | undefined>): Promise<void> {
  appliedFilters.value = {
    name: params.name ?? '',
    isHot: params.isHot ?? '',
    isRecommended: params.isRecommended ?? ''
  };
  resetPage();
  await load();
}

async function resetSearch(): Promise<void> {
  appliedFilters.value = { name: '', isHot: '', isRecommended: '' };
  resetPage();
  await load();
}

function openCreate(): void {
  if (changing.value || !canCreate.value) return;
  closeEditor();
  editorMode.value = 'create';
  editingTag.value = null;
  editorForm.name = '';
  editorForm.code = null;
  editorForm.icon = null;
  editorForm.color = null;
  editorForm.description = null;
  editorForm.isHot = false;
  editorForm.isRecommended = false;
  clearFieldErrors();
  editorOpen.value = true;
}

function openEdit(tag: HostDocumentTag): void {
  if (changing.value || !canUpdate.value) {
    return;
  }
  editorMode.value = 'edit';
  editingTag.value = tag;
  editorForm.name = tag.name;
  editorForm.code = tag.code;
  editorForm.icon = tag.icon;
  editorForm.color = tag.color;
  editorForm.description = tag.description;
  editorForm.isHot = tag.isHot;
  editorForm.isRecommended = tag.isRecommended;
  clearFieldErrors();
  editorOpen.value = true;
}

async function submitEditor(): Promise<void> {
  if (changing.value || !editorOpen.value || !(editorMode.value === 'create' ? canCreate.value : canUpdate.value)) {
    return;
  }
  editorForm.name = editorForm.name.trim();
  if (!applyFieldErrors()) {
    showWarning(fieldErrors.name);
    return;
  }
  if (editorMode.value === 'create') {
    await create();
    return;
  }
  await saveEdit();
}

async function create(): Promise<void> {
  if (!canCreate.value) {
    return;
  }
  const request = scope.begin('document.tags.create');
  if (!request) return;
  editorRequest = request;
  changing.value = true;
  problem.value = undefined;
  try {
    await createDocumentTag(
      editorForm.name,
      editorForm.code ?? null,
      editorForm.icon ?? null,
      editorForm.color ?? null,
      editorForm.description ?? null,
      editorForm.isHot,
      editorForm.isRecommended,
      request.signal
    );
    if (!request.current()) return;
    closeEditor();
    showSuccess(t('documentTags.createSuccess'));
    await load();
  } catch (error: unknown) {
    if (request.current()) showProblem(toProblem(error, 'documentTags.operationFailed'), t('documentTags.operationFailed'));
  } finally {
    if (request.current()) changing.value = false;
    request.finish();
  }
}

async function saveEdit(): Promise<void> {
  const tag = editingTag.value;
  if (!canUpdate.value || !tag) {
    return;
  }
  const request = scope.begin('document.tags.update');
  if (!request) return;
  editorRequest = request;
  changing.value = true;
  problem.value = undefined;
  try {
    await updateDocumentTag(
      tag.id,
      editorForm.name,
      editorForm.code ?? null,
      editorForm.icon ?? null,
      editorForm.color ?? null,
      editorForm.description ?? null,
      tag.version,
      editorForm.isHot,
      editorForm.isRecommended,
      request.signal
    );
    if (!request.current()) return;
    closeEditor();
    showSuccess(t('documentTags.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (request.current()) showProblem(toProblem(error, 'documentTags.operationFailed'), t('documentTags.operationFailed'));
  } finally {
    if (request.current()) changing.value = false;
    request.finish();
  }
}

async function remove(tag: HostDocumentTag): Promise<void> {
  if (changing.value || !canDelete.value) return;
  const request = scope.begin('document.tags.delete');
  if (!request) return;
  // 确认等待本身也是在途操作，不能再次弹窗或借旧权限进入写入。
  changing.value = true;
  const id = tag.id; const version = tag.version;
  try {
    const confirmed = await new Promise<boolean>(resolve => {
      confirmation.value = { message: t('documentTags.confirmDelete', { name: tag.name }), resolve };
    });
    if (!confirmed || !request.current()) return;
    const removed = await deleteDocumentTag(id, version, request.signal);
    if (!request.current()) return;
    if (!removed) throw new Error('client.document_tag_delete_failed');
    showSuccess(t('documentTags.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (request.current()) showProblem(toProblem(error, 'documentTags.operationFailed'), t('documentTags.operationFailed'));
  } finally {
    if (request.current()) changing.value = false;
    request.finish();
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'documentTags.loadFailed' | 'documentTags.operationFailed' = 'documentTags.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_document_tag_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section v-if="canRead" class="document-tags-view document-module-page art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentTags.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card class="document-module-query-card" shadow="never">
      <ArtSearchBar
        v-model="searchForm"
        :items="searchItems"
        :default-visible-count="1"
        :search-label="t('documentTags.query')"
        :reset-label="t('documentTags.reset')"
        :expand-label="t('documentTags.expand')"
        :collapse-label="t('documentTags.collapse')"
        @search="handleSearch"
        @reset="resetSearch"
      />
    </el-card>

    <el-card class="art-table-card art-full-height" shadow="never">
      <div ref="tableMainRef" class="art-crud-table-main">
        <ArtTableHeader
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="loading"
          full-class="art-crud-table-main"
          layout="refresh,size,fullscreen,settings"
          @refresh="load"
        >
          <template #left>
            <PermissionGate code="document.tags.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="document-tag-create"
                @click="openCreate"
              >
                {{ t('documentTags.addTag') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedTags.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedTags"
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

            <el-table-column :label="t('documentTags.name')" min-width="280">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.name.slice(0, 2).toUpperCase() }}</span>
                  <div class="art-crud-table-row__name" translate="no">{{ row.name }}</div>
                </div>
              </template>
            </el-table-column>

            <el-table-column :label="t('documentTags.flags')" width="160" align="center">
              <template #default="{ row }">
                <el-tag v-if="row.isHot" size="small" type="danger" class="document-tags-view__flag">
                  {{ t('documentTags.isHot') }}
                </el-tag>
                <el-tag v-if="row.isRecommended" size="small" type="warning" class="document-tags-view__flag">
                  {{ t('documentTags.isRecommended') }}
                </el-tag>
                <span v-if="!row.isHot && !row.isRecommended">—</span>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('users.columnActions')"
              width="120"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate code="document.tags.update">
                    <ArtTableActionButton
                      type="edit"
                      test-id="document-tag-edit"
                      :title="t('documentTags.edit')"
                      :disabled="changing"
                      @click="openEdit(row as HostDocumentTag)"
                    />
                  </PermissionGate>
                  <PermissionGate code="document.tags.delete">
                    <ArtTableActionButton
                      type="delete"
                      test-id="document-tag-delete"
                      :title="t('documentTags.delete')"
                      :disabled="changing"
                      @click="remove(row as HostDocumentTag)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>

            <template #empty>{{ t('documentTags.emptyDirectory') }}</template>
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
        />
      </div>
    </el-card>

    <ArtFormDialog
      v-if="confirmation"
      :open="true"
      :title="t('documentTags.delete')"
      :confirm-label="t('documentTags.delete')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="document-tag-delete-confirm"
      @confirm="finishConfirmation(true)"
      @update:open="open => { if (!open) finishConfirmation(false); }"
    >
      <p>{{ confirmation.message }}</p>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="editorModel"
      :title="editorMode === 'create' ? t('documentTags.createDialogTitle') : t('documentTags.editDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="document-tag-editor-submit"
      :show-confirm="editorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="document-tag-editor-form"
        :model="editorForm"
        :disabled="changing"
        label-width="96px"
        class="document-tags-editor-form"
      >
        <el-form-item
          :label="t('documentTags.name')"
          prop="name"
          required
          :error="fieldErrors.name || undefined"
        >
          <el-input
            v-model="editorForm.name"
            data-testid="document-tag-name"
            :placeholder="t('documentTags.namePlaceholder')"
            @update:model-value="fieldErrors.name = validateName()"
          />
        </el-form-item>
        <el-form-item :label="t('documentTags.isHot')">
          <el-switch v-model="editorForm.isHot" data-testid="document-tag-is-hot" />
        </el-form-item>
        <el-form-item :label="t('documentTags.isRecommended')">
          <el-switch v-model="editorForm.isRecommended" data-testid="document-tag-is-recommended" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.document-tags-view {
  flex: 1;
  min-height: 0;
}

.document-tags-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.document-tags-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.document-tags-view :deep(.art-crud-table-main) {
  flex: 1;
  min-height: 200px;
}

.document-tags-editor-form {
  padding-top: 8px;
}

.document-tags-view__flag {
  margin: 0 4px 4px 0;
}

.art-sr-heading {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
