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
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, HostRole, HostUser } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { createHostUser, disableHostUser, enableHostUser, exportHostUsers, getHostUserRoles, listHostUsers, replaceHostUserRoles, resetHostUserPassword, updateHostUser } from '../api/users';
import { listHostRoles } from '../api/roles';

const session = useSessionStore();
const { t } = useAdminI18n();
const users = ref<HostUser[]>([]);
const username = ref('');
const displayName = ref('');
const password = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const rolesVisible = ref(false);
const editingUser = ref<HostUser>();
const assignableRoles = ref<HostRole[]>([]);
const selectedRoleIds = ref<string[]>([]);
const rolesVersion = ref(0);
const canWrite = computed(() => session.can('identity.users.write'));
const canExport = computed(() => session.can('identity.users.export'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listHostUsers();
    users.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value || !username.value.trim() || !displayName.value.trim() || !password.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createHostUser(
      username.value.trim(),
      displayName.value.trim(),
      password.value
    );
    username.value = '';
    displayName.value = '';
    password.value = '';
    ElMessage.success(t('users.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function exportUsers(): Promise<void> {
  if (changing.value) return;
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

async function edit(user: HostUser): Promise<void> {
  if (changing.value) return;
  try {
    const result = await ElMessageBox.prompt(
      t('users.editTitle'),
      t('users.edit'),
      {
        inputValue: user.displayName,
        inputPattern: /.+/,
        showCancelButton: true
      }
    );
    changing.value = true;
    await updateHostUser(user.id, result.value.trim(), user.version);
    ElMessage.success(t('users.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function resetPassword(user: HostUser): Promise<void> {
  if (changing.value || !user.isActive) return;
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
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(user: HostUser): Promise<void> {
  if (changing.value || !user.isActive) return;
  try {
    await ElMessageBox.confirm(
      t('users.confirmDisable', { name: user.username }),
      t('users.disable'),
      { type: 'warning', confirmButtonText: t('users.disable'), cancelButtonText: t('status.back') }
    );
    changing.value = true;
    await disableHostUser(user.id);
    ElMessage.success(t('users.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function enable(user: HostUser): Promise<void> {
  if (changing.value || user.isActive) return;
  try {
    await ElMessageBox.confirm(
      t('users.confirmEnable', { name: user.username }),
      t('users.enable'),
      { type: 'warning', confirmButtonText: t('users.enable'), cancelButtonText: t('status.back') }
    );
    changing.value = true;
    await enableHostUser(user.id);
    ElMessage.success(t('users.enableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function openRoles(user: HostUser): Promise<void> {
  if (changing.value) return;
  changing.value = true;
  problem.value = undefined;
  try {
    const [rolesPage, userRoles] = await Promise.all([
      listHostRoles(),
      getHostUserRoles(user.id)
    ]);
    assignableRoles.value = rolesPage.items.filter(
      role => role.isActive && !role.isSystem && !role.isSuperAdministrator
    );
    editingUser.value = user;
    selectedRoleIds.value = [...userRoles.roleIds];
    rolesVersion.value = userRoles.version;
    rolesVisible.value = true;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function saveRoles(): Promise<void> {
  const user = editingUser.value;
  if (!user || changing.value) return;
  changing.value = true;
  problem.value = undefined;
  try {
    await replaceHostUserRoles(
      user.id,
      [...selectedRoleIds.value].sort(),
      rolesVersion.value
    );
    rolesVisible.value = false;
    editingUser.value = undefined;
    ElMessage.success(t('users.rolesSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'users.operationFailed');
  } finally {
    changing.value = false;
  }
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

    <el-card v-if="canWrite" class="art-form-card" shadow="never">
      <div class="art-form-grid" aria-labelledby="create-title">
        <div>
          <h2 id="create-title">{{ t('users.createTitle') }}</h2>
        </div>
        <label>
          <span>{{ t('users.username') }}</span>
          <el-input v-model="username" :placeholder="t('users.usernamePlaceholder')" />
        </label>
        <label>
          <span>{{ t('users.displayName') }}</span>
          <el-input v-model="displayName" :placeholder="t('users.displayNamePlaceholder')" />
        </label>
        <label>
          <span>{{ t('users.password') }}</span>
          <el-input
            v-model="password"
            type="password"
            show-password
            :placeholder="t('users.passwordPlaceholder')"
            @keyup.enter="create"
          />
        </label>
        <el-button type="primary" :loading="changing" @click="create">{{ t('users.create') }}</el-button>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('users.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ users.length }}</span>
          <el-button v-if="canExport" plain :disabled="changing" @click="exportUsers">
            {{ t('users.export') }}
          </el-button>
        </div>
      </template>

      <p v-if="users.length === 0" class="users-empty">{{ t('users.emptyDirectory') }}</p>
      <article v-for="user in users" :key="user.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ user.username.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ user.displayName }}</strong>
          <code translate="no">{{ user.username }}</code>
          <small v-if="user.projectedFields?.effectiveFieldKeys.includes('preferred_locale')">
            locale: <code translate="no">{{ user.projectedFields.preferredLocale ?? '—' }}</code>
          </small>
          <small v-if="user.projectedFields?.effectiveFieldKeys.includes('failed_login_count')">
            failed-login: {{ user.projectedFields.failedLoginCount ?? 0 }}
          </small>
          <small v-if="user.projectedFields?.effectiveFieldKeys.includes('lockout_end_utc')">
            lockout: <time translate="no">{{ user.projectedFields.lockoutEndUtc ?? '—' }}</time>
          </small>
        </div>
        <el-tag :type="user.isActive ? 'success' : 'info'">
          {{ t(user.isActive ? 'users.active' : 'users.inactive') }}
        </el-tag>
        <div class="art-data-row__actions">
          <el-button
            v-if="canWrite"
            plain
            :disabled="changing"
            @click="openRoles(user)"
          >
            {{ t('users.roles') }}
          </el-button>
          <el-button
            v-if="canWrite"
            plain
            :disabled="changing"
            @click="edit(user)"
          >
            {{ t('users.edit') }}
          </el-button>
          <el-button
            v-if="canWrite && user.isActive"
            plain
            :disabled="changing"
            @click="resetPassword(user)"
          >
            {{ t('users.resetPassword') }}
          </el-button>
          <el-button
            v-if="canWrite && user.isActive"
            type="danger"
            plain
            :disabled="changing"
            @click="disable(user)"
          >
            {{ t('users.disable') }}
          </el-button>
          <el-button
            v-if="canWrite && !user.isActive"
            type="success"
            plain
            :disabled="changing"
            @click="enable(user)"
          >
            {{ t('users.enable') }}
          </el-button>
        </div>
      </article>
    </el-card>

    <el-dialog
      v-model="rolesVisible"
      :title="t('users.rolesTitle')"
      width="520px"
    >
      <el-checkbox-group v-model="selectedRoleIds" class="users-roles">
        <el-checkbox
          v-for="role in assignableRoles"
          :key="role.id"
          :value="role.id"
        >
          {{ role.name }}
          <code translate="no">{{ role.code }}</code>
        </el-checkbox>
      </el-checkbox-group>
      <template #footer>
        <el-button @click="rolesVisible = false">{{ t('status.back') }}</el-button>
        <el-button type="primary" :loading="changing" @click="saveRoles">
          {{ t('users.saveRoles') }}
        </el-button>
      </template>
    </el-dialog>
  </section>
</template>

<style scoped>
.users-empty {
  margin: 0;
  padding: 28px 20px;
  color: var(--art-gray-600);
  text-align: center;
}

.users-roles {
  display: grid;
  gap: 10px;
}

.users-roles code {
  margin-left: 8px;
  color: var(--art-gray-600);
  font-size: 11px;
}
</style>
