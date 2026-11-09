<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem } from 'element-plus';
import { isFullNetProblemDetails, isGuid, type FullNetProblemDetails, type PaymentOrder, type PaymentOrderListItem } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { getPaymentOrder, reconcilePaymentOrder } from '../api/payment-orders';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import { formatPaymentAmount } from '../utils/payment-amount';
import { isNonnegativeInt64String } from '../utils/exact-integer';
import { translatePaymentReconciliation } from '@fullnet/admin-i18n';
import { translateRuntimeMessage } from '../i18n/runtimeMessage';

const props = defineProps<{ open: boolean; reference?: Readonly<PaymentOrderListItem>; refundOnly?: boolean }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; updated: [order: PaymentOrder]; busy: [value: boolean] }>();
const session = useSessionStore(); const { t, locale } = useAdminI18n();
const text = (key: Parameters<typeof translatePaymentReconciliation>[1]) => translatePaymentReconciliation(locale.value, key);
const allowed = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can('payments.orders.read') && session.can('payments.orders.reconcile'));
const visible = ref(false), busy = ref(false), confirmed = ref(false), readAt = ref('');
const snapshot = ref<Readonly<PaymentOrder>>(); const problem = ref<FullNetProblemDetails>();
// 本页只记住是否已经发出对账，关闭窗口不能把未知结果变成新的首次确认。
const outcomes = new Map<string, 'pending' | 'unconfirmed' | 'accepted'>(); const outcome = ref<string>();
let sequence = 0; let pending: { cancel(): void } | undefined;
let pendingWriteId: string | undefined;
const identity = (id: string) => id.toLowerCase();
const validId = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
const sameId = (value: unknown, expected: string) => validId(value) && validId(expected) && value.toLowerCase() === expected.toLowerCase();
function clear() {
  sequence++; pending?.cancel(); pending = undefined; busy.value = false; emit('busy', false);
  if (pendingWriteId && outcomes.get(pendingWriteId) === 'pending') outcomes.set(pendingWriteId, 'unconfirmed');
  pendingWriteId = undefined; outcome.value = outcomes.get(identity(props.reference?.id ?? ''));
  snapshot.value = undefined; problem.value = undefined; confirmed.value = false; readAt.value = '';
}
function close() { clear(); visible.value = false; emit('update:open', false); }
const scope = useAuthorizedViewScope(session, () => { close(); outcomes.clear(); outcome.value = undefined; }, () => {
  if (visible.value && !outcomes.has(identity(props.reference?.id ?? ''))) void read();
});
watch(() => [props.open, props.reference], () => {
  clear(); visible.value = props.open && allowed.value;
  if (visible.value && !outcome.value) void read();
}, { immediate: true });
function matches(value: PaymentOrder, original: Readonly<PaymentOrderListItem>) {
  return sameId(value.id, original.id) && sameId(value.tenantId, original.tenantId) && sameId(value.merchantConfigId, original.merchantConfigId)
    && ['wechat_native', 'alipay_page'].includes(value.channelKey) && value.channelKey === original.channelKey
    && value.outTradeNo === original.outTradeNo && !!value.outTradeNo.trim() && value.currency === original.currency
    && /^[A-Z]{3}$/.test(value.currency) && value.amountMinor === original.amountMinor && isNonnegativeInt64String(value.amountMinor) && value.amountMinor !== '0'
    && Number.isSafeInteger(value.version) && value.version >= 1
    && ['created', 'awaiting_payment', 'succeeded', 'closed', 'failed', 'provider_unknown', 'refunding', 'refunded'].includes(value.tradeStateKey);
}
const eligible = computed(() => {
  const order = snapshot.value;
  if (!order) return false;
  if (props.refundOnly) return order.tradeStateKey === 'refunding' && order.channelKey === 'wechat_native';
  return order.tradeStateKey !== 'refunded' && (order.tradeStateKey !== 'refunding' || order.channelKey === 'wechat_native');
});
const canConfirm = computed(() => allowed.value && !!eligible.value && !busy.value && !confirmed.value && !outcome.value);
function stateLabel(value: string) { const key = `paymentOrders.status.${value}`; const label = translateRuntimeMessage(t, key); return label === key ? value : label; }
function mismatch(): FullNetProblemDetails { return { type: 'about:blank', status: 502, code: 'client.reconcile_order_mismatch', title: text('mismatch') }; }
async function read() {
  if (!visible.value || !allowed.value || busy.value || !props.reference || !validId(props.reference.id)) return;
  clear(); const original = { ...props.reference }; const ticket = sequence;
  const operation = scope.begin('payments.orders.read'); if (!operation) return;
  pending = operation; busy.value = true; emit('busy', true);
  try {
    const value = await getPaymentOrder(original.id, operation.signal);
    if (!operation.current() || ticket !== sequence) return;
    if (!matches(value, original)) throw mismatch();
    snapshot.value = Object.freeze({ ...value }); readAt.value = new Date().toISOString(); outcomes.delete(identity(original.id)); outcome.value = undefined;
  } catch (error) {
    if (operation.current() && ticket === sequence) problem.value = isFullNetProblemDetails(error) ? error
      : { type: 'about:blank', status: 500, code: 'client.request_failed', title: t('paymentOrders.detailFailed') };
  } finally { operation.finish(); if (ticket === sequence) { pending = undefined; busy.value = false; emit('busy', false); } }
}
async function send() {
  if (!canConfirm.value || !snapshot.value) return;
  const original = snapshot.value; const operation = scope.begin('payments.orders.reconcile'); if (!operation) return;
  const ticket = sequence; pending = operation; pendingWriteId = identity(original.id); busy.value = true; emit('busy', true);
  outcomes.set(identity(original.id), 'pending'); outcome.value = 'pending'; problem.value = undefined;
  try {
    const value = await reconcilePaymentOrder(original.id, operation.signal);
    if (!operation.current() || ticket !== sequence) return;
    if (!matches(value, original) || value.version < original.version) throw mismatch();
    snapshot.value = Object.freeze({ ...value }); readAt.value = new Date().toISOString(); confirmed.value = true;
    outcomes.set(identity(original.id), 'accepted'); outcome.value = 'accepted'; emit('updated', value);
  } catch (error) {
    if (!operation.current() || ticket !== sequence) return;
    outcomes.set(identity(original.id), 'unconfirmed'); outcome.value = 'unconfirmed';
    problem.value = isFullNetProblemDetails(error) ? error : { type: 'about:blank', status: 500, code: 'client.request_failed', title: text('unconfirmed') };
  } finally { operation.finish(); if (ticket === sequence) { pending = undefined; pendingWriteId = undefined; busy.value = false; emit('busy', false); } }
}
</script>

<template>
  <ArtFormDialog :open="visible && allowed" :title="text('title')" :saving="busy" :confirm-disabled="!canConfirm"
    :confirm-label="text('confirm')" :cancel-label="t('paymentRefunds.close')" confirm-test-id="payment-reconcile-confirm" trap-focus
    width="min(640px, calc(100vw - 32px))" @update:open="close" @confirm="send">
    <div class="payment-reconcile" :aria-busy="busy">
      <p>{{ text('hint') }}</p>
      <p data-testid="payment-reconcile-id">{{ reference?.id }}</p>
      <p v-if="outcome === 'unconfirmed' || outcome === 'pending'" role="status" data-testid="payment-reconcile-unconfirmed">{{ text('unconfirmed') }}</p>
      <p v-if="outcome === 'accepted'" role="status" data-testid="payment-reconcile-accepted">{{ text('accepted') }}</p>
      <ElAlert v-if="problem" :closable="false" type="warning" :title="problem.title" data-testid="payment-reconcile-problem" />
      <ElDescriptions v-if="snapshot" :column="1" border data-testid="payment-reconcile-snapshot">
        <ElDescriptionsItem :label="t('paymentOrders.fieldTenantId')">{{ snapshot.tenantId }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldMerchantConfigId')">{{ snapshot.merchantConfigId }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldChannelKey')">{{ snapshot.channelKey }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldOutTradeNo')">{{ snapshot.outTradeNo }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldAmount')">{{ formatPaymentAmount(snapshot.amountMinor) }} {{ snapshot.currency }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldStatus')">{{ stateLabel(snapshot.tradeStateKey) }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.version')">{{ snapshot.version }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('readAt')">{{ readAt }}</ElDescriptionsItem>
      </ElDescriptions>
      <p v-if="snapshot && !eligible">{{ text('ineligible') }}</p>
      <ElButton :disabled="busy" data-testid="payment-reconcile-read" @click="read">{{ text('read') }}</ElButton>
    </div>
  </ArtFormDialog>
</template>

<style scoped>
.payment-reconcile { overflow-wrap: anywhere; }
.payment-reconcile :deep(.el-descriptions__table) { table-layout: fixed; width: 100%; }
.payment-reconcile :deep(.el-descriptions__cell) { padding: 2px 6px; vertical-align: top; }
.payment-reconcile :deep(.el-descriptions__label) { width: 35%; }
</style>
