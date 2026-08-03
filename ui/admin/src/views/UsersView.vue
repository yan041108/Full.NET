<script setup lang="ts">
import { computed, nextTick, onActivated, onMounted, onUnmounted, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag,
  ElTree
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type {
  FullNetProblemDetails,
  HostRole,
  HostUser,
  HostUserProfileWrite,
  OrganizationPosition,
  OrganizationUnit,
  OrganizationUserPosition,
  OrganizationUserUnit
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableHeader, { type ArtTableColumnOption } from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import UserEditorDialog from './components/UserEditorDialog.vue';

defineOptions({ name: 'UsersView' });
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listOrganizationUnits } from '../api/org-units';
import { listOrganizationPositions } from '../api/org-positions';
import {
  createOrganizationUserPosition,
  listOrganizationUserPositions
} from '../api/org-user-positions';
import {
  createOrganizationUserUnit,
  listOrganizationUserUnits
} from '../api/org-user-units';
import { listHostRoles } from '../api/roles';
import {
  createHostUser,
  disableHostUser,
  enableHostUser,
  exportHostUsers,
  getHostUserRoles,
  listHostUsers,
  replaceHostUserRoles,
  resetHostUserPassword,
  updateHostUser
} from '../api/users';

type EditorTab = 'basic' | 'roles' | 'org' | 'profile' | 'binding';
type EditorMode = 'create' | 'edit';

interface AppliedFilters {
  username: string;
  displayName: string;
  phone: string;
  email: string;
  status: '' | 'active' | 'inactive';
}

interface UserRow extends HostUser {
  roleLabels: string;
  orgLabel: string;
  positionLabel: string;
}

interface OrgTreeNode {
  id: string;
  label: string;
  children?: OrgTreeNode[];
}

const session = useSessionStore();
const { t } = useAdminI18n();
const allUsers = ref<UserRow[]>([]);
const roles = ref<HostRole[]>([]);
const orgUnits = ref<OrganizationUnit[]>([]);
const orgPositions = ref<OrganizationPosition[]>([]);
const userUnits = ref<OrganizationUserUnit[]>([]);
const userPositions = ref<OrganizationUserPosition[]>([]);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({
  username: '',
  displayName: '',
  phone: '',
  email: '',
  status: ''
});
const selectedUnitId = ref<string | null>(null);
const orgFilter = ref('');
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const tableMainRef = ref<HTMLElement | null>(null);
const tableHeight = ref(360);
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editorTab = ref<EditorTab>('basic');
const editingUser = ref<HostUser | null>(null);
const editorUsername = ref('');
const editorDisplayName = ref('');
const editorPassword = ref('');
const editorProfile = ref<HostUserProfileWrite>(emptyProfile());
const editorPrimaryUnitId = ref('');
const editorSubsidiaryUnitIds = ref<string[]>([]);
const editorPositionId = ref('');
const selectedRoleIds = ref<string[]>([]);
const rolesVersion = ref(0);
const tableSize = ref<'large' | 'default' | 'small'>('default');
const tableZebra = ref(true);
const tableBorder = ref(true);
const tableHeaderBackground = ref(true);
type UserTableColumnKey = 'gender' | 'phone' | 'createdAt';
const columnVisibility = ref<Record<UserTableColumnKey, boolean>>({
  gender: true,
  phone: true,
  createdAt: true
});

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'gender', label: t('users.gender'), visible: columnVisibility.value.gender },
    { key: 'phone', label: t('users.phone'), visible: columnVisibility.value.phone },
    { key: 'createdAt', label: t('users.createdAt'), visible: columnVisibility.value.createdAt }
  ],
  set: (columns) => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as UserTableColumnKey] = column.visible !== false;
      }
    }
  }
});

function isColumnVisible(key: UserTableColumnKey): boolean {
  return columnVisibility.value[key];
}

const tableHeaderCellStyle = computed(() => ({
  background: tableHeaderBackground.value
    ? 'var(--art-gray-100)'
    : 'var(--art-default-box-color)'
}));

const canCreate = computed(() => session.can('identity.users.create'));
const canUpdate = computed(() => session.can('identity.users.update'));
const canAssignRoles = computed(() => session.can('identity.users.assign_roles'));

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'username',
    label: t('users.username'),
    placeholder: t('users.searchAccountPlaceholder')
  },
  {
    key: 'displayName',
    label: t('users.realName'),
    placeholder: t('users.searchNamePlaceholder')
  },
  {
    key: 'phone',
    label: t('users.phone'),
    placeholder: t('users.searchPhonePlaceholder')
  },
  {
    key: 'email',
    label: t('users.email'),
    placeholder: t('users.searchEmailPlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('users.searchStatusPlaceholder'),
    options: [
      { label: t('users.active'), value: 'active' },
      { label: t('users.inactive'), value: 'inactive' }
    ]
  }
]);

const transferRoles = computed(() =>
  roles.value
    .filter(role => role.isActive && !role.isSystem && !role.isSuperAdministrator)
    .map(role => ({
      key: role.id,
      label: role.name
    }))
);

const orgUnitOptions = computed(() =>
  orgUnits.value
    .filter(unit => unit.isActive)
    .sort((left, right) => left.displayOrder - right.displayOrder)
    .map(unit => ({
      value: unit.id,
      label: unit.name
    }))
);

const positionOptions = computed(() =>
  orgPositions.value
    .filter(position => position.isActive)
    .sort((left, right) => left.displayOrder - right.displayOrder)
    .map(position => ({
      value: position.id,
      label: position.name
    }))
);

const orgTreeData = computed<OrgTreeNode[]>(() => {
  const keyword = orgFilter.value.trim().toLowerCase();
  const activeUnits = orgUnits.value.filter(unit => unit.isActive);
  const filtered = keyword
    ? activeUnits.filter(unit => unit.name.toLowerCase().includes(keyword))
    : activeUnits;
  const childrenByParent = new Map<string | null, OrganizationUnit[]>();

  for (const unit of filtered) {
    const siblings = childrenByParent.get(unit.parentId) ?? [];
    siblings.push(unit);
    childrenByParent.set(unit.parentId, siblings);
  }

  const toNodes = (parentId: string | null): OrgTreeNode[] =>
    (childrenByParent.get(parentId) ?? [])
      .sort((left, right) => left.displayOrder - right.displayOrder)
      .map(unit => ({
        id: unit.id,
        label: unit.name,
        children: toNodes(unit.id)
      }));

  return [{
    id: '__all__',
    label: t('users.orgTreeAll'),
    children: toNodes(null)
  }];
});

const filteredUsers = computed(() => {
  let rows = allUsers.value;
  const filters = appliedFilters.value;

  if (filters.username.trim()) {
    const keyword = filters.username.trim().toLowerCase();
    rows = rows.filter(user => user.username.toLowerCase().includes(keyword));
  }

  if (filters.displayName.trim()) {
    const keyword = filters.displayName.trim().toLowerCase();
    rows = rows.filter(user => user.displayName.toLowerCase().includes(keyword));
  }

  if (filters.phone.trim()) {
    const keyword = filters.phone.trim().toLowerCase();
    rows = rows.filter(user => user.profile?.phoneNumber?.toLowerCase().includes(keyword));
  }

  if (filters.email.trim()) {
    const keyword = filters.email.trim().toLowerCase();
    rows = rows.filter(user => user.profile?.email?.toLowerCase().includes(keyword));
  }

  if (filters.status === 'active') {
    rows = rows.filter(user => user.isActive);
  } else if (filters.status === 'inactive') {
    rows = rows.filter(user => !user.isActive);
  }

  if (selectedUnitId.value) {
    const userIds = new Set(
      userUnits.value
        .filter(item => item.isActive && item.unitId === selectedUnitId.value)
        .map(item => item.userId)
    );
    rows = rows.filter(user => userIds.has(user.id));
  }

  return rows;
});

const pagedUsers = computed(() => {
  const start = (page.value - 1) * pageSize.value;
  return filteredUsers.value.slice(start, start + pageSize.value);
});

watch(filteredUsers, rows => {
  total.value = rows.length;
  const maxPage = Math.max(1, Math.ceil(rows.length / pageSize.value) || 1);
  if (page.value > maxPage) {
    page.value = maxPage;
  }
});

onMounted(() => {
  void load();
  updateTableHeight();
  window.addEventListener('resize', updateTableHeight);
});

onActivated(() => {
  void nextTick(updateTableHeight);
});

onUnmounted(() => {
  window.removeEventListener('resize', updateTableHeight);
});

watch(loading, () => {
  void nextTick(updateTableHeight);
});

function updateTableHeight(): void {
  const container = tableMainRef.value;
  if (!container) {
    return;
  }
  const top = container.getBoundingClientRect().top;
  tableHeight.value = Math.max(240, window.innerHeight - top - 68);
}

function emptyProfile(): HostUserProfileWrite {
  return {
    nickname: null,
    phoneNumber: null,
    email: null,
    employeeNumber: null,
    gender: null,
    remark: null,
    version: null
  };
}

function loadProfileFromUser(user: HostUser | null): void {
  if (!user?.profile) {
    editorProfile.value = emptyProfile();
    return;
  }
  editorProfile.value = {
    nickname: user.profile.nickname,
    phoneNumber: user.profile.phoneNumber,
    email: user.profile.email,
    employeeNumber: user.profile.employeeNumber,
    gender: user.profile.gender,
    birthDate: user.profile.birthDate,
    address: user.profile.address,
    remark: user.profile.remark,
    version: user.profile.version
  };
}

function resetOrgEditor(): void {
  editorPrimaryUnitId.value = '';
  editorSubsidiaryUnitIds.value = [];
  editorPositionId.value = '';
}

function loadOrgFromUser(user: HostUser | null): void {
  if (!user) {
    resetOrgEditor();
    return;
  }

  const assignments = userUnits.value.filter(
    item => item.userId === user.id && item.isActive
  );
  editorPrimaryUnitId.value = assignments.find(item => item.isPrimary)?.unitId ?? '';
  editorSubsidiaryUnitIds.value = assignments
    .filter(item => !item.isPrimary)
    .map(item => item.unitId);

  const positions = userPositions.value.filter(
    item => item.userId === user.id && item.isActive
  );
  editorPositionId.value = positions.find(item => item.isPrimary)?.positionId ?? '';
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [referenceBundle, users] = await Promise.all([
      loadReferenceData(),
      fetchAllUsers()
    ]);

    roles.value = referenceBundle.roles;
    orgUnits.value = referenceBundle.orgUnits;
    orgPositions.value = referenceBundle.orgPositions;
    userUnits.value = referenceBundle.userUnits;
    userPositions.value = referenceBundle.userPositions;

    allUsers.value = enrichUsers(users);
    total.value = filteredUsers.value.length;
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function loadReferenceData(): Promise<{
  roles: HostRole[];
  orgUnits: OrganizationUnit[];
  orgPositions: OrganizationPosition[];
  userUnits: OrganizationUserUnit[];
  userPositions: OrganizationUserPosition[];
}> {
  const [rolePage, unitPage, positionPage, assignmentPage, userPositionPage] = await Promise.all([
    listHostRoles(1, 200).catch(() => ({
      items: [] as HostRole[],
      page: 1,
      pageSize: 200,
      total: 0
    })),
    listOrganizationUnits(1, 200).catch(() => ({
      items: [] as OrganizationUnit[],
      page: 1,
      pageSize: 200,
      total: 0
    })),
    listOrganizationPositions(1, 200).catch(() => ({
      items: [] as OrganizationPosition[],
      page: 1,
      pageSize: 200,
      total: 0
    })),
    listOrganizationUserUnits(1, 500).catch(() => ({
      items: [] as OrganizationUserUnit[],
      page: 1,
      pageSize: 500,
      total: 0
    })),
    listOrganizationUserPositions(1, 500).catch(() => ({
      items: [] as OrganizationUserPosition[],
      page: 1,
      pageSize: 500,
      total: 0
    }))
  ]);

  return {
    roles: rolePage.items,
    orgUnits: unitPage.items,
    orgPositions: positionPage.items,
    userUnits: assignmentPage.items,
    userPositions: userPositionPage.items
  };
}

async function fetchAllUsers(): Promise<HostUser[]> {
  const pageSizeLimit = 100;
  const firstPage = await listHostUsers(1, pageSizeLimit);
  const items = [...firstPage.items];
  const totalPages = Math.ceil(firstPage.total / pageSizeLimit);

  for (let current = 2; current <= totalPages; current += 1) {
    const nextPage = await listHostUsers(current, pageSizeLimit);
    items.push(...nextPage.items);
  }

  return items;
}

function enrichUsers(source: HostUser[]): UserRow[] {
  const primaryOrgMap = new Map<string, string>();
  const primaryPositionMap = new Map<string, string>();

  for (const assignment of userUnits.value) {
    if (!assignment.isActive) {
      continue;
    }
    if (!primaryOrgMap.has(assignment.userId) || assignment.isPrimary) {
      primaryOrgMap.set(assignment.userId, assignment.unitName);
    }
  }

  for (const assignment of userPositions.value) {
    if (!assignment.isActive) {
      continue;
    }
    if (!primaryPositionMap.has(assignment.userId) || assignment.isPrimary) {
      primaryPositionMap.set(assignment.userId, assignment.positionName);
    }
  }

  return source.map(user => ({
    ...user,
    roleLabels: '',
    orgLabel: primaryOrgMap.get(user.id) ?? t('users.unassignedOrg'),
    positionLabel: primaryPositionMap.get(user.id) ?? t('users.fieldEmpty')
  }));
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    username: params.username ?? '',
    displayName: params.displayName ?? '',
    phone: params.phone ?? '',
    email: params.email ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
  page.value = 1;
}

function resetSearch(): void {
  appliedFilters.value = {
    username: '',
    displayName: '',
    phone: '',
    email: '',
    status: ''
  };
  page.value = 1;
}

function handleOrgSelect(node: OrgTreeNode): void {
  selectedUnitId.value = node.id === '__all__' ? null : node.id;
  page.value = 1;
}

function openCreate(): void {
  editorMode.value = 'create';
  editorTab.value = 'basic';
  editingUser.value = null;
  editorUsername.value = '';
  editorDisplayName.value = '';
  editorPassword.value = '';
  editorProfile.value = emptyProfile();
  resetOrgEditor();
  selectedRoleIds.value = [];
  editorOpen.value = true;
}

async function openEdit(user: HostUser, tab: EditorTab = 'basic'): Promise<void> {
  if (changing.value) {
    return;
  }

  editorMode.value = 'edit';
  editorTab.value = tab;
  editingUser.value = user;
  editorUsername.value = user.username;
  editorDisplayName.value = user.displayName;
  editorPassword.value = '';
  loadProfileFromUser(user);
  loadOrgFromUser(user);

  if (canAssignRoles.value) {
    changing.value = true;
    try {
      const userRoles = await getHostUserRoles(user.id);
      selectedRoleIds.value = [...userRoles.roleIds];
      rolesVersion.value = userRoles.version;
    } catch (error: unknown) {
      problem.value = toProblem(error, 'users.operationFailed');
      return;
    } finally {
      changing.value = false;
    }
  }

  editorOpen.value = true;
}

async function submitEditor(): Promise<void> {
  if (changing.value) {
    return;
  }

  if (editorMode.value === 'edit' && editorTab.value === 'roles') {
    await saveRoles();
    return;
  }

  if (editorMode.value === 'create') {
    await createUser();
    return;
  }

  await updateUser();
}

async function syncOrgAssignments(userId: string): Promise<void> {
  const existingUnits = userUnits.value.filter(
    item => item.userId === userId && item.isActive
  );
  const primaryExisting = existingUnits.find(item => item.isPrimary);

  if (
    editorPrimaryUnitId.value
    && primaryExisting?.unitId !== editorPrimaryUnitId.value
  ) {
    await createOrganizationUserUnit(userId, editorPrimaryUnitId.value, true);
  }

  for (const unitId of editorSubsidiaryUnitIds.value) {
    if (unitId === editorPrimaryUnitId.value) {
      continue;
    }
    if (!existingUnits.some(item => item.unitId === unitId)) {
      await createOrganizationUserUnit(userId, unitId, false);
    }
  }

  const existingPositions = userPositions.value.filter(
    item => item.userId === userId && item.isActive
  );
  const primaryPosition = existingPositions.find(item => item.isPrimary);

  if (editorPositionId.value && primaryPosition?.positionId !== editorPositionId.value) {
    await createOrganizationUserPosition(userId, editorPositionId.value, true);
  }
}

async function syncRoles(userId: string): Promise<void> {
  if (!canAssignRoles.value || selectedRoleIds.value.length === 0) {
    return;
  }

  const userRoles = await getHostUserRoles(userId);
  await replaceHostUserRoles(
    userId,
    [...selectedRoleIds.value].sort(),
    userRoles.version
  );
}

async function createUser(): Promise<void> {
  if (!editorUsername.value.trim() || !editorDisplayName.value.trim() || !editorPassword.value) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    const created = await createHostUser(
      editorUsername.value.trim(),
      editorDisplayName.value.trim(),
      editorPassword.value,
      editorProfile.value
    );
    await syncOrgAssignments(created.id);
    await syncRoles(created.id);
    editorOpen.value = false;
    ElMessage.success(t('users.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function updateUser(): Promise<void> {
  const user = editingUser.value;
  if (!user || !editorDisplayName.value.trim()) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    await updateHostUser(
      user.id,
      editorDisplayName.value.trim(),
      user.version,
      editorProfile.value
    );
    await syncOrgAssignments(user.id);
    if (canAssignRoles.value) {
      await syncRoles(user.id);
    }
    editorOpen.value = false;
    ElMessage.success(t('users.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveRoles(): Promise<void> {
  const user = editingUser.value;
  if (!user) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    await replaceHostUserRoles(
      user.id,
      [...selectedRoleIds.value].sort(),
      rolesVersion.value
    );
    editorOpen.value = false;
    ElMessage.success(t('users.rolesSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function resetPassword(user: HostUser): Promise<void> {
  if (changing.value || !user.isActive) {
    return;
  }

  try {
    const result = await ElMessageBox.prompt(
      t('users.resetPasswordTitle'),
      t('users.resetPassword'),
      {
        inputType: 'password',
        inputPattern: /.{8,}/,
        inputErrorMessage: t('users.passwordPlaceholder'),
        showCancelButton: true
      }
    );
    changing.value = true;
    await resetHostUserPassword(user.id, result.value);
    ElMessage.success(t('users.resetPasswordSuccess'));
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(user: HostUser): Promise<void> {
  if (changing.value || !user.isActive) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('users.confirmDisable', { name: user.username }),
      t('users.disable'),
      {
        type: 'warning',
        confirmButtonText: t('users.disable'),
        cancelButtonText: t('hostDocumentItems.cancel')
      }
    );
    changing.value = true;
    await disableHostUser(user.id);
    ElMessage.success(t('users.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function enable(user: HostUser): Promise<void> {
  if (changing.value || user.isActive) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('users.confirmEnable', { name: user.username }),
      t('users.enable'),
      {
        type: 'warning',
        confirmButtonText: t('users.enable'),
        cancelButtonText: t('hostDocumentItems.cancel')
      }
    );
    changing.value = true;
    await enableHostUser(user.id);
    ElMessage.success(t('users.enableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function exportUsers(): Promise<void> {
  if (changing.value) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    const rows = await exportHostUsers();
    const blob = new Blob([JSON.stringify(rows, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'host-users.json';
    link.click();
    URL.revokeObjectURL(url);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

function avatarText(user: HostUser): string {
  return user.profile?.nickname?.slice(0, 1)
    || user.displayName.slice(0, 1)
    || user.username.slice(0, 2).toUpperCase();
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function formatDate(value: string): string {
  return new Date(value).toLocaleString();
}

function genderLabel(gender: string | null | undefined): string {
  if (gender === 'male') {
    return t('users.genderMale');
  }
  if (gender === 'female') {
    return t('users.genderFemale');
  }
  return t('users.fieldEmpty');
}

function profileText(value: string | null | undefined): string {
  return value?.trim() ? value : t('users.fieldEmpty');
}

function userSubtitle(user: HostUser): string {
  return user.profile?.email?.trim() || user.displayName;
}

function toProblem(
  error: unknown,
  fallbackKey: 'users.loadFailed' | 'users.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_user_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="users-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('users.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="4"
      :search-label="t('users.query')"
      :reset-label="t('users.reset')"
      :expand-label="t('users.expand')"
      :collapse-label="t('users.collapse')"
      @search="handleSearch"
      @reset="resetSearch"
    />

    <el-card class="art-table-card" shadow="never">
      <div class="users-page-body">
        <aside class="users-org-panel">
          <div class="users-org-panel__title">{{ t('users.orgTreeTitle') }}</div>
          <el-input
            v-model="orgFilter"
            clearable
            :placeholder="t('users.orgTreeFilterPlaceholder')"
          />
          <el-tree
            class="users-org-panel__tree"
            :data="orgTreeData"
            node-key="id"
            default-expand-all
            highlight-current
            :expand-on-click-node="false"
            @node-click="handleOrgSelect"
          />
          <p v-if="orgUnits.length === 0" class="users-org-panel__empty">
            {{ t('users.orgTreeEmpty') }}
          </p>
        </aside>

        <div ref="tableMainRef" class="users-table-main">
          <ArtTableHeader
            v-model:columns="tableColumns"
            v-model:table-size="tableSize"
            v-model:zebra="tableZebra"
            v-model:border="tableBorder"
            v-model:header-background="tableHeaderBackground"
            :loading="loading"
            full-class="users-table-main"
            layout="refresh,size,fullscreen,columns,settings"
            @refresh="load"
          >
            <template #left>
              <PermissionGate code="identity.users.create">
                <el-button
                  type="primary"
                  plain
                  :icon="Plus"
                  data-testid="users-action-create"
                  @click="openCreate"
                >
                  {{ t('users.addUser') }}
                </el-button>
              </PermissionGate>
              <PermissionGate code="identity.users.export">
                <el-button
                  data-testid="users-action-export"
                  plain
                  :disabled="changing"
                  @click="exportUsers"
                >
                  {{ t('users.export') }}
                </el-button>
              </PermissionGate>
            </template>
          </ArtTableHeader>

          <div class="art-table" :class="{ 'is-empty': pagedUsers.length === 0 }">
            <el-table
              v-loading="loading"
              :data="pagedUsers"
              :height="tableHeight"
              :size="tableSize"
              :stripe="tableZebra"
              :border="tableBorder"
              :header-cell-style="tableHeaderCellStyle"
              class="users-data-table"
              :class="{ 'art-table--header-bg': tableHeaderBackground }"
              style="width: 100%"
            >
              <el-table-column :label="t('users.columnIndex')" width="72" align="center">
                <template #default="{ $index }">
                  {{ rowIndex($index) }}
                </template>
              </el-table-column>

              <el-table-column :label="t('users.username')" min-width="220">
                <template #default="{ row }">
                  <div class="users-table-user">
                    <span class="users-table-user__avatar">{{ avatarText(row) }}</span>
                    <div>
                      <div class="users-table-user__name" translate="no">{{ row.username }}</div>
                      <div class="users-table-user__sub" translate="no">{{ userSubtitle(row) }}</div>
                    </div>
                  </div>
                </template>
              </el-table-column>

              <el-table-column
                v-if="isColumnVisible('gender')"
                :label="t('users.gender')"
                width="88"
                align="center"
              >
                <template #default="{ row }">
                  {{ genderLabel(row.profile?.gender) }}
                </template>
              </el-table-column>

              <el-table-column
                v-if="isColumnVisible('phone')"
                :label="t('users.phone')"
                width="140"
                align="center"
              >
                <template #default="{ row }">
                  {{ profileText(row.profile?.phoneNumber) }}
                </template>
              </el-table-column>

              <el-table-column :label="t('users.status')" width="100" align="center">
                <template #default="{ row }">
                  <el-tag size="small" :type="row.isActive ? 'success' : 'info'">
                    {{ t(row.isActive ? 'users.active' : 'users.inactive') }}
                  </el-tag>
                </template>
              </el-table-column>

              <el-table-column
                v-if="isColumnVisible('createdAt')"
                :label="t('users.createdAt')"
                width="180"
                align="center"
              >
                <template #default="{ row }">
                  {{ formatDate(row.createdAtUtc) }}
                </template>
              </el-table-column>

              <el-table-column
                :label="t('users.columnActions')"
                width="148"
                fixed="right"
                align="center"
              >
                <template #default="{ row }">
                  <div class="users-table-actions">
                    <PermissionGate code="identity.users.update">
                      <ArtTableActionButton
                        type="edit"
                        test-id="users-action-edit"
                        :title="t('users.edit')"
                        @click="openEdit(row)"
                      />
                    </PermissionGate>
                    <PermissionGate code="identity.users.assign_roles">
                      <ArtTableActionButton
                        type="roles"
                        test-id="users-action-roles"
                        :title="t('users.roles')"
                        @click="openEdit(row, 'roles')"
                      />
                    </PermissionGate>
                    <PermissionGate v-if="row.isActive" code="identity.users.reset_password">
                      <ArtTableActionButton
                        type="password"
                        test-id="users-action-reset-password"
                        :title="t('users.resetPassword')"
                        @click="resetPassword(row)"
                      />
                    </PermissionGate>
                    <PermissionGate v-if="row.isActive" code="identity.users.disable">
                      <ArtTableActionButton
                        type="delete"
                        test-id="users-action-disable"
                        :title="t('users.disable')"
                        @click="disable(row)"
                      />
                    </PermissionGate>
                    <PermissionGate v-if="!row.isActive" code="identity.users.enable">
                      <el-button
                        link
                        type="success"
                        data-testid="users-action-enable"
                        @click="enable(row)"
                      >
                        {{ t('users.enable') }}
                      </el-button>
                    </PermissionGate>
                  </div>
                </template>
              </el-table-column>

              <template #empty>
                {{ t('users.emptyDirectory') }}
              </template>
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
      </div>
    </el-card>

    <UserEditorDialog
      v-model:open="editorOpen"
      :mode="editorMode"
      :user="editingUser"
      :username="editorUsername"
      :display-name="editorDisplayName"
      :password="editorPassword"
      :profile="editorProfile"
      :active-tab="editorTab"
      :transfer-roles="transferRoles"
      :selected-role-ids="selectedRoleIds"
      :org-unit-options="orgUnitOptions"
      :position-options="positionOptions"
      :primary-unit-id="editorPrimaryUnitId"
      :subsidiary-unit-ids="editorSubsidiaryUnitIds"
      :position-id="editorPositionId"
      :saving="changing"
      :can-assign-roles="canAssignRoles"
      :can-create="canCreate"
      :can-update="canUpdate"
      :translate="t"
      @update:username="editorUsername = $event"
      @update:display-name="editorDisplayName = $event"
      @update:password="editorPassword = $event"
      @update:profile="editorProfile = $event"
      @update:active-tab="editorTab = $event"
      @update:selected-role-ids="selectedRoleIds = $event"
      @update:primary-unit-id="editorPrimaryUnitId = $event"
      @update:subsidiary-unit-ids="editorSubsidiaryUnitIds = $event"
      @update:position-id="editorPositionId = $event"
      @submit="submitEditor"
    />
  </section>
</template>

<style scoped>
.users-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.users-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.users-page-body {
  display: flex;
  flex: 1;
  flex-direction: row;
  align-items: stretch;
  gap: 12px;
  min-height: 0;
  min-width: 0;
}

.users-org-panel {
  display: flex;
  flex-direction: column;
  width: 220px;
  flex-shrink: 0;
  min-height: 0;
}

.users-table-main {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
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
