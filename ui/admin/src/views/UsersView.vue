<script setup lang="ts">
import { computed, nextTick, onActivated, onMounted, onUnmounted, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElSelect,
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
  ImportHostUserRowResult,
  OrganizationPosition,
  OrganizationUnit,
  OrganizationUserPosition,
  OrganizationUserUnit
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isMaskedHostUserIdCardNumber, isMaskedHostUserPhoneNumber } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import { ART_TABLE_ACTION_COLUMN_WIDTH } from '../framework/art-design/components/artTableActions';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import ArtTableColumnEditor, {
  type ArtTableColumnEditorItem
} from '../framework/art-design/components/ArtTableColumnEditor.vue';
import UsersGridDataColumn from './components/UsersGridDataColumn.vue';
import PermissionGate from '../components/PermissionGate.vue';
import UserEditorDialog from './components/UserEditorDialog.vue';

defineOptions({ name: 'UsersView' });
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostUserOrganizationPosition,
  createHostUserOrganizationUnit,
  disableHostUserOrganizationPosition,
  disableHostUserOrganizationUnit,
  getHostUserOrganizationReference,
  updateHostUserOrganizationPosition,
  updateHostUserOrganizationUnit
} from '../api/host-user-organization-reference';
import { listHostRoles } from '../api/roles';
import {
  buildOrganizationUnitTree,
  filterOrganizationUnitsForTree,
  mapOrganizationUnitTreeToSelectOptions,
  type OrganizationUnitTreeNode
} from '../organization/org-unit-tree';
import {
  batchDisableHostUsers,
  batchEnableHostUsers,
  createHostUser,
  disableHostUser,
  downloadHostUserImportTemplate,
  enableHostUser,
  retireHostUser,
  exportHostUsersWorkbook,
  getHostUserRoles,
  importHostUsersWorkbook,
  listHostUsers,
  replaceHostUserRoles,
  resetHostUserPassword,
  revealHostUserProfileFields,
  unlockHostUserLogin,
  updateHostUser
} from '../api/users';
import {
  HOST_USER_PROFILE_DICT_CODES,
  loadHostUserProfileDictOptions,
  type HostUserProfileDictOption
} from '../users/profile-dict-options';
import { gridPreferencesApi } from '../api/grid-preferences';
import {
  applyUsersGridPreference,
  createDefaultUsersGridColumns,
  resetUsersGridColumns,
  toUsersGridPreferenceColumns,
  USERS_GRID_KEY,
  type UsersGridColumnKey,
  type UsersGridColumnState
} from '../users/users-grid-columns';

type EditorTab = 'basic' | 'roles' | 'org-units' | 'org-positions' | 'profile' | 'binding';
type EditorMode = 'create' | 'edit';

const maximumImportWorkbookBytes = 1024 * 1024;

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

interface EditorSubmitCheckpoint {
  user: HostUser;
  identityKey: string;
  orgKey: string | null;
  rolesKey: string | null;
}

interface SubmitProgressStep {
  label: string;
  status: 'completed' | 'pending';
}

const profileEditorFieldKeys = [
  'nickname',
  'phone_number',
  'email',
  'employee_number',
  'gender',
  'join_date_utc',
  'sort_order',
  'id_card_type',
  'id_card_number',
  'birth_date',
  'ethnicity',
  'education_level',
  'emergency_contact_relation',
  'emergency_contact',
  'emergency_contact_phone',
  'emergency_contact_address',
  'address',
  'remark'
] as const;
const profileColumnFieldMap = {
  gender: 'gender',
  employeeNumber: 'employee_number',
  sortOrder: 'sort_order',
  phone: 'phone_number'
} as const;
const projectedMetaFieldKeys = [
  'preferred_locale',
  'failed_login_count',
  'lockout_end_utc'
] as const;

const session = useSessionStore();
const { t } = useAdminI18n();
const HOST_USER_PROFILE_DICT_CODES_EXPORT = HOST_USER_PROFILE_DICT_CODES;
const allUsers = ref<UserRow[]>([]);
const roles = ref<HostRole[]>([]);
const orgUnits = ref<OrganizationUnit[]>([]);
const orgPositions = ref<OrganizationPosition[]>([]);
const userUnits = ref<OrganizationUserUnit[]>([]);
const userPositions = ref<OrganizationUserPosition[]>([]);
const loading = ref(false);
const changing = ref(false);
/** 初始 load 写入租户上下文时跳过 watch，避免与 load 内机构请求竞态并误报 common.unexpected。 */
let suppressOrgTenantReload = false;
const revealingProfileField = ref<string | null>(null);
const selectedUsers = ref<HostUser[]>([]);
const problem = ref<FullNetProblemDetails>();
const importResults = ref<ImportHostUserRowResult[]>([]);
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({
  username: '',
  displayName: '',
  phone: '',
  email: '',
  status: ''
});
const selectedUnitId = ref<string | null>(null);
const selectedOrgTenantId = ref('');
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
const editorAccountType = ref('normal_user');
const editorPassword = ref('');
const editorProfile = ref<HostUserProfileWrite>({
  fieldKeys: [],
  nickname: null,
  phoneNumber: null,
  email: null,
  employeeNumber: null,
  gender: null,
  remark: null,
  version: null
});
const editorPrimaryUnitId = ref('');
const editorSubsidiaryUnitIds = ref<string[]>([]);
const editorPositionId = ref('');
const profileDictOptions = ref<Record<string, HostUserProfileDictOption[]>>({
  [HOST_USER_PROFILE_DICT_CODES.accountType]: [],
  [HOST_USER_PROFILE_DICT_CODES.idCardType]: [],
  [HOST_USER_PROFILE_DICT_CODES.ethnicity]: [],
  [HOST_USER_PROFILE_DICT_CODES.educationLevel]: [],
  [HOST_USER_PROFILE_DICT_CODES.emergencyContactRelation]: []
});
const selectedRoleIds = ref<string[]>([]);
const rolesVersion = ref(0);
const editorSubmitCheckpoint = ref<EditorSubmitCheckpoint | null>(null);
const tableSize = ref<'large' | 'default' | 'small'>('default');
const tableZebra = ref(true);
const tableBorder = ref(true);
const tableHeaderBackground = ref(true);
const gridColumns = ref<UsersGridColumnState[]>([]);
const gridPreferenceVersion = ref(0);
const columnEditorOpen = ref(false);
const gridPreferenceSaving = ref(false);
const gridPreferenceResetting = ref(false);
const userRoleLabelsById = ref<Record<string, string>>({});
let roleLabelsRequestId = 0;

function usersGridColumnLabel(key: UsersGridColumnKey): string {
  const labels: Record<UsersGridColumnKey, string> = {
    gender: t('users.gender'),
    roles: t('users.columnRoles'),
    org: t('users.columnOrg'),
    position: t('users.columnPosition'),
    employeeNumber: t('users.employeeNumber'),
    accountType: t('users.accountType'),
    sortOrder: t('users.columnSortOrder'),
    phone: t('users.phone'),
    createdAt: t('users.createdAt')
  };
  return labels[key];
}

function rebuildGridColumns(preference?: Awaited<ReturnType<typeof gridPreferencesApi.load>>): void {
  const defaults = createDefaultUsersGridColumns(usersGridColumnLabel, isColumnAuthorized);
  gridColumns.value = applyUsersGridPreference(defaults, preference);
}

async function loadGridPreference(): Promise<void> {
  try {
    const preference = await gridPreferencesApi.load(USERS_GRID_KEY);
    if (preference) {
      gridPreferenceVersion.value = preference.version;
    }
    rebuildGridColumns(preference);
  } catch {
    rebuildGridColumns();
  }
}

const columnEditorItems = computed<ArtTableColumnEditorItem[]>(() =>
  gridColumns.value.map(column => ({
    key: column.key,
    label: column.label,
    visible: column.visible,
    fixed: column.fixed,
    disabled: column.disabled
  })));

const visibleGridColumns = computed(() =>
  gridColumns.value.filter(column => column.visible && isColumnAuthorized(column.key)));

async function saveColumnEditor(editorItems: ArtTableColumnEditorItem[]): Promise<void> {
  const byKey = new Map(editorItems.map((item, index) => [item.key, { ...item, order: index }]));
  const nextColumns = gridColumns.value
    .map(column => {
      const edited = byKey.get(column.key);
      return edited
        ? {
            ...column,
            visible: edited.visible,
            fixed: edited.fixed,
            order: edited.order
          }
        : column;
    })
    .sort((left, right) => left.order - right.order);
  gridPreferenceSaving.value = true;
  try {
    const saved = await gridPreferencesApi.save(USERS_GRID_KEY, {
      schemaVersion: 2,
      columns: toUsersGridPreferenceColumns(nextColumns),
      version: gridPreferenceVersion.value
    });
    gridPreferenceVersion.value = saved.version;
    gridColumns.value = applyUsersGridPreference(nextColumns, saved);
    columnEditorOpen.value = false;
    ElMessage.success(t('table.columnEditorSaveSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    gridPreferenceSaving.value = false;
  }
}

async function resetColumnEditor(): Promise<void> {
  gridPreferenceResetting.value = true;
  try {
    const saved = await gridPreferencesApi.reset(USERS_GRID_KEY);
    gridPreferenceVersion.value = saved.version;
    gridColumns.value = applyUsersGridPreference(
      resetUsersGridColumns(usersGridColumnLabel, isColumnAuthorized),
      saved);
    columnEditorOpen.value = false;
    ElMessage.success(t('table.columnEditorResetSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    gridPreferenceResetting.value = false;
  }
}

function isColumnAuthorized(key: UsersGridColumnKey): boolean {
  const fieldKey = profileColumnFieldMap[key as keyof typeof profileColumnFieldMap];
  return !fieldKey || hasEffectiveField(fieldKey);
}

const tableHeaderCellStyle = computed(() => ({
  background: tableHeaderBackground.value
    ? 'var(--art-gray-100)'
    : 'var(--art-default-box-color)'
}));

const canCreate = computed(() => session.can('identity.users.create'));
const canUpdate = computed(() => session.can('identity.users.update'));
const canRevealPhoneNumber = computed(() => session.can('identity.users.reveal_phone_number'));
const canRevealIdCardNumber = computed(() => session.can('identity.users.reveal_id_card_number'));
const canAssignRoles = computed(() => session.can('identity.users.assign_roles'));
const canReadUserUnits = computed(() => session.can('organization.user_units.read'));
const canCreateUserUnits = computed(() => session.can('organization.user_units.create'));
const canUpdateUserUnits = computed(() => session.can('organization.user_units.update'));
const canDisableUserUnits = computed(() => session.can('organization.user_units.disable'));
const canReadUserPositions = computed(() => session.can('organization.user_positions.read'));
const canCreateUserPositions = computed(() => session.can('organization.user_positions.create'));
const canUpdateUserPositions = computed(() => session.can('organization.user_positions.update'));
const canDisableUserPositions = computed(() => session.can('organization.user_positions.disable'));
const isHostUserDirectoryContext = computed(() =>
  session.currentUser?.tenantId == null);
const canAssignHostOrgFromDirectory = computed(() =>
  isHostUserDirectoryContext.value
  && !!selectedOrgTenantId.value
  && (canCreate.value || canUpdate.value));
const canViewHostOrgFromDirectory = computed(() =>
  isHostUserDirectoryContext.value
  && !!selectedOrgTenantId.value
  && session.can('identity.users.read'));
const canViewUserUnits = computed(() =>
  canReadUserUnits.value
  || canCreateUserUnits.value
  || canUpdateUserUnits.value
  || canDisableUserUnits.value
  || canViewHostOrgFromDirectory.value);
const canViewUserPositions = computed(() =>
  canReadUserPositions.value
  || canCreateUserPositions.value
  || canUpdateUserPositions.value
  || canDisableUserPositions.value
  || canViewHostOrgFromDirectory.value);
const canManageUserUnits = computed(() =>
  canCreateUserUnits.value
  || canUpdateUserUnits.value
  || canDisableUserUnits.value
  || canAssignHostOrgFromDirectory.value);
const canManageUserPositions = computed(() =>
  canCreateUserPositions.value
  || canUpdateUserPositions.value
  || canDisableUserPositions.value
  || canAssignHostOrgFromDirectory.value);
const canMutateHostOrgUnits = computed(() =>
  canCreateUserUnits.value
  || canUpdateUserUnits.value
  || canDisableUserUnits.value
  || canAssignHostOrgFromDirectory.value);
const canManageOrganizations = computed(() =>
  canReadUserUnits.value
  || canManageUserUnits.value
  || canReadUserPositions.value
  || canManageUserPositions.value
  || canViewHostOrgFromDirectory.value);

function isUserLoginLocked(user: HostUser): boolean {
  const failedLoginCount = user.projectedFields?.failedLoginCount ?? 0;
  const lockoutEndUtc = user.projectedFields?.lockoutEndUtc;
  if (failedLoginCount > 0) {
    return true;
  }

  if (!lockoutEndUtc) {
    return false;
  }

  const lockoutEnd = new Date(lockoutEndUtc);
  return !Number.isNaN(lockoutEnd.getTime()) && lockoutEnd.getTime() > Date.now();
}

function hasEffectiveField(fieldKey: string, user?: HostUser | null): boolean {
  if (
    isHostUserDirectoryContext.value
    && (canCreate.value || canUpdate.value)
    && (profileEditorFieldKeys as readonly string[]).includes(fieldKey)
  ) {
    return true;
  }

  return resolveUserFieldKeys(user).includes(fieldKey);
}

function resolveUserFieldKeys(user?: HostUser | null): string[] {
  const keys = user?.projectedFields?.effectiveFieldKeys;
  if (keys && keys.length > 0) {
    return keys;
  }

  return effectiveUserFieldKeys.value;
}

/** 与编辑弹窗 `:effective-field-keys` 一致，避免列表能看、弹窗能编辑但 loadProfile 按单行键集清空值。 */
function resolveEditorFieldKeys(user: HostUser | null): string[] {
  const keys = new Set(resolveUserFieldKeys(user));
  for (const fieldKey of profileEditorFieldKeys) {
    if (hasEffectiveField(fieldKey, user)) {
      keys.add(fieldKey);
    }
  }
  for (const fieldKey of projectedMetaFieldKeys) {
    if (hasEffectiveField(fieldKey, user)) {
      keys.add(fieldKey);
    }
  }
  return [...keys];
}

const editorEffectiveFieldKeys = computed(() => resolveEditorFieldKeys(editingUser.value));

function shouldLoadProfileField(fieldKey: string, user: HostUser): boolean {
  if (hasEffectiveField(fieldKey, user)) {
    return true;
  }

  return resolveEditorFieldKeys(user).includes(fieldKey);
}
const canSubmitEditor = computed(() => {
  if (editorMode.value === 'create') {
    return canCreate.value;
  }

  switch (editorTab.value) {
    case 'roles':
      return canAssignRoles.value;
    case 'org-units':
      return canManageUserUnits.value;
    case 'org-positions':
      return canManageUserPositions.value;
    default:
      return canUpdate.value;
  }
});
const effectiveUserFieldKeys = computed(() => {
  const editingKeys = editingUser.value?.projectedFields?.effectiveFieldKeys;
  if (editingKeys && editingKeys.length > 0) {
    return editingKeys;
  }

  const sampleUser = allUsers.value.find(user => user.projectedFields?.effectiveFieldKeys?.length);
  return sampleUser?.projectedFields?.effectiveFieldKeys ?? [];
});
const editableProfileFieldKeys = computed(() =>
  profileEditorFieldKeys.filter(fieldKey => hasEffectiveField(fieldKey))
);
const hasEditableProfileFields = computed(() => editableProfileFieldKeys.value.length > 0);
const hasProfileTabFields = computed(() =>
  profileEditorFieldKeys.some(fieldKey => hasEffectiveField(fieldKey))
  || projectedMetaFieldKeys.some(fieldKey => hasEffectiveField(fieldKey))
);

const orgTenantOptions = computed(() =>
  session.availableTenants.map(tenant => ({
    value: tenant.id,
    label: tenant.name
  }))
);

function resolveDefaultOrgTenantId(): string {
  if (session.currentUser?.tenantId) {
    return session.currentUser.tenantId;
  }

  return session.availableTenants[0]?.id ?? '';
}

const searchItems = computed<ArtSearchBarItem[]>(() => {
  const items: ArtSearchBarItem[] = [
    {
      key: 'username',
      label: t('users.username'),
      placeholder: t('users.searchAccountPlaceholder')
    },
    {
      key: 'displayName',
      label: t('users.realName'),
      placeholder: t('users.searchNamePlaceholder')
    }
  ];

  if (hasEffectiveField('phone_number')) {
    items.push({
      key: 'phone',
      label: t('users.phone'),
      placeholder: t('users.searchPhonePlaceholder')
    });
  }

  if (hasEffectiveField('email')) {
    items.push({
      key: 'email',
      label: t('users.email'),
      placeholder: t('users.searchEmailPlaceholder')
    });
  }

  items.push({
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('users.searchStatusPlaceholder'),
    options: [
      { label: t('users.active'), value: 'active' },
      { label: t('users.inactive'), value: 'inactive' }
    ]
  });

  return items;
});

const transferRoles = computed(() =>
  roles.value
    .filter(role => role.isActive && !role.isSystem && !role.isSuperAdministrator)
    .map(role => ({
      key: role.id,
      label: role.name
    }))
);

const orgUnitTreeOptions = computed(() =>
  mapOrganizationUnitTreeToSelectOptions(
    buildOrganizationUnitTree(orgUnits.value.filter(unit => unit.isActive))
  )
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
    ? filterOrganizationUnitsForTree(activeUnits, unit =>
      unit.name.toLowerCase().includes(keyword)
      || unit.code.toLowerCase().includes(keyword))
    : activeUnits;

  return [{
    id: '__all__',
    label: t('users.orgTreeAll'),
    children: mapOrgTreeNodes(buildOrganizationUnitTree(filtered))
  }];
});

const hasVisibleOrgUnits = computed(() =>
  orgUnits.value.some(unit => unit.isActive)
);

const submitProgressSteps = computed<SubmitProgressStep[]>(() => {
  const checkpoint = editorSubmitCheckpoint.value;
  if (!checkpoint) {
    return [];
  }

  const steps: SubmitProgressStep[] = [{
    label: t('users.tabBasic'),
    status: 'completed'
  }];

  if (canViewUserUnits.value) {
    steps.push({
      label: t('users.tabOrgUnits'),
      status: checkpoint.orgKey === null ? 'pending' : 'completed'
    });
  }

  if (canViewUserPositions.value) {
    steps.push({
      label: t('users.tabOrgPositions'),
      status: checkpoint.orgKey === null ? 'pending' : 'completed'
    });
  }

  if (canAssignRoles.value || checkpoint.rolesKey === null) {
    steps.push({
      label: t('users.tabRoles'),
      status: checkpoint.rolesKey === null ? 'pending' : 'completed'
    });
  }

  return steps;
});

function mapOrgTreeNodes(nodes: OrganizationUnitTreeNode[]): OrgTreeNode[] {
  return nodes.map(node => ({
    id: node.id,
    label: node.name,
    children: node.children.length > 0
      ? mapOrgTreeNodes(node.children)
      : undefined
  }));
}

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

  if (filters.phone.trim() && hasEffectiveField('phone_number')) {
    const keyword = filters.phone.trim().toLowerCase();
    rows = rows.filter(user => user.profile?.phoneNumber?.toLowerCase().includes(keyword));
  }

  if (filters.email.trim() && hasEffectiveField('email')) {
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
  if (!selectedOrgTenantId.value) {
    selectedOrgTenantId.value = resolveDefaultOrgTenantId();
  }
  void loadHostUserProfileDictOptions()
    .then(options => {
      profileDictOptions.value = options;
    })
    .catch(() => {
      profileDictOptions.value = {
        [HOST_USER_PROFILE_DICT_CODES.accountType]: [],
        [HOST_USER_PROFILE_DICT_CODES.idCardType]: [],
        [HOST_USER_PROFILE_DICT_CODES.ethnicity]: [],
        [HOST_USER_PROFILE_DICT_CODES.educationLevel]: [],
        [HOST_USER_PROFILE_DICT_CODES.emergencyContactRelation]: []
      };
    });
  void load();
  void loadGridPreference();
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

watch(pagedUsers, users => {
  void hydrateRoleLabels(users);
});

watch(selectedOrgTenantId, (tenantId, previousTenantId) => {
  if (suppressOrgTenantReload || !tenantId || tenantId === previousTenantId) {
    return;
  }

  void reloadOrganizationReference();
});

watch(editorPrimaryUnitId, primaryUnitId => {
  if (!primaryUnitId) {
    return;
  }

  editorSubsidiaryUnitIds.value = editorSubsidiaryUnitIds.value.filter(
    unitId => unitId !== primaryUnitId
  );
});

watch(
  () => session.availableTenants.length,
  (count, previousCount) => {
    if (count === 0 || previousCount > 0) {
      return;
    }

    if (!selectedOrgTenantId.value) {
      selectedOrgTenantId.value = resolveDefaultOrgTenantId();
    }

    if (selectedOrgTenantId.value) {
      void reloadOrganizationReference();
    }
  }
);

watch(editorOpen, (open) => {
  if (!open) {
    resetEditorSubmitCheckpoint();
  }
});

function updateTableHeight(): void {
  const container = tableMainRef.value;
  if (!container) {
    return;
  }
  const top = container.getBoundingClientRect().top;
  tableHeight.value = Math.max(240, window.innerHeight - top - 68);
}

function resetEditorSubmitCheckpoint(): void {
  editorSubmitCheckpoint.value = null;
}

function profilePayloadForSubmit(): HostUserProfileWrite | undefined {
  if (!hasEditableProfileFields.value) {
    return undefined;
  }

  const fieldKeys = [...(editorProfile.value.fieldKeys ?? [])];
  let phoneNumber = editorProfile.value.phoneNumber ?? null;
  let idCardNumber = editorProfile.value.idCardNumber ?? null;
  if (isMaskedHostUserPhoneNumber(phoneNumber)) {
    phoneNumber = null;
    const index = fieldKeys.indexOf('phone_number');
    if (index >= 0) {
      fieldKeys.splice(index, 1);
    }
  }
  if (isMaskedHostUserIdCardNumber(idCardNumber)) {
    idCardNumber = null;
    const index = fieldKeys.indexOf('id_card_number');
    if (index >= 0) {
      fieldKeys.splice(index, 1);
    }
  }

  return {
    fieldKeys: fieldKeys.sort(),
    nickname: editorProfile.value.nickname ?? null,
    phoneNumber,
    email: editorProfile.value.email ?? null,
    employeeNumber: editorProfile.value.employeeNumber ?? null,
    gender: editorProfile.value.gender ?? null,
    joinDateUtc: editorProfile.value.joinDateUtc ?? null,
    sortOrder: editorProfile.value.sortOrder ?? null,
    idCardType: editorProfile.value.idCardType ?? null,
    idCardNumber,
    birthDate: editorProfile.value.birthDate ?? null,
    ethnicity: editorProfile.value.ethnicity ?? null,
    educationLevel: editorProfile.value.educationLevel ?? null,
    emergencyContactRelation: editorProfile.value.emergencyContactRelation ?? null,
    emergencyContact: editorProfile.value.emergencyContact ?? null,
    emergencyContactPhone: editorProfile.value.emergencyContactPhone ?? null,
    emergencyContactAddress: editorProfile.value.emergencyContactAddress ?? null,
    address: editorProfile.value.address ?? null,
    remark: editorProfile.value.remark ?? null,
    version: editorProfile.value.version ?? null
  };
}

async function revealProfileField(fieldKey: 'phone_number' | 'id_card_number'): Promise<void> {
  const user = editingUser.value;
  if (!user) {
    return;
  }

  revealingProfileField.value = fieldKey;
  try {
    const values = await revealHostUserProfileFields(user.id, [fieldKey]);
    const revealedValue = values[fieldKey] ?? null;
    if (fieldKey === 'phone_number') {
      editorProfile.value = { ...editorProfile.value, phoneNumber: revealedValue };
    } else {
      editorProfile.value = { ...editorProfile.value, idCardNumber: revealedValue };
    }
  } catch {
    ElMessage.error(t('users.revealSensitiveFieldFailed'));
  } finally {
    revealingProfileField.value = null;
  }
}

function buildIdentityCheckpointKey(): string {
  return JSON.stringify({
    mode: editorMode.value,
    displayName: editorDisplayName.value.trim(),
    accountType: editorAccountType.value,
    profile: profilePayloadForSubmit() ?? null
  });
}

function buildOrgCheckpointKey(): string {
  return JSON.stringify({
    tenantId: selectedOrgTenantId.value || null,
    primaryUnitId: editorPrimaryUnitId.value || null,
    subsidiaryUnitIds: [...editorSubsidiaryUnitIds.value].sort(),
    positionId: editorPositionId.value || null
  });
}

function buildRolesCheckpointKey(): string {
  return JSON.stringify([...selectedRoleIds.value].sort());
}

function emptyProfile(): HostUserProfileWrite {
  return {
    fieldKeys: [...editableProfileFieldKeys.value],
    nickname: null,
    phoneNumber: null,
    email: null,
    employeeNumber: null,
    gender: null,
    joinDateUtc: null,
    sortOrder: 100,
    idCardType: null,
    idCardNumber: null,
    birthDate: null,
    ethnicity: null,
    educationLevel: null,
    emergencyContactRelation: null,
    emergencyContact: null,
    emergencyContactPhone: null,
    emergencyContactAddress: null,
    address: null,
    remark: null,
    version: null
  };
}

function loadProfileFromUser(user: HostUser | null): void {
  if (!user?.profile || !hasEditableProfileFields.value) {
    editorProfile.value = emptyProfile();
    return;
  }
  editorProfile.value = {
    fieldKeys: [...editableProfileFieldKeys.value],
    nickname: shouldLoadProfileField('nickname', user) ? user.profile.nickname : null,
    phoneNumber: shouldLoadProfileField('phone_number', user) ? user.profile.phoneNumber : null,
    email: shouldLoadProfileField('email', user) ? user.profile.email : null,
    employeeNumber: shouldLoadProfileField('employee_number', user) ? user.profile.employeeNumber : null,
    gender: shouldLoadProfileField('gender', user) ? user.profile.gender : null,
    joinDateUtc: shouldLoadProfileField('join_date_utc', user) ? user.profile.joinDateUtc : null,
    sortOrder: shouldLoadProfileField('sort_order', user) ? user.profile.sortOrder : null,
    idCardType: shouldLoadProfileField('id_card_type', user) ? user.profile.idCardType : null,
    idCardNumber: shouldLoadProfileField('id_card_number', user) ? user.profile.idCardNumber : null,
    birthDate: shouldLoadProfileField('birth_date', user) ? user.profile.birthDate : null,
    ethnicity: shouldLoadProfileField('ethnicity', user) ? user.profile.ethnicity : null,
    educationLevel: shouldLoadProfileField('education_level', user) ? user.profile.educationLevel : null,
    emergencyContactRelation: shouldLoadProfileField('emergency_contact_relation', user)
      ? user.profile.emergencyContactRelation
      : null,
    emergencyContact: shouldLoadProfileField('emergency_contact', user)
      ? user.profile.emergencyContact
      : null,
    emergencyContactPhone: shouldLoadProfileField('emergency_contact_phone', user)
      ? user.profile.emergencyContactPhone
      : null,
    emergencyContactAddress: shouldLoadProfileField('emergency_contact_address', user)
      ? user.profile.emergencyContactAddress
      : null,
    address: shouldLoadProfileField('address', user) ? user.profile.address : null,
    remark: shouldLoadProfileField('remark', user) ? user.profile.remark : null,
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
  suppressOrgTenantReload = true;
  try {
    if (!selectedOrgTenantId.value) {
      selectedOrgTenantId.value = resolveDefaultOrgTenantId();
    }

    const users = await fetchAllUsers();
    const rolePage = await listHostRoles(1, 200).catch(() => ({
      items: [] as HostRole[],
      page: 1,
      pageSize: 200,
      total: 0
    }));

    roles.value = rolePage.items;

    let orgLoadFailed = false;
    if (selectedOrgTenantId.value && canManageOrganizations.value) {
      try {
        const orgReference = await loadOrganizationReference(selectedOrgTenantId.value);
        orgUnits.value = orgReference.orgUnits;
        orgPositions.value = orgReference.orgPositions;
        userUnits.value = orgReference.userUnits;
        userPositions.value = orgReference.userPositions;
      } catch {
        orgLoadFailed = true;
        orgUnits.value = [];
        orgPositions.value = [];
        userUnits.value = [];
        userPositions.value = [];
      }
    } else {
      orgUnits.value = [];
      orgPositions.value = [];
      userUnits.value = [];
      userPositions.value = [];
    }

    userRoleLabelsById.value = {};
    allUsers.value = enrichUsers(users);
    if (orgLoadFailed) {
      ElMessage.warning(t('users.orgReferenceLoadFailed'));
    }
    total.value = filteredUsers.value.length;
    await hydrateRoleLabels(pagedUsers.value);
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.loadFailed');
  } finally {
    suppressOrgTenantReload = false;
    loading.value = false;
  }
}

async function reloadOrganizationReference(): Promise<void> {
  if (!selectedOrgTenantId.value || !canManageOrganizations.value) {
    orgUnits.value = [];
    orgPositions.value = [];
    userUnits.value = [];
    userPositions.value = [];
    allUsers.value = enrichUsers(allUsers.value);
    return;
  }

  try {
    const orgReference = await loadOrganizationReference(selectedOrgTenantId.value);
    orgUnits.value = orgReference.orgUnits;
    orgPositions.value = orgReference.orgPositions;
    userUnits.value = orgReference.userUnits;
    userPositions.value = orgReference.userPositions;
    allUsers.value = enrichUsers(allUsers.value);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.loadFailed');
  }
}

async function loadOrganizationReference(tenantId: string): Promise<{
  orgUnits: OrganizationUnit[];
  orgPositions: OrganizationPosition[];
  userUnits: OrganizationUserUnit[];
  userPositions: OrganizationUserPosition[];
}> {
  const reference = await getHostUserOrganizationReference(tenantId);
  return {
    orgUnits: reference.units,
    orgPositions: reference.positions,
    userUnits: reference.userUnits,
    userPositions: reference.userPositions
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
    roleLabels: userRoleLabelsById.value[user.id] ?? '',
    orgLabel: primaryOrgMap.get(user.id) ?? t('users.unassignedOrg'),
    positionLabel: primaryPositionMap.get(user.id) ?? t('users.fieldEmpty')
  }));
}

async function hydrateRoleLabels(users: HostUser[]): Promise<void> {
  const pending = users.filter(user => userRoleLabelsById.value[user.id] === undefined);
  if (pending.length === 0) {
    return;
  }

  const roleNameById = new Map(roles.value.map(role => [role.id, role.name]));
  const requestId = ++roleLabelsRequestId;
  const entries = await Promise.all(
    pending.map(async user => {
      try {
        const response = await getHostUserRoles(user.id);
        const labels = response.roleIds
          .map(roleId => roleNameById.get(roleId) ?? roleId)
          .join('、');
        return [user.id, labels || t('users.noRoles')] as const;
      } catch {
        return [user.id, t('users.fieldEmpty')] as const;
      }
    })
  );

  if (requestId !== roleLabelsRequestId) {
    return;
  }

  const next = { ...userRoleLabelsById.value };
  for (const [userId, labels] of entries) {
    next[userId] = labels;
  }
  userRoleLabelsById.value = next;
  allUsers.value = allUsers.value.map(user => ({
    ...user,
    roleLabels: next[user.id] ?? user.roleLabels
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
  resetEditorSubmitCheckpoint();
  editorMode.value = 'create';
  editorTab.value = 'basic';
  editingUser.value = null;
  editorUsername.value = '';
  editorDisplayName.value = '';
  editorAccountType.value = 'normal_user';
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

  const freshUser = allUsers.value.find(item => item.id === user.id) ?? user;

  resetEditorSubmitCheckpoint();
  editorMode.value = 'edit';
  editorTab.value = tab;
  editingUser.value = freshUser;
  editorUsername.value = freshUser.username;
  editorDisplayName.value = freshUser.displayName;
  editorAccountType.value = freshUser.accountType;
  editorPassword.value = '';
  loadProfileFromUser(freshUser);
  loadOrgFromUser(freshUser);

  if (canAssignRoles.value) {
    changing.value = true;
    try {
      const userRoles = await getHostUserRoles(freshUser.id);
      selectedRoleIds.value = [...userRoles.roleIds];
      rolesVersion.value = userRoles.version;
    } catch (error: unknown) {
      problem.value = toProblem(error, 'users.operationFailed');
      return;
    } finally {
      changing.value = false;
    }
  }

  loadProfileFromUser(editingUser.value);
  await nextTick();
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

  if (editorMode.value === 'edit' && editorTab.value === 'org-units') {
    await saveOrgUnits();
    return;
  }

  if (editorMode.value === 'edit' && editorTab.value === 'org-positions') {
    await saveOrgPositions();
    return;
  }

  if (editorMode.value === 'create') {
    await createUser();
    return;
  }

  await updateUser();
}

async function syncOrgAssignments(userId: string): Promise<void> {
  if (!selectedOrgTenantId.value) {
    return;
  }

  await syncUserUnitAssignments(userId);
  await syncUserPositionAssignments(userId);
  await reloadOrganizationReference();
}

async function syncUserUnitAssignments(userId: string): Promise<void> {
  if (!selectedOrgTenantId.value || !canMutateHostOrgUnits.value) {
    return;
  }

  const tenantId = selectedOrgTenantId.value;
  const canCreateAssignments = canCreateUserUnits.value || canAssignHostOrgFromDirectory.value;
  const canUpdateAssignments = canUpdateUserUnits.value || canAssignHostOrgFromDirectory.value;
  const canDisableAssignments = canDisableUserUnits.value || canAssignHostOrgFromDirectory.value;
  const existingUnits = userUnits.value.filter(
    item => item.userId === userId && item.isActive
  );
  const desiredPrimary = editorPrimaryUnitId.value || null;
  const desiredSubsidiary = new Set(
    editorSubsidiaryUnitIds.value.filter(unitId => unitId !== desiredPrimary)
  );

  if (desiredPrimary) {
    const primaryAssignment = existingUnits.find(item => item.unitId === desiredPrimary);
    if (primaryAssignment) {
      if (!primaryAssignment.isPrimary && canUpdateAssignments) {
        await updateHostUserOrganizationUnit(
          tenantId,
          primaryAssignment.id,
          true,
          primaryAssignment.version
        );
      }
    } else if (canCreateAssignments) {
      await createHostUserOrganizationUnit(tenantId, userId, desiredPrimary, true);
    }
  }

  if (canUpdateAssignments || canDisableAssignments) {
    for (const assignment of existingUnits.filter(item => item.isPrimary)) {
      if (assignment.unitId === desiredPrimary) {
        continue;
      }

      if (desiredSubsidiary.has(assignment.unitId) && canUpdateAssignments) {
        await updateHostUserOrganizationUnit(
          tenantId,
          assignment.id,
          false,
          assignment.version
        );
        continue;
      }

      if (canDisableAssignments) {
        await disableHostUserOrganizationUnit(tenantId, assignment.id);
      }
    }

    for (const assignment of existingUnits.filter(item => !item.isPrimary)) {
      if (assignment.unitId === desiredPrimary || desiredSubsidiary.has(assignment.unitId)) {
        continue;
      }

      if (canDisableAssignments) {
        await disableHostUserOrganizationUnit(tenantId, assignment.id);
      }
    }
  }

  if (canCreateAssignments) {
    for (const unitId of desiredSubsidiary) {
      if (existingUnits.some(item => item.unitId === unitId)) {
        continue;
      }

      await createHostUserOrganizationUnit(tenantId, userId, unitId, false);
    }
  }
}

async function syncUserPositionAssignments(userId: string): Promise<void> {
  if (!selectedOrgTenantId.value || !canManageUserPositions.value) {
    return;
  }

  const tenantId = selectedOrgTenantId.value;
  const canCreateAssignments = canCreateUserPositions.value || canAssignHostOrgFromDirectory.value;
  const canUpdateAssignments = canUpdateUserPositions.value || canAssignHostOrgFromDirectory.value;
  const canDisableAssignments = canDisableUserPositions.value || canAssignHostOrgFromDirectory.value;
  const existingPositions = userPositions.value.filter(
    item => item.userId === userId && item.isActive
  );
  const desiredPositionId = editorPositionId.value || null;
  const desiredPosition = desiredPositionId
    ? existingPositions.find(item => item.positionId === desiredPositionId)
    : undefined;

  let desiredPositionApplied = !desiredPositionId;
  if (desiredPositionId) {
    if (desiredPosition?.isPrimary) {
      desiredPositionApplied = true;
    } else if (desiredPosition) {
      if (canUpdateAssignments) {
        await updateHostUserOrganizationPosition(
          tenantId,
          desiredPosition.id,
          true,
          desiredPosition.version
        );
        desiredPositionApplied = true;
      }
    } else if (canCreateAssignments) {
      await createHostUserOrganizationPosition(
        tenantId,
        userId,
        desiredPositionId,
        true
      );
      desiredPositionApplied = true;
    }
  }

  if (desiredPositionApplied && canDisableAssignments) {
    for (const assignment of existingPositions) {
      if (desiredPositionId && assignment.positionId === desiredPositionId) {
        continue;
      }

      await disableHostUserOrganizationPosition(tenantId, assignment.id);
    }
  }
}

async function ensureIdentitySaved(): Promise<HostUser> {
  const identityKey = buildIdentityCheckpointKey();
  const checkpoint = editorSubmitCheckpoint.value;
  if (checkpoint && checkpoint.identityKey === identityKey) {
    return checkpoint.user;
  }

  const profile = profilePayloadForSubmit();
  let savedUser: HostUser;
  if (checkpoint?.user) {
    savedUser = await updateHostUser(
      checkpoint.user.id,
      editorDisplayName.value.trim(),
      checkpoint.user.version,
      profile,
      editorAccountType.value
    );
  } else if (editorMode.value === 'create') {
    savedUser = await createHostUser(
      editorUsername.value.trim(),
      editorDisplayName.value.trim(),
      editorPassword.value,
      profile,
      editorAccountType.value
    );
  } else {
    const user = editingUser.value;
    if (!user) {
      throw new Error('client.host_user_missing');
    }

    savedUser = await updateHostUser(
      user.id,
      editorDisplayName.value.trim(),
      user.version,
      profile,
      editorAccountType.value
    );
  }

  editorSubmitCheckpoint.value = {
    user: savedUser,
    identityKey,
    orgKey: null,
    rolesKey: null
  };

  if (editorMode.value === 'edit') {
    editingUser.value = savedUser;
  }

  return savedUser;
}

async function ensureOrgAssignmentsSaved(userId: string): Promise<void> {
  const checkpoint = editorSubmitCheckpoint.value;
  if (!checkpoint) {
    return;
  }

  const orgKey = buildOrgCheckpointKey();
  if (checkpoint.orgKey === orgKey) {
    return;
  }

  await syncOrgAssignments(userId);
  editorSubmitCheckpoint.value = {
    ...checkpoint,
    orgKey
  };
}

async function ensureRolesSaved(userId: string): Promise<void> {
  const checkpoint = editorSubmitCheckpoint.value;
  if (!checkpoint) {
    return;
  }

  const rolesKey = buildRolesCheckpointKey();
  if (checkpoint.rolesKey === rolesKey) {
    return;
  }

  await syncRoles(userId);
  editorSubmitCheckpoint.value = {
    ...checkpoint,
    rolesKey
  };
}

async function syncRoles(userId: string): Promise<void> {
  if (!canAssignRoles.value) {
    return;
  }

  const nextRoleIds = [...selectedRoleIds.value].sort();
  // 创建场景：未勾选角色则无需写入；编辑场景：角色集合未变不得调用替换，否则会轮换 SecurityStamp 并吊销全部会话。
  const userRoles = await getHostUserRoles(userId);
  if (sameSortedIds(nextRoleIds, userRoles.roleIds)) {
    return;
  }

  await replaceHostUserRoles(userId, nextRoleIds, userRoles.version);
}

function sameSortedIds(left: readonly string[], right: readonly string[]): boolean {
  const sortedRight = [...right].sort();
  return left.length === sortedRight.length
    && left.every((id, index) => id === sortedRight[index]);
}

async function createUser(): Promise<void> {
  if (!editorUsername.value.trim() || !editorDisplayName.value.trim() || !editorPassword.value) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    const created = await ensureIdentitySaved();
    await ensureOrgAssignmentsSaved(created.id);
    await ensureRolesSaved(created.id);
    editorOpen.value = false;
    ElMessage.success(t('users.createSuccess'));
    await load();
  } catch (error: unknown) {
    const pendingTab = resolvePendingEditorTab();
    if (pendingTab) {
      editorTab.value = pendingTab;
    }
    problem.value = toSubmitProblem(error);
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
    const updatedUser = await ensureIdentitySaved();
    await ensureOrgAssignmentsSaved(updatedUser.id);
    if (canAssignRoles.value) {
      await ensureRolesSaved(updatedUser.id);
    }
    editorOpen.value = false;
    ElMessage.success(t('users.updateSuccess'));
    await load();
  } catch (error: unknown) {
    const pendingTab = resolvePendingEditorTab();
    if (pendingTab) {
      editorTab.value = pendingTab;
    }
    problem.value = toSubmitProblem(error);
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
    const nextRoleIds = [...selectedRoleIds.value].sort();
    const userRoles = await getHostUserRoles(user.id);
    // 角色未变时跳过替换 API，避免无意义吊销目标用户全部会话。
    if (!sameSortedIds(nextRoleIds, userRoles.roleIds)) {
      await replaceHostUserRoles(user.id, nextRoleIds, userRoles.version);
    }
    editorOpen.value = false;
    ElMessage.success(t('users.rolesSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveOrgUnits(): Promise<void> {
  const user = editingUser.value;
  if (!user) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    await syncUserUnitAssignments(user.id);
    await reloadOrganizationReference();
    editorOpen.value = false;
    ElMessage.success(t('users.orgUnitsSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveOrgPositions(): Promise<void> {
  const user = editingUser.value;
  if (!user) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    await syncUserPositionAssignments(user.id);
    await reloadOrganizationReference();
    editorOpen.value = false;
    ElMessage.success(t('users.positionsSuccess'));
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

async function retire(user: HostUser): Promise<void> {
  if (changing.value || !user.isActive) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('users.confirmRetire', { name: user.username }),
      t('users.retire'),
      {
        type: 'warning',
        confirmButtonText: t('users.retire'),
        cancelButtonText: t('hostDocumentItems.cancel')
      }
    );
    changing.value = true;
    await retireHostUser(user.id);
    ElMessage.success(t('users.retireSuccess'));
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

async function unlockLogin(user: HostUser): Promise<void> {
  if (changing.value || !user.isActive || !isUserLoginLocked(user)) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('users.confirmUnlockLogin', { name: user.username }),
      t('users.unlockLogin'),
      {
        type: 'warning',
        confirmButtonText: t('users.unlockLogin'),
        cancelButtonText: t('hostDocumentItems.cancel')
      }
    );
    changing.value = true;
    await unlockHostUserLogin(user.id);
    ElMessage.success(t('users.unlockLoginSuccess'));
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

function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
}

async function exportUsers(): Promise<void> {
  if (changing.value) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    downloadBlob(await exportHostUsersWorkbook(), 'host-users.xlsx');
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

const importFileInput = ref<HTMLInputElement>();

function importUsers(): void {
  if (changing.value) {
    return;
  }

  importFileInput.value?.click();
}

async function downloadImportTemplate(): Promise<void> {
  if (changing.value) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    downloadBlob(
      await downloadHostUserImportTemplate(),
      'host-users-import-template.xlsx'
    );
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function onImportFileChange(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = '';
  if (!file) {
    return;
  }

  if (!file.name.toLowerCase().endsWith('.xlsx') || file.size > maximumImportWorkbookBytes) {
    problem.value = toProblem(
      new Error('client.invalid_host_user_import_workbook'),
      'users.importWorkbookInvalid'
    );
    return;
  }

  changing.value = true;
  problem.value = undefined;
  importResults.value = [];
  try {
    const result = await importHostUsersWorkbook(file);
    importResults.value = result.results.slice(0, 1_000);
    ElMessage.success(t('users.importSuccess', { count: result.succeededCount }));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

function onUserSelectionChange(rows: HostUser[]): void {
  selectedUsers.value = rows;
}

async function batchDisableSelected(): Promise<void> {
  if (changing.value) {
    return;
  }
  const userIds = selectedUsers.value
    .filter(user => user.isActive)
    .map(user => user.id);
  if (userIds.length === 0) {
    ElMessage.warning(t('users.batchEmpty'));
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('users.batchDisable'),
      t('users.disable'),
      {
        type: 'warning',
        confirmButtonText: t('users.batchDisable'),
        cancelButtonText: t('hostDocumentItems.cancel')
      }
    );
    changing.value = true;
    const result = await batchDisableHostUsers(userIds);
    ElMessage.success(t('users.batchSuccess', { count: result.succeededCount }));
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

async function batchEnableSelected(): Promise<void> {
  if (changing.value) {
    return;
  }
  const userIds = selectedUsers.value
    .filter(user => !user.isActive)
    .map(user => user.id);
  if (userIds.length === 0) {
    ElMessage.warning(t('users.batchEmpty'));
    return;
  }

  try {
    await ElMessageBox.confirm(
      t('users.batchEnable'),
      t('users.enable'),
      {
        confirmButtonText: t('users.batchEnable'),
        cancelButtonText: t('hostDocumentItems.cancel')
      }
    );
    changing.value = true;
    const result = await batchEnableHostUsers(userIds);
    ElMessage.success(t('users.batchSuccess', { count: result.succeededCount }));
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

function avatarText(user: HostUser): string {
  return (hasEffectiveField('nickname', user) ? user.profile?.nickname?.slice(0, 1) : undefined)
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

function profileText(value: string | number | null | undefined): string {
  return String(value ?? '').trim() || t('users.fieldEmpty');
}

function accountTypeLabel(user: HostUser): string {
  const option = profileDictOptions.value[HOST_USER_PROFILE_DICT_CODES.accountType]
    ?.find(item => item.value === user.accountType);
  return option?.label ?? user.accountType;
}

function sortOrderText(value: number | null | undefined): string {
  if (value === null || value === undefined) {
    return t('users.fieldEmpty');
  }
  return String(value);
}

function userSubtitle(user: HostUser): string {
  return (hasEffectiveField('email', user) ? user.profile?.email?.trim() : '')
    || user.displayName;
}

function toProblem(
  error: unknown,
  fallbackKey:
    | 'users.loadFailed'
    | 'users.operationFailed'
    | 'users.importWorkbookInvalid'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_user_failed', title: t(fallbackKey) };
}

function resolvePendingEditorTab(): EditorTab | null {
  const checkpoint = editorSubmitCheckpoint.value;
  if (!checkpoint) {
    return null;
  }

  if (checkpoint.orgKey === null) {
    return canViewUserUnits.value ? 'org-units' : 'org-positions';
  }

  if (checkpoint.rolesKey === null) {
    return 'roles';
  }

  return null;
}

function toSubmitProblem(error: unknown): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }

  const checkpoint = editorSubmitCheckpoint.value;
  if (checkpoint) {
    if (checkpoint.orgKey === null) {
      return {
        status: 500,
        code: 'client.host_user_org_sync_pending',
        title: t('users.orgSyncPending')
      };
    }

    if (checkpoint.rolesKey === null) {
      return {
        status: 500,
        code: 'client.host_user_roles_sync_pending',
        title: t('users.rolesSyncPending')
      };
    }
  }

  return toProblem(error, 'users.operationFailed');
}
</script>

<template>
  <section class="users-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('users.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <div
        v-if="submitProgressSteps.length > 0"
        class="users-submit-progress"
        data-testid="users-submit-progress"
      >
        <div class="users-submit-progress__title">{{ t('users.submitProgress') }}</div>
        <ul class="users-submit-progress__list">
          <li
            v-for="step in submitProgressSteps"
            :key="step.label"
            class="users-submit-progress__item"
          >
            <span>{{ step.label }}</span>
            <strong>{{ t(step.status === 'completed' ? 'users.stepCompleted' : 'users.stepPending') }}</strong>
          </li>
        </ul>
      </div>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card
      v-if="importResults.length > 0"
      class="art-table-card"
      shadow="never"
      data-testid="users-import-results"
    >
      <template #header>{{ t('users.importResults') }}</template>
      <el-table :data="importResults" size="small">
        <el-table-column prop="line" :label="t('users.importResultLine')" width="88" />
        <el-table-column :label="t('users.importResultStatus')" width="120">
          <template #default="{ row }">
            <el-tag :type="row.succeeded ? 'success' : 'danger'">
              {{ t(row.succeeded ? 'users.importResultSucceeded' : 'users.importResultFailed') }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="errorCode" :label="t('users.importResultCode')" min-width="200" />
        <el-table-column prop="message" :label="t('users.importResultMessage')" min-width="280" />
      </el-table>
    </el-card>

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
          <el-select
            v-if="orgTenantOptions.length > 0"
            v-model="selectedOrgTenantId"
            class="users-org-panel__tenant"
            filterable
            :placeholder="t('users.orgTenantPlaceholder')"
          >
            <el-option
              v-for="option in orgTenantOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
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
          <p v-if="!selectedOrgTenantId" class="users-org-panel__empty">
            {{ t('users.orgTreeEmptyNoTenant') }}
          </p>
          <p v-else-if="!hasVisibleOrgUnits" class="users-org-panel__empty">
            {{ t('users.orgTreeEmpty') }}
          </p>
        </aside>

        <div ref="tableMainRef" class="users-table-main">
          <ArtTableHeader
            v-model:table-size="tableSize"
            v-model:zebra="tableZebra"
            v-model:border="tableBorder"
            v-model:header-background="tableHeaderBackground"
            :loading="loading"
            full-class="users-table-main"
            layout="refresh,size,fullscreen,settings"
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
              <PermissionGate code="identity.users.import">
                <el-button
                  data-testid="users-action-import-template"
                  plain
                  :disabled="changing"
                  @click="downloadImportTemplate"
                >
                  {{ t('users.importTemplate') }}
                </el-button>
                <el-button
                  data-testid="users-action-import"
                  plain
                  :disabled="changing"
                  @click="importUsers"
                >
                  {{ t('users.import') }}
                </el-button>
                <input
                  ref="importFileInput"
                  class="users-import-file-input"
                  data-testid="users-import-file-input"
                  type="file"
                  accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                  @change="onImportFileChange"
                >
              </PermissionGate>
              <PermissionGate code="identity.users.disable">
                <el-button
                  data-testid="users-action-batch-disable"
                  plain
                  :disabled="changing"
                  @click="batchDisableSelected"
                >
                  {{ t('users.batchDisable') }}
                </el-button>
              </PermissionGate>
              <PermissionGate code="identity.users.enable">
                <el-button
                  data-testid="users-action-batch-enable"
                  plain
                  :disabled="changing"
                  @click="batchEnableSelected"
                >
                  {{ t('users.batchEnable') }}
                </el-button>
              </PermissionGate>
            </template>
            <template #right>
              <el-button
                data-testid="users-action-column-editor"
                @click="columnEditorOpen = true"
              >
                {{ t('table.columns') }}
              </el-button>
            </template>
          </ArtTableHeader>

          <ArtTableColumnEditor
            v-model:open="columnEditorOpen"
            :columns="columnEditorItems"
            :saving="gridPreferenceSaving"
            :resetting="gridPreferenceResetting"
            @save="saveColumnEditor"
            @reset="resetColumnEditor"
          />

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
              @selection-change="onUserSelectionChange"
            >
              <el-table-column type="selection" width="48" />
              <el-table-column :label="t('users.columnIndex')" width="72" align="center">
                <template #default="{ $index }">
                  {{ rowIndex($index) }}
                </template>
              </el-table-column>

              <!-- 中文注释：Element Plus 默认将 el-table 的 slot row 推断为 DefaultRow；用单层 as UserRow 断言保持类型安全（禁止 as unknown as HostUser 双重断言） -->
              <el-table-column :label="t('users.username')" min-width="220">
                <template #default="{ row }">
                  <div class="users-table-user">
                  <span class="users-table-user__avatar">{{ avatarText(row as UserRow) }}</span>
                    <div>
                      <div class="users-table-user__name" translate="no">{{ row.username }}</div>
                  <div class="users-table-user__sub" translate="no">{{ userSubtitle(row as UserRow) }}</div>
                    </div>
                  </div>
                </template>
              </el-table-column>

              <UsersGridDataColumn
                v-for="column in visibleGridColumns"
                :key="column.key"
                :column="column"
                :gender-label="genderLabel"
                :profile-text="profileText"
                :sort-order-text="sortOrderText"
                :account-type-label="accountTypeLabel"
                :format-date="formatDate"
                :empty-label="t('users.fieldEmpty')"
              />

              <el-table-column :label="t('users.status')" width="100" align="center">
                <template #default="{ row }">
                  <el-tag size="small" :type="row.isActive ? 'success' : 'info'">
                    {{ t(row.isActive ? 'users.active' : 'users.inactive') }}
                  </el-tag>
                </template>
              </el-table-column>

              <!-- 中文注释：UserRow extends HostUser，操作列参数 row 直接传递给接受 HostUser 的函数即可 -->
              <el-table-column
                :label="t('users.columnActions')"
                :width="ART_TABLE_ACTION_COLUMN_WIDTH"
                fixed="right"
                align="center"
              >
                <template #default="{ row }">
                  <ArtTableActionGroup
                    :key="`${(row as UserRow).id}:${(row as UserRow).isActive}`"
                    :max-visible="8"
                  >
                    <PermissionGate code="identity.users.update">
                      <ArtTableActionButton
                        type="edit"
                        test-id="users-action-edit"
                        :title="t('users.edit')"
                  @click="openEdit(row as UserRow)"
                      />
                    </PermissionGate>
                    <PermissionGate code="identity.users.assign_roles">
                      <ArtTableActionButton
                        type="roles"
                        test-id="users-action-roles"
                        :title="t('users.roles')"
                  @click="openEdit(row as UserRow, 'roles')"
                      />
                    </PermissionGate>
                    <ArtTableActionButton
                      v-if="canViewUserUnits"
                      type="org"
                      test-id="users-action-org-units"
                      :title="t('users.assignOrgUnits')"
                  @click="openEdit(row as UserRow, 'org-units')"
                    />
                    <ArtTableActionButton
                      v-if="canViewUserPositions"
                      type="position"
                      test-id="users-action-org-positions"
                      :title="t('users.assignPositions')"
                  @click="openEdit(row as UserRow, 'org-positions')"
                    />
                    <PermissionGate v-if="row.isActive" code="identity.users.reset_password">
                      <ArtTableActionButton
                        type="password"
                        test-id="users-action-reset-password"
                        :title="t('users.resetPassword')"
                  @click="resetPassword(row as UserRow)"
                      />
                    </PermissionGate>
                    <PermissionGate
                      v-if="row.isActive && isUserLoginLocked(row as UserRow)"
                      code="identity.users.unlock_login"
                    >
                      <el-button
                        link
                        type="warning"
                        data-testid="users-action-unlock-login"
                        :title="t('users.unlockLogin')"
                        @click="unlockLogin(row as UserRow)"
                      >
                        {{ t('users.unlockLogin') }}
                      </el-button>
                    </PermissionGate>
                    <PermissionGate
                      v-if="row.isActive"
                      key="disable-user"
                      code="identity.users.disable"
                    >
                      <ArtTableActionButton
                        type="delete"
                        test-id="users-action-disable"
                        :title="t('users.disable')"
                  @click="disable(row as UserRow)"
                      />
                    </PermissionGate>
                    <PermissionGate
                      v-if="row.isActive"
                      key="retire-user"
                      code="identity.users.retire"
                    >
                      <el-button
                        link
                        type="danger"
                        data-testid="users-action-retire"
                        :title="t('users.retire')"
                        @click="retire(row as UserRow)"
                      >
                        {{ t('users.retire') }}
                      </el-button>
                    </PermissionGate>
                    <PermissionGate
                      v-if="!row.isActive"
                      key="enable-user"
                      code="identity.users.enable"
                    >
                      <el-button
                        link
                        type="success"
                        data-testid="users-action-enable"
                  @click="enable(row as UserRow)"
                      >
                        {{ t('users.enable') }}
                      </el-button>
                    </PermissionGate>
                  </ArtTableActionGroup>
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
      :account-type="editorAccountType"
      :password="editorPassword"
      :profile="editorProfile"
      :active-tab="editorTab"
      :transfer-roles="transferRoles"
      :selected-role-ids="selectedRoleIds"
      :org-unit-tree-options="orgUnitTreeOptions"
      :position-options="positionOptions"
      :primary-unit-id="editorPrimaryUnitId"
      :subsidiary-unit-ids="editorSubsidiaryUnitIds"
      :position-id="editorPositionId"
      :identity-committed="editorMode === 'create' && editorSubmitCheckpoint !== null"
      :saving="changing"
      :can-assign-roles="canAssignRoles"
      :can-create="canCreate"
      :can-update="canUpdate"
      :can-manage-user-units="canManageUserUnits"
      :can-manage-user-positions="canManageUserPositions"
      :can-view-user-units="canViewUserUnits"
      :can-view-user-positions="canViewUserPositions"
      :account-type-options="profileDictOptions[HOST_USER_PROFILE_DICT_CODES_EXPORT.accountType]"
      :id-card-type-options="profileDictOptions[HOST_USER_PROFILE_DICT_CODES_EXPORT.idCardType]"
      :ethnicity-options="profileDictOptions[HOST_USER_PROFILE_DICT_CODES_EXPORT.ethnicity]"
      :education-level-options="profileDictOptions[HOST_USER_PROFILE_DICT_CODES_EXPORT.educationLevel]"
      :emergency-contact-relation-options="profileDictOptions[HOST_USER_PROFILE_DICT_CODES_EXPORT.emergencyContactRelation]"
      :can-submit="canSubmitEditor"
      :effective-field-keys="editorEffectiveFieldKeys"
      :show-profile-tab="hasProfileTabFields"
      :can-reveal-phone-number="canRevealPhoneNumber"
      :can-reveal-id-card-number="canRevealIdCardNumber"
      :revealing-field-key="revealingProfileField"
      :translate="t"
      @update:username="editorUsername = $event"
      @update:display-name="editorDisplayName = $event"
      @update:account-type="editorAccountType = $event"
      @update:password="editorPassword = $event"
      @update:profile="editorProfile = $event"
      @update:active-tab="editorTab = $event"
      @update:selected-role-ids="selectedRoleIds = $event"
      @update:primary-unit-id="editorPrimaryUnitId = $event"
      @update:subsidiary-unit-ids="editorSubsidiaryUnitIds = $event"
      @update:position-id="editorPositionId = $event"
      @reveal-profile-field="revealProfileField"
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
  gap: 8px;
}

.users-org-panel__tenant {
  width: 100%;
}

.users-org-panel__tree {
  flex: 1;
  min-height: 0;
  overflow: auto;
}

.users-table-main {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.users-import-file-input {
  display: none;
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

.users-submit-progress {
  margin-top: 8px;
}

.users-submit-progress__title {
  font-size: 13px;
  font-weight: 600;
}

.users-submit-progress__list {
  margin: 6px 0 0;
  padding-left: 18px;
}

.users-submit-progress__item {
  display: list-item;
  margin: 2px 0;
}

.users-submit-progress__item strong {
  margin-left: 8px;
}
</style>
