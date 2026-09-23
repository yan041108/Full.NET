<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, reactive, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  type FormInstance,
  type FormRules
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
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';

defineOptions({ name: 'NotificationPreferencesView' });

/** 当前切片开放 SMTP 邮箱、阿里云短信、钉钉与企微 userId 端点；静默时段和营销同意继续保持诚实的未交付状态。 */
const session = useSessionStore();
const { t } = useAdminI18n();
const profiles = ref<NotificationProviderProfileResponse[]>([]);
const endpoints = ref<RecipientEndpointResponse[]>([]);
const registerFormRef = ref<FormInstance>();
const registerForm = reactive({
  providerProfileVersionId: '',
  rawValue: ''
});
const loading = ref(false);
const { tableMainRef, tableHeight, updateTableHeight, watchLoading } = useArtCrudTableLayout({
  bottomOffset: 8
});
watchLoading(loading);
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
  (profile.providerTypeKey === 'email.smtp'
    || profile.providerTypeKey === 'sms.aliyun'
    || profile.providerTypeKey === 'im.dingtalk'
    || profile.providerTypeKey === 'im.wecom')
  && profile.isEnabled
  && profile.latestPublishedVersionId !== null
));
const selectedProfile = computed(() =>
  availableProfiles.value.find(profile =>
    profile.latestPublishedVersionId === registerForm.providerProfileVersionId)
);
const registerRules = computed<FormRules>(() => {
  const message = t('notificationPreferences.validationRequired');
  const requiredTextRule = {
    validator: (_rule: unknown, value: unknown, callback: (error?: Error) => void) => {
      if (typeof value !== 'string' || !value.trim()) {
        callback(new Error(message));
        return;
      }
      callback();
    },
    trigger: ['blur', 'change'] as const
  };
  return {
    providerProfileVersionId: [requiredTextRule],
    rawValue: [requiredTextRule]
  };
});
const selectedEndpointKind = computed(() => {
  switch (selectedProfile.value?.providerTypeKey) {
    case 'sms.aliyun':
      return 'sms';
    case 'im.dingtalk':
      return 'dingtalk';
    case 'im.wecom':
      return 'wecom';
    default:
      return 'email';
  }
});
const selectedEndpointInputTestId = computed(() => {
  switch (selectedEndpointKind.value) {
    case 'sms':
      return 'notification-preferences-phone';
    case 'dingtalk':
      return 'notification-preferences-dingtalk-user-id';
    case 'wecom':
      return 'notification-preferences-wecom-user-id';
    default:
      return 'notification-preferences-email';
  }
});
const selectedEndpointInputType = computed(() => {
  switch (selectedEndpointKind.value) {
    case 'sms':
      return 'tel';
    case 'dingtalk':
      return 'text';
    default:
      return 'email';
  }
});
const selectedEndpointPlaceholder = computed(() => {
  switch (selectedEndpointKind.value) {
    case 'sms':
      return t('notificationPreferences.phonePlaceholder');
    case 'dingtalk':
      return t('notificationPreferences.dingtalkUserIdPlaceholder');
    case 'wecom':
      return t('notificationPreferences.wecomUserIdPlaceholder');
    default:
      return t('notificationPreferences.emailPlaceholder');
  }
});

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
      profile.latestPublishedVersionId === registerForm.providerProfileVersionId
    );
    if (!selectedStillAvailable) {
      registerForm.providerProfileVersionId = availableProfiles.value[0]?.latestPublishedVersionId ?? '';
    }
  } catch {
    errorMessage.value = t('notificationPreferences.loadFailed');
  } finally {
    loading.value = false;
    void nextTick(updateTableHeight);
  }
}

function passesRegisterValidation(): boolean {
  return Boolean(registerForm.providerProfileVersionId.trim())
    && Boolean(registerForm.rawValue.trim());
}

async function validateRegisterForm(): Promise<boolean> {
  const form = registerFormRef.value;
  if (!form) {
    return false;
  }
  let formValid = true;
  try {
    await form.validate();
  } catch {
    formValid = false;
  }
  if (!passesRegisterValidation()) {
    const fields = ['providerProfileVersionId', 'rawValue'] as const;
    for (const field of fields) {
      try {
        await form.validateField(field);
      } catch {
        /* 单字段错误由表单项展示 */
      }
    }
    return false;
  }
  return formValid;
}

/** 登记待验证端点；请求体不携带用户、租户或验证状态。 */
async function createEndpoint(): Promise<void> {
  if (saving.value || !(await validateRegisterForm())) {
    return;
  }

  saving.value = true;
  errorMessage.value = undefined;
  try {
    await createMyRecipientEndpoint({
      providerProfileVersionId: registerForm.providerProfileVersionId,
      endpointKindKey: selectedEndpointKind.value,
      rawValue: registerForm.rawValue.trim()
    });
    registerForm.rawValue = '';
    registerFormRef.value?.clearValidate(['rawValue']);
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

function endpointNeedsCodeVerification(endpoint: RecipientEndpointResponse): boolean {
  return endpoint.verificationStatusKey === 'pending'
    && endpoint.endpointKindKey !== 'dingtalk'
    && endpoint.endpointKindKey !== 'wecom';
}
</script>

<template>
  <section class="notification-preferences-view art-page-stack art-full-height">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('notificationPreferences.title') }}</h1>
    <p class="art-sr-heading">{{ t('notificationPreferences.description') }}</p>

    <ElAlert
      v-if="errorMessage"
      type="error"
      :title="errorMessage"
      :closable="false"
      show-icon
      class="notification-preferences-error"
    />

    <div class="notification-preferences-layout art-split-layout">
      <ElCard class="notification-preferences-list art-table-card" shadow="never">
        <template #header>
          <div class="notification-preferences-list__header">
            <div>
              <h2>{{ t('notificationPreferences.endpointTitle') }}</h2>
              <p class="art-muted">{{ t('notificationPreferences.endpointHint') }}</p>
            </div>
            <ElButton :loading="loading" data-testid="notification-preferences-load" @click="load">
              {{ t('notificationPreferences.refresh') }}
            </ElButton>
          </div>
        </template>

        <div class="notification-preferences-notices">
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
        </div>

        <div
          ref="tableMainRef"
          data-testid="notification-preferences-endpoint-list"
          class="art-crud-table-main notification-preferences-table-wrap"
        >
          <ElTable
            v-loading="loading"
            :data="endpoints"
            :height="tableHeight"
            class="notification-preferences-table"
            row-key="id"
            empty-text=""
          >
            <ElTableColumn :label="t('notificationPreferences.fieldEndpoint')" min-width="140">
              <template #default="{ row }">
                <strong translate="no">{{ row.maskedValue }}</strong>
              </template>
            </ElTableColumn>

            <ElTableColumn :label="t('notificationPreferences.profile')" min-width="120" show-overflow-tooltip>
              <template #default="{ row }">
                <span translate="no">{{ profileLabel(row.providerProfileVersionId) }}</span>
              </template>
            </ElTableColumn>

            <ElTableColumn :label="t('users.status')" width="100" align="center">
              <template #default="{ row }">
                <ElTag
                  size="small"
                  :type="row.verificationStatusKey === 'verified' ? 'success' : 'warning'"
                >
                  {{ statusText(row.verificationStatusKey) }}
                </ElTag>
              </template>
            </ElTableColumn>

            <ElTableColumn
              v-if="canUpdate"
              :label="t('notificationPreferences.actions')"
              min-width="320"
              class-name="notification-preferences-actions-col"
            >
              <template #default="{ row }">
                <div class="recipient-endpoint-actions">
                  <div
                    v-if="endpointNeedsCodeVerification(row as RecipientEndpointResponse)"
                    class="recipient-endpoint-verify"
                  >
                    <ElInput
                      v-model="verificationCodes[row.id]"
                      data-testid="notification-preferences-code"
                      maxlength="6"
                      inputmode="numeric"
                      :placeholder="t('notificationPreferences.codePlaceholder')"
                    />
                    <ElButton
                      :loading="sendingId === row.id"
                      :disabled="isResendCooldown(row.id)"
                      data-testid="notification-preferences-send-code"
                      @click="sendVerification(row.id)"
                    >
                      {{ sendCodeLabel(row.id) }}
                    </ElButton>
                    <ElButton
                      type="primary"
                      :loading="verifyingId === row.id"
                      data-testid="notification-preferences-verify"
                      @click="verifyEndpoint(row.id)"
                    >
                      {{ t('notificationPreferences.verifyCode') }}
                    </ElButton>
                  </div>
                  <ElButton
                    text
                    type="danger"
                    :loading="deletingId === row.id"
                    data-testid="notification-preferences-delete"
                    @click="removeEndpoint(row.id)"
                  >
                    {{ pendingDeleteId === row.id
                      ? t('notificationPreferences.confirmDelete')
                      : t('notificationPreferences.delete') }}
                  </ElButton>
                </div>
              </template>
            </ElTableColumn>

            <template #empty>
              <p v-if="!loading" class="art-empty-state">{{ t('notificationPreferences.empty') }}</p>
            </template>
          </ElTable>
        </div>
      </ElCard>

      <div class="notification-preferences-side">
        <ElCard v-if="canUpdate" class="notification-preferences-register art-form-card" shadow="never">
          <template #header>
            <h2>{{ t('notificationPreferences.registerTitle') }}</h2>
          </template>

          <ElForm
            v-if="availableProfiles.length > 0"
            ref="registerFormRef"
            :model="registerForm"
            :rules="registerRules"
            label-position="top"
            class="notification-preferences-register__form"
            @submit.prevent
          >
            <ElFormItem prop="providerProfileVersionId" :label="t('notificationPreferences.profile')" required>
              <ElSelect
                v-model="registerForm.providerProfileVersionId"
                data-testid="notification-preferences-profile"
                :teleported="false"
              >
                <ElOption
                  v-for="profileItem in availableProfiles"
                  :key="profileItem.latestPublishedVersionId!"
                  :label="profileItem.profileKey"
                  :value="profileItem.latestPublishedVersionId!"
                />
              </ElSelect>
            </ElFormItem>
            <ElFormItem prop="rawValue" :label="t('notificationPreferences.fieldEndpoint')" required>
              <ElInput
                v-model="registerForm.rawValue"
                :data-testid="selectedEndpointInputTestId"
                :type="selectedEndpointInputType"
                :placeholder="selectedEndpointPlaceholder"
                @keyup.enter="createEndpoint"
              />
            </ElFormItem>
            <ElFormItem>
              <ElButton
                type="primary"
                :loading="saving"
                data-testid="notification-preferences-save"
                @click="createEndpoint"
              >
                {{ t('notificationPreferences.register') }}
              </ElButton>
            </ElFormItem>
          </ElForm>

          <ElAlert
            v-else
            data-testid="notification-preferences-no-profile"
            type="info"
            :title="t('notificationPreferences.noProfile')"
            :closable="false"
          />
        </ElCard>

        <ElCard class="notification-preferences-policy" shadow="never">
          <template #header>
            <h2>{{ t('notificationPreferences.policyTitle') }}</h2>
          </template>
          <p class="art-muted">{{ t('notificationPreferences.policyUnavailable') }}</p>
        </ElCard>
      </div>
    </div>
  </section>
</template>

<style scoped>
.notification-preferences-view {
  min-height: 0;
}

.notification-preferences-error {
  margin-bottom: 0;
}

.notification-preferences-layout {
  display: flex;
  flex: 1;
  gap: 12px;
  min-height: 0;
}

.notification-preferences-list {
  flex: 1 1 0;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.notification-preferences-list :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 12px;
  min-height: 0;
  padding-top: 0;
}

.notification-preferences-list :deep(.el-card__header) {
  padding: 12px 16px;
}

.notification-preferences-list__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.notification-preferences-list__header h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}

.notification-preferences-list__header p {
  margin: 4px 0 0;
  font-size: 12px;
}

.notification-preferences-notices {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.notification-preferences-notices :deep(.el-alert) {
  padding: 8px 12px;
}

.notification-preferences-table-wrap {
  flex: 1;
  min-height: 200px;
}

.notification-preferences-table {
  width: 100%;
}

.recipient-endpoint-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.recipient-endpoint-verify {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.recipient-endpoint-verify .el-input {
  width: 120px;
}

.notification-preferences-side {
  flex: 0 0 320px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 280px;
  max-width: 380px;
}

.notification-preferences-register :deep(.el-card__header),
.notification-preferences-policy :deep(.el-card__header) {
  padding: 12px 16px;
}

.notification-preferences-register :deep(.el-card__header) h2,
.notification-preferences-policy :deep(.el-card__header) h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}

.notification-preferences-register :deep(.el-card__body) {
  padding: 12px 16px 16px;
}

.notification-preferences-register__form :deep(.el-form-item) {
  margin-bottom: 12px;
}

.notification-preferences-register__form :deep(.el-form-item:last-child) {
  margin-bottom: 0;
}

.notification-preferences-policy :deep(.el-card__body) {
  padding: 12px 16px 16px;
}

.notification-preferences-policy p {
  margin: 0;
  font-size: 13px;
  line-height: 1.5;
}

@media (max-width: 960px) {
  .notification-preferences-layout {
    flex-direction: column;
  }

  .notification-preferences-side {
    flex: none;
    max-width: none;
    width: 100%;
  }
}
</style>
