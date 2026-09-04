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
  ElTag,
  ElSelect,
  ElOption,
  ElTreeSelect
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type {
  FullNetProblemDetails,
  HostAnnouncement,
  HostAnnouncementTargetOrganization,
  HostUser,
  AnnouncementAudienceKind,
  AnnouncementKind
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostAnnouncement,
  listHostAnnouncements,
  publishHostAnnouncement,
  retractHostAnnouncement,
  updateHostAnnouncement
} from '../api/host-announcements';
import { listHostUsers } from '../api/users';
import { listHostTenants } from '../api/tenants';
import { getHostUserOrganizationReference } from '../api/host-user-organization-reference';
import {
  buildOrganizationUnitTree,
  mapOrganizationUnitTreeToSelectOptions,
  type OrganizationUnitTreeSelectOption
} from '../organization/org-unit-tree';
import { useNotificationsRealtime } from '../notifications/realtime';

defineOptions({ name: 'HostAnnouncementsView' });

type EditorMode = 'create' | 'edit';
type AnnouncementTableColumnKey = 'status' | 'content' | 'createdAt' | 'publishedAt';

interface AppliedFilters {
  title: string;
  status: '' | 'draft' | 'published' | 'retracted';
  kind: '' | AnnouncementKind;
  audienceKind: '' | AnnouncementAudienceKind;
}

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const pagedItems = ref<HostAnnouncement[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ title: '', status: '', kind: '', audienceKind: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingItem = ref<HostAnnouncement | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  title: '',
  content: '',
  kind: 'announcement' as AnnouncementKind,
  audienceKind: 'all' as AnnouncementAudienceKind,
  targetUserIds: [] as string[],
  targetOrganizations: [] as HostAnnouncementTargetOrganization[]
});
const fieldErrors = reactive({ title: '', content: '', targetAudience: '' });
const hostUserOptions = ref<HostUser[]>([]);
const hostUsersLoading = ref(false);
const orgTenants = ref<Array<{ id: string; name: string }>>([]);
const orgTenantsLoading = ref(false);
const editorOrgTenantId = ref('');
const editorOrgUnitIds = ref<string[]>([]);
const orgUnitTreeOptions = ref<OrganizationUnitTreeSelectOption[]>([]);
const orgUnitsLoading = ref(false);
const syncingOrgUnitSelection = ref(false);
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
      { label: t('hostAnnouncements.statusPublished'), value: 'published' },
      { label: t('hostAnnouncements.statusRetracted'), value: 'retracted' }
    ]
  },
  {
    key: 'kind',
    label: t('hostAnnouncements.fieldKind'),
    type: 'select',
    options: [
      { label: t('hostAnnouncements.kindNotice'), value: 'notice' },
      { label: t('hostAnnouncements.kindAnnouncement'), value: 'announcement' }
    ]
  },
  {
    key: 'audienceKind',
    label: t('hostAnnouncements.fieldAudience'),
    type: 'select',
    options: [
      { label: t('hostAnnouncements.audienceAll'), value: 'all' },
      { label: t('hostAnnouncements.audienceUsers'), value: 'users' },
      { label: t('hostAnnouncements.audienceOrganizations'), value: 'organizations' }
    ]
  }
]);

const canCreate = computed(() => session.can('notifications.announcements.create'));
const canUpdate = computed(() => session.can('notifications.announcements.update'));
const canPublish = computed(() => session.can('notifications.announcements.publish'));
const canRetract = computed(() => session.can('notifications.announcements.retract'));

watch([page, pageSize], () => {
  void load();
});

watchLoading(loading);

onMounted(() => {
  void load();
});

watch(notificationsRealtime.announcementRevision, () => {
  void load();
});

watch(
  () => editorForm.audienceKind,
  kind => {
    fieldErrors.targetAudience = '';
    if (kind === 'all') {
      editorForm.targetUserIds = [];
      editorForm.targetOrganizations = [];
      editorOrgTenantId.value = '';
      editorOrgUnitIds.value = [];
      orgUnitTreeOptions.value = [];
      return;
    }
    if (kind === 'users') {
      editorForm.targetOrganizations = [];
      editorOrgTenantId.value = '';
      editorOrgUnitIds.value = [];
      orgUnitTreeOptions.value = [];
      if (editorOpen.value) {
        void ensureHostUserOptions();
      }
      return;
    }
    editorForm.targetUserIds = [];
    if (editorOpen.value) {
      void ensureOrgAudienceEditor();
    }
  }
);

watch(editorOrgTenantId, tenantId => {
  syncEditorOrgUnitIdsFromTargets();
  if (!tenantId) {
    orgUnitTreeOptions.value = [];
    editorOrgUnitIds.value = [];
    return;
  }
  void loadOrgUnitTree(tenantId);
});

watch(editorOrgUnitIds, unitIds => {
  if (syncingOrgUnitSelection.value || editorForm.audienceKind !== 'organizations') {
    return;
  }
  const tenantId = editorOrgTenantId.value;
  if (!tenantId) {
    return;
  }
  const others = editorForm.targetOrganizations.filter(target => target.tenantId !== tenantId);
  editorForm.targetOrganizations = [
    ...others,
    ...unitIds.map(organizationUnitId => ({ tenantId, organizationUnitId }))
  ];
  fieldErrors.targetAudience = '';
});

watch(editorOpen, open => {
  if (!open) {
    return;
  }
  if (editorForm.audienceKind === 'users') {
    void ensureHostUserOptions();
    return;
  }
  if (editorForm.audienceKind === 'organizations') {
    void ensureOrgAudienceEditor();
  }
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
  if (status === 'published') {
    return t('hostAnnouncements.statusPublished');
  }
  if (status === 'retracted') {
    return t('hostAnnouncements.statusRetracted');
  }
  return t('hostAnnouncements.statusDraft');
}

function statusTagType(status: HostAnnouncement['status']): 'success' | 'info' | 'warning' {
  if (status === 'published') {
    return 'success';
  }
  if (status === 'retracted') {
    return 'warning';
  }
  return 'info';
}

function resetPage(): void {
  page.value = 1;
}

function clearFieldErrors(): void {
  fieldErrors.title = '';
  fieldErrors.content = '';
  fieldErrors.targetAudience = '';
}

function resetEditorTargets(): void {
  editorForm.targetUserIds = [];
  editorForm.targetOrganizations = [];
  editorOrgTenantId.value = '';
  editorOrgUnitIds.value = [];
  orgUnitTreeOptions.value = [];
}

function hostUserLabel(user: HostUser): string {
  return `${user.displayName} (${user.username})`;
}

function formatAudienceSummary(item: HostAnnouncement): string {
  if (item.audienceKind === 'users') {
    return t('hostAnnouncements.audienceSummaryUsers', { count: item.targetUserIds.length });
  }
  if (item.audienceKind === 'organizations') {
    return t('hostAnnouncements.audienceSummaryOrganizations', {
      count: item.targetOrganizations.length
    });
  }
  return t('hostAnnouncements.audienceAll');
}

async function ensureHostUserOptions(): Promise<void> {
  if (hostUsersLoading.value || hostUserOptions.value.length > 0) {
    return;
  }
  hostUsersLoading.value = true;
  try {
    const page = await listHostUsers(1, 200);
    hostUserOptions.value = page.items.filter(user => user.isActive);
  } finally {
    hostUsersLoading.value = false;
  }
}

async function ensureOrgTenants(): Promise<void> {
  if (orgTenantsLoading.value || orgTenants.value.length > 0) {
    return;
  }
  orgTenantsLoading.value = true;
  try {
    const page = await listHostTenants(1, 200);
    orgTenants.value = page.items.map(tenant => ({ id: tenant.id, name: tenant.name }));
  } finally {
    orgTenantsLoading.value = false;
  }
}

async function ensureOrgAudienceEditor(): Promise<void> {
  await ensureOrgTenants();
  const preferredTenantId =
    editorForm.targetOrganizations[0]?.tenantId
    ?? orgTenants.value[0]?.id
    ?? '';
  editorOrgTenantId.value = preferredTenantId;
}

async function loadOrgUnitTree(tenantId: string): Promise<void> {
  orgUnitsLoading.value = true;
  try {
    const reference = await getHostUserOrganizationReference(tenantId);
    const activeUnits = reference.units.filter(unit => unit.isActive);
    orgUnitTreeOptions.value = mapOrganizationUnitTreeToSelectOptions(
      buildOrganizationUnitTree(activeUnits)
    );
    syncEditorOrgUnitIdsFromTargets();
  } finally {
    orgUnitsLoading.value = false;
  }
}

function syncEditorOrgUnitIdsFromTargets(): void {
  syncingOrgUnitSelection.value = true;
  const tenantId = editorOrgTenantId.value;
  editorOrgUnitIds.value = tenantId
    ? editorForm.targetOrganizations
        .filter(target => target.tenantId === tenantId)
        .map(target => target.organizationUnitId)
    : [];
  syncingOrgUnitSelection.value = false;
}

function buildAudiencePayload():
  | { targetUserIds: string[] }
  | { targetOrganizations: HostAnnouncementTargetOrganization[] }
  | Record<string, never> {
  if (editorForm.audienceKind === 'users') {
    return { targetUserIds: [...editorForm.targetUserIds] };
  }
  if (editorForm.audienceKind === 'organizations') {
    return { targetOrganizations: [...editorForm.targetOrganizations] };
  }
  return {};
}

function validateTargetAudience(): string {
  if (editorForm.audienceKind === 'users' && editorForm.targetUserIds.length === 0) {
    return t('hostAnnouncements.targetAudienceRequired');
  }
  if (editorForm.audienceKind === 'organizations' && editorForm.targetOrganizations.length === 0) {
    return t('hostAnnouncements.targetAudienceRequired');
  }
  return '';
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
  fieldErrors.targetAudience = validateTargetAudience();
  return !fieldErrors.title && !fieldErrors.content && !fieldErrors.targetAudience;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const filters = appliedFilters.value;
    const response = await listHostAnnouncements({
      page: page.value,
      pageSize: pageSize.value,
      title: filters.title,
      status: filters.status,
      kind: filters.kind,
      audienceKind: filters.audienceKind
    });
    pagedItems.value = response.items;
    total.value = response.total;
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
    status: (params.status as AppliedFilters['status']) ?? '',
    kind: (params.kind as AppliedFilters['kind']) ?? '',
    audienceKind: (params.audienceKind as AppliedFilters['audienceKind']) ?? ''
  };
  resetPage();
  void load();
}

function resetSearch(): void {
  appliedFilters.value = { title: '', status: '', kind: '', audienceKind: '' };
  resetPage();
  void load();
}

function openCreate(): void {
  editorMode.value = 'create';
  editingItem.value = null;
  editorForm.title = '';
  editorForm.content = '';
  editorForm.kind = 'announcement';
  editorForm.audienceKind = 'all';
  resetEditorTargets();
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
  editorForm.kind = item.kind;
  editorForm.audienceKind = item.audienceKind;
  editorForm.targetUserIds = [...item.targetUserIds];
  editorForm.targetOrganizations = item.targetOrganizations.map(target => ({ ...target }));
  editorOrgTenantId.value = item.targetOrganizations[0]?.tenantId ?? '';
  syncEditorOrgUnitIdsFromTargets();
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
    await createHostAnnouncement({
      title: editorForm.title,
      content: editorForm.content,
      kind: editorForm.kind,
      audienceKind: editorForm.audienceKind,
      ...buildAudiencePayload()
    });
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
    await updateHostAnnouncement(item.id, {
      title: editorForm.title,
      content: editorForm.content,
      version: item.version,
      kind: editorForm.kind,
      audienceKind: editorForm.audienceKind,
      ...buildAudiencePayload()
    });
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

async function retract(item: HostAnnouncement): Promise<void> {
  if (changing.value || item.status !== 'published' || !canRetract.value) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('hostAnnouncements.confirmRetract', { title: item.title }),
      t('hostAnnouncements.retract'),
      {
        type: 'warning',
        confirmButtonText: t('hostAnnouncements.retract'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await retractHostAnnouncement(item.id, item.version);
    ElMessage.success(t('hostAnnouncements.retractSuccess'));
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
                <el-tag :type="statusTagType(row.status)">
                  {{ statusLabel(row.status) }}
                </el-tag>
              </template>
            </el-table-column>

            <!-- Element Plus 将表格插槽行推断为 DefaultRow；这里按数据源契约单层收窄为公告。 -->
            <el-table-column :label="t('hostAnnouncements.fieldAudience')" min-width="120">
              <template #default="{ row }">{{ formatAudienceSummary(row as HostAnnouncement) }}</template>
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
                <ArtTableActionGroup v-if="row.status === 'draft'">
                  <PermissionGate code="notifications.announcements.update">
                    <ArtTableActionButton
                      type="edit"
                      test-id="host-announcements-edit"
                      :title="t('hostAnnouncements.edit')"
                      :disabled="changing"
                  @click="openEdit(row as HostAnnouncement)"
                    />
                  </PermissionGate>
                  <PermissionGate code="notifications.announcements.publish">
                    <ArtTableActionButton
                      type="view"
                      test-id="host-announcements-publish"
                      :title="t('hostAnnouncements.publish')"
                      :disabled="changing"
                  @click="publish(row as HostAnnouncement)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
                <ArtTableActionGroup v-else-if="row.status === 'published'">
                  <PermissionGate code="notifications.announcements.retract">
                    <ArtTableActionButton
                      type="delete"
                      test-id="host-announcements-retract"
                      :title="t('hostAnnouncements.retract')"
                      :disabled="changing"
                  @click="retract(row as HostAnnouncement)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
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
        <el-form-item :label="t('hostAnnouncements.fieldKind')" prop="kind">
          <el-select v-model="editorForm.kind" data-testid="host-announcements-kind">
            <el-option :label="t('hostAnnouncements.kindNotice')" value="notice" />
            <el-option :label="t('hostAnnouncements.kindAnnouncement')" value="announcement" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('hostAnnouncements.fieldAudience')" prop="audienceKind">
          <el-select v-model="editorForm.audienceKind" data-testid="host-announcements-audience">
            <el-option :label="t('hostAnnouncements.audienceAll')" value="all" />
            <el-option :label="t('hostAnnouncements.audienceUsers')" value="users" />
            <el-option :label="t('hostAnnouncements.audienceOrganizations')" value="organizations" />
          </el-select>
        </el-form-item>
        <el-form-item
          v-if="editorForm.audienceKind === 'users'"
          :label="t('hostAnnouncements.fieldTargetUsers')"
          prop="targetUserIds"
          required
          :error="fieldErrors.targetAudience || undefined"
        >
          <el-select
            v-model="editorForm.targetUserIds"
            multiple
            filterable
            collapse-tags
            collapse-tags-tooltip
            :loading="hostUsersLoading"
            :placeholder="t('hostAnnouncements.targetUsersPlaceholder')"
            data-testid="host-announcements-target-users"
            style="width: 100%"
            @update:model-value="fieldErrors.targetAudience = ''"
          >
            <el-option
              v-for="user in hostUserOptions"
              :key="user.id"
              :label="hostUserLabel(user)"
              :value="user.id"
            />
          </el-select>
        </el-form-item>
        <template v-if="editorForm.audienceKind === 'organizations'">
          <el-form-item :label="t('hostAnnouncements.fieldTargetTenant')" prop="editorOrgTenantId">
            <el-select
              v-model="editorOrgTenantId"
              filterable
              :loading="orgTenantsLoading"
              :placeholder="t('hostAnnouncements.targetTenantPlaceholder')"
              data-testid="host-announcements-target-tenant"
              style="width: 100%"
            >
              <el-option
                v-for="tenant in orgTenants"
                :key="tenant.id"
                :label="tenant.name"
                :value="tenant.id"
              />
            </el-select>
          </el-form-item>
          <el-form-item
            :label="t('hostAnnouncements.fieldTargetOrganizations')"
            prop="targetOrganizations"
            required
            :error="fieldErrors.targetAudience || undefined"
          >
            <el-tree-select
              v-model="editorOrgUnitIds"
              :data="orgUnitTreeOptions"
              multiple
              check-strictly
              filterable
              clearable
              collapse-tags
              collapse-tags-tooltip
              :loading="orgUnitsLoading"
              :disabled="!editorOrgTenantId"
              :render-after-expand="false"
              :placeholder="t('hostAnnouncements.targetOrganizationsPlaceholder')"
              data-testid="host-announcements-target-organizations"
              style="width: 100%"
            />
          </el-form-item>
        </template>
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
