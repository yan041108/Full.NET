<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue';
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
import type { FullNetProblemDetails, HostAnnouncement } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostAnnouncement,
  listHostAnnouncements,
  publishHostAnnouncement,
  updateHostAnnouncement
} from '../api/host-announcements';
import { useNotificationsRealtime } from '../notifications/realtime';

defineOptions({ name: 'HostAnnouncementsView' });

type EditorMode = 'create' | 'edit';
type AnnouncementTableColumnKey = 'status' | 'content' | 'createdAt' | 'publishedAt';

interface AppliedFilters {
  title: string;
  status: '' | 'draft' | 'published';
}

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const allItems = ref<HostAnnouncement[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ title: '', status: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingItem = ref<HostAnnouncement | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({ title: '', content: '' });
const fieldErrors = reactive({ title: '', content: '' });
const columnVisibility = ref<Record<AnnouncementTableColumnKey, boolean>>({
  status: true,
  content: true,
  createdAt: true,
  publishedAt: true
});
const notificationsRealtime = useNotificationsRealtime();

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

const filteredItems = computed(() => {
  let rows = allItems.value;
  const filters = appliedFilters.value;

  if (filters.title.trim()) {
    const keyword = filters.title.trim().toLowerCase();
    rows = rows.filter(item => item.title.toLowerCase().includes(keyword));
  }

  if (filters.status === 'draft') {
    rows = rows.filter(item => item.status === 'draft');
  } else if (filters.status === 'published') {
    rows = rows.filter(item => item.status === 'published');
  }

  return rows;
});

const { page, pageSize, total, pagedItems, resetPage } = useArtClientPagination(filteredItems);

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'status', label: t('users.status'), visible: columnVisibility.value.status },
    { key: 'content', label: t('hostAnnouncements.fieldContent'), visible: columnVisibility.value.content },
    {
      key: 'createdAt',
      label: t('hostAnnouncements.createdAt'),
      visible: columnVisibility.value.createdAt
    },
    {
      key: 'publishedAt',
      label: t('hostAnnouncements.publishedAt'),
      visible: columnVisibility.value.publishedAt
    }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as AnnouncementTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'title',
    label: t('hostAnnouncements.fieldTitle'),
    placeholder: t('hostAnnouncements.searchTitlePlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('hostAnnouncements.searchStatusPlaceholder'),
    options: [
      { label: t('hostAnnouncements.statusDraft'), value: 'draft' },
      { label: t('hostAnnouncements.statusPublished'), value: 'published' }
    ]
  }
]);

const canCreate = computed(() => session.can('notifications.announcements.create'));
const canUpdate = computed(() => session.can('notifications.announcements.update'));
const canPublish = computed(() => session.can('notifications.announcements.publish'));

watchLoading(loading);

onMounted(() => {
  void load();
});

watch(notificationsRealtime.announcementRevision, () => {
  void load();
});

function isColumnVisible(key: AnnouncementTableColumnKey): boolean {
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

function statusLabel(status: HostAnnouncement['status']): string {
  return status === 'published'
    ? t('hostAnnouncements.statusPublished')
    : t('hostAnnouncements.statusDraft');
}

function clearFieldErrors(): void {
  fieldErrors.title = '';
  fieldErrors.content = '';
}

function validateTitle(): string {
  const title = editorForm.title.trim();
  if (!title) {
    return t('hostAnnouncements.titleRequired');
  }
  if (title.length > 200) {
    return t('hostAnnouncements.titleInvalid');
  }
  return '';
}

function validateContent(): string {
  const content = editorForm.content.trim();
  if (!content) {
    return t('hostAnnouncements.contentRequired');
  }
  if (content.length > 4000) {
    return t('hostAnnouncements.contentInvalid');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.title = validateTitle();
  fieldErrors.content = validateContent();
  return !fieldErrors.title && !fieldErrors.content;
}

async function fetchAllItems(): Promise<HostAnnouncement[]> {
  const pageLimit = 100;
  const firstPage = await listHostAnnouncements(1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listHostAnnouncements(current, pageLimit);
    items.push(...nextPage.items);
  }
  return items;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    allItems.value = await fetchAllItems();
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostAnnouncements.loadFailed');
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    title: params.title ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { title: '', status: '' };
  resetPage();
}

function openCreate(): void {
  editorMode.value = 'create';
  editingItem.value = null;
  editorForm.title = '';
  editorForm.content = '';
  clearFieldErrors();
  editorOpen.value = true;
}

function openEdit(item: HostAnnouncement): void {
  if (changing.value || item.status !== 'draft') {
    return;
  }
  editorMode.value = 'edit';
  editingItem.value = item;
  editorForm.title = item.title;
  editorForm.content = item.content;
  clearFieldErrors();
  editorOpen.value = true;
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
  }
  editorForm.title = editorForm.title.trim();
  editorForm.content = editorForm.content.trim();
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
    await createHostAnnouncement(editorForm.title, editorForm.content);
    editorOpen.value = false;
    ElMessage.success(t('hostAnnouncements.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostAnnouncements.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveEdit(): Promise<void> {
  const item = editingItem.value;
  if (!canUpdate.value || !item) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateHostAnnouncement(item.id, editorForm.title, editorForm.content, item.version);
    editorOpen.value = false;
    ElMessage.success(t('hostAnnouncements.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostAnnouncements.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function publish(item: HostAnnouncement): Promise<void> {
  if (changing.value || item.status !== 'draft' || !canPublish.value) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('hostAnnouncements.confirmPublish', { title: item.title }),
      t('hostAnnouncements.publish'),
      {
        type: 'warning',
        confirmButtonText: t('hostAnnouncements.publish'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await publishHostAnnouncement(item.id, item.version);
    ElMessage.success(t('hostAnnouncements.publishSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'hostAnnouncements.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'hostAnnouncements.loadFailed' | 'hostAnnouncements.operationFailed' = 'hostAnnouncements.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_announcement_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="host-announcements-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('hostAnnouncements.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="2"
      :search-label="t('hostAnnouncements.query')"
      :reset-label="t('hostAnnouncements.reset')"
      :expand-label="t('hostAnnouncements.expand')"
      :collapse-label="t('hostAnnouncements.collapse')"
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
            <PermissionGate code="notifications.announcements.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="host-announcements-action-create"
                @click="openCreate"
              >
                {{ t('hostAnnouncements.addAnnouncement') }}
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

            <el-table-column :label="t('hostAnnouncements.fieldTitle')" min-width="200">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.title.slice(0, 2).toUpperCase() }}</span>
                  <div>
                    <div class="art-crud-table-row__name" translate="no">{{ row.title }}</div>
                  </div>
                </div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('status')"
              :label="t('users.status')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                <el-tag :type="row.status === 'published' ? 'success' : 'info'">
                  {{ statusLabel(row.status) }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('content')"
              :label="t('hostAnnouncements.fieldContent')"
              min-width="240"
              show-overflow-tooltip
            >
              <template #default="{ row }">{{ row.content }}</template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('createdAt')"
              :label="t('hostAnnouncements.createdAt')"
              width="160"
              align="center"
            >
              <template #default="{ row }">
                <span translate="no">{{ formatDateTime(row.createdAtUtc) }}</span>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('publishedAt')"
              :label="t('hostAnnouncements.publishedAt')"
              width="160"
              align="center"
            >
              <template #default="{ row }">
                <span translate="no">{{ formatDateTime(row.publishedAtUtc) }}</span>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('users.columnActions')"
              width="120"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <div v-if="row.status === 'draft'" class="art-crud-table-actions">
                  <PermissionGate code="notifications.announcements.update">
                    <ArtTableActionButton
                      type="edit"
                      test-id="host-announcements-edit"
                      :title="t('hostAnnouncements.edit')"
                      :disabled="changing"
                      @click="openEdit(row)"
                    />
                  </PermissionGate>
                  <PermissionGate code="notifications.announcements.publish">
                    <ArtTableActionButton
                      type="view"
                      test-id="host-announcements-publish"
                      :title="t('hostAnnouncements.publish')"
                      :disabled="changing"
                      @click="publish(row)"
                    />
                  </PermissionGate>
                </div>
              </template>
            </el-table-column>

            <template #empty>{{ t('hostAnnouncements.emptyList') }}</template>
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
      :title="editorMode === 'create' ? t('hostAnnouncements.createDialogTitle') : t('hostAnnouncements.editDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="host-announcements-editor-submit"
      :show-confirm="editorMode === 'create' ? canCreate : canUpdate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="host-announcements-editor-form"
        :model="editorForm"
        label-width="96px"
        class="host-announcements-editor-form"
      >
        <el-form-item
          :label="t('hostAnnouncements.fieldTitle')"
          prop="title"
          required
          :error="fieldErrors.title || undefined"
        >
          <el-input
            v-model="editorForm.title"
            maxlength="200"
            data-testid="host-announcements-title"
            @update:model-value="fieldErrors.title = validateTitle()"
          />
        </el-form-item>
        <el-form-item
          :label="t('hostAnnouncements.fieldContent')"
          prop="content"
          required
          :error="fieldErrors.content || undefined"
        >
          <el-input
            v-model="editorForm.content"
            type="textarea"
            :rows="4"
            maxlength="4000"
            data-testid="host-announcements-content"
            @update:model-value="fieldErrors.content = validateContent()"
          />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.host-announcements-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.host-announcements-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.host-announcements-editor-form {
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
