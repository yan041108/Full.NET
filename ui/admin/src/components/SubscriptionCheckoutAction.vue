<script setup lang="ts">
import { computed, ref } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem, ElInput } from 'element-plus';
import type { SubscriptionCheckoutResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { subscriptionBillingText, subscriptionPhaseText } from '../i18n/subscription-billing';
import { checkoutSubscription } from '../api/subscription-billing';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import SubscriptionFulfillmentAction from './SubscriptionFulfillmentAction.vue';

const props = defineProps<{ tenantId: string; subscriptionId: string }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof subscriptionBillingText>[1]) => subscriptionBillingText(locale.value, key);
const allowed = computed(() => session.can('payments.orders.create') && session.can('tenancy.tenant_subscriptions.manage'));
const open = ref(false); const loading = ref(false); const failure = ref(''); const checkout = ref<SubscriptionCheckoutResponse>();
async function submit() {
  if (loading.value) return;
  loading.value = true; failure.value = '';
  try { checkout.value = await checkoutSubscription(props.tenantId, props.subscriptionId); }
  catch { failure.value = text('failure'); }
  finally { loading.value = false; }
}
</script>
<template>
  <el-button v-if="allowed" link type="primary" data-testid="subscription-checkout" @click="open = true">{{ text('checkout') }}</el-button>
  <ArtFormDialog v-model:open="open" trap-focus :title="text('checkout')" width="min(520px, calc(100vw - 32px))" :saving="loading" :confirm-label="text('checkout')" confirm-test-id="subscription-checkout-confirm"
    :cancel-label="text('close')" @confirm="submit">
    <p>{{ text('safeRetry') }}</p>
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
