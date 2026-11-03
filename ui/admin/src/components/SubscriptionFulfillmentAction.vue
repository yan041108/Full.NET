<script setup lang="ts">
import { computed, ref } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem } from 'element-plus';
import { isFullNetProblemDetails, type SubscriptionFulfillmentStatusResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { subscriptionBillingText, subscriptionPhaseText } from '../i18n/subscription-billing';
import { getSubscriptionFulfillment, retrySubscriptionFulfillment } from '../api/subscription-billing';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';

const props = defineProps<{ orderId: string }>();
const session = useSessionStore();
const { locale } = useAdminI18n();
const text = (key: Parameters<typeof subscriptionBillingText>[1]) => subscriptionBillingText(locale.value, key);
const allowed = computed(() => session.can('payments.orders.read') && session.can('tenancy.tenant_subscriptions.read'));
const retryAllowed = computed(() => session.can('payments.orders.reconcile') && session.can('tenancy.tenant_subscriptions.manage'));
const open = ref(false); const loading = ref(false); const failure = ref('');
const status = ref<SubscriptionFulfillmentStatusResponse>();
async function load(retry = false) {
  loading.value = true; failure.value = '';
  try { status.value = await (retry ? retrySubscriptionFulfillment(props.orderId) : getSubscriptionFulfillment(props.orderId)); }
  catch (error) {
    status.value = undefined;
    failure.value = isFullNetProblemDetails(error) && error.code === 'payments.subscription_binding_not_found' ? text('noBinding') : text('failure');
  } finally { loading.value = false; }
}
</script>
<template>
  <el-button v-if="allowed" link type="primary" data-testid="subscription-fulfillment" @click="open = true; load()">{{ text('fulfillment') }}</el-button>
  <ArtFormDialog v-model:open="open" trap-focus :title="text('fulfillment')" width="min(520px, calc(100vw - 32px))" :show-confirm="false" :cancel-label="text('close')">
    <el-alert v-if="failure" :title="failure" type="error" :closable="false" />
    <p>{{ text('safeRetry') }}</p>
    <el-descriptions v-if="status" :column="1" border>
      <el-descriptions-item :label="text('invoice')"><code>{{ status.subscriptionId }}</code></el-descriptions-item>
      <el-descriptions-item :label="text('fulfillment')">{{ subscriptionPhaseText(locale, status.stateKey) }}</el-descriptions-item>
    </el-descriptions>
    <el-button :loading="loading" @click="load()">{{ text('refresh') }}</el-button>
    <el-button v-if="retryAllowed && status?.stateKey === 'Review'" :loading="loading" @click="load(true)">{{ text('retry') }}</el-button>
  </ArtFormDialog>
</template>
