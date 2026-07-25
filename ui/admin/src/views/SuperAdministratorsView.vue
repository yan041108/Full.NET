<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElTag
} from 'element-plus';
import type {
  FullNetProblemDetails,
  SuperAdministrator,
  SuperAdministratorAudit,
  TotpEnrollmentStatus
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
import {
  beginTotpEnrollment,
  confirmTotpEnrollment,
  getTotpEnrollmentStatus
} from '../api/totpEnrollment';

const session = useSessionStore();
const { t } = useAdminI18n();
const administrators = ref<SuperAdministrator[]>([]);
const audits = ref<SuperAdministratorAudit[]>([]);
const totpStatus = ref<TotpEnrollmentStatus>({ isEnrolled: false, isEnabled: false });
const pendingSecret = ref('');
const pendingOtpAuthUri = ref('');
const enrollCode = ref('');
const username = ref('');
const currentPassword = ref('');
const totpCode = ref('');
const loading = ref(false);
const changing = ref(false);
const enrolling = ref(false);
const problem = ref<FullNetProblemDetails>();
const canManage = computed(() => session.can('identity.super_administrators.manage'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [admins, auditRows, status] = await Promise.all([
      getSuperAdministrators(),
      getSuperAdministratorAudits(),
      getTotpEnrollmentStatus().catch(() => ({ isEnrolled: false, isEnabled: false }))
    ]);
    administrators.value = admins;
    audits.value = auditRows;
    totpStatus.value = status;
    if (status.isEnabled) {
      pendingSecret.value = '';
      pendingOtpAuthUri.value = '';
      enrollCode.value = '';
    }
  } catch (error: unknown) {
    problem.value = toProblem(error, 'superAdmin.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function beginEnroll(): Promise<void> {
  if (enrolling.value) return;
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
  if (enrolling.value || !enrollCode.value.trim()) return;
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

async function grant(): Promise<void> {
  if (changing.value || !username.value.trim() || !currentPassword.value) return;
  changing.value = true;
  problem.value = undefined;
  try {
    await grantSuperAdministrator(
      username.value.trim(),
      currentPassword.value,
      totpCode.value.trim() || undefined);
    username.value = '';
    totpCode.value = '';
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
    let code = '';
    try {
      const totpPrompt = await ElMessageBox.prompt(
        t('superAdmin.confirmRevokeTotp'),
        t('superAdmin.totpCode'),
        {
          inputPattern: /^$|^\d{6}$/,
          inputPlaceholder: t('superAdmin.totpCodePlaceholder'),
          showCancelButton: true
        }
      );
      code = totpPrompt.value.trim();
    } catch (error: unknown) {
      if (error === 'cancel' || error === 'close') return;
      throw error;
    }
    changing.value = true;
    await revokeSuperAdministrator(
      administrator.userId,
      password.value,
      code || undefined);
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

    <el-card v-if="canManage" class="art-form-card" shadow="never" data-super-admin-grant-form>
      <div class="art-form-grid art-form-grid--cols-3" aria-labelledby="grant-title">
        <div><h2 id="grant-title">{{ t('superAdmin.grantTitle') }}</h2></div>
        <label>
          <span>{{ t('superAdmin.username') }}</span>
          <el-input v-model="username" :placeholder="t('superAdmin.usernamePlaceholder')" />
        </label>
        <label>
          <span>{{ t('superAdmin.currentPassword') }}</span>
          <el-input v-model="currentPassword" type="password" show-password :placeholder="t('superAdmin.passwordPlaceholder')" />
        </label>
        <label>
          <span>{{ t('superAdmin.totpCode') }}</span>
          <el-input v-model="totpCode" name="totpCode" maxlength="6" :placeholder="t('superAdmin.totpCodePlaceholder')" @keyup.enter="grant" />
        </label>
        <el-button type="primary" :loading="changing" @click="grant">{{ t('superAdmin.grant') }}</el-button>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('superAdmin.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ administrators.length }}</span>
        </div>
      </template>

      <article v-for="administrator in administrators" :key="administrator.userId" class="art-data-row">
        <span class="art-data-row__avatar">{{ administrator.username.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ administrator.displayName }}</strong>
          <code translate="no">{{ administrator.username }}</code>
        </div>
        <el-tag :type="administrator.isActive ? 'success' : 'info'">
          {{ t(administrator.isActive ? 'superAdmin.active' : 'superAdmin.inactive') }}
        </el-tag>
        <div class="art-data-row__actions">
          <el-button v-if="canManage" type="danger" plain :disabled="changing" @click="revoke(administrator)">
            {{ t('superAdmin.revoke') }}
          </el-button>
        </div>
      </article>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('superAdmin.auditTitle') }}</h2>
        </div>
      </template>

      <p v-if="audits.length === 0" class="art-empty-state">{{ t('superAdmin.emptyAudit') }}</p>
      <ol v-else class="art-audit-list">
        <li v-for="audit in audits" :key="audit.id">
          <time translate="no">{{ new Date(audit.occurredAtUtc).toLocaleString() }}</time>
          <strong translate="no">{{ audit.eventType }}</strong>
          <code translate="no">{{ audit.actorUserId ?? 'system' }} → {{ audit.targetUserId }}</code>
        </li>
      </ol>
    </el-card>
  </section>
</template>

<style scoped>
.super-admin-view__toolbar {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 4px;
}
</style>
