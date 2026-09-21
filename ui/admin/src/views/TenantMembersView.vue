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
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTabs,
  ElTabPane,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, TenantInvitation, TenantMember } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { isIdentityPasswordValid } from '../auth/identity-password-policy';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createTenantInvitation,
  listTenantInvitations,
  listTenantMembers,
  provisionTenantMember,
  removeTenantMember,
  revokeTenantInvitation,
  updateTenantMember
} from '../api/tenant-members';

defineOptions({ name: 'TenantMembersView' });

const MEMBER_ROLES = ['Admin', 'Member'] as const;
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/u;

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const inTenantContext = computed(() => Boolean(session.currentUser?.tenantId));
const members = ref<TenantMember[]>([]);
const invitations = ref<TenantInvitation[]>([]);
const memberTotal = ref(0);
const invitationTotal = ref(0);
const memberPage = ref(1);
const invitationPage = ref(1);
const pageSize = ref(20);
const loadingMembers = ref(false);
const loadingInvitations = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const memberSearchForm = ref<Record<string, string | undefined>>({});
const invitationSearchForm = ref<Record<string, string | undefined>>({});
const appliedMemberStatus = ref('');
const appliedInvitationStatus = ref('');

const inviteOpen = ref(false);
const provisionOpen = ref(false);
const editOpen = ref(false);
const inviteFormRef = ref<FormInstance>();
const editFormRef = ref<FormInstance>();
const editingMember = ref<TenantMember | null>(null);
const invitationToken = ref('');
const activeTab = ref<'members' | 'invitations'>('members');

const inviteForm = reactive({
  targetEmail: '',
  memberRole: 'Member',
  expiresInHours: '72'
});
const provisionForm = reactive({
  username: '',
  displayName: '',
  password: '',
  memberRole: 'Member',
  email: ''
});
const provisionFieldErrors = reactive({
  username: '',
  displayName: '',
  password: '',
  memberRole: '',
  email: ''
});
const editForm = reactive({
  memberRole: 'Member'
});
const inviteFieldErrors = reactive({
  targetEmail: '',
  memberRole: '',
  expiresInHours: ''
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

const {
  tableMainRef: invitationTableMainRef,
  tableHeight: invitationTableHeight,
  updateTableHeight: updateInvitationTableHeight,
  watchLoading: watchInvitationLoading
} = useArtCrudTableLayout();

watchLoading(loadingMembers);
watchInvitationLoading(loadingInvitations);

const memberStatusOptions = computed(() => [
  { label: t('tenantMembers.filterStatusAll'), value: '' },
  { label: t('tenantMembers.statusPending'), value: 'Pending' },
  { label: t('tenantMembers.statusActive'), value: 'Active' },
  { label: t('tenantMembers.statusSuspended'), value: 'Suspended' },
  { label: t('tenantMembers.statusRemoved'), value: 'Removed' }
]);

const invitationStatusOptions = computed(() => [
  { label: t('tenantMembers.filterStatusAll'), value: '' },
  { label: t('tenantMembers.invitationPending'), value: 'Pending' },
  { label: t('tenantMembers.invitationAccepted'), value: 'Accepted' },
  { label: t('tenantMembers.invitationRevoked'), value: 'Revoked' },
  { label: t('tenantMembers.invitationExpired'), value: 'Expired' }
]);

const assignableRoleOptions = computed(() => [
  { label: t('tenantMembers.roleAdmin'), value: 'Admin' },
  { label: t('tenantMembers.roleMember'), value: 'Member' }
]);

const memberSearchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'status',
    label: t('tenantMembers.fieldStatus'),
    type: 'select',
    placeholder: t('tenantMembers.filterStatusAll'),
    options: memberStatusOptions.value.filter(item => item.value !== '')
  }
]);

const invitationSearchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'status',
    label: t('tenantMembers.fieldStatus'),
    type: 'select',
    placeholder: t('tenantMembers.filterStatusAll'),
    options: invitationStatusOptions.value.filter(item => item.value !== '')
  }
]);

function memberRowIndex(index: number) {
  return (memberPage.value - 1) * pageSize.value + index + 1;
}

function invitationRowIndex(index: number) {
  return (invitationPage.value - 1) * pageSize.value + index + 1;
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short'
  }).format(new Date(value));
}

function memberRoleLabel(role: string): string {
  if (role === 'Owner') {
    return t('tenantMembers.roleOwner');
  }
  if (role === 'Admin') {
    return t('tenantMembers.roleAdmin');
  }
  if (role === 'Member') {
    return t('tenantMembers.roleMember');
  }
  return role;
}

function memberStatusLabel(status: string): string {
  const map: Record<string, string> = {
    Pending: t('tenantMembers.statusPending'),
    Active: t('tenantMembers.statusActive'),
    Suspended: t('tenantMembers.statusSuspended'),
    Removed: t('tenantMembers.statusRemoved')
  };
  return map[status] ?? status;
}

function memberStatusTagType(status: string): 'success' | 'warning' | 'info' | 'danger' {
  if (status === 'Active') {
    return 'success';
  }
  if (status === 'Pending') {
    return 'warning';
  }
  if (status === 'Removed') {
    return 'info';
  }
  return 'danger';
}

function invitationStatusLabel(status: string): string {
  const map: Record<string, string> = {
    Pending: t('tenantMembers.invitationPending'),
    Accepted: t('tenantMembers.invitationAccepted'),
    Revoked: t('tenantMembers.invitationRevoked'),
    Expired: t('tenantMembers.invitationExpired')
  };
  return map[status] ?? status;
}

function invitationStatusTagType(status: string): 'success' | 'warning' | 'info' | 'danger' {
  if (status === 'Pending') {
    return 'warning';
  }
  if (status === 'Accepted') {
    return 'success';
  }
  if (status === 'Expired') {
    return 'info';
  }
  return 'danger';
}

function canManageMember(row: TenantMember): boolean {
  return row.memberRole !== 'Owner' && row.status !== 'Removed';
}

async function loadMembers() {
  if (!inTenantContext.value) {
    members.value = [];
    memberTotal.value = 0;
    return;
  }

  loadingMembers.value = true;
  problem.value = undefined;
  try {
    const result = await listTenantMembers({
      page: memberPage.value,
      pageSize: pageSize.value,
      status: appliedMemberStatus.value || undefined
    });
    members.value = result.items;
    memberTotal.value = result.total;
    await updateTableHeight();
  } catch (error) {
    problem.value = toProblem(error, 'tenantMembers.loadFailed');
  } finally {
    loadingMembers.value = false;
  }
}

async function loadInvitations() {
  if (!inTenantContext.value) {
    invitations.value = [];
    invitationTotal.value = 0;
    return;
  }

  loadingInvitations.value = true;
  try {
    const result = await listTenantInvitations({
      page: invitationPage.value,
      pageSize: pageSize.value,
      status: appliedInvitationStatus.value || undefined
    });
    invitations.value = result.items;
    invitationTotal.value = result.total;
    await updateInvitationTableHeight();
  } catch (error) {
    problem.value = toProblem(error, 'tenantMembers.loadFailed');
  } finally {
    loadingInvitations.value = false;
  }
}

async function refreshAll() {
  await Promise.all([loadMembers(), loadInvitations()]);
}

function applyMemberSearch(values: Record<string, string | undefined>) {
  appliedMemberStatus.value = values.status?.trim() ?? '';
  memberPage.value = 1;
  void loadMembers();
}

function resetMemberSearch() {
  memberSearchForm.value = {};
  appliedMemberStatus.value = '';
  memberPage.value = 1;
  void loadMembers();
}

function applyInvitationSearch(values: Record<string, string | undefined>) {
  appliedInvitationStatus.value = values.status?.trim() ?? '';
  invitationPage.value = 1;
  void loadInvitations();
}

function resetInvitationSearch() {
  invitationSearchForm.value = {};
  appliedInvitationStatus.value = '';
  invitationPage.value = 1;
  void loadInvitations();
}

function openProvision() {
  provisionForm.username = '';
  provisionForm.displayName = '';
  provisionForm.password = '';
  provisionForm.memberRole = 'Member';
  provisionForm.email = '';
  provisionFieldErrors.username = '';
  provisionFieldErrors.displayName = '';
  provisionFieldErrors.password = '';
  provisionFieldErrors.memberRole = '';
  provisionFieldErrors.email = '';
  provisionOpen.value = true;
}

function validateProvisionForm(): boolean {
  provisionFieldErrors.username = '';
  provisionFieldErrors.displayName = '';
  provisionFieldErrors.password = '';
  provisionFieldErrors.memberRole = '';
  provisionFieldErrors.email = '';

  const username = provisionForm.username.trim();
  if (!username) {
    provisionFieldErrors.username = t('tenantMembers.usernameRequired');
  } else if (username.length < 3 || username.length > 128) {
    provisionFieldErrors.username = t('tenantMembers.usernameInvalid');
  }

  const displayName = provisionForm.displayName.trim();
  if (!displayName) {
    provisionFieldErrors.displayName = t('tenantMembers.displayNameRequired');
  } else if (displayName.length > 128) {
    provisionFieldErrors.displayName = t('tenantMembers.displayNameInvalid');
  }

  if (!provisionForm.password) {
    provisionFieldErrors.password = t('tenantMembers.passwordRequired');
  } else if (!isIdentityPasswordValid(provisionForm.password)) {
    provisionFieldErrors.password = t('tenantMembers.passwordInvalid');
  }

  if (!MEMBER_ROLES.includes(provisionForm.memberRole as typeof MEMBER_ROLES[number])) {
    provisionFieldErrors.memberRole = t('tenantMembers.roleRequired');
  }

  const email = provisionForm.email.trim().toLowerCase();
  if (email && (email.length < 3 || email.length > 320 || !EMAIL_PATTERN.test(email))) {
    provisionFieldErrors.email = t('tenantMembers.emailInvalid');
  }

  return !provisionFieldErrors.username
    && !provisionFieldErrors.displayName
    && !provisionFieldErrors.password
    && !provisionFieldErrors.memberRole
    && !provisionFieldErrors.email;
}

async function submitProvision() {
  if (!validateProvisionForm()) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    const email = provisionForm.email.trim().toLowerCase();
    await provisionTenantMember({
      username: provisionForm.username.trim(),
      displayName: provisionForm.displayName.trim(),
      password: provisionForm.password,
      memberRole: provisionForm.memberRole,
      email: email || null
    });
    provisionOpen.value = false;
    ElMessage.success(t('tenantMembers.provisionSuccess'));
    memberPage.value = 1;
    await loadMembers();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function openInvite() {
  inviteForm.targetEmail = '';
  inviteForm.memberRole = 'Member';
  inviteForm.expiresInHours = '72';
  inviteFieldErrors.targetEmail = '';
  inviteFieldErrors.memberRole = '';
  inviteFieldErrors.expiresInHours = '';
  invitationToken.value = '';
  inviteOpen.value = true;
}

function openEditRole(row: TenantMember) {
  editingMember.value = row;
  editForm.memberRole = row.memberRole === 'Admin' ? 'Admin' : 'Member';
  editOpen.value = true;
}

function validateInviteForm(): boolean {
  inviteFieldErrors.targetEmail = '';
  inviteFieldErrors.memberRole = '';
  inviteFieldErrors.expiresInHours = '';

  const email = inviteForm.targetEmail.trim().toLowerCase();
  if (!email) {
    inviteFieldErrors.targetEmail = t('tenantMembers.emailRequired');
  } else if (email.length < 3 || email.length > 320 || !EMAIL_PATTERN.test(email)) {
    inviteFieldErrors.targetEmail = t('tenantMembers.emailInvalid');
  }

  if (!MEMBER_ROLES.includes(inviteForm.memberRole as typeof MEMBER_ROLES[number])) {
    inviteFieldErrors.memberRole = t('tenantMembers.roleRequired');
  }

  const hours = Number.parseInt(inviteForm.expiresInHours.trim(), 10);
  if (!Number.isInteger(hours) || hours < 1 || hours > 168) {
    inviteFieldErrors.expiresInHours = t('tenantMembers.expiresInHoursInvalid');
  }

  return !inviteFieldErrors.targetEmail
    && !inviteFieldErrors.memberRole
    && !inviteFieldErrors.expiresInHours;
}

async function submitInvite() {
  if (!validateInviteForm()) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    const hours = Number.parseInt(inviteForm.expiresInHours.trim(), 10);
    const result = await createTenantInvitation({
      targetEmail: inviteForm.targetEmail.trim().toLowerCase(),
      memberRole: inviteForm.memberRole,
      expiresInHours: hours
    });
    invitationToken.value = result.invitationToken;
    inviteOpen.value = false;
    ElMessage.success(t('tenantMembers.inviteSuccess'));
    invitationPage.value = 1;
    await loadInvitations();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function submitEditRole() {
  if (!editingMember.value) {
    return;
  }

  changing.value = true;
  problem.value = undefined;
  try {
    await updateTenantMember(editingMember.value.id, {
      memberRole: editForm.memberRole,
      version: editingMember.value.version
    });
    editOpen.value = false;
    ElMessage.success(t('tenantMembers.updateSuccess'));
    await loadMembers();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function confirmRemove(row: TenantMember) {
  await ElMessageBox.confirm(
    t('tenantMembers.confirmRemove', { name: row.displayName, username: row.username }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    await removeTenantMember(row.id, row.version);
    ElMessage.success(t('tenantMembers.removeSuccess'));
    await loadMembers();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function confirmRevoke(row: TenantInvitation) {
  await ElMessageBox.confirm(
    t('tenantMembers.confirmRevoke', { email: row.targetEmail }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    await revokeTenantInvitation(row.id);
    ElMessage.success(t('tenantMembers.revokeSuccess'));
    await loadInvitations();
  } catch (error) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function copyInvitationToken() {
  if (!invitationToken.value) {
    return;
  }
  await navigator.clipboard.writeText(invitationToken.value);
  ElMessage.success(t('tenantMembers.copyTokenSuccess'));
}

function toProblem(
  error: unknown,
  fallbackKey: 'tenantMembers.loadFailed' | 'tenantMembers.operationFailed' = 'tenantMembers.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.tenant_members_failed', title: t(fallbackKey) };
}

watch(
  () => session.currentUser?.tenantId,
  () => {
    void refreshAll();
  }
);

async function onTabChange(name: string | number): Promise<void> {
  await nextTick();
  await nextTick();
  if (name === 'members') {
    updateTableHeight();
    return;
  }

  if (name === 'invitations') {
    updateInvitationTableHeight();
    if (invitations.value.length === 0 && !loadingInvitations.value) {
      await loadInvitations();
    } else {
      await nextTick(updateInvitationTableHeight);
    }
  }
}

onMounted(() => {
  void refreshAll().then(async () => {
    await nextTick();
    updateTableHeight();
    if (activeTab.value === 'invitations') {
      updateInvitationTableHeight();
    }
  });
});
</script>

<template>
  <section
    class="tenant-members-view art-page-stack art-full-height"
    :aria-busy="loadingMembers || loadingInvitations"
  >
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('tenantMembers.title') }}</h1>

    <div v-if="!inTenantContext" class="art-inline-alert" role="status">
      <span>{{ t('tenantMembers.tenantContextRequired') }}</span>
    </div>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-card v-if="invitationToken" class="art-form-card" shadow="never" data-testid="tenant-member-invitation-token">
      <h2>{{ t('tenantMembers.tokenTitle') }}</h2>
      <p role="alert">{{ t('tenantMembers.tokenWarning') }}</p>
      <code translate="no">{{ invitationToken }}</code>
      <el-button type="primary" plain @click="copyInvitationToken">
        {{ t('tenantMembers.copyToken') }}
      </el-button>
    </el-card>

    <template v-if="inTenantContext">
    <el-card class="art-table-card tenant-members-view__panel" shadow="never">
      <el-tabs
        v-model="activeTab"
        class="tenant-members-view__tabs"
        data-testid="tenant-members-tabs"
        @tab-change="onTabChange"
      >
        <el-tab-pane :label="t('tenantMembers.membersSection')" name="members">
          <ArtSearchBar
            v-model="memberSearchForm"
            :items="memberSearchItems"
            :search-label="t('tenantMembers.query')"
            :reset-label="t('tenantMembers.reset')"
            @search="applyMemberSearch"
            @reset="resetMemberSearch"
          />

          <div ref="tableMainRef" class="art-crud-table-main">
            <ArtTableHeader
              v-model:table-size="tableSize"
              v-model:zebra="tableZebra"
              v-model:border="tableBorder"
              v-model:header-background="tableHeaderBackground"
              :loading="loadingMembers"
              full-class="art-crud-table-main"
              layout="refresh,size,fullscreen,settings"
              @refresh="loadMembers"
            >
              <template #left>
                <PermissionGate code="identity.tenant_members.provision">
                  <el-button
                    type="primary"
                    :icon="Plus"
                    data-testid="tenant-members-action-provision"
                    @click="openProvision"
                  >
                    {{ t('tenantMembers.provision') }}
                  </el-button>
                </PermissionGate>
                <PermissionGate code="identity.tenant_members.invite">
                  <el-button
                    type="primary"
                    plain
                    data-testid="tenant-members-action-invite"
                    @click="openInvite"
                  >
                    {{ t('tenantMembers.invite') }}
                  </el-button>
                </PermissionGate>
              </template>
            </ArtTableHeader>

            <el-table
              v-loading="loadingMembers"
              :data="members"
              row-key="id"
              :height="tableHeight"
              :size="tableSize"
              :stripe="tableZebra"
              :border="tableBorder"
              :header-cell-style="tableHeaderCellStyle"
            >
              <el-table-column :label="t('users.columnIndex')" width="72" align="center">
                <template #default="{ $index }">{{ memberRowIndex($index) }}</template>
              </el-table-column>
              <el-table-column :label="t('tenantMembers.fieldDisplayName')" min-width="140" prop="displayName" />
              <el-table-column :label="t('tenantMembers.fieldUsername')" min-width="120" prop="username" />
              <el-table-column :label="t('tenantMembers.fieldRole')" width="120">
                <template #default="{ row }">{{ memberRoleLabel(row.memberRole) }}</template>
              </el-table-column>
              <el-table-column :label="t('tenantMembers.fieldStatus')" width="110">
                <template #default="{ row }">
                  <el-tag :type="memberStatusTagType(row.status)">
                    {{ memberStatusLabel(row.status) }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column :label="t('users.columnActions')" width="200" fixed="right">
                <template #default="{ row }">
                  <ArtTableActionGroup v-if="canManageMember(row)">
                    <PermissionGate code="identity.tenant_members.update">
                      <ArtTableActionButton
                        type="edit"
                        :title="t('tenantMembers.editRole')"
                        test-id="tenant-members-action-edit-role"
                        @click="openEditRole(row)"
                      />
                    </PermissionGate>
                    <PermissionGate code="identity.tenant_members.remove">
                      <ArtTableActionButton
                        type="delete"
                        :title="t('tenantMembers.remove')"
                        test-id="tenant-members-action-remove"
                        @click="confirmRemove(row)"
                      />
                    </PermissionGate>
                  </ArtTableActionGroup>
                </template>
              </el-table-column>
            </el-table>

            <div class="art-table-pagination">
              <el-pagination
                v-model:current-page="memberPage"
                v-model:page-size="pageSize"
                :total="memberTotal"
                layout="total, sizes, prev, pager, next"
                @current-change="loadMembers"
                @size-change="() => { memberPage = 1; void loadMembers(); }"
              />
            </div>
          </div>
        </el-tab-pane>

        <el-tab-pane :label="t('tenantMembers.invitationsSection')" name="invitations">
          <ArtSearchBar
            v-model="invitationSearchForm"
            :items="invitationSearchItems"
            :search-label="t('tenantMembers.query')"
            :reset-label="t('tenantMembers.reset')"
            @search="applyInvitationSearch"
            @reset="resetInvitationSearch"
          />

          <div ref="invitationTableMainRef" class="art-crud-table-main">
            <el-table
              v-loading="loadingInvitations"
              :data="invitations"
              row-key="id"
              :height="invitationTableHeight"
              :size="tableSize"
              :stripe="tableZebra"
              :border="tableBorder"
              :header-cell-style="tableHeaderCellStyle"
            >
              <el-table-column :label="t('users.columnIndex')" width="72" align="center">
                <template #default="{ $index }">{{ invitationRowIndex($index) }}</template>
              </el-table-column>
              <el-table-column :label="t('tenantMembers.fieldEmail')" min-width="200" prop="targetEmail" />
              <el-table-column :label="t('tenantMembers.fieldRole')" width="120">
                <template #default="{ row }">{{ memberRoleLabel(row.memberRole) }}</template>
              </el-table-column>
              <el-table-column :label="t('tenantMembers.fieldStatus')" width="110">
                <template #default="{ row }">
                  <el-tag :type="invitationStatusTagType(row.status)">
                    {{ invitationStatusLabel(row.status) }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column :label="t('tenantMembers.fieldExpiresAt')" min-width="160">
                <template #default="{ row }">
                  <span translate="no">{{ formatDateTime(row.expiresAtUtc) }}</span>
                </template>
              </el-table-column>
              <el-table-column :label="t('users.columnActions')" width="120" fixed="right">
                <template #default="{ row }">
                  <ArtTableActionGroup v-if="row.status === 'Pending'">
                    <PermissionGate code="identity.tenant_members.revoke_invitation">
                      <ArtTableActionButton
                        type="delete"
                        :title="t('tenantMembers.revokeInvitation')"
                        test-id="tenant-members-action-revoke"
                        @click="confirmRevoke(row)"
                      />
                    </PermissionGate>
                  </ArtTableActionGroup>
                </template>
              </el-table-column>
            </el-table>

            <div class="art-table-pagination">
              <el-pagination
                v-model:current-page="invitationPage"
                v-model:page-size="pageSize"
                :total="invitationTotal"
                layout="total, sizes, prev, pager, next"
                @current-change="loadInvitations"
                @size-change="() => { invitationPage = 1; void loadInvitations(); }"
              />
            </div>
          </div>
        </el-tab-pane>
      </el-tabs>
    </el-card>

    <ArtFormDialog
      v-model:open="provisionOpen"
      :title="t('tenantMembers.provisionTitle')"
      :confirm-label="t('tenantMembers.provision')"
      :cancel-label="t('tenantMembers.cancel')"
      :saving="changing"
      @confirm="submitProvision"
    >
      <el-form label-position="top" @submit.prevent>
        <el-form-item :label="t('tenantMembers.fieldUsername')" required :error="provisionFieldErrors.username">
          <el-input v-model="provisionForm.username" autocomplete="off" />
        </el-form-item>
        <el-form-item :label="t('tenantMembers.fieldDisplayName')" required :error="provisionFieldErrors.displayName">
          <el-input v-model="provisionForm.displayName" autocomplete="name" />
        </el-form-item>
        <el-form-item :label="t('tenantMembers.fieldPassword')" required :error="provisionFieldErrors.password">
          <el-input v-model="provisionForm.password" type="password" show-password autocomplete="new-password" />
        </el-form-item>
        <el-form-item :label="t('tenantMembers.fieldRole')" required :error="provisionFieldErrors.memberRole">
          <el-select v-model="provisionForm.memberRole" style="width: 100%">
            <el-option
              v-for="option in assignableRoleOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('tenantMembers.fieldEmail')" :error="provisionFieldErrors.email">
          <el-input v-model="provisionForm.email" autocomplete="email" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="inviteOpen"
      :title="t('tenantMembers.inviteTitle')"
      :confirm-label="t('tenantMembers.invite')"
      :cancel-label="t('tenantMembers.cancel')"
      :saving="changing"
      @confirm="submitInvite"
    >
      <el-form ref="inviteFormRef" label-position="top" @submit.prevent>
        <el-form-item :label="t('tenantMembers.fieldEmail')" required :error="inviteFieldErrors.targetEmail">
          <el-input v-model="inviteForm.targetEmail" autocomplete="email" />
        </el-form-item>
        <el-form-item :label="t('tenantMembers.fieldRole')" required :error="inviteFieldErrors.memberRole">
          <el-select v-model="inviteForm.memberRole" style="width: 100%">
            <el-option
              v-for="option in assignableRoleOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item
          :label="t('tenantMembers.fieldExpiresInHours')"
          required
          :error="inviteFieldErrors.expiresInHours"
        >
          <el-input v-model="inviteForm.expiresInHours" inputmode="numeric" />
          <p class="field-hint">{{ t('tenantMembers.expiresInHoursHint') }}</p>
        </el-form-item>
      </el-form>
    </ArtFormDialog>

    </template>

    <ArtFormDialog
      v-model:open="editOpen"
      :title="t('tenantMembers.editRoleTitle')"
      :confirm-label="t('tenantMembers.save')"
      :cancel-label="t('tenantMembers.cancel')"
      :saving="changing"
      @confirm="submitEditRole"
    >
      <el-form ref="editFormRef" label-position="top" @submit.prevent>
        <el-form-item :label="t('tenantMembers.fieldRole')" required>
          <el-select v-model="editForm.memberRole" style="width: 100%">
            <el-option
              v-for="option in assignableRoleOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.tenant-members-view__panel :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 8px;
}

.tenant-members-view__tabs {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}

.tenant-members-view__tabs :deep(.el-tabs__header) {
  flex-shrink: 0;
  margin-bottom: 8px;
}

.tenant-members-view__tabs :deep(.el-tabs__content) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
}

.tenant-members-view__tabs :deep(.el-tab-pane) {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 12px;
  min-height: 0;
  overflow: hidden;
}

.tenant-members-view__tabs :deep(.el-tab-pane > .art-search-bar) {
  flex-shrink: 0;
}

.tenant-members-view .art-table-pagination {
  flex-shrink: 0;
  padding-top: 4px;
}

.tenant-members-view__panel {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.tenant-members-view__panel :deep(.el-card__body) {
  flex: 1;
}

.field-hint {
  margin: 6px 0 0;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.art-form-card code {
  display: block;
  margin: 12px 0;
  word-break: break-all;
}
</style>
