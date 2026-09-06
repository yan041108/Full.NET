<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
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
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, HostReleaseNote } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostReleaseNote,
  deleteHostReleaseNote,
  listHostReleaseNotes,
  publishHostReleaseNote,
  retractHostReleaseNote,
  updateHostReleaseNote
} from '../api/host-release-notes';

defineOptions({ name: 'HostReleaseNotesView' });

type EditorMode = 'create' | 'edit';
type TableColumnKey = 'version' | 'status' | 'content' | 'createdAt' | 'publishedAt';

interface AppliedFilters {
  title: string;
  status: '' | 'draft' | 'published' | 'retracted';
  versionLabel: string;
}

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const pagedItems = ref<HostReleaseNote[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ title: '', status: '', versionLabel: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingItem = ref<HostReleaseNote | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  versionLabel: '',
  title: '',
  content: ''
});
const fieldErrors = reactive({ versionLabel: '', title: '', content: '' });
const columnVisibility = ref<Record<TableColumnKey, boolean>>({
  version: true,
  status: true,
  content: true,
  createdAt: true,
  publishedAt: true
});

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  watchLoading
} = useArtCrudTableLayout();

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'version', label: t('hostReleaseNotes.fieldVersion'), visible: columnVisibility.value.version },
    { key: 'status', label: t('users.status'), visible: columnVisibility.value.status },
    { key: 'content', label: t('hostReleaseNotes.fieldContent'), visible: columnVisibility.value.content },
    { key: 'createdAt', label: t('hostReleaseNotes.createdAt'), visible: columnVisibility.value.createdAt },
    { key: 'publishedAt', label: t('hostReleaseNotes.publishedAt'), visible: columnVisibility.value.publishedAt }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as TableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'title',
    label: t('hostReleaseNotes.fieldTitle'),
    placeholder: t('hostReleaseNotes.searchTitlePlaceholder')
  },
  {
    key: 'versionLabel',
    label: t('hostReleaseNotes.fieldVersion'),
    placeholder: t('hostReleaseNotes.searchVersionPlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('hostReleaseNotes.searchStatusPlaceholder'),
    options: [
      { label: t('hostReleaseNotes.statusDraft'), value: 'draft' },
      { label: t('hostReleaseNotes.statusPublished'), value: 'published' },
      { label: t('hostReleaseNotes.statusRetracted'), value: 'retracted' }
    ]
  }
]);

const canCreate = computed(() => session.can('platform.release_notes.create'));
const canUpdate = computed(() => session.can('platform.release_notes.update'));
const canPublish = computed(() => session.can('platform.release_notes.publish'));
const canRetract = computed(() => session.can('platform.release_notes.retract'));
const canDelete = computed(() => session.can('platform.release_notes.delete'));

watch([page, pageSize], () => {
  void load();
});

watchLoading(loading);

onMounted(() => {
  void load();
});

function isColumnVisible(key: TableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

function statusLabel(status: HostReleaseNote['status']): string {
  if (status === 'published') {
    return t('hostReleaseNotes.statusPublished');
  }
  if (status === 'retracted') {
    return t('hostReleaseNotes.statusRetracted');
  }
  return t('hostReleaseNotes.statusDraft');
}

function statusTagType(status: HostReleaseNote['status']): 'info' | 'success' | 'warning' {
  if (status === 'published') {
    return 'success';
  }
  if (status === 'retracted') {
    return 'warning';
  }
  return 'info';
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const response = await listHostReleaseNotes({
      page: page.value,
      pageSize: pageSize.value,
      title: appliedFilters.value.title,
      status: appliedFilters.value.status,
      versionLabel: appliedFilters.value.versionLabel
    });
    pagedItems.value = response.items;
    total.value = response.total;
  } catch (error: unknown) {
    problem.value = resolveProblem(error);
  } finally {
    loading.value = false;
  }
}

function handleSearch(): void {
  appliedFilters.value = {
    title: searchForm.value.title?.trim() ?? '',
    status: (searchForm.value.status as AppliedFilters['status']) ?? '',
    versionLabel: searchForm.value.versionLabel?.trim() ?? ''
  };
  page.value = 1;
  void load();
}

function resetSearch(): void {
  searchForm.value = {};
  appliedFilters.value = { title: '', status: '', versionLabel: '' };
  page.value = 1;
  void load();
}

function openCreate(): void {
  editorMode.value = 'create';
  editingItem.value = null;
  editorForm.versionLabel = '';
  editorForm.title = '';
  editorForm.content = '';
  clearFieldErrors();
  editorOpen.value = true;
}

function openEdit(item: HostReleaseNote): void {
  editorMode.value = 'edit';
  editingItem.value = item;
  editorForm.versionLabel = item.versionLabel;
  editorForm.title = item.title;
  editorForm.content = item.content;
  clearFieldErrors();
  editorOpen.value = true;
}

function clearFieldErrors(): void {
  fieldErrors.versionLabel = '';
  fieldErrors.title = '';
  fieldErrors.content = '';
}

function validateEditor(): boolean {
  clearFieldErrors();
  let valid = true;
  if (!editorForm.versionLabel.trim()) {
    fieldErrors.versionLabel = t('hostReleaseNotes.versionRequired');
    valid = false;
  }
  if (!editorForm.title.trim()) {
    fieldErrors.title = t('hostReleaseNotes.titleRequired');
    valid = false;
  }
  if (!editorForm.content.trim()) {
    fieldErrors.content = t('hostReleaseNotes.contentRequired');
    valid = false;
  }
  return valid;
}

async function submitEditor(): Promise<void> {
  if (!validateEditor()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    if (editorMode.value === 'create') {
      await createHostReleaseNote({
        versionLabel: editorForm.versionLabel.trim(),
        title: editorForm.title.trim(),
        content: editorForm.content.trim()
      });
      ElMessage.success(t('hostReleaseNotes.createSuccess'));
    } else if (editingItem.value) {
      await updateHostReleaseNote(editingItem.value.id, {
        versionLabel: editorForm.versionLabel.trim(),
        title: editorForm.title.trim(),
        content: editorForm.content.trim(),
        version: editingItem.value.version
      });
      ElMessage.success(t('hostReleaseNotes.updateSuccess'));
    }
    editorOpen.value = false;
    await load();
  } catch (error: unknown) {
    problem.value = resolveProblem(error, 'hostReleaseNotes.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function confirmPublish(item: HostReleaseNote): Promise<void> {
  try {
    await ElMessageBox.confirm(
      t('hostReleaseNotes.confirmPublish', { title: item.title }),
      t('hostReleaseNotes.publish'),
      { type: 'warning' }
    );
  } catch {
    return;
  }
  changing.value = true;
  try {
    await publishHostReleaseNote(item.id, item.version);
    ElMessage.success(t('hostReleaseNotes.publishSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = resolveProblem(error, 'hostReleaseNotes.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function confirmRetract(item: HostReleaseNote): Promise<void> {
  try {
    await ElMessageBox.confirm(
      t('hostReleaseNotes.confirmRetract', { title: item.title }),
      t('hostReleaseNotes.retract'),
      { type: 'warning' }
    );
  } catch {
    return;
  }
  changing.value = true;
  try {
    await retractHostReleaseNote(item.id, item.version);
    ElMessage.success(t('hostReleaseNotes.retractSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = resolveProblem(error, 'hostReleaseNotes.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function confirmDelete(item: HostReleaseNote): Promise<void> {
  try {
    await ElMessageBox.confirm(
      t('hostReleaseNotes.confirmDelete', { title: item.title }),
      t('hostReleaseNotes.delete'),
      { type: 'warning' }
    );
  } catch {
    return;
  }
  changing.value = true;
  try {
    await deleteHostReleaseNote(item.id, item.version);
    ElMessage.success(t('hostReleaseNotes.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = resolveProblem(error, 'hostReleaseNotes.operationFailed');
  } finally {
    changing.value = false;
  }
}

function resolveProblem(
  error: unknown,
  fallbackKey: 'hostReleaseNotes.loadFailed' | 'hostReleaseNotes.operationFailed' = 'hostReleaseNotes.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_release_note_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="host-release-notes-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('hostReleaseNotes.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="2"
      :search-label="t('hostReleaseNotes.query')"
      :reset-label="t('hostReleaseNotes.reset')"
      :expand-label="t('hostReleaseNotes.expand')"
      :collapse-label="t('hostReleaseNotes.collapse')"
      @search="handleSearch"
      @reset="resetSearch"
    />

    <el-card class="art-table-card" shadow="never">
      <div ref="tableMainRef" class="art-crud-table-main">
        <ArtTableHeader
          v-model:columns="tableColumns"
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="loading"
          full-class="art-crud-table-main"
          layout="refresh,size,fullscreen,columns,settings"
          @refresh="load"
        >
          <template #left>
            <PermissionGate code="platform.release_notes.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="host-release-notes-action-create"
                @click="openCreate"
              >
                {{ t('hostReleaseNotes.addReleaseNote') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedItems.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedItems"
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

            <el-table-column :label="t('hostReleaseNotes.fieldTitle')" min-width="180">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.title.slice(0, 2).toUpperCase() }}</span>
                  <div>
                    <div class="art-crud-table-row__name" translate="no">{{ row.title }}</div>
                    <div v-if="isColumnVisible('version')" class="art-crud-table-row__meta" translate="no">
                      v{{ row.versionLabel }}
                    </div>
                  </div>
                </div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('status')"
              :label="t('users.status')"
              width="120"
              align="center"
            >
              <template #default="{ row }">
                <el-tag :type="statusTagType(row.status)" size="small">{{ statusLabel(row.status) }}</el-tag>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('content')"
              :label="t('hostReleaseNotes.fieldContent')"
              min-width="220"
              show-overflow-tooltip
            >
              <template #default="{ row }">
                <span translate="no">{{ row.content }}</span>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('createdAt')"
              :label="t('hostReleaseNotes.createdAt')"
              width="168"
            >
              <template #default="{ row }">{{ formatDateTime(row.createdAtUtc) }}</template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('publishedAt')"
              :label="t('hostReleaseNotes.publishedAt')"
              width="168"
            >
              <template #default="{ row }">{{ formatDateTime(row.publishedAtUtc) }}</template>
            </el-table-column>

            <el-table-column :label="t('users.columnActions')" width="220" fixed="right">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate v-if="row.status === 'draft'" code="platform.release_notes.update">
                    <ArtTableActionButton
                      test-id="host-release-notes-edit"
                      @click="openEdit(row)"
                    >
                      {{ t('hostReleaseNotes.edit') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <PermissionGate v-if="row.status === 'draft'" code="platform.release_notes.publish">
                    <ArtTableActionButton
                      test-id="host-release-notes-publish"
                      @click="confirmPublish(row)"
                    >
                      {{ t('hostReleaseNotes.publish') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <PermissionGate v-if="row.status === 'published'" code="platform.release_notes.retract">
                    <ArtTableActionButton
                      test-id="host-release-notes-retract"
                      @click="confirmRetract(row)"
                    >
                      {{ t('hostReleaseNotes.retract') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <PermissionGate v-if="row.status === 'draft'" code="platform.release_notes.delete">
                    <ArtTableActionButton
                      test-id="host-release-notes-delete"
                      type="danger"
                      @click="confirmDelete(row)"
                    >
                      {{ t('hostReleaseNotes.delete') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
          </el-table>

          <p v-if="!loading && pagedItems.length === 0" class="art-table-empty">
            {{ t('hostReleaseNotes.emptyList') }}
          </p>
        </div>

        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          class="art-table-pagination"
          layout="total, sizes, prev, pager, next"
          :total="total"
          :page-sizes="[10, 20, 50]"
        />
      </div>
    </el-card>

    <ArtFormDialog
      v-model="editorOpen"
      :title="editorMode === 'create' ? t('hostReleaseNotes.createDialogTitle') : t('hostReleaseNotes.editDialogTitle')"
      :submit-label="editorMode === 'create' ? t('hostReleaseNotes.create') : t('hostReleaseNotes.save')"
      :submitting="changing"
      confirm-test-id="host-release-notes-editor-submit"
      @submit="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="host-release-notes-editor-form"
        label-position="top"
        class="host-release-notes-editor-form"
        @submit.prevent
      >
        <el-form-item :label="t('hostReleaseNotes.fieldVersion')" :error="fieldErrors.versionLabel">
          <el-input
            v-model="editorForm.versionLabel"
            data-testid="host-release-notes-version"
            :placeholder="t('hostReleaseNotes.versionPlaceholder')"
          />
        </el-form-item>
        <el-form-item :label="t('hostReleaseNotes.fieldTitle')" :error="fieldErrors.title">
          <el-input
            v-model="editorForm.title"
            data-testid="host-release-notes-title"
            maxlength="200"
            show-word-limit
          />
        </el-form-item>
        <el-form-item :label="t('hostReleaseNotes.fieldContent')" :error="fieldErrors.content">
          <el-input
            v-model="editorForm.content"
            data-testid="host-release-notes-content"
            type="textarea"
            :rows="8"
            maxlength="16000"
            show-word-limit
          />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>
