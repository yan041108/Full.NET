<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElCheckbox,
  ElForm,
  ElFormItem,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import {
  type FullNetProblemDetails,
  type OrganizationAssignableUser,
  type OrganizationPosition,
  type OrganizationUserPosition
} from '@fullnet/client-contracts';
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
import { listOrganizationPositions } from '../api/org-positions';
import {
  createOrganizationUserPosition,
  disableOrganizationUserPosition,
  listAssignableOrganizationUserPositionUsers,
  listOrganizationUserPositions,
  updateOrganizationUserPosition
} from '../api/org-user-positions';

defineOptions({ name: 'OrgUserPositionsView' });

type AssignmentTableColumnKey = 'username' | 'position' | 'primary' | 'status';

interface AppliedFilters {
  user: string;
  position: string;
  status: '' | 'active' | 'inactive';
}

const session = useSessionStore();
const { t } = useAdminI18n();
const allAssignments = ref<OrganizationUserPosition[]>([]);
const users = ref<OrganizationAssignableUser[]>([]);
const positions = ref<OrganizationPosition[]>([]);
const loading = ref(false);
const changing = ref(false);
const loadingMoreUsers = ref(false);
const userPage = ref(1);
const userTotal = ref(0);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ user: '', position: '', status: '' });
const editorOpen = ref(false);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  userId: '',
  positionId: '',
  isPrimary: false
});
const fieldErrors = reactive({ userId: '', positionId: '' });
const columnVisibility = ref<Record<AssignmentTableColumnKey, boolean>>({
  username: true,
  position: true,
  primary: true,
  status: true
});

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

const canCreate = computed(() => session.can('organization.user_positions.create'));
const canUpdate = computed(() => session.can('organization.user_positions.update'));
const canDisable = computed(() => session.can('organization.user_positions.disable'));
const hasMoreUsers = computed(() => users.value.length < userTotal.value);

const filteredAssignments = computed(() => {
  let rows = allAssignments.value;
  const filters = appliedFilters.value;

  if (filters.user.trim()) {
    const keyword = filters.user.trim().toLowerCase();
    rows = rows.filter(
      assignment =>
        assignment.username.toLowerCase().includes(keyword)
        || assignment.displayName.toLowerCase().includes(keyword)
    );
  }

  if (filters.position.trim()) {
    const keyword = filters.position.trim().toLowerCase();
    rows = rows.filter(
      assignment =>
        assignment.positionCode.toLowerCase().includes(keyword)
        || assignment.positionName.toLowerCase().includes(keyword)
    );
  }

  if (filters.status === 'active') {
    rows = rows.filter(assignment => assignment.isActive);
  } else if (filters.status === 'inactive') {
    rows = rows.filter(assignment => !assignment.isActive);
  }

  return rows;
});

const { page, pageSize, total, pagedItems: pagedAssignments, resetPage } = useArtClientPagination(filteredAssignments);

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'username', label: t('orgUserPositions.user'), visible: columnVisibility.value.username },
    {
      key: 'position',
      label: t('orgUserPositions.position'),
      visible: columnVisibility.value.position
    },
    { key: 'primary', label: t('orgUserPositions.primary'), visible: columnVisibility.value.primary },
    { key: 'status', label: t('users.status'), visible: columnVisibility.value.status }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as AssignmentTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'user',
    label: t('orgUserPositions.user'),
    placeholder: t('orgUserPositions.searchUserPlaceholder')
  },
  {
    key: 'position',
    label: t('orgUserPositions.position'),
    placeholder: t('orgUserPositions.searchPositionPlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('orgUserPositions.searchStatusPlaceholder'),
    options: [
      { label: t('orgUserPositions.active'), value: 'active' },
      { label: t('orgUserPositions.inactive'), value: 'inactive' }
    ]
  }
]);

watchLoading(loading);

onMounted(() => {
  void load();
});

function isColumnVisible(key: AssignmentTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function clearFieldErrors(): void {
  fieldErrors.userId = '';
  fieldErrors.positionId = '';
}

function validateUserId(): string {
  if (!editorForm.userId) {
    return t('orgUserPositions.userRequired');
  }
  return '';
}

function validatePositionId(): string {
  if (!editorForm.positionId) {
    return t('orgUserPositions.positionRequired');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.userId = validateUserId();
  fieldErrors.positionId = validatePositionId();
  return !fieldErrors.userId && !fieldErrors.positionId;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [assignmentPage, positionPage, assignableUserPage] = await Promise.all([
      listOrganizationUserPositions(),
      listOrganizationPositions(),
      canCreate.value
        ? listAssignableOrganizationUserPositionUsers().catch(error => {
          if (isForbidden(error)) {
            return {
              items: [] as OrganizationAssignableUser[],
              page: 1,
              pageSize: 100,
              total: 0
            };
          }
          throw error;
        })
        : Promise.resolve({
          items: [] as OrganizationAssignableUser[],
          page: 1,
          pageSize: 100,
          total: 0
        })
    ]);
    allAssignments.value = assignmentPage.items;
    users.value = assignableUserPage.items;
    userPage.value = assignableUserPage.page;
    userTotal.value = assignableUserPage.total;
    positions.value = positionPage.items.filter(position => position.isActive);
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserPositions.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function loadMoreUsers(): Promise<void> {
  if (loadingMoreUsers.value || !canCreate.value || !hasMoreUsers.value) {
    return;
  }
  loadingMoreUsers.value = true;
  problem.value = undefined;
  try {
    const nextPage = await listAssignableOrganizationUserPositionUsers(userPage.value + 1);
    users.value = appendUniqueUsers(users.value, nextPage.items);
    userPage.value = nextPage.page;
    userTotal.value = nextPage.total;
  } catch (error: unknown) {
    if (isForbidden(error)) {
      userTotal.value = users.value.length;
      return;
    }
    problem.value = toProblem(error, 'orgUserPositions.loadFailed');
  } finally {
    loadingMoreUsers.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    user: params.user ?? '',
    position: params.position ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { user: '', position: '', status: '' };
  resetPage();
}

function openCreate(): void {
  editorForm.userId = '';
  editorForm.positionId = '';
  editorForm.isPrimary = false;
  clearFieldErrors();
  editorOpen.value = true;
}

async function submitEditor(): Promise<void> {
  if (changing.value || !applyFieldErrors()) {
    return;
  }
  if (!canCreate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createOrganizationUserPosition(
      editorForm.userId,
      editorForm.positionId,
      editorForm.isPrimary
    );
    editorOpen.value = false;
    ElMessage.success(t('orgUserPositions.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function setPrimary(assignment: OrganizationUserPosition): Promise<void> {
  if (changing.value || !assignment.isActive || assignment.isPrimary) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateOrganizationUserPosition(assignment.id, true, assignment.version);
    ElMessage.success(t('orgUserPositions.primarySuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(assignment: OrganizationUserPosition): Promise<void> {
  if (changing.value || !assignment.isActive) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('orgUserPositions.confirmDisable', {
        name: `${assignment.displayName} / ${assignment.positionName}`
      }),
      t('orgUserPositions.disable'),
      {
        type: 'warning',
        confirmButtonText: t('orgUserPositions.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await disableOrganizationUserPosition(assignment.id);
    ElMessage.success(t('orgUserPositions.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'orgUserPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'orgUserPositions.loadFailed' | 'orgUserPositions.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.organization_user_position_failed', title: t(fallbackKey) };
}

function isForbidden(error: unknown): boolean {
  return typeof error === 'object'
    && error !== null
    && 'status' in error
    && error.status === 403;
}

function appendUniqueUsers(
  current: OrganizationAssignableUser[],
  incoming: OrganizationAssignableUser[]
): OrganizationAssignableUser[] {
  const byId = new Map(current.map(user => [user.id, user]));
  incoming.forEach(user => byId.set(user.id, user));
  return [...byId.values()];
}
</script>

<template>
  <section class="org-user-positions-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('orgUserPositions.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="3"
      :search-label="t('orgUserPositions.query')"
      :reset-label="t('orgUserPositions.reset')"
      :expand-label="t('orgUserPositions.expand')"
      :collapse-label="t('orgUserPositions.collapse')"
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
            <PermissionGate code="organization.user_positions.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="org-user-positions-action-create"
                @click="openCreate"
              >
                {{ t('orgUserPositions.addAssignment') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedAssignments.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedAssignments"
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

            <el-table-column :label="t('orgUserPositions.user')" min-width="200">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.positionCode.slice(0, 2).toUpperCase() }}</span>
                  <div>
                    <div class="art-crud-table-row__name" translate="no">{{ row.displayName }}</div>
                    <div class="art-crud-table-row__sub" translate="no">{{ row.username }}</div>
                  </div>
                </div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('position')"
              :label="t('orgUserPositions.position')"
              min-width="180"
            >
              <template #default="{ row }">
                <div translate="no">{{ row.positionName }}</div>
                <div class="art-crud-table-row__sub" translate="no">{{ row.positionCode }}</div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('primary')"
              :label="t('orgUserPositions.primary')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                <el-tag v-if="row.isPrimary" type="warning">{{ t('orgUserPositions.primary') }}</el-tag>
                <span v-else>—</span>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('status')"
              :label="t('users.status')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                <el-tag :type="row.isActive ? 'success' : 'info'">
                  {{ t(row.isActive ? 'orgUserPositions.active' : 'orgUserPositions.inactive') }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('users.columnActions')"
              width="120"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <div class="art-crud-table-actions">
                  <PermissionGate v-if="canUpdate" code="organization.user_positions.update">
                    <ArtTableActionButton
                      v-if="row.isActive && !row.isPrimary"
                      type="edit"
                      test-id="org-user-positions-action-set-primary"
                      :title="t('orgUserPositions.setPrimary')"
                      :disabled="changing"
                  @click="setPrimary(row as OrganizationUserPosition)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="row.isActive && canDisable" code="organization.user_positions.disable">
                    <ArtTableActionButton
                      type="delete"
                      test-id="org-user-positions-action-disable"
                      :title="t('orgUserPositions.disable')"
                      :disabled="changing"
                  @click="disable(row as OrganizationUserPosition)"
                    />
                  </PermissionGate>
                </div>
              </template>
            </el-table-column>

            <template #empty>{{ t('orgUserPositions.emptyDirectory') }}</template>
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
      :title="t('orgUserPositions.createDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="org-user-positions-editor-submit"
      :show-confirm="canCreate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="org-user-positions-editor-form"
        :model="editorForm"
        label-width="96px"
        class="org-user-positions-editor-form"
      >
        <el-form-item
          :label="t('orgUserPositions.user')"
          prop="userId"
          required
          :error="fieldErrors.userId || undefined"
        >
          <el-select
            v-model="editorForm.userId"
            :placeholder="t('orgUserPositions.userPlaceholder')"
            @update:model-value="fieldErrors.userId = validateUserId()"
          >
            <el-option
              v-for="user in users"
              :key="user.id"
              :label="`${user.displayName} (${user.username})`"
              :value="user.id"
            />
          </el-select>
          <el-button
            v-if="hasMoreUsers"
            link
            :loading="loadingMoreUsers"
            data-testid="org-user-positions-load-more-users"
            @click.prevent="loadMoreUsers"
          >
            {{ t('orgUserPositions.loadMoreUsers') }}
          </el-button>
        </el-form-item>
        <el-form-item
          :label="t('orgUserPositions.position')"
          prop="positionId"
          required
          :error="fieldErrors.positionId || undefined"
        >
          <el-select
            v-model="editorForm.positionId"
            :placeholder="t('orgUserPositions.positionPlaceholder')"
            @update:model-value="fieldErrors.positionId = validatePositionId()"
          >
            <el-option
              v-for="position in positions"
              :key="position.id"
              :label="`${position.name} (${position.code})`"
              :value="position.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="editorForm.isPrimary">{{ t('orgUserPositions.isPrimary') }}</el-checkbox>
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.org-user-positions-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.org-user-positions-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.org-user-positions-editor-form {
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
