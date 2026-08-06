<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type {
  FullNetProblemDetails,
  SuperAdministrator,
  SuperAdministratorAudit,
  TotpEnrollmentStatus
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
import {
  getSuperAdministratorAudits,
  getSuperAdministrators,
  grantSuperAdministrator,
  revokeSuperAdministrator
} from '../api/superAdministrators';
import {
  beginTotpEnrollment,
  confirmTotpEnrollment,
  getTotpEnrollmentStatus
} from '../api/totpEnrollment';

defineOptions({ name: 'SuperAdministratorsView' });

type AdminTableColumnKey = 'username' | 'status';

interface AppliedFilters {
  username: string;
  status: '' | 'active' | 'inactive';
}

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const allAdministrators = ref<SuperAdministrator[]>([]);
const audits = ref<SuperAdministratorAudit[]>([]);
const totpStatus = ref<TotpEnrollmentStatus>({ isEnrolled: false, isEnabled: false });
const pendingSecret = ref('');
const pendingOtpAuthUri = ref('');
const enrollCode = ref('');
const loading = ref(false);
const changing = ref(false);
const enrolling = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref<AppliedFilters>({ username: '', status: '' });
const grantOpen = ref(false);
const revokeOpen = ref(false);
const revokingAdministrator = ref<SuperAdministrator | null>(null);
const grantFormRef = ref<FormInstance>();
const revokeFormRef = ref<FormInstance>();
const grantForm = reactive({
  username: '',
  currentPassword: '',
  totpCode: ''
});
const revokeForm = reactive({
  currentPassword: '',
  totpCode: ''
});
const grantFieldErrors = reactive({
  username: '',
  currentPassword: ''
});
const revokeFieldErrors = reactive({
  currentPassword: ''
});
const columnVisibility = ref<Record<AdminTableColumnKey, boolean>>({
  username: true,
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

const canManage = computed(() => session.can('identity.super_administrators.manage'));

const filteredAdministrators = computed(() => {
  let rows = allAdministrators.value;
  const filters = appliedFilters.value;

  if (filters.username.trim()) {
    const keyword = filters.username.trim().toLowerCase();
    rows = rows.filter(
      admin =>
        admin.username.toLowerCase().includes(keyword)
        || admin.displayName.toLowerCase().includes(keyword)
    );
  }

  if (filters.status === 'active') {
    rows = rows.filter(admin => admin.isActive);
  } else if (filters.status === 'inactive') {
    rows = rows.filter(admin => !admin.isActive);
  }

  return rows;
});

const { page, pageSize, total, pagedItems: pagedAdministrators, resetPage } = useArtClientPagination(filteredAdministrators);

const tableColumns = computed<ArtTableColumnOption[]>({
  get: () => [
    { key: 'username', label: t('superAdmin.username'), visible: columnVisibility.value.username },
    { key: 'status', label: t('users.status'), visible: columnVisibility.value.status }
  ],
  set: columns => {
    for (const column of columns) {
      if (column.key in columnVisibility.value) {
        columnVisibility.value[column.key as AdminTableColumnKey] = column.visible !== false;
      }
    }
  }
});

const searchItems = computed<ArtSearchBarItem[]>(() => [
  {
    key: 'username',
    label: t('superAdmin.username'),
    placeholder: t('superAdmin.searchUsernamePlaceholder')
  },
  {
    key: 'status',
    label: t('users.status'),
    type: 'select',
    placeholder: t('superAdmin.searchStatusPlaceholder'),
    options: [
      { label: t('superAdmin.active'), value: 'active' },
      { label: t('superAdmin.inactive'), value: 'inactive' }
    ]
  }
]);

watchLoading(loading);

onMounted(() => {
  void load();
});

function isColumnVisible(key: AdminTableColumnKey): boolean {
  return columnVisibility.value[key];
}

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function clearGrantFieldErrors(): void {
  grantFieldErrors.username = '';
  grantFieldErrors.currentPassword = '';
}

function clearRevokeFieldErrors(): void {
  revokeFieldErrors.currentPassword = '';
}

function validateGrantUsername(): string {
  if (!grantForm.username.trim()) {
    return t('superAdmin.usernameRequired');
  }
  return '';
}

function validateGrantPassword(): string {
  if (!grantForm.currentPassword) {
    return t('superAdmin.passwordRequired');
  }
  return '';
}

function validateRevokePassword(): string {
  if (!revokeForm.currentPassword) {
    return t('superAdmin.passwordRequired');
  }
  return '';
}

function applyGrantFieldErrors(): boolean {
  grantFieldErrors.username = validateGrantUsername();
  grantFieldErrors.currentPassword = validateGrantPassword();
  return !grantFieldErrors.username && !grantFieldErrors.currentPassword;
}

function applyRevokeFieldErrors(): boolean {
  revokeFieldErrors.currentPassword = validateRevokePassword();
  return !revokeFieldErrors.currentPassword;
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [admins, auditRows, status] = await Promise.all([
      getSuperAdministrators(),
      getSuperAdministratorAudits(),
      getTotpEnrollmentStatus().catch(() => ({ isEnrolled: false, isEnabled: false }))
    ]);
    allAdministrators.value = admins;
    audits.value = auditRows;
    totpStatus.value = status;
    if (status.isEnabled) {
      pendingSecret.value = '';
      pendingOtpAuthUri.value = '';
      enrollCode.value = '';
    }
    await nextTick(updateTableHeight);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'superAdmin.loadFailed');
  } finally {
    loading.value = false;
  }
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = {
    username: params.username ?? '',
    status: (params.status as AppliedFilters['status']) ?? ''
  };
  resetPage();
}

function resetSearch(): void {
  appliedFilters.value = { username: '', status: '' };
  resetPage();
}

function openGrant(): void {
  grantForm.username = '';
  grantForm.currentPassword = '';
  grantForm.totpCode = '';
  clearGrantFieldErrors();
  grantOpen.value = true;
}

function openRevoke(administrator: SuperAdministrator): void {
  if (changing.value) {
    return;
  }
  revokingAdministrator.value = administrator;
  revokeForm.currentPassword = '';
  revokeForm.totpCode = '';
  clearRevokeFieldErrors();
  revokeOpen.value = true;
}

async function beginEnroll(): Promise<void> {
  if (enrolling.value) {
    return;
  }
  enrolling.value = true;
  problem.value = undefined;
  try {
    const began = await beginTotpEnrollment();
    pendingSecret.value = began.sharedSecretBase32;
    pendingOtpAuthUri.value = began.otpAuthUri;
    totpStatus.value = { isEnrolled: true, isEnabled: false };
    ElMessage.success(t('superAdmin.totpBeginSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'superAdmin.operationFailed');
  } finally {
    enrolling.value = false;
  }
}

async function confirmEnroll(): Promise<void> {
  if (enrolling.value || !enrollCode.value.trim()) {
    return;
  }
  enrolling.value = true;
  problem.value = undefined;
  try {
    totpStatus.value = await confirmTotpEnrollment(enrollCode.value.trim());
    pendingSecret.value = '';
    pendingOtpAuthUri.value = '';
    enrollCode.value = '';
    ElMessage.success(t('superAdmin.totpConfirmSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'superAdmin.operationFailed');
  } finally {
    enrolling.value = false;
  }
}

async function submitGrant(): Promise<void> {
  if (changing.value || !applyGrantFieldErrors()) {
    return;
  }
  if (!canManage.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await grantSuperAdministrator(
      grantForm.username.trim(),
      grantForm.currentPassword,
      grantForm.totpCode.trim() || undefined
    );
    grantOpen.value = false;
    grantForm.currentPassword = '';
    ElMessage.success(t('superAdmin.grantSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'superAdmin.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function submitRevoke(): Promise<void> {
  const administrator = revokingAdministrator.value;
  if (changing.value || !administrator || !applyRevokeFieldErrors()) {
    return;
  }
  if (!canManage.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await revokeSuperAdministrator(
      administrator.userId,
      revokeForm.currentPassword,
      revokeForm.totpCode.trim() || undefined
    );
    revokeOpen.value = false;
    revokeForm.currentPassword = '';
    ElMessage.success(t('superAdmin.revokeSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'superAdmin.operationFailed');
  } finally {
    changing.value = false;
  }
}

function formatAuditTime(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

function toProblem(
  error: unknown,
  fallbackKey: 'superAdmin.loadFailed' | 'superAdmin.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.super_administrator_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="super-admin-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('superAdmin.title') }}</h1>

    <div class="super-admin-view__toolbar">
      <el-tag effect="plain" type="danger">{{ t('superAdmin.protected') }}</el-tag>
    </div>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <section class="art-info-strip" aria-labelledby="totp-title">
      <div class="art-info-strip__header">
        <h2 id="totp-title">{{ t('superAdmin.totpTitle') }}</h2>
        <p>{{ t(totpStatus.isEnabled ? 'superAdmin.totpEnabled' : 'superAdmin.totpDisabled') }}</p>
      </div>
      <div v-if="pendingSecret" class="art-info-strip__grid">
        <label>
          <span>{{ t('superAdmin.totpSecret') }}</span>
          <code translate="no">{{ pendingSecret }}</code>
        </label>
        <label>
          <span>{{ t('superAdmin.totpUri') }}</span>
          <code translate="no">{{ pendingOtpAuthUri }}</code>
        </label>
        <label>
          <span>{{ t('superAdmin.totpCode') }}</span>
          <el-input
            v-model="enrollCode"
            maxlength="6"
            :placeholder="t('superAdmin.totpCodePlaceholder')"
            @keyup.enter="confirmEnroll"
          />
        </label>
        <el-button type="primary" :loading="enrolling" @click="confirmEnroll">{{ t('superAdmin.totpConfirm') }}</el-button>
      </div>
      <el-button
        v-else
        :loading="enrolling"
        :disabled="totpStatus.isEnabled"
        @click="beginEnroll"
      >
        {{ totpStatus.isEnabled ? t('superAdmin.totpEnabled') : t('superAdmin.totpBegin') }}
      </el-button>
    </section>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :default-visible-count="2"
      :search-label="t('superAdmin.query')"
      :reset-label="t('superAdmin.reset')"
      :expand-label="t('superAdmin.expand')"
      :collapse-label="t('superAdmin.collapse')"
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
            <PermissionGate code="identity.super_administrators.manage">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="super-admin-action-grant"
                data-super-admin-grant-form
                @click="openGrant"
              >
                {{ t('superAdmin.addGrant') }}
              </el-button>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': pagedAdministrators.length === 0 }">
          <el-table
            v-loading="loading"
            :data="pagedAdministrators"
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

            <el-table-column :label="t('superAdmin.username')" min-width="220">
              <template #default="{ row }">
                <div class="art-crud-table-row">
                  <span class="art-crud-table-row__avatar">{{ row.username.slice(0, 2).toUpperCase() }}</span>
                  <div>
                    <div class="art-crud-table-row__name" translate="no">{{ row.displayName }}</div>
                    <div class="art-crud-table-row__sub" translate="no">{{ row.username }}</div>
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
                <el-tag :type="row.isActive ? 'success' : 'info'">
                  {{ t(row.isActive ? 'superAdmin.active' : 'superAdmin.inactive') }}
                </el-tag>
              </template>
            </el-table-column>

            <el-table-column
              :label="t('users.columnActions')"
              width="100"
              fixed="right"
              align="center"
            >
              <template #default="{ row }">
                <div class="art-crud-table-actions">
                  <PermissionGate v-if="canManage" code="identity.super_administrators.manage">
                    <ArtTableActionButton
                      type="delete"
                      test-id="super-admin-action-revoke"
                      :title="t('superAdmin.revoke')"
                      :disabled="changing"
                  @click="openRevoke(row as SuperAdministrator)"
                    />
                  </PermissionGate>
                </div>
              </template>
            </el-table-column>

            <template #empty>{{ t('superAdmin.emptyDirectory') }}</template>
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

    <el-card class="art-table-card super-admin-audit-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('superAdmin.auditTitle') }}</h2>
        </div>
      </template>

      <p v-if="audits.length === 0" class="art-empty-state">{{ t('superAdmin.emptyAudit') }}</p>
      <ol v-else class="art-audit-list">
        <li v-for="audit in audits" :key="audit.id">
          <time translate="no">{{ formatAuditTime(audit.occurredAtUtc) }}</time>
          <strong translate="no">{{ audit.eventType }}</strong>
          <code translate="no">{{ audit.actorUserId ?? 'system' }} → {{ audit.targetUserId }}</code>
        </li>
      </ol>
    </el-card>

    <ArtFormDialog
      v-model:open="grantOpen"
      :title="t('superAdmin.grantDialogTitle')"
      :saving="changing"
      :confirm-label="t('superAdmin.grant')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="super-admin-grant-submit"
      :show-confirm="canManage"
      @confirm="submitGrant"
    >
      <el-form
        ref="grantFormRef"
        data-testid="super-admin-grant-form"
        :model="grantForm"
        label-width="112px"
        class="super-admin-grant-form"
      >
        <el-form-item
          :label="t('superAdmin.username')"
          prop="username"
          required
          :error="grantFieldErrors.username || undefined"
        >
          <el-input
            v-model="grantForm.username"
            :placeholder="t('superAdmin.usernamePlaceholder')"
            @update:model-value="grantFieldErrors.username = validateGrantUsername()"
          />
        </el-form-item>
        <el-form-item
          :label="t('superAdmin.currentPassword')"
          prop="currentPassword"
          required
          :error="grantFieldErrors.currentPassword || undefined"
        >
          <el-input
            v-model="grantForm.currentPassword"
            type="password"
            show-password
            :placeholder="t('superAdmin.passwordPlaceholder')"
            @update:model-value="grantFieldErrors.currentPassword = validateGrantPassword()"
          />
        </el-form-item>
        <el-form-item :label="t('superAdmin.totpCode')">
          <el-input
            v-model="grantForm.totpCode"
            name="totpCode"
            maxlength="6"
            :placeholder="t('superAdmin.totpCodePlaceholder')"
          />
        </el-form-item>
      </el-form>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="revokeOpen"
      :title="t('superAdmin.revokeDialogTitle')"
      :saving="changing"
      :confirm-label="t('superAdmin.revoke')"
      :cancel-label="t('users.cancel')"
      confirm-test-id="super-admin-revoke-submit"
      :show-confirm="canManage"
      @confirm="submitRevoke"
    >
      <el-form
        ref="revokeFormRef"
        data-testid="super-admin-revoke-form"
        :model="revokeForm"
        label-width="112px"
        class="super-admin-revoke-form"
      >
        <el-form-item :label="t('superAdmin.revokeTarget')">
          <el-input
            :model-value="revokingAdministrator?.username ?? ''"
            disabled
          />
        </el-form-item>
        <p class="super-admin-revoke-form__hint">
          {{ t('superAdmin.confirmRevoke', { name: revokingAdministrator?.username ?? '' }) }}
        </p>
        <el-form-item
          :label="t('superAdmin.currentPassword')"
          prop="currentPassword"
          required
          :error="revokeFieldErrors.currentPassword || undefined"
        >
          <el-input
            v-model="revokeForm.currentPassword"
            type="password"
            show-password
            :placeholder="t('superAdmin.passwordPlaceholder')"
            @update:model-value="revokeFieldErrors.currentPassword = validateRevokePassword()"
          />
        </el-form-item>
        <el-form-item :label="t('superAdmin.totpCode')">
          <el-input
            v-model="revokeForm.totpCode"
            maxlength="6"
            :placeholder="t('superAdmin.totpCodePlaceholder')"
          />
          <small class="super-admin-revoke-form__totp-hint">{{ t('superAdmin.confirmRevokeTotp') }}</small>
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.super-admin-view__toolbar {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 4px;
}

.super-admin-view :deep(.art-table-card) {
  flex: 1;
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.super-admin-view :deep(.art-table-card .el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.super-admin-audit-card {
  flex: none;
}

.super-admin-grant-form,
.super-admin-revoke-form {
  padding-top: 8px;
}

.super-admin-revoke-form__hint {
  margin: 0 0 12px;
  color: var(--art-gray-600);
  font-size: 13px;
}

.super-admin-revoke-form__totp-hint {
  display: block;
  margin-top: 4px;
  color: var(--art-gray-500);
  font-size: 12px;
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
