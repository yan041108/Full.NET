<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem, ElInput } from 'element-plus';
import type { SubscriptionCheckoutResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { subscriptionBillingText, subscriptionPhaseText } from '../i18n/subscription-billing';
import { checkoutSubscription } from '../api/subscription-billing';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import SubscriptionFulfillmentAction from './SubscriptionFulfillmentAction.vue';
const props = defineProps<{ tenantId: string; subscriptionId: string }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof subscriptionBillingText>[1]) => subscriptionBillingText(locale.value, key);
const allowed = computed(() => session.state === 'authenticated' && session.can('payments.orders.create') && session.can('tenancy.tenant_subscriptions.manage'));
const open = ref(false); const loading = ref(false); const failure = ref(''); const attempted = ref(false); const checkout = ref<SubscriptionCheckoutResponse>();
const scope = useAuthorizedViewScope(session, () => { open.value = false; loading.value = false; failure.value = ''; checkout.value = undefined; attempted.value = false; }, () => {});
let pending: ReturnType<typeof scope.begin>;
watch(() => [props.tenantId, props.subscriptionId], () => scope.invalidate(), { flush: 'sync' });
watch(open, value => { if (!value) { pending?.cancel(); pending = undefined; loading.value = false; } }, { flush: 'sync' });
function begin() { if (allowed.value) open.value = true; }
async function submit() {
  if (!open.value || !allowed.value || loading.value || checkout.value) return;
  const operation = scope.begin('payments.orders.create'); if (!operation) return;
  const tenantId = props.tenantId; const subscriptionId = props.subscriptionId;
  pending = operation; loading.value = true; failure.value = ''; attempted.value = true;
  try {
    const value = await checkoutSubscription(tenantId, subscriptionId, operation.signal);
    if (!operation.current() || !open.value || !allowed.value || tenantId !== props.tenantId || subscriptionId !== props.subscriptionId) return;
    if (value.subscriptionId.toLowerCase() !== subscriptionId.toLowerCase()) { failure.value = text('responseMismatch'); return; }
    checkout.value = value;
  } catch { if (operation.current() && open.value) failure.value = text('checkoutUnknown'); }
  finally { operation.finish(); if (pending === operation) { pending = undefined; loading.value = false; } }
}
</script>
<template>
  <el-button v-if="allowed" link type="primary" data-testid="subscription-checkout" @click="begin">{{ text('checkout') }}</el-button>
  <ArtFormDialog v-model:open="open" trap-focus :title="text('checkout')" width="min(520px, calc(100vw - 32px))" :saving="loading" :show-confirm="!checkout" :confirm-disabled="!allowed || loading" :confirm-label="text(attempted ? 'recoverCheckout' : 'checkout')" confirm-test-id="subscription-checkout-confirm"
    :cancel-label="text('close')" @confirm="submit">
    <p>{{ text('safeRetry') }}</p>
    <p v-if="attempted && !checkout" role="status" data-testid="subscription-checkout-recovery">{{ text('checkoutUnknown') }}</p>
    <el-alert v-if="failure" :title="failure" type="error" :closable="false" />
    <el-descriptions v-if="checkout" :column="1" border>
      <el-descriptions-item :label="text('order')"><code>{{ checkout.order.id }}</code></el-descriptions-item>
      <el-descriptions-item :label="text('funds')"><code>{{ checkout.order.tradeStateKey }}</code></el-descriptions-item>
      <el-descriptions-item :label="text('fulfillment')">{{ subscriptionPhaseText(locale, checkout.fulfillmentStateKey) }}</el-descriptions-item>
      <el-descriptions-item :label="text('paymentLink')"><el-input :model-value="checkout.order.codeUrl ?? ''" :aria-label="text('paymentLink')" readonly /></el-descriptions-item>
    </el-descriptions>
    <SubscriptionFulfillmentAction v-if="checkout" :order-id="checkout.order.id" />
  </ArtFormDialog>
</template>
