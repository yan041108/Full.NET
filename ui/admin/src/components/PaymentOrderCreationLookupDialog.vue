<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem, ElForm, ElFormItem, ElInput } from 'element-plus';
import { isFullNetProblemDetails, isGuid, type FullNetProblemDetails, type PaymentOrderCreationLookupResponse } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { getPaymentOrderCreationRequest } from '../api/payment-orders';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import { formatPaymentAmount } from '../utils/payment-amount';
import { isNonnegativeInt64String } from '../utils/exact-integer';

const props = defineProps<{ open: boolean; tenantId?: string; idempotencyKey?: string; fixedIdentity?: boolean }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; found: [value: PaymentOrderCreationLookupResponse]; 'open-order': [id: string] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const allowed = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can('payments.orders.read'));
const visible = ref(false), busy = ref(false); const tenant = ref(''), key = ref('');
const result = ref<PaymentOrderCreationLookupResponse>(); const problem = ref<FullNetProblemDetails>();
const validId = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
const sameId = (value: unknown, expected: string) => validId(value) && value.toLowerCase() === expected.toLowerCase();
let sequence = 0; let pending: { cancel(): void } | undefined;
function clear() { sequence++; pending?.cancel(); pending = undefined; busy.value = false; result.value = undefined; problem.value = undefined; }
function close() { clear(); visible.value = false; emit('update:open', false); }
const scope = useAuthorizedViewScope(session, () => { close(); tenant.value = ''; key.value = ''; }, () => {
  if (visible.value && props.fixedIdentity) void read();
});
watch([tenant, key], clear, { flush: 'sync' });
watch(() => [props.open, props.tenantId, props.idempotencyKey], () => {
  clear(); visible.value = props.open && allowed.value;
  if (props.tenantId !== undefined) tenant.value = props.tenantId;
  if (props.idempotencyKey !== undefined) key.value = props.idempotencyKey;
  if (visible.value && props.fixedIdentity) void read();
}, { immediate: true });
async function read() {
  if (!allowed.value || !visible.value) return;
  clear(); const tenantId = tenant.value.trim(), idempotencyKey = key.value.trim();
  if (!validId(tenantId) || !validId(idempotencyKey)) {
    problem.value = { type: 'about:blank', status: 400, code: 'client.invalid_creation_identity', title: t('paymentOrders.creationIdentityInvalid') }; return;
  }
  const operation = scope.begin('payments.orders.read'); if (!operation) return;
  const ticket = sequence; pending = operation; busy.value = true;
  try {
    const value = await getPaymentOrderCreationRequest(tenantId, idempotencyKey, operation.signal);
    if (!operation.current() || ticket !== sequence) return;
    if (!sameId(value.idempotencyKey, idempotencyKey) || !validId(value.order.id) || !sameId(value.order.tenantId, tenantId)
      || !validId(value.order.merchantConfigId) || !isNonnegativeInt64String(value.order.amountMinor) || value.order.amountMinor === '0'
      || !Number.isSafeInteger(value.order.version) || value.order.version < 1 || !Number.isFinite(Date.parse(value.readAtUtc)))
      throw { type: 'about:blank', status: 502, code: 'client.creation_lookup_mismatch', title: t('paymentOrders.creationResponseMismatch') };
    result.value = value; emit('found', value);
  } catch (error) {
    if (operation.current() && ticket === sequence) problem.value = isFullNetProblemDetails(error) ? error
      : { type: 'about:blank', status: 500, code: 'client.request_failed', title: t('paymentOrders.creationLookupFailed') };
  } finally { operation.finish(); if (ticket === sequence) { busy.value = false; pending = undefined; } }
}
function openOrder() { if (result.value && allowed.value) { const id = result.value.order.id; close(); emit('open-order', id); } }
</script>

<template>
  <ArtFormDialog :open="visible && allowed" :title="t('paymentOrders.creationLookupTitle')" :show-confirm="false" :cancel-label="t('paymentRefunds.close')"
    trap-focus width="min(640px, calc(100vw - 32px))" @update:open="close">
    <div class="creation-lookup" :aria-busy="busy">
      <p>{{ t('paymentOrders.creationLookupHint') }}</p>
      <ElForm label-position="top" @submit.prevent="read">
        <ElFormItem :label="t('paymentOrders.fieldTenantId')"><ElInput v-model="tenant" :readonly="fixedIdentity" data-testid="creation-lookup-tenant" /></ElFormItem>
        <ElFormItem :label="t('paymentOrders.creationRequestKey')"><ElInput v-model="key" :readonly="fixedIdentity" data-testid="creation-lookup-key" /></ElFormItem>
        <ElButton native-type="submit" :loading="busy" data-testid="creation-lookup-read">{{ t('paymentOrders.creationLookupRead') }}</ElButton>
      </ElForm>
      <ElAlert v-if="problem" :closable="false" type="warning" :title="problem.title" data-testid="creation-lookup-problem" />
      <ElDescriptions v-if="result" :column="1" border data-testid="creation-lookup-snapshot">
        <ElDescriptionsItem :label="t('paymentOrders.creationRequestKey')">{{ result.idempotencyKey }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.orderId')">{{ result.order.id }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldOutTradeNo')">{{ result.order.outTradeNo }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldStatus')">{{ result.order.tradeStateKey }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.fieldAmount')">{{ formatPaymentAmount(result.order.amountMinor) }} {{ result.order.currency }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentOrders.creationServerReadAt')">{{ result.readAtUtc }}</ElDescriptionsItem>
      </ElDescriptions>
      <ElButton v-if="result" data-testid="creation-lookup-open-order" @click="openOrder">{{ t('paymentOrders.detail') }}</ElButton>
    </div>
  </ArtFormDialog>
</template>

<style scoped>
.creation-lookup { overflow-wrap: anywhere; }
.creation-lookup :deep(.el-descriptions__table) { table-layout: fixed; width: 100%; }
.creation-lookup :deep(.el-descriptions__cell) { padding: 2px 6px; vertical-align: top; }
.creation-lookup :deep(.el-descriptions__label) { width: 35%; }
</style>
