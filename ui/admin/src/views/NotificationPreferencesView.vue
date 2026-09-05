<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect,
  ElTag
} from 'element-plus';
import type {
  NotificationProviderProfileResponse,
  RecipientEndpointResponse
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createMyRecipientEndpoint,
  deleteMyRecipientEndpoint,
  listMyRecipientEndpoints,
  listNotificationProviderProfiles,
  sendMyRecipientEndpointVerification,
  verifyMyRecipientEndpoint
} from '../api/notification-platform';

defineOptions({ name: 'NotificationPreferencesView' });

/** 当前切片只开放 SMTP 邮箱端点；静默时段和营销同意继续保持诚实的未交付状态。 */
const session = useSessionStore();
const { t } = useAdminI18n();
const profiles = ref<NotificationProviderProfileResponse[]>([]);
const endpoints = ref<RecipientEndpointResponse[]>([]);
const selectedProfileVersionId = ref('');
const rawEmail = ref('');
const loading = ref(false);
const saving = ref(false);
const deletingId = ref<string>();
const pendingDeleteId = ref<string>();
const verificationCodes = ref<Record<string, string>>({});
const resendAvailableAt = ref<Record<string, string>>({});
const clockNow = ref(Date.now());
const verifyingId = ref<string>();
const sendingId = ref<string>();
const errorMessage = ref<string>();
let resendClockTimer: ReturnType<typeof setInterval> | undefined;
const canUpdate = computed(() => session.can('notifications.preferences.update'));
const availableProfiles = computed(() => profiles.value.filter(profile =>
  profile.providerTypeKey === 'email.smtp'
  && profile.isEnabled
  && profile.latestPublishedVersionId !== null
));

onMounted(() => {
  resendClockTimer = setInterval(() => {
    clockNow.value = Date.now();
  }, 1000);
  void load();
});

onUnmounted(() => {
  if (resendClockTimer) {
    clearInterval(resendClockTimer);
  }
});

/** 同时加载可用邮件 Profile 与本人端点，任何失败都不伪造空成功状态。 */
async function load(): Promise<void> {
  loading.value = true;
  errorMessage.value = undefined;
  try {
    const [profilePage, endpointItems] = await Promise.all([
      listNotificationProviderProfiles(1, 100),
      listMyRecipientEndpoints()
    ]);
    profiles.value = profilePage.items;
    endpoints.value = endpointItems;
    const selectedStillAvailable = availableProfiles.value.some(profile =>
      profile.latestPublishedVersionId === selectedProfileVersionId.value
    );
    if (!selectedStillAvailable) {
      selectedProfileVersionId.value = availableProfiles.value[0]?.latestPublishedVersionId ?? '';
    }
  } catch {
    errorMessage.value = t('notificationPreferences.loadFailed');
  } finally {
    loading.value = false;
  }
}

/** 登记待验证邮箱；请求体不携带用户、租户或验证状态。 */
async function createEndpoint(): Promise<void> {
  const providerProfileVersionId = selectedProfileVersionId.value;
  const rawValue = rawEmail.value.trim();
  if (saving.value || !providerProfileVersionId || !rawValue) {
    return;
  }

  saving.value = true;
  errorMessage.value = undefined;
  try {
    await createMyRecipientEndpoint({
      providerProfileVersionId,
      endpointKindKey: 'email',
      rawValue
    });
    rawEmail.value = '';
    endpoints.value = await listMyRecipientEndpoints();
    ElMessage.success(t('notificationPreferences.createSuccess'));
  } catch {
    errorMessage.value = t('notificationPreferences.operationFailed');
  } finally {
    saving.value = false;
  }
}

/** 二次点击确认后删除本人端点，避免误操作立即使后续投递失去地址。 */
async function removeEndpoint(endpointId: string): Promise<void> {
  if (pendingDeleteId.value !== endpointId) {
    pendingDeleteId.value = endpointId;
    return;
  }

  deletingId.value = endpointId;
  errorMessage.value = undefined;
  try {
    await deleteMyRecipientEndpoint(endpointId);
    pendingDeleteId.value = undefined;
    endpoints.value = await listMyRecipientEndpoints();
    ElMessage.success(t('notificationPreferences.deleteSuccess'));
  } catch {
    errorMessage.value = t('notificationPreferences.operationFailed');
  } finally {
    deletingId.value = undefined;
  }
}

/** 计算指定端点距离允许重发还剩多少秒；未进入冷却时返回 0。 */
function resendRemainingSeconds(endpointId: string): number {
  const availableAt = resendAvailableAt.value[endpointId];
  if (!availableAt) {
    return 0;
  }
  const remaining = Math.ceil((Date.parse(availableAt) - clockNow.value) / 1000);
  return Math.max(0, remaining);
}

/** 发送按钮在冷却窗口内禁用，避免客户端绕过服务端 1 分钟限流。 */
function isResendCooldown(endpointId: string): boolean {
  return resendRemainingSeconds(endpointId) > 0;
}

/** 首次发送与冷却结束后的重发使用不同文案，冷却中展示倒计时。 */
function sendCodeLabel(endpointId: string): string {
  const remaining = resendRemainingSeconds(endpointId);
  if (remaining > 0) {
    return t('notificationPreferences.resendCountdown', { seconds: remaining });
  }
  return resendAvailableAt.value[endpointId]
    ? t('notificationPreferences.resendCode')
    : t('notificationPreferences.sendCode');
}

async function sendVerification(endpointId: string): Promise<void> {
  if (sendingId.value || isResendCooldown(endpointId) || !canUpdate.value) {
    return;
  }
  sendingId.value = endpointId;
  errorMessage.value = undefined;
  try {
    const response = await sendMyRecipientEndpointVerification(endpointId);
    resendAvailableAt.value[endpointId] = response.resendAvailableAtUtc;
    ElMessage.success(t('notificationPreferences.sendCodeSuccess'));
  } catch {
    errorMessage.value = t('notificationPreferences.operationFailed');
  } finally {
    sendingId.value = undefined;
  }
}

async function verifyEndpoint(endpointId: string): Promise<void> {
  const code = verificationCodes.value[endpointId]?.trim() ?? '';
  if (!code || verifyingId.value || !canUpdate.value) {
    return;
  }
  verifyingId.value = endpointId;
  errorMessage.value = undefined;
  try {
    const verified = await verifyMyRecipientEndpoint(endpointId, code);
    endpoints.value = endpoints.value.map(item =>
      item.id === endpointId ? verified : item
    );
    verificationCodes.value[endpointId] = '';
    delete resendAvailableAt.value[endpointId];
    ElMessage.success(t('notificationPreferences.verifySuccess'));
  } catch {
    errorMessage.value = t('notificationPreferences.operationFailed');
  } finally {
    verifyingId.value = undefined;
  }
}

/** 将服务端稳定状态键映射为本地化文案，未知值保持可识别而不显示成功色。 */
function statusText(status: string): string {
  switch (status) {
    case 'pending':
      return t('notificationPreferences.status.pending');
    case 'verified':
      return t('notificationPreferences.status.verified');
    case 'failed':
      return t('notificationPreferences.status.failed');
    default:
      return status;
  }
}

/** 通过不可变 ProfileVersion 找到用户可识别的渠道配置名称。 */
function profileLabel(profileVersionId: string): string {
  return profiles.value.find(profile =>
    profile.latestPublishedVersionId === profileVersionId
  )?.profileKey ?? profileVersionId;
}
</script>

<template>
  <section class="notification-preferences-view art-page-stack">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('notificationPreferences.eyebrow') }}</p>
      <h1 data-route-heading tabindex="-1">{{ t('notificationPreferences.title') }}</h1>
      <p>{{ t('notificationPreferences.description') }}</p>
    </header>

    <ElAlert
      v-if="errorMessage"
      type="error"
      :title="errorMessage"
      :closable="false"
      show-icon
    />

    <ElCard shadow="never">
      <template #header>
        <div class="art-section-heading">
          <div>
            <h2>{{ t('notificationPreferences.endpointTitle') }}</h2>
            <p>{{ t('notificationPreferences.endpointHint') }}</p>
          </div>
          <ElButton :loading="loading" data-testid="notification-preferences-load" @click="load">
            {{ t('notificationPreferences.refresh') }}
          </ElButton>
        </div>
      </template>

      <ElAlert
        type="warning"
        :title="t('notificationPreferences.pendingWarning')"
        :closable="false"
        show-icon
      />
      <ElAlert
        type="info"
        :title="t('notificationPreferences.externalAuthNotice')"
        :closable="false"
        show-icon
      />

      <div
        v-if="canUpdate && availableProfiles.length > 0"
        class="recipient-endpoint-form"
      >
        <ElSelect
          v-model="selectedProfileVersionId"
          data-testid="notification-preferences-profile"
          :aria-label="t('notificationPreferences.profile')"
        >
          <ElOption
            v-for="profile in availableProfiles"
            :key="profile.latestPublishedVersionId!"
            :label="profile.profileKey"
            :value="profile.latestPublishedVersionId!"
          />
        </ElSelect>
        <ElInput
          v-model="rawEmail"
          data-testid="notification-preferences-email"
          type="email"
          :placeholder="t('notificationPreferences.emailPlaceholder')"
          @keyup.enter="createEndpoint"
        />
        <ElButton
          type="primary"
          :loading="saving"
          data-testid="notification-preferences-save"
          @click="createEndpoint"
        >
          {{ t('notificationPreferences.register') }}
        </ElButton>
      </div>
      <ElAlert
        v-else-if="canUpdate"
        data-testid="notification-preferences-no-profile"
        type="info"
        :title="t('notificationPreferences.noProfile')"
        :closable="false"
      />

      <div
        class="recipient-endpoint-list"
        data-testid="notification-preferences-endpoint-list"
      >
        <article
          v-for="endpoint in endpoints"
          :key="endpoint.id"
          class="recipient-endpoint-item"
        >
          <div>
            <strong>{{ endpoint.maskedValue }}</strong>
            <p>{{ profileLabel(endpoint.providerProfileVersionId) }}</p>
          </div>
          <div class="recipient-endpoint-actions">
            <ElTag :type="endpoint.verificationStatusKey === 'verified' ? 'success' : 'warning'">
              {{ statusText(endpoint.verificationStatusKey) }}
            </ElTag>
            <div
              v-if="canUpdate && endpoint.verificationStatusKey === 'pending'"
              class="recipient-endpoint-verify"
            >
              <ElInput
                v-model="verificationCodes[endpoint.id]"
                data-testid="notification-preferences-code"
                maxlength="6"
                inputmode="numeric"
                :placeholder="t('notificationPreferences.codePlaceholder')"
              />
              <ElButton
                :loading="sendingId === endpoint.id"
                :disabled="isResendCooldown(endpoint.id)"
                data-testid="notification-preferences-send-code"
                @click="sendVerification(endpoint.id)"
              >
                {{ sendCodeLabel(endpoint.id) }}
              </ElButton>
              <ElButton
                type="primary"
                :loading="verifyingId === endpoint.id"
                data-testid="notification-preferences-verify"
                @click="verifyEndpoint(endpoint.id)"
              >
                {{ t('notificationPreferences.verifyCode') }}
              </ElButton>
            </div>
            <ElButton
              v-if="canUpdate"
              text
              type="danger"
              :loading="deletingId === endpoint.id"
              data-testid="notification-preferences-delete"
              @click="removeEndpoint(endpoint.id)"
            >
              {{ pendingDeleteId === endpoint.id
                ? t('notificationPreferences.confirmDelete')
                : t('notificationPreferences.delete') }}
            </ElButton>
          </div>
        </article>
        <p v-if="!loading && endpoints.length === 0" class="art-empty-state">
          {{ t('notificationPreferences.empty') }}
        </p>
      </div>
    </ElCard>

    <ElCard shadow="never">
      <h2>{{ t('notificationPreferences.policyTitle') }}</h2>
      <p>{{ t('notificationPreferences.policyUnavailable') }}</p>
    </ElCard>
  </section>
</template>

<style scoped>
.recipient-endpoint-form,
.recipient-endpoint-item,
.recipient-endpoint-actions,
.art-section-heading {
  display: flex;
  align-items: center;
  gap: 12px;
}

.recipient-endpoint-verify {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.recipient-endpoint-verify .el-input {
  width: 140px;
}

.art-section-heading,
.recipient-endpoint-item {
  justify-content: space-between;
}

.recipient-endpoint-form {
  margin: 18px 0;
}

.recipient-endpoint-form .el-select {
  width: 220px;
}

.recipient-endpoint-list {
  display: grid;
  gap: 10px;
  margin-top: 18px;
}

.recipient-endpoint-item {
  padding: 14px 16px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 10px;
}

.recipient-endpoint-item p,
.art-section-heading p {
  margin: 4px 0 0;
  color: var(--el-text-color-secondary);
}

@media (max-width: 720px) {
  .recipient-endpoint-form,
  .recipient-endpoint-item {
    align-items: stretch;
    flex-direction: column;
  }

  .recipient-endpoint-form .el-select {
    width: 100%;
  }
}
</style>
