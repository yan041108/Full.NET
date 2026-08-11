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
  ElTag,
  ElTreeSelect
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import {
  type FullNetProblemDetails,
  type OrganizationAssignableUser,
  type OrganizationUnit,
  type OrganizationUserUnit
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import {
  useArtClientPagination,
  useArtCrudTableLayout
} from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listOrganizationUnits } from '../api/org-units';
import {
  createOrganizationUserUnit,
  disableOrganizationUserUnit,
  listAssignableOrganizationUserUnitUsers,
  listOrganizationUserUnits,
  updateOrganizationUserUnit
} from '../api/org-user-units';
import {
  buildOrganizationUnitTree,
  type OrganizationUnitTreeNode
} from '../organization/org-unit-tree';

defineOptions({ name: 'OrgUserUnitsView' });

interface UnitTreeOption {
  value: string;
  label: string;
  children?: UnitTreeOption[];
}

type AssignmentTableColumnKey = 'username' | 'unit' | 'primary' | 'status';

interface AppliedFilters {
  user: string;
  unit: string;
  status: '' | 'active' | 'inactive';
}

const session = useSessionStore();
const { t } = useAdminI18n();
const allAssignments = ref<OrganizationUserUnit[]>([]);
const users = ref<OrganizationAssignableUser[]>([]);
const units = ref<OrganizationUnit[]>([]);
const loading = ref(false);
const changing = ref(false);
const loadingMoreUsers = ref(false);
const userPage = ref(1);
const userTotal = ref(0);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ user: '', unit: '', status: '' });
const editorOpen = ref(false);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  userId: '',
  unitId: '',
  isPrimary: false
});
const fieldErrors = reactive({ userId: '', unitId: '' });
const columnVisibility = ref<Record<AssignmentTableColumnKey, boolean>>({
  username: true,
  unit: true,
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

const canCreate = computed(() => session.can('organization.user_units.create'));
const canUpdate = computed(() => session.can('organization.user_units.update'));
const canDisable = computed(() => session.can('organization.user_units.disable'));
const hasMoreUsers = computed(() => users.value.length < userTotal.value);

const unitTreeOptions = computed(() =>
  mapUnitTreeOptions(buildOrganizationUnitTree(units.value))
);

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

  if (filters.unit.trim()) {
    const keyword = filters.unit.trim().toLowerCase();
    rows = rows.filter(
      assignment =>
        assignment.unitCode.toLowerCase().includes(keyword)
        || assignment.unitName.toLowerCase().includes(keyword)
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
    { key: 'username', label: t('orgUserUnits.user'), visible: columnVisibility.value.username },
    { key: 'unit', label: t('orgUserUnits.unit'), visible: columnVisibility.value.unit },
    { key: 'primary', label: t('orgUserUnits.primary'), visible: columnVisibility.value.primary },
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
    label: t('orgUserUnits.user'),
    placeholder: t('orgUserUnits.searchUserPlaceholder')
  },
  {
    key: 'unit',
    label: t('orgUserUnits.unit'),
    placeholder: t('orgUserUnits.searchUnitPlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('orgUserUnits.searchStatusPlaceholder'),
    options: [
      { label: t('orgUserUnits.active'), value: 'active' },
      { label: t('orgUserUnits.inactive'), value: 'inactive' }
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
  fieldErrors.unitId = '';
}

function validateUserId(): string {
  if (!editorForm.userId) {
    return t('orgUserUnits.userRequired');
  }
  return '';
}

function validateUnitId(): string {
  if (!editorForm.unitId) {
    return t('orgUserUnits.unitRequired');
  }
  return '';
}

function applyFieldErrors(): boolean {
  fieldErrors.userId = validateUserId();
  fieldErrors.unitId = validateUnitId();
  return !fieldErrors.userId && !fieldErrors.unitId;
}

function mapUnitTreeOptions(nodes: OrganizationUnitTreeNode[]): UnitTreeOption[] {
  return nodes.map(node => ({
    value: node.id,
    label: `${node.name} (${node.code})`,
    children: node.children.length > 0 ? mapUnitTreeOptions(node.children) : undefined
  }));
}

async function fetchAllUnits(): Promise<OrganizationUnit[]> {
  const pageLimit = 100;
  const firstPage = await listOrganizationUnits(1, pageLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageLimit);
  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listOrganizationUnits(current, pageLimit);
    items.push(...nextPage.items);
  }

  return items.filter(unit => unit.isActive);
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [assignmentPage, unitItems, assignableUserPage] = await Promise.all([
      listOrganizationUserUnits(),
      fetchAllUnits(),
      canCreate.value
        ? listAssignableOrganizationUserUnitUsers().catch(error => {
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
    units.value = unitItems;
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserUnits.loadFailed');
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
    const nextPage = await listAssignableOrganizationUserUnitUsers(userPage.value + 1);
    users.value = appendUniqueUsers(users.value, nextPage.items);
    userPage.value = nextPage.page;
    userTotal.value = nextPage.total;
  } catch (error: unknown) {
    if (isForbidden(error)) {
      userTotal.value = users.value.length;
      return;
    }
    problem.value = toProblem(error, 'orgUserUnits.loadFailed');
  } finally {
    loadingMoreUsers.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    user: params.user ?? '',
    unit: params.unit ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { user: '', unit: '', status: '' };
  resetPage();
}

function openCreate(): void {
  editorForm.userId = '';
  editorForm.unitId = '';
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
    await createOrganizationUserUnit(editorForm.userId, editorForm.unitId, editorForm.isPrimary);
    editorOpen.value = false;
    ElMessage.success(t('orgUserUnits.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserUnits.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function setPrimary(assignment: OrganizationUserUnit): Promise<void> {
  if (changing.value || !assignment.isActive || assignment.isPrimary) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateOrganizationUserUnit(assignment.id, true, assignment.version);
    ElMessage.success(t('orgUserUnits.primarySuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUserUnits.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(assignment: OrganizationUserUnit): Promise<void> {
  if (changing.value || !assignment.isActive) {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('orgUserUnits.confirmDisable', {
        name: `${assignment.displayName} / ${assignment.unitName}`
      }),
      t('orgUserUnits.disable'),
      {
        type: 'warning',
        confirmButtonText: t('orgUserUnits.disable'),
        cancelButtonText: t('users.cancel')
      }
    );
    changing.value = true;
    await disableOrganizationUserUnit(assignment.id);
    ElMessage.success(t('orgUserUnits.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'orgUserUnits.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'orgUserUnits.loadFailed' | 'orgUserUnits.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.organization_user_unit_failed', title: t(fallbackKey) };
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
  <section class="org-user-units-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('orgUserUnits.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="3"
      :search-label="t('orgUserUnits.query')"
      :reset-label="t('orgUserUnits.reset')"
      :expand-label="t('orgUserUnits.expand')"
      :collapse-label="t('orgUserUnits.collapse')"
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
            <PermissionGate code="organization.user_units.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="org-user-units-action-create"
                @click="openCreate"
              >
                {{ t('orgUserUnits.addAssignment') }}
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

            <el-table-column :label="t('orgUserUnits.user')" min-width="200">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.unitCode.slice(0, 2).toUpperCase() }}</span>
                  <div>
                    <div class="art-crud-table-row__name" translate="no">{{ row.displayName }}</div>
                    <div class="art-crud-table-row__sub" translate="no">{{ row.username }}</div>
                  </div>
                </div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('unit')"
              :label="t('orgUserUnits.unit')"
              min-width="180"
            >
              <template #default="{ row }">
                <div translate="no">{{ row.unitName }}</div>
                <div class="art-crud-table-row__sub" translate="no">{{ row.unitCode }}</div>
              </template>
            </el-table-column>

            <el-table-column
              v-if="isColumnVisible('primary')"
              :label="t('orgUserUnits.primary')"
              width="100"
              align="center"
            >
              <template #default="{ row }">
                <el-tag v-if="row.isPrimary" type="warning">{{ t('orgUserUnits.primary') }}</el-tag>
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
                  {{ t(row.isActive ? 'orgUserUnits.active' : 'orgUserUnits.inactive') }}
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
                <ArtTableActionGroup>
                  <PermissionGate v-if="canUpdate" code="organization.user_units.update">
                    <ArtTableActionButton
                      v-if="row.isActive && !row.isPrimary"
                      type="edit"
                      test-id="org-user-units-action-set-primary"
                      :title="t('orgUserUnits.setPrimary')"
                      :disabled="changing"
                  @click="setPrimary(row as OrganizationUserUnit)"
                    />
                  </PermissionGate>
                  <PermissionGate v-if="row.isActive && canDisable" code="organization.user_units.disable">
                    <ArtTableActionButton
                      type="delete"
                      test-id="org-user-units-action-disable"
                      :title="t('orgUserUnits.disable')"
                      :disabled="changing"
                  @click="disable(row as OrganizationUserUnit)"
                    />
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>

            <template #empty>{{ t('orgUserUnits.emptyDirectory') }}</template>
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
      :title="t('orgUserUnits.createDialogTitle')"
      :saving="changing"
      :confirm-label="t('users.confirm')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="org-user-units-editor-submit"
      :show-confirm="canCreate"
      @confirm="submitEditor"
    >
      <el-form
        ref="editorFormRef"
        data-testid="org-user-units-editor-form"
        :model="editorForm"
        label-width="96px"
        class="org-user-units-editor-form"
      >
        <el-form-item
          :label="t('orgUserUnits.user')"
          prop="userId"
          required
          :error="fieldErrors.userId || undefined"
        >
          <el-select
            v-model="editorForm.userId"
            :placeholder="t('orgUserUnits.userPlaceholder')"
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
            data-testid="org-user-units-load-more-users"
            @click.prevent="loadMoreUsers"
          >
            {{ t('orgUserUnits.loadMoreUsers') }}
          </el-button>
        </el-form-item>
        <el-form-item
          :label="t('orgUserUnits.unit')"
          prop="unitId"
          required
          :error="fieldErrors.unitId || undefined"
        >
          <el-tree-select
            v-model="editorForm.unitId"
            :data="unitTreeOptions"
            check-strictly
            filterable
            :render-after-expand="false"
            :placeholder="t('orgUserUnits.unitPlaceholder')"
            style="width: 100%"
            @update:model-value="fieldErrors.unitId = validateUnitId()"
          />
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="editorForm.isPrimary">{{ t('orgUserUnits.isPrimary') }}</el-checkbox>
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.org-user-units-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.org-user-units-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.org-user-units-editor-form {
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
