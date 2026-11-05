<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem } from 'element-plus';
import { isFullNetProblemDetails, type TenantSubscriptionInvoiceResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { subscriptionBillingText } from '../i18n/subscription-billing';
import { getSubscriptionInvoice } from '../api/subscription-billing';

const props = defineProps<{ tenantId: string; subscriptionId: string }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof subscriptionBillingText>[1]) => subscriptionBillingText(locale.value, key);
const allowed = computed(() => session.state === 'authenticated' && session.can('tenancy.tenant_subscriptions.read'));
const invoice = ref<TenantSubscriptionInvoiceResponse>(); const loading = ref(false); const failure = ref('');
const scope = useAuthorizedViewScope(session, () => { invoice.value = undefined; loading.value = false; failure.value = ''; }, () => {});
watch([() => props.tenantId, () => props.subscriptionId], () => scope.invalidate(), { flush: 'sync' });
const sameId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
// 不能经过 Number 中转；原金额和额度即使超过 JavaScript 安全整数也必须原样核对。
const exactInteger = (value: string) => value.length <= 19 && /^(0|[1-9]\d*)$/.test(value) && BigInt(value) <= 9223372036854775807n;
const phaseText = computed(() => invoice.value?.phase === 'Applied' ? text('invoiceApplied')
  : invoice.value?.phase === 'Refunded' ? text('invoiceRefunded') : text('invoicePending'));
async function read() {
  if (!allowed.value || loading.value) return;
  const operation = scope.begin('tenancy.tenant_subscriptions.read'); if (!operation) return;
  const tenant = props.tenantId; const subscription = props.subscriptionId;
  loading.value = true; failure.value = '';
  try {
    const value = await getSubscriptionInvoice(tenant, subscription, operation.signal);
    if (!operation.current()) return;
    if (!sameId(value.tenantId, tenant) || !sameId(value.subscriptionId, subscription)
      || !exactInteger(value.amountMinor) || value.amountMinor === '0' || !exactInteger(value.seatLimit) || !exactInteger(value.storageLimit)
      || !Number.isInteger(value.offerVersion) || value.offerVersion < 1 || !['Pending', 'Applied', 'Refunded'].includes(value.phase)
      || !Number.isFinite(Date.parse(value.readAtUtc)) || !Number.isFinite(Date.parse(value.effectiveFromUtc))
      || !(Date.parse(value.effectiveToUtc) > Date.parse(value.effectiveFromUtc))
      || ((value.renewalOfSubscriptionId != null) !== (value.renewalSourceVersion != null))
      || (value.renewalSourceVersion != null && (!Number.isInteger(value.renewalSourceVersion) || value.renewalSourceVersion < 1))) {
      invoice.value = undefined; failure.value = text('responseMismatch'); return;
    }
    invoice.value = value;
  } catch (error) {
    if (!operation.current()) return;
    if (isFullNetProblemDetails(error) && [401, 403, 404].includes(error.status)) {
      invoice.value = undefined; failure.value = text(error.status === 404 ? 'invoiceAbsent' : 'detailForbidden');
    } else failure.value = text('invoiceReadFailed');
  } finally { operation.finish(); if (operation.current()) loading.value = false; }
}
</script>

<template>
  <section v-if="allowed" class="subscription-invoice">
    <h3>{{ text('frozenInvoice') }}</h3>
    <p>{{ text('invoiceSnapshotHint') }}</p>
    <el-button data-testid="subscription-invoice-read" :loading="loading" @click="read">{{ text(invoice ? 'invoiceRefresh' : 'invoiceRead') }}</el-button>
    <el-alert v-if="failure" data-testid="subscription-invoice-failure" :title="failure" type="warning" :closable="false" />
    <el-descriptions v-if="invoice" :column="1" border data-testid="subscription-invoice-snapshot">
      <el-descriptions-item :label="text('invoice')"><code>{{ invoice.subscriptionId }}</code></el-descriptions-item>
      <el-descriptions-item :label="text('invoiceSubject')">{{ invoice.subject }}</el-descriptions-item>
      <el-descriptions-item :label="text('detailPackageId')"><code>{{ invoice.packageId }}</code></el-descriptions-item>
      <el-descriptions-item :label="text('offerVersion')">{{ invoice.offerVersion }}</el-descriptions-item>
      <el-descriptions-item :label="text('amount')"><span data-testid="invoice-exact-amount">{{ invoice.amountMinor }}</span> {{ invoice.currency }}</el-descriptions-item>
      <el-descriptions-item :label="text('seats')">{{ invoice.seatLimit }}</el-descriptions-item>
      <el-descriptions-item :label="text('storage')">{{ invoice.storageLimit }}</el-descriptions-item>
      <el-descriptions-item :label="text('start')">{{ invoice.effectiveFromUtc }}</el-descriptions-item>
      <el-descriptions-item :label="text('end')">{{ invoice.effectiveToUtc }}</el-descriptions-item>
      <el-descriptions-item :label="text('invoicePhase')">{{ phaseText }}</el-descriptions-item>
      <el-descriptions-item :label="text('sourceSubscription')"><code>{{ invoice.renewalOfSubscriptionId ?? '—' }}</code></el-descriptions-item>
      <el-descriptions-item v-if="invoice.renewalSourceVersion != null" :label="text('invoiceSourceVersion')">{{ invoice.renewalSourceVersion }}</el-descriptions-item>
      <el-descriptions-item :label="text('invoicePaymentOrder')"><code>{{ invoice.paymentOrderId ?? '—' }}</code></el-descriptions-item>
      <el-descriptions-item :label="text('fulfillmentReadAt')">{{ invoice.readAtUtc }}</el-descriptions-item>
    </el-descriptions>
  </section>
</template>

<style scoped>
.subscription-invoice { margin-top: 20px; }
.subscription-invoice :deep(.el-descriptions) { margin-top: 12px; }
.subscription-invoice code, .subscription-invoice :deep(.el-descriptions__content) { overflow-wrap: anywhere; }
</style>
