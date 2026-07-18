<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElTag
} from 'element-plus';
import type {
  FullNetProblemDetails,
  SuperAdministrator,
  SuperAdministratorAudit
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getSuperAdministratorAudits,
  getSuperAdministrators,
  grantSuperAdministrator,
  revokeSuperAdministrator
} from '../api/superAdministrators';

const session = useSessionStore();
const { t } = useAdminI18n();
const administrators = ref<SuperAdministrator[]>([]);
const audits = ref<SuperAdministratorAudit[]>([]);
const username = ref('');
const currentPassword = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canManage = computed(() => session.can('identity.super_administrators.manage'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    [administrators.value, audits.value] = await Promise.all([
      getSuperAdministrators(),
      getSuperAdministratorAudits()
    ]);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'superAdmin.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function grant(): Promise<void> {
  if (changing.value || !username.value.trim() || !currentPassword.value) return;
  changing.value = true;
  problem.value = undefined;
  try {
    await grantSuperAdministrator(username.value.trim(), currentPassword.value);
    username.value = '';
    ElMessage.success(t('superAdmin.grantSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'superAdmin.operationFailed');
  } finally {
    currentPassword.value = '';
    changing.value = false;
  }
}

async function revoke(administrator: SuperAdministrator): Promise<void> {
  if (changing.value) return;
  try {
    const password = await ElMessageBox.prompt(
      t('superAdmin.confirmRevoke', { name: administrator.username }),
      t('superAdmin.revoke'),
      { inputType: 'password', inputPattern: /.+/, showCancelButton: true }
    );
    changing.value = true;
    await revokeSuperAdministrator(administrator.userId, password.value);
    ElMessage.success(t('superAdmin.revokeSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'superAdmin.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(error: unknown, fallbackKey: 'superAdmin.loadFailed' | 'superAdmin.operationFailed'): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.super_administrator_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="super-admin-view" :aria-busy="loading">
    <header class="super-admin-heading">
      <div>
        <p>{{ t('superAdmin.eyebrow') }}</p>
        <h1 data-route-heading tabindex="-1">{{ t('superAdmin.title') }}</h1>
        <span>{{ t('superAdmin.description') }}</span>
      </div>
      <el-tag effect="dark" type="danger">{{ t('superAdmin.protected') }}</el-tag>
    </header>

    <div v-if="problem" class="super-admin-problem" role="alert">
      <strong translate="no">{{ problem.code }}</strong><span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <section v-if="canManage" class="grant-strip" aria-labelledby="grant-title">
      <div><small>01</small><h2 id="grant-title">{{ t('superAdmin.grantTitle') }}</h2></div>
      <label><span>{{ t('superAdmin.username') }}</span><el-input v-model="username" :placeholder="t('superAdmin.usernamePlaceholder')" /></label>
      <label><span>{{ t('superAdmin.currentPassword') }}</span><el-input v-model="currentPassword" type="password" show-password :placeholder="t('superAdmin.passwordPlaceholder')" @keyup.enter="grant" /></label>
      <el-button type="primary" :loading="changing" @click="grant">{{ t('superAdmin.grant') }}</el-button>
    </section>

    <section class="identity-ledger">
      <header><div><small>02</small><h2>{{ t('superAdmin.directoryTitle') }}</h2></div><b>{{ administrators.length }}</b></header>
      <article v-for="administrator in administrators" :key="administrator.userId">
        <span class="identity-mark">{{ administrator.username.slice(0, 2).toUpperCase() }}</span>
        <div><strong translate="no">{{ administrator.displayName }}</strong><code translate="no">{{ administrator.username }}</code></div>
        <el-tag :type="administrator.isActive ? 'success' : 'info'">{{ t(administrator.isActive ? 'superAdmin.active' : 'superAdmin.inactive') }}</el-tag>
        <el-button v-if="canManage" type="danger" plain :disabled="changing" @click="revoke(administrator)">{{ t('superAdmin.revoke') }}</el-button>
      </article>
    </section>

    <section class="audit-ledger">
      <header><small>03</small><h2>{{ t('superAdmin.auditTitle') }}</h2></header>
      <p v-if="audits.length === 0">{{ t('superAdmin.emptyAudit') }}</p>
      <ol v-else>
        <li v-for="audit in audits" :key="audit.id">
          <time translate="no">{{ new Date(audit.occurredAtUtc).toLocaleString() }}</time>
          <strong translate="no">{{ audit.eventType }}</strong>
          <code translate="no">{{ audit.actorUserId ?? 'system' }} → {{ audit.targetUserId }}</code>
        </li>
      </ol>
    </section>
  </section>
</template>

<style scoped>
.super-admin-view { display: grid; gap: 18px; }
.super-admin-heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 24px; padding: 8px 2px; }
.super-admin-heading p { margin: 0 0 10px; color: var(--fullnet-color-danger); font-family: var(--fullnet-font-display); font-size: 10px; font-weight: 700; letter-spacing: .2em; }
.super-admin-heading h1 { margin: 0; font-family: var(--fullnet-font-display); font-size: clamp(30px, 4vw, 48px); font-weight: 500; letter-spacing: -.05em; }
.super-admin-heading span { display: block; margin-top: 10px; color: var(--fullnet-color-ink-muted); font-size: 13px; }
.super-admin-problem { display: flex; gap: 14px; padding: 13px 16px; border-left: 3px solid var(--fullnet-color-danger); background: rgb(201 74 74 / 8%); }
.super-admin-problem code { margin-left: auto; }
.grant-strip { display: grid; grid-template-columns: minmax(160px, .7fr) minmax(220px, 1fr) minmax(240px, 1fr) auto; align-items: end; gap: 16px; padding: 20px; border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-sidebar); color: #fff; }
.grant-strip > div { align-self: center; }.grant-strip small,.identity-ledger small,.audit-ledger small { color: var(--fullnet-color-accent-bright); font-family: var(--fullnet-font-display); }.grant-strip h2 { margin: 4px 0 0; font-size: 17px; }.grant-strip label span { display: block; margin-bottom: 7px; color: #aeb8b9; font-size: 11px; }
.identity-ledger,.audit-ledger { overflow: hidden; border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-panel); box-shadow: var(--fullnet-shadow-panel); }
.identity-ledger > header,.audit-ledger > header { display: flex; min-height: 66px; align-items: center; justify-content: space-between; padding: 0 22px; border-bottom: 1px solid var(--fullnet-color-line); }.identity-ledger header div,.audit-ledger header { gap: 12px; }.identity-ledger h2,.audit-ledger h2 { margin: 0; font-size: 15px; }
.identity-ledger article { display: grid; grid-template-columns: 44px minmax(180px, 1fr) auto auto; align-items: center; gap: 16px; padding: 15px 22px; border-bottom: 1px solid var(--fullnet-color-line); }.identity-mark { display: grid; width: 40px; height: 40px; place-items: center; border-radius: 12px; background: var(--fullnet-color-ink); color: #fff; font-weight: 700; }.identity-ledger article div { display: grid; gap: 4px; }.identity-ledger code { color: var(--fullnet-color-ink-muted); font-size: 11px; }
.audit-ledger ol { margin: 0; padding: 0; list-style: none; }.audit-ledger li { display: grid; grid-template-columns: 180px minmax(250px, 1fr) minmax(260px, 1.4fr); gap: 18px; padding: 13px 22px; border-bottom: 1px solid var(--fullnet-color-line); font-size: 11px; }.audit-ledger time,.audit-ledger code { color: var(--fullnet-color-ink-muted); overflow: hidden; text-overflow: ellipsis; }.audit-ledger > p { padding: 28px; text-align: center; color: var(--fullnet-color-ink-muted); }
@media (max-width: 920px) { .grant-strip { grid-template-columns: 1fr; }.identity-ledger article { grid-template-columns: 44px 1fr auto; }.identity-ledger article .el-button { grid-column: 2 / -1; }.audit-ledger li { grid-template-columns: 1fr; gap: 5px; } }
</style>
