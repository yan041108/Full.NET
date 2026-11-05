<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem } from 'element-plus';
import { isFullNetProblemDetails, isGuid, type FullNetProblemDetails, type PaymentMerchantConfig, type PaymentMerchantConfigListItem } from '@fullnet/client-contracts';
import { translatePaymentMerchantOperations } from '@fullnet/admin-i18n';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { getPaymentMerchantConfig } from '../api/payment-merchant-configs';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';

const props = defineProps<{ open: boolean; merchantConfigId: string; reference?: Pick<PaymentMerchantConfigListItem, 'tenantId' | 'channelKey' | 'version'> }>();
const emit = defineEmits<{ 'update:open': [value: boolean] }>();
const session = useSessionStore(); const { t, locale } = useAdminI18n();
const text = (key: Parameters<typeof translatePaymentMerchantOperations>[1]) => translatePaymentMerchantOperations(locale.value, key);
const allowed = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can('payments.merchants.read'));
const visible = ref(false), busy = ref(false), readAt = ref(''); const detail = ref<PaymentMerchantConfig>(); const problem = ref<FullNetProblemDetails>();
let sequence = 0; let pending: { cancel(): void } | undefined;
const validId = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
const sameId = (left: string | null, right: string | null) => left === null ? right === null : validId(left) && validId(right) && left.toLowerCase() === right.toLowerCase();
const date = (value: unknown) => typeof value === 'string' && Number.isFinite(Date.parse(value));
function clear() { sequence++; pending?.cancel(); pending = undefined; detail.value = undefined; problem.value = undefined; readAt.value = ''; busy.value = false; }
function close() { clear(); visible.value = false; emit('update:open', false); }
const scope = useAuthorizedViewScope(session, close, () => { if (visible.value) void read(); });
watch(() => [props.open, props.merchantConfigId, props.reference?.tenantId, props.reference?.channelKey, props.reference?.version], () => {
  clear(); visible.value = props.open && allowed.value; if (visible.value) void read();
}, { immediate: true });
async function read() {
  if (!visible.value || !allowed.value) return;
  clear(); const requested = props.merchantConfigId; const reference = props.reference ? { ...props.reference } : undefined;
  if (!validId(requested)) { problem.value = { type: 'about:blank', status: 400, code: 'client.invalid_merchant_id', title: text('invalidId') }; return; }
  const operation = scope.begin('payments.merchants.read'); if (!operation) return;
  const ticket = sequence; pending = operation; busy.value = true;
  try {
    const value = await getPaymentMerchantConfig(requested, operation.signal); if (!operation.current() || ticket !== sequence) return;
    if (!sameId(value.id, requested) || (value.tenantId !== null && !validId(value.tenantId)) || !['wechat_native', 'alipay_page'].includes(value.channelKey)
      || typeof value.name !== 'string' || !value.name.trim() || ![value.appId, value.merchantId, value.certificateSerialNo, value.notifyUrl, value.returnUrl].every(item => typeof item === 'string')
      || ![value.hasApiV3Key, value.hasPrivateKey, value.isDefault, value.isEnabled].every(item => typeof item === 'boolean')
      || !Number.isSafeInteger(value.version) || value.version < 1 || !date(value.createdAtUtc) || (value.updatedAtUtc !== null && !date(value.updatedAtUtc))
      || (reference && (!sameId(value.tenantId, reference.tenantId) || value.channelKey !== reference.channelKey || value.version < reference.version)))
      throw { type: 'about:blank', status: 502, code: 'client.merchant_detail_mismatch', title: text('mismatch') };
    // 仅保留公开详情字段，不把来源额外夹带的密钥或其他属性存入页面快照。
    detail.value = { id: value.id, tenantId: value.tenantId, name: value.name, channelKey: value.channelKey, appId: value.appId,
      merchantId: value.merchantId, certificateSerialNo: value.certificateSerialNo, notifyUrl: value.notifyUrl, returnUrl: value.returnUrl,
      hasApiV3Key: value.hasApiV3Key, hasPrivateKey: value.hasPrivateKey, isDefault: value.isDefault, isEnabled: value.isEnabled,
      createdAtUtc: value.createdAtUtc, updatedAtUtc: value.updatedAtUtc, version: value.version };
    readAt.value = new Date().toISOString();
  } catch (error) { if (operation.current() && ticket === sequence) problem.value = isFullNetProblemDetails(error) ? error
    : { type: 'about:blank', status: 500, code: 'client.request_failed', title: t('paymentMerchantConfigs.loadFailed') }; }
  finally { operation.finish(); if (ticket === sequence) { pending = undefined; busy.value = false; } }
}
const yesNo = (value: boolean) => t(value ? 'paymentMerchantConfigs.enabledYes' : 'paymentMerchantConfigs.enabledNo');
</script>

<template>
  <ArtFormDialog :open="visible && allowed" :title="text('detailTitle')" :show-confirm="false" :cancel-label="text('close')" trap-focus
    width="min(720px, calc(100vw - 32px))" @update:open="close">
    <div class="merchant-detail" :aria-busy="busy">
      <p>{{ text('id') }} <code data-testid="merchant-detail-id">{{ merchantConfigId }}</code></p><p>{{ text('detailHint') }}</p>
      <ElButton :loading="busy" data-testid="merchant-detail-refresh" @click="read">{{ t('common.refresh') }}</ElButton>
      <ElAlert v-if="problem" :closable="false" type="warning" :title="problem.title" data-testid="merchant-detail-problem" />
      <ElDescriptions v-if="detail" :column="1" border data-testid="merchant-detail-snapshot">
        <ElDescriptionsItem :label="t('paymentMerchantConfigs.fieldTenantId')">{{ detail.tenantId ?? 'Host' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentMerchantConfigs.fieldName')">{{ detail.name }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentMerchantConfigs.fieldChannelKey')">{{ detail.channelKey }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentMerchantConfigs.fieldAppId')">{{ detail.appId }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentMerchantConfigs.fieldMerchantId')">{{ detail.merchantId }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentMerchantConfigs.fieldCertificateSerialNo')">{{ detail.certificateSerialNo }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentMerchantConfigs.fieldNotifyUrl')">{{ detail.notifyUrl }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentMerchantConfigs.fieldReturnUrl')">{{ detail.returnUrl }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('apiKeyConfigured')">{{ yesNo(detail.hasApiV3Key) }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('privateKeyConfigured')">{{ yesNo(detail.hasPrivateKey) }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentMerchantConfigs.fieldDefault')">{{ yesNo(detail.isDefault) }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentMerchantConfigs.fieldEnabled')">{{ yesNo(detail.isEnabled) }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('version')">{{ detail.version }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('createdAt')">{{ detail.createdAtUtc }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('updatedAt')">{{ detail.updatedAtUtc ?? '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('observedAt')">{{ readAt }}</ElDescriptionsItem>
      </ElDescriptions>
    </div>
  </ArtFormDialog>
</template>

<style scoped>
.merchant-detail { overflow-wrap: anywhere; }
.merchant-detail :deep(.el-descriptions__table) { table-layout: fixed; width: 100%; }
.merchant-detail :deep(.el-descriptions__cell) { padding: 2px 6px; vertical-align: top; }
.merchant-detail :deep(.el-descriptions__label) { width: 35%; }
</style>
