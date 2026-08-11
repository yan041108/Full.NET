<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElPagination,
  ElTable,
  ElTableColumn
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
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createDocumentTag,
  deleteDocumentTag,
  listDocumentTags,
  updateDocumentTag
} from '../api/document-tags';

defineOptions({ name: 'DocumentTagsView' });

type EditorMode = 'create' | 'edit';

interface AppliedFilters {
  name: string;
}

const session = useSessionStore();
const { t } = useAdminI18n();
const allTags = ref<HostDocumentTag[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ name: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingTag = ref<HostDocumentTag | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  name: '',
  code: null as string | null,
  icon: null as string | null,
  color: null as string | null,
  description: null as string | null
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
  updateTableHeight,
  watchLoading
} = useArtCrudTableLayout();

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

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'name',
    label: t('documentTags.name'),
    placeholder: t('documentTags.searchNamePlaceholder')
  }
]);

const canCreate = computed(() => session.can('document.tags.create'));
const canUpdate = computed(() => session.can('document.tags.update'));
const canDelete = computed(() => session.can('document.tags.delete'));

watchLoading(loading);

onMounted(() => {
  void load();
});

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

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    allTags.value = await listDocumentTags();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'documentTags.loadFailed');
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = { name: params.name ?? '' };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { name: '' };
  resetPage();
}

function openCreate(): void {
  editorMode.value = 'create';
  editingTag.value = null;
  editorForm.name = '';
  editorForm.code = null;
  editorForm.icon = null;
  editorForm.color = null;
  editorForm.description = null;
  clearFieldErrors();
  editorOpen.value = true;
}

function openEdit(tag: HostDocumentTag): void {
  if (changing.value) {
    return;
  }
  editorMode.value = 'edit';
  editingTag.value = tag;
  editorForm.name = tag.name;
  editorForm.code = null;
  editorForm.icon = null;
  editorForm.color = tag.color;
  editorForm.description = null;
  clearFieldErrors();
  editorOpen.value = true;
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
  }
  editorForm.name = editorForm.name.trim();
  if (!applyFieldErrors()) {
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
  changing.value = true;
  problem.value = undefined;
  try {
    await createDocumentTag(
      editorForm.name,
      editorForm.code ?? null,
      editorForm.icon ?? null,
      editorForm.color ?? null,
      editorForm.description ?? null
    );
    editorOpen.value = false;
    ElMessage.success(t('documentTags.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'documentTags.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveEdit(): Promise<void> {
  const tag = editingTag.value;
  if (!canUpdate.value || !tag) {
    return;
  }
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
      tag.version
    );
    editorOpen.value = false;
    ElMessage.success(t('documentTags.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'documentTags.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function remove(tag: HostDocumentTag): Promise<void> {
  if (changing.value || !canDelete.value) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('documentTags.confirmDelete', { name: tag.name }),
      t('documentTags.delete'),
      {
        type: 'warning',
        confirmButtonText: t('documentTags.delete'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await deleteDocumentTag(tag.id, tag.version);
    ElMessage.success(t('documentTags.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'documentTags.operationFailed');
  } finally {
    changing.value = false;
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
  <section class="document-tags-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentTags.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

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

    <el-card class="art-table-card" shadow="never">
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

          <div class="art-table__pagination center custom-pagination">
            <el-pagination
              v-model:current-page="page"
              v-model:page-size="pageSize"
              :total="total"
              background
              layout="total, sizes, prev, pager, next, jumper"
              :page-sizes="[10, 20, 50, 100]"
            />
          </div>
        </div>
      </div>
    </el-card>

    <ArtFormDialog
      v-model:open="editorOpen"
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
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
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

.document-tags-editor-form {
  padding-top: 8px;
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
