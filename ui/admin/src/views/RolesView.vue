<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCheckbox,
  ElCheckboxGroup,
  ElDialog,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElSelect,
  ElTag
} from 'element-plus';
import {
  HOST_ROLE_ASSIGNABLE_PERMISSIONS,
  ROLE_DATA_SCOPE_KINDS,
  type FullNetProblemDetails,
  type HostRole,
  type OrganizationUnit,
  type RoleDataScopeKind
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostRole,
  disableHostRole,
  getHostRoleDataScope,
  listHostRoles,
  replaceHostRolePermissions,
  updateHostRole,
  updateHostRoleDataScope
} from '../api/roles';
import { listOrganizationUnits } from '../api/org-units';

const session = useSessionStore();
const { t } = useAdminI18n();
const roles = ref<HostRole[]>([]);
const code = ref('');
const name = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const permissionsVisible = ref(false);
const dataScopeVisible = ref(false);
const editingRole = ref<HostRole>();
const selectedPermissions = ref<string[]>([]);
const selectedDataScopeKind = ref<RoleDataScopeKind>('identity.data_scope.all');
const selectedUnitIds = ref<string[]>([]);
const dataScopeVersion = ref(0);
const orgUnits = ref<OrganizationUnit[]>([]);
const dataScopeKinds = ROLE_DATA_SCOPE_KINDS;
const assignablePermissions = HOST_ROLE_ASSIGNABLE_PERMISSIONS;
const canWrite = computed(() => session.can('identity.roles.write'));
const inTenantContext = computed(() => !!session.currentUser?.tenantId);

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listHostRoles();
    roles.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value || !code.value.trim() || !name.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createHostRole(code.value.trim(), name.value.trim());
    code.value = '';
    name.value = '';
    ElMessage.success(t('roles.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function edit(role: HostRole): Promise<void> {
  if (changing.value || role.isSystem) return;
  try {
    const result = await ElMessageBox.prompt(
      t('roles.editTitle'),
      t('roles.edit'),
      {
        inputValue: role.name,
        inputPattern: /.+/,
        showCancelButton: true
      }
    );
    changing.value = true;
    await updateHostRole(role.id, result.value.trim(), role.version);
    ElMessage.success(t('roles.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'roles.operationFailed');
  } finally {
    changing.value = false;
  }
}

function openPermissions(role: HostRole): void {
  if (role.isSystem) return;
  editingRole.value = role;
  selectedPermissions.value = [...role.permissionCodes];
  permissionsVisible.value = true;
}

async function savePermissions(): Promise<void> {
  const role = editingRole.value;
  if (!role || changing.value) return;
  changing.value = true;
  problem.value = undefined;
  try {
    await replaceHostRolePermissions(
      role.id,
      [...selectedPermissions.value].sort(),
      role.version
    );
    permissionsVisible.value = false;
    editingRole.value = undefined;
    ElMessage.success(t('roles.permissionsSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.operationFailed');
  } finally {
    changing.value = false;
  }
}

function dataScopeKindLabel(kind: RoleDataScopeKind): string {
  const labels: Record<RoleDataScopeKind, 'roles.dataScopeKindAll' | 'roles.dataScopeKindOrg' | 'roles.dataScopeKindOrgSubtree' | 'roles.dataScopeKindSelf' | 'roles.dataScopeKindCustom'> = {
    'identity.data_scope.all': 'roles.dataScopeKindAll',
    'identity.data_scope.org': 'roles.dataScopeKindOrg',
    'identity.data_scope.org_subtree': 'roles.dataScopeKindOrgSubtree',
    'identity.data_scope.self': 'roles.dataScopeKindSelf',
    'identity.data_scope.custom': 'roles.dataScopeKindCustom'
  };
  return t(labels[kind]);
}

async function openDataScope(role: HostRole): Promise<void> {
  if (role.isSystem || changing.value) return;
  editingRole.value = role;
  problem.value = undefined;
  try {
    const scope = await getHostRoleDataScope(role.id);
    selectedDataScopeKind.value = scope.dataScopeKind;
    selectedUnitIds.value = [...scope.unitIds];
    dataScopeVersion.value = scope.version;
    if (scope.dataScopeKind === 'identity.data_scope.custom' && inTenantContext.value) {
      await loadOrgUnits();
    } else {
      orgUnits.value = [];
    }
    dataScopeVisible.value = true;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.operationFailed');
  }
}

async function loadOrgUnits(): Promise<void> {
  const page = await listOrganizationUnits(1, 100);
  orgUnits.value = page.items;
}

async function onDataScopeKindChange(kind: RoleDataScopeKind): Promise<void> {
  selectedDataScopeKind.value = kind;
  if (kind === 'identity.data_scope.custom' && inTenantContext.value) {
    await loadOrgUnits();
    return;
  }
  selectedUnitIds.value = [];
  orgUnits.value = [];
}

async function saveDataScope(): Promise<void> {
  const role = editingRole.value;
  if (!role || changing.value) return;
  changing.value = true;
  problem.value = undefined;
  try {
    const unitIds = selectedDataScopeKind.value === 'identity.data_scope.custom'
      ? [...selectedUnitIds.value]
      : null;
    await updateHostRoleDataScope(
      role.id,
      selectedDataScopeKind.value,
      unitIds,
      dataScopeVersion.value
    );
    dataScopeVisible.value = false;
    editingRole.value = undefined;
    ElMessage.success(t('roles.dataScopeSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'roles.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(role: HostRole): Promise<void> {
  if (changing.value || !role.isActive || role.isSystem) return;
  try {
    await ElMessageBox.confirm(
      t('roles.confirmDisable', { name: role.code }),
      t('roles.disable'),
      { type: 'warning', confirmButtonText: t('roles.disable'), cancelButtonText: t('status.back') }
    );
    changing.value = true;
    await disableHostRole(role.id);
    ElMessage.success(t('roles.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'roles.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'roles.loadFailed' | 'roles.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_role_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="roles-view" :aria-busy="loading">
    <header class="roles-heading">
      <div>
        <p>{{ t('roles.eyebrow') }}</p>
        <h1 data-route-heading tabindex="-1">{{ t('roles.title') }}</h1>
        <span>{{ t('roles.description') }}</span>
      </div>
    </header>

    <div v-if="problem" class="roles-problem" role="alert">
      <strong translate="no">{{ problem.code }}</strong><span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <section v-if="canWrite" class="create-strip" aria-labelledby="create-title">
      <div><small>01</small><h2 id="create-title">{{ t('roles.createTitle') }}</h2></div>
      <label>
        <span>{{ t('roles.code') }}</span>
        <el-input v-model="code" :placeholder="t('roles.codePlaceholder')" />
      </label>
      <label>
        <span>{{ t('roles.name') }}</span>
        <el-input v-model="name" :placeholder="t('roles.namePlaceholder')" @keyup.enter="create" />
      </label>
      <el-button type="primary" :loading="changing" @click="create">{{ t('roles.create') }}</el-button>
    </section>

    <section class="identity-ledger">
      <header>
        <div><small>02</small><h2>{{ t('roles.directoryTitle') }}</h2></div>
        <b>{{ roles.length }}</b>
      </header>
      <p v-if="roles.length === 0" class="roles-empty">{{ t('roles.emptyDirectory') }}</p>
      <article v-for="role in roles" :key="role.id">
        <span class="identity-mark">{{ role.code.slice(0, 2).toUpperCase() }}</span>
        <div>
          <strong translate="no">{{ role.name }}</strong>
          <code translate="no">{{ role.code }}</code>
        </div>
        <div class="roles-tags">
          <el-tag v-if="role.isSystem" type="warning">{{ t('roles.system') }}</el-tag>
          <el-tag :type="role.isActive ? 'success' : 'info'">
            {{ t(role.isActive ? 'roles.active' : 'roles.inactive') }}
          </el-tag>
        </div>
        <div class="roles-actions">
          <el-button
            v-if="canWrite && !role.isSystem"
            plain
            :disabled="changing"
            @click="edit(role)"
          >
            {{ t('roles.edit') }}
          </el-button>
          <el-button
            v-if="canWrite && !role.isSystem"
            plain
            :disabled="changing"
            @click="openPermissions(role)"
          >
            {{ t('roles.permissions') }}
          </el-button>
          <el-button
            v-if="canWrite && !role.isSystem"
            plain
            :disabled="changing"
            @click="openDataScope(role)"
          >
            {{ t('roles.dataScope') }}
          </el-button>
          <el-button
            v-if="canWrite && role.isActive && !role.isSystem"
            type="danger"
            plain
            :disabled="changing"
            @click="disable(role)"
          >
            {{ t('roles.disable') }}
          </el-button>
        </div>
      </article>
    </section>

    <el-dialog
      v-model="permissionsVisible"
      :title="t('roles.permissionsTitle')"
      width="520px"
    >
      <el-checkbox-group v-model="selectedPermissions" class="roles-permissions">
        <el-checkbox
          v-for="permission in assignablePermissions"
          :key="permission"
          :label="permission"
          translate="no"
        >
          {{ permission }}
        </el-checkbox>
      </el-checkbox-group>
      <template #footer>
        <el-button @click="permissionsVisible = false">{{ t('status.back') }}</el-button>
        <el-button type="primary" :loading="changing" @click="savePermissions">
          {{ t('roles.savePermissions') }}
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="dataScopeVisible"
      :title="t('roles.dataScopeTitle')"
      width="560px"
    >
      <label class="roles-data-scope-kind">
        <span>{{ t('roles.dataScopeKind') }}</span>
        <el-select
          :model-value="selectedDataScopeKind"
          @update:model-value="onDataScopeKindChange"
        >
          <el-option
            v-for="kind in dataScopeKinds"
            :key="kind"
            :label="dataScopeKindLabel(kind)"
            :value="kind"
          />
        </el-select>
      </label>
      <p
        v-if="selectedDataScopeKind === 'identity.data_scope.custom' && !inTenantContext"
        class="roles-data-scope-hint"
      >
        {{ t('roles.dataScopeTenantRequired') }}
      </p>
      <section
        v-if="selectedDataScopeKind === 'identity.data_scope.custom' && inTenantContext"
        class="roles-data-scope-units"
      >
        <span>{{ t('roles.dataScopeUnits') }}</span>
        <el-checkbox-group v-model="selectedUnitIds">
          <el-checkbox
            v-for="unit in orgUnits"
            :key="unit.id"
            :label="unit.id"
          >
            <span translate="no">{{ unit.name }}</span>
            <code translate="no">{{ unit.code }}</code>
          </el-checkbox>
        </el-checkbox-group>
      </section>
      <template #footer>
        <el-button @click="dataScopeVisible = false">{{ t('status.back') }}</el-button>
        <el-button type="primary" :loading="changing" @click="saveDataScope">
          {{ t('roles.saveDataScope') }}
        </el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.roles-view { display: grid; gap: 18px; }
.roles-heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 24px; padding: 8px 2px; }
.roles-heading p { margin: 0 0 10px; color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 10px; font-weight: 700; letter-spacing: .2em; }
.roles-heading h1 { margin: 0; font-family: var(--fullnet-font-display); font-size: clamp(30px, 4vw, 48px); font-weight: 500; letter-spacing: -.05em; }
.roles-heading span { display: block; margin-top: 10px; color: var(--fullnet-color-ink-muted); font-size: 13px; }
.roles-problem { display: flex; gap: 14px; padding: 13px 16px; border-left: 3px solid var(--fullnet-color-danger); background: rgb(201 74 74 / 8%); }
.roles-problem code { margin-left: auto; }
.create-strip { display: grid; grid-template-columns: minmax(160px, .7fr) repeat(2, minmax(180px, 1fr)) auto; align-items: end; gap: 16px; padding: 20px; border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-sidebar); color: #fff; }
.create-strip > div { align-self: center; }
.create-strip small, .identity-ledger small { color: var(--fullnet-color-accent-bright); font-family: var(--fullnet-font-display); }
.create-strip h2, .identity-ledger h2 { margin: 4px 0 0; font-size: 17px; }
.create-strip label span { display: block; margin-bottom: 7px; color: #aeb8b9; font-size: 11px; }
.identity-ledger { overflow: hidden; border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-panel); box-shadow: var(--fullnet-shadow-panel); }
.identity-ledger > header { display: flex; min-height: 66px; align-items: center; justify-content: space-between; padding: 0 22px; border-bottom: 1px solid var(--fullnet-color-line); }
.identity-ledger article { display: grid; grid-template-columns: 44px minmax(180px, 1fr) auto auto; align-items: center; gap: 16px; padding: 15px 22px; border-bottom: 1px solid var(--fullnet-color-line); }
.roles-tags { display: flex; gap: 8px; flex-wrap: wrap; }
.roles-actions { display: flex; gap: 8px; justify-content: flex-end; flex-wrap: wrap; }
.identity-mark { display: grid; width: 40px; height: 40px; place-items: center; border-radius: 12px; background: var(--fullnet-color-ink); color: #fff; font-weight: 700; }
.identity-ledger article div { display: grid; gap: 4px; }
.identity-ledger code { color: var(--fullnet-color-ink-muted); font-size: 11px; }
.roles-empty { padding: 28px; margin: 0; text-align: center; color: var(--fullnet-color-ink-muted); }
.roles-permissions { display: grid; gap: 10px; }
.roles-data-scope-kind { display: grid; gap: 8px; margin-bottom: 16px; }
.roles-data-scope-kind span { font-size: 12px; color: var(--fullnet-color-ink-muted); }
.roles-data-scope-hint { margin: 0 0 12px; color: var(--fullnet-color-danger); font-size: 13px; }
.roles-data-scope-units { display: grid; gap: 10px; }
.roles-data-scope-units code { margin-left: 8px; color: var(--fullnet-color-ink-muted); font-size: 11px; }
@media (max-width: 1080px) {
  .create-strip { grid-template-columns: 1fr; }
  .identity-ledger article { grid-template-columns: 44px 1fr auto; }
  .identity-ledger article .roles-actions { grid-column: 2 / -1; }
}
</style>
