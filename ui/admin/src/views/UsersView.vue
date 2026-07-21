<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, HostUser } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { createHostUser, disableHostUser, listHostUsers, updateHostUser } from '../api/users';

const session = useSessionStore();
const { t } = useAdminI18n();
const users = ref<HostUser[]>([]);
const username = ref('');
const displayName = ref('');
const password = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canWrite = computed(() => session.can('identity.users.write'));

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
  <section class="users-view" :aria-busy="loading">
    <header class="users-heading">
      <div>
        <p>{{ t('users.eyebrow') }}</p>
        <h1 data-route-heading tabindex="-1">{{ t('users.title') }}</h1>
        <span>{{ t('users.description') }}</span>
      </div>
    </header>

    <div v-if="problem" class="users-problem" role="alert">
      <strong translate="no">{{ problem.code }}</strong><span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <section v-if="canWrite" class="create-strip" aria-labelledby="create-title">
      <div><small>01</small><h2 id="create-title">{{ t('users.createTitle') }}</h2></div>
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
    </section>

    <section class="identity-ledger">
      <header>
        <div><small>02</small><h2>{{ t('users.directoryTitle') }}</h2></div>
        <b>{{ users.length }}</b>
      </header>
      <p v-if="users.length === 0" class="users-empty">{{ t('users.emptyDirectory') }}</p>
      <article v-for="user in users" :key="user.id">
        <span class="identity-mark">{{ user.username.slice(0, 2).toUpperCase() }}</span>
        <div>
          <strong translate="no">{{ user.displayName }}</strong>
          <code translate="no">{{ user.username }}</code>
        </div>
        <el-tag :type="user.isActive ? 'success' : 'info'">
          {{ t(user.isActive ? 'users.active' : 'users.inactive') }}
        </el-tag>
        <div class="users-actions">
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
            type="danger"
            plain
            :disabled="changing"
            @click="disable(user)"
          >
            {{ t('users.disable') }}
          </el-button>
        </div>
      </article>
    </section>
  </section>
</template>

<style scoped>
.users-view { display: grid; gap: 18px; }
.users-heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 24px; padding: 8px 2px; }
.users-heading p { margin: 0 0 10px; color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 10px; font-weight: 700; letter-spacing: .2em; }
.users-heading h1 { margin: 0; font-family: var(--fullnet-font-display); font-size: clamp(30px, 4vw, 48px); font-weight: 500; letter-spacing: -.05em; }
.users-heading span { display: block; margin-top: 10px; color: var(--fullnet-color-ink-muted); font-size: 13px; }
.users-problem { display: flex; gap: 14px; padding: 13px 16px; border-left: 3px solid var(--fullnet-color-danger); background: rgb(201 74 74 / 8%); }
.users-problem code { margin-left: auto; }
.create-strip { display: grid; grid-template-columns: minmax(160px, .7fr) repeat(3, minmax(180px, 1fr)) auto; align-items: end; gap: 16px; padding: 20px; border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-sidebar); color: #fff; }
.create-strip > div { align-self: center; }
.create-strip small, .identity-ledger small { color: var(--fullnet-color-accent-bright); font-family: var(--fullnet-font-display); }
.create-strip h2, .identity-ledger h2 { margin: 4px 0 0; font-size: 17px; }
.create-strip label span { display: block; margin-bottom: 7px; color: #aeb8b9; font-size: 11px; }
.identity-ledger { overflow: hidden; border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-panel); box-shadow: var(--fullnet-shadow-panel); }
.identity-ledger > header { display: flex; min-height: 66px; align-items: center; justify-content: space-between; padding: 0 22px; border-bottom: 1px solid var(--fullnet-color-line); }
.identity-ledger header div { gap: 12px; }
.identity-ledger article { display: grid; grid-template-columns: 44px minmax(180px, 1fr) auto auto; align-items: center; gap: 16px; padding: 15px 22px; border-bottom: 1px solid var(--fullnet-color-line); }
.users-actions { display: flex; gap: 8px; justify-content: flex-end; }
.identity-mark { display: grid; width: 40px; height: 40px; place-items: center; border-radius: 12px; background: var(--fullnet-color-ink); color: #fff; font-weight: 700; }
.identity-ledger article div { display: grid; gap: 4px; }
.identity-ledger code { color: var(--fullnet-color-ink-muted); font-size: 11px; }
.users-empty { padding: 28px; margin: 0; text-align: center; color: var(--fullnet-color-ink-muted); }
@media (max-width: 1080px) {
  .create-strip { grid-template-columns: 1fr; }
  .identity-ledger article { grid-template-columns: 44px 1fr auto; }
  .identity-ledger article .el-button { grid-column: 2 / -1; }
}
</style>
