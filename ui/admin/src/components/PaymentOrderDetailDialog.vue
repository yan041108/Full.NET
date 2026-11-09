<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem } from 'element-plus';
import { isFullNetProblemDetails, isGuid, type FullNetProblemDetails, type PaymentOrder } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { getPaymentOrder } from '../api/payment-orders';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import { formatPaymentAmount } from '../utils/payment-amount';
import { isNonnegativeInt64String } from '../utils/exact-integer';

const props = defineProps<{ open: boolean; orderId: string; showRefundReconcile?: boolean;
  reference?: Pick<PaymentOrder, 'tenantId' | 'merchantConfigId' | 'outTradeNo' | 'currency'> }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; reconcile: [order: PaymentOrder] }>();
const { t } = useAdminI18n(); const session = useSessionStore();
const allowed = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can('payments.orders.read'));
const visible = ref(false), busy = ref(false); const detail = ref<PaymentOrder>();
const canReconcileRefund = computed(() => props.showRefundReconcile && allowed.value && !session.switching
  && session.can('payments.orders.reconcile') && detail.value?.tradeStateKey === 'refunding'
  && detail.value.channelKey === 'wechat_native');
const problem = ref<FullNetProblemDetails>(); const readAt = ref('');
let sequence = 0; let pending: { cancel(): void } | undefined;
const validId = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
const sameId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
const date = (value: unknown) => typeof value === 'string' && Number.isFinite(Date.parse(value));
function clear() { sequence++; pending?.cancel(); pending = undefined; detail.value = undefined; problem.value = undefined; readAt.value = ''; busy.value = false; }
function close() { clear(); visible.value = false; emit('update:open', false); }
const scope = useAuthorizedViewScope(session, close, () => { if (visible.value) void read(); });
watch(() => [props.open, props.orderId, props.reference?.tenantId, props.reference?.merchantConfigId,
  props.reference?.outTradeNo, props.reference?.currency], () => {
  clear(); visible.value = props.open && allowed.value;
  if (visible.value) void read();
}, { immediate: true });

async function read() {
  if (!visible.value || !allowed.value || !validId(props.orderId)) return;
  const operation = scope.begin('payments.orders.read'); if (!operation) return;
  clear(); const ticket = sequence; pending = operation; busy.value = true;
  const requested = props.orderId; const reference = props.reference ? { ...props.reference } : undefined;
  try {
    const value = await getPaymentOrder(requested, operation.signal);
    if (!operation.current() || ticket !== sequence) return;
    // 从退款追溯时核对原资金归属，不能把同一页面里的其他订单当作原订单展示。
    if (!validId(value.id) || !sameId(value.id, requested) || !validId(value.tenantId) || !validId(value.merchantConfigId)
      || !['wechat_native', 'alipay_page'].includes(value.channelKey)
      || !['created', 'awaiting_payment', 'succeeded', 'closed', 'failed', 'provider_unknown', 'refunding', 'refunded'].includes(value.tradeStateKey)
      || typeof value.outTradeNo !== 'string' || !value.outTradeNo.trim() || typeof value.subject !== 'string'
      || typeof value.currency !== 'string' || !/^[A-Z]{3}$/.test(value.currency)
      || !isNonnegativeInt64String(value.amountMinor) || value.amountMinor === '0'
      || !Number.isSafeInteger(value.version) || value.version < 1 || !date(value.createdAtUtc)
      || ![value.updatedAtUtc, value.paidAtUtc].every(item => item === null || date(item))
      || ![value.description, value.codeUrl, value.providerTransactionId, value.failMessage].every(item => item === null || typeof item === 'string')
      || (reference && (!validId(reference.tenantId) || !validId(reference.merchantConfigId)
        || !sameId(value.tenantId, reference.tenantId) || !sameId(value.merchantConfigId, reference.merchantConfigId)
        || value.outTradeNo !== reference.outTradeNo || value.currency !== reference.currency)))
      throw { type: 'about:blank', status: 502, code: 'client.payment_order_mismatch', title: t('paymentOrders.detailMismatch') };
    detail.value = value; readAt.value = new Date().toISOString();
  } catch (error) {
    if (operation.current() && ticket === sequence) problem.value = isFullNetProblemDetails(error) ? error
      : { type: 'about:blank', status: 500, code: 'client.request_failed', title: t('paymentOrders.detailFailed') };
  } finally { operation.finish(); if (ticket === sequence) { busy.value = false; pending = undefined; } }
}
</script>

<template>
  <ArtFormDialog :open="visible && allowed" :title="t('paymentOrders.detailTitle')" :cancel-label="t('paymentRefunds.close')"
    :show-confirm="false" trap-focus width="min(760px, calc(100vw - 32px))" @update:open="close">
    <div class="order-detail-content" :aria-busy="busy">
      <p>{{ t('paymentOrders.orderId') }} <code data-testid="payment-order-detail-id">{{ orderId }}</code></p>
      <p>{{ t('paymentOrders.detailReadOnly') }}</p>
      <ElButton :loading="busy" data-testid="payment-order-detail-refresh" @click="read">{{ t('common.refresh') }}</ElButton>
      <ElButton v-if="canReconcileRefund && detail" data-testid="payment-order-detail-reconcile-refund" @click="emit('reconcile', detail)">
        {{ t('paymentRefunds.reconcileOriginal') }}
      </ElButton>
      <ElAlert v-if="problem" type="warning" :closable="false" :title="problem.title" data-testid="payment-order-detail-problem" />
      <ElDescriptions v-if="detail" :column="1" border data-testid="payment-order-detail-snapshot">
        <ElDescriptionsItem :label="t('paymentOrders.fieldTenantId')">{{ detail.tenantId }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldMerchantConfigId')">{{ detail.merchantConfigId }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldChannelKey')">{{ detail.channelKey }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldOutTradeNo')">{{ detail.outTradeNo }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldStatus')">{{ detail.tradeStateKey }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldAmount')"><span data-testid="payment-order-detail-amount">{{ formatPaymentAmount(detail.amountMinor) ?? t('paymentOrders.amountUnavailable') }} {{ detail.currency }}</span></ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldSubject')">{{ detail.subject }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldDescription')">{{ detail.description || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.providerTransactionId')">{{ detail.providerTransactionId || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.paymentLink')">{{ detail.codeUrl || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldCreatedAt')">{{ detail.createdAtUtc }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.updatedAt')">{{ detail.updatedAtUtc || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.paidAt')">{{ detail.paidAtUtc || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldFailMessage')">{{ detail.failMessage || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.version')">{{ detail.version }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.readAt')">{{ readAt }}</ElDescriptionsItem>
      </ElDescriptions>
    </div>
  </ArtFormDialog>
</template>

<style scoped>
.order-detail-content { overflow-wrap: anywhere; }
.order-detail-content :deep(.el-descriptions__table) { table-layout: fixed; width: 100%; border-collapse: collapse; }
.order-detail-content :deep(.el-descriptions__cell) { padding: 2px 6px; vertical-align: top; }
.order-detail-content :deep(.el-descriptions__label) { width: 35%; }
</style>
