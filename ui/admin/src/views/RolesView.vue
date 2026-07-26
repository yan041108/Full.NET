<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
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
      dataScopeVersion.value,
      session.currentUser?.tenantId ?? null
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
  <section class="roles-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('roles.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canWrite" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-2" aria-labelledby="create-title">
        <div>
          <h2 id="create-title">{{ t('roles.createTitle') }}</h2>
        </div>
        <label>
          <span>{{ t('roles.code') }}</span>
          <el-input v-model="code" :placeholder="t('roles.codePlaceholder')" />
        </label>
        <label>
          <span>{{ t('roles.name') }}</span>
          <el-input v-model="name" :placeholder="t('roles.namePlaceholder')" @keyup.enter="create" />
        </label>
        <el-button type="primary" :loading="changing" @click="create">{{ t('roles.create') }}</el-button>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('roles.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ roles.length }}</span>
        </div>
      </template>

      <p v-if="roles.length === 0" class="art-empty-state">{{ t('roles.emptyDirectory') }}</p>
      <article v-for="role in roles" :key="role.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ role.code.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ role.name }}</strong>
          <code translate="no">{{ role.code }}</code>
        </div>
        <div class="art-tag-group">
          <el-tag v-if="role.isSystem" type="warning">{{ t('roles.system') }}</el-tag>
          <el-tag :type="role.isActive ? 'success' : 'info'">
            {{ t(role.isActive ? 'roles.active' : 'roles.inactive') }}
          </el-tag>
        </div>
        <div class="art-data-row__actions">
          <el-button v-if="canWrite && !role.isSystem" plain :disabled="changing" @click="edit(role)">
            {{ t('roles.edit') }}
          </el-button>
          <el-button v-if="canWrite && !role.isSystem" plain :disabled="changing" @click="openPermissions(role)">
            {{ t('roles.permissions') }}
          </el-button>
          <el-button v-if="canWrite && !role.isSystem" plain :disabled="changing" @click="openDataScope(role)">
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
    </el-card>

    <el-dialog v-model="permissionsVisible" :title="t('roles.permissionsTitle')" width="520px">
      <el-checkbox-group v-model="selectedPermissions" class="art-dialog-grid">
        <el-checkbox v-for="permission in assignablePermissions" :key="permission" :label="permission" translate="no">
          {{ permission }}
        </el-checkbox>
      </el-checkbox-group>
      <template #footer>
        <el-button @click="permissionsVisible = false">{{ t('status.back') }}</el-button>
        <el-button type="primary" :loading="changing" @click="savePermissions">{{ t('roles.savePermissions') }}</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="dataScopeVisible" :title="t('roles.dataScopeTitle')" width="560px">
      <label class="art-dialog-field">
        <span>{{ t('roles.dataScopeKind') }}</span>
        <el-select :model-value="selectedDataScopeKind" @update:model-value="onDataScopeKindChange">
          <el-option v-for="kind in dataScopeKinds" :key="kind" :label="dataScopeKindLabel(kind)" :value="kind" />
        </el-select>
      </label>
      <p v-if="selectedDataScopeKind === 'identity.data_scope.custom' && !inTenantContext" class="art-dialog-hint">
        {{ t('roles.dataScopeTenantRequired') }}
      </p>
      <section v-if="selectedDataScopeKind === 'identity.data_scope.custom' && inTenantContext" class="art-dialog-grid">
        <span>{{ t('roles.dataScopeUnits') }}</span>
        <el-checkbox-group v-model="selectedUnitIds">
          <el-checkbox v-for="unit in orgUnits" :key="unit.id" :label="unit.id">
            <span translate="no">{{ unit.name }}</span>
            <code translate="no">{{ unit.code }}</code>
          </el-checkbox>
        </el-checkbox-group>
      </section>
      <template #footer>
        <el-button @click="dataScopeVisible = false">{{ t('status.back') }}</el-button>
        <el-button type="primary" :loading="changing" @click="saveDataScope">{{ t('roles.saveDataScope') }}</el-button>
      </template>
    </el-dialog>
  </section>
</template>
