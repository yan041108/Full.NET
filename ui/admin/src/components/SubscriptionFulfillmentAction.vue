<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem } from 'element-plus';
import { isFullNetProblemDetails, type SubscriptionFulfillmentStatusResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { subscriptionBillingText, subscriptionPhaseText } from '../i18n/subscription-billing';
import { getSubscriptionFulfillment, retrySubscriptionFulfillment } from '../api/subscription-billing';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
const props = defineProps<{ orderId: string }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof subscriptionBillingText>[1]) => subscriptionBillingText(locale.value, key);
const allowed = computed(() => session.state === 'authenticated' && session.can('payments.orders.read') && session.can('tenancy.tenant_subscriptions.read'));
const retryAllowed = computed(() => allowed.value && session.can('payments.orders.reconcile') && session.can('tenancy.tenant_subscriptions.manage'));
const open = ref(false); const loading = ref(false); const failure = ref(''); const uncertain = ref(false);
const status = ref<SubscriptionFulfillmentStatusResponse>();
const dates = computed(() => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'medium', timeZone: 'UTC' }));
const formatDate = (value: string) => dates.value.format(new Date(value));
const waitingForEffective = computed(() => status.value?.stateKey === 'ReadyPaid'
  && Date.parse(status.value.effectiveFromUtc) > Date.parse(status.value.readAtUtc));
function validSchedule(value: SubscriptionFulfillmentStatusResponse) {
  if (![value.effectiveFromUtc, value.readAtUtc].every(date => Number.isFinite(Date.parse(date)))) return false;
  const ready = ['ReadyPaid', 'ReadyRefund'].includes(value.stateKey);
  if (ready !== (value.nextAttemptAtUtc != null)) return false;
  return value.nextAttemptAtUtc == null || (Number.isFinite(Date.parse(value.nextAttemptAtUtc))
    && (value.stateKey !== 'ReadyPaid' || Date.parse(value.nextAttemptAtUtc) >= Date.parse(value.effectiveFromUtc)));
}
const scope = useAuthorizedViewScope(session, () => { open.value = false; loading.value = false; failure.value = ''; status.value = undefined; uncertain.value = false; }, () => {});
let pending: ReturnType<typeof scope.begin>;
watch(() => props.orderId, () => scope.invalidate(), { flush: 'sync' });
watch(open, value => { if (!value) { pending?.cancel(); pending = undefined; loading.value = false; } }, { flush: 'sync' });
function begin() { if (allowed.value) { open.value = true; void load(); } }
async function load(retry = false) {
  if (!open.value || !allowed.value || loading.value || (retry && (!retryAllowed.value || uncertain.value || status.value?.stateKey !== 'Review'))) return;
  const operation = scope.begin(retry ? 'payments.orders.reconcile' : 'payments.orders.read'); if (!operation) return;
  const orderId = props.orderId; pending = operation; loading.value = true; failure.value = '';
  // 重试结果不明时只允许重新读取当前订单，不把旧 Review 状态继续作为写入资格。
  if (retry) uncertain.value = true;
  status.value = undefined;
  try {
    const value = await (retry ? retrySubscriptionFulfillment(orderId, operation.signal) : getSubscriptionFulfillment(orderId, operation.signal));
    if (!operation.current() || !open.value || !allowed.value || orderId !== props.orderId) return;
    if (value.paymentOrderId?.toLowerCase() !== orderId.toLowerCase() || !validSchedule(value)) { failure.value = text('responseMismatch'); return; }
    status.value = value; uncertain.value = false;
  } catch (error) {
    if (!operation.current() || !open.value) return;
    failure.value = isFullNetProblemDetails(error) && error.code === 'payments.subscription_binding_not_found' ? text('noBinding')
      : text(retry ? 'fulfillmentUnknown' : 'failure');
  } finally { operation.finish(); if (pending === operation) { pending = undefined; loading.value = false; } }
}
</script>
<template>
  <el-button v-if="allowed" link type="primary" data-testid="subscription-fulfillment" @click="begin">{{ text('fulfillment') }}</el-button>
  <ArtFormDialog v-model:open="open" trap-focus :title="text('fulfillment')" width="min(520px, calc(100vw - 32px))" :show-confirm="false" :cancel-label="text('close')">
    <el-alert v-if="failure" :title="failure" type="error" :closable="false" />
    <p>{{ text('safeRetry') }}</p>
    <el-alert v-if="waitingForEffective" class="fulfillment-waiting" type="info" :title="text('waitingEffective')" :closable="false" data-testid="fulfillment-waiting" />
    <el-descriptions v-if="status" class="fulfillment-snapshot" :column="1" border>
      <el-descriptions-item :label="text('invoice')"><code>{{ status.subscriptionId }}</code></el-descriptions-item>
      <el-descriptions-item :label="text('fulfillment')">{{ subscriptionPhaseText(locale, status.stateKey) }}</el-descriptions-item>
      <el-descriptions-item :label="text('effectiveAt')"><time data-testid="fulfillment-effective" :datetime="status.effectiveFromUtc">{{ formatDate(status.effectiveFromUtc) }}</time></el-descriptions-item>
      <el-descriptions-item v-if="status.nextAttemptAtUtc" :label="text('nextAttemptAt')"><time data-testid="fulfillment-next" :datetime="status.nextAttemptAtUtc">{{ formatDate(status.nextAttemptAtUtc) }}</time></el-descriptions-item>
      <el-descriptions-item :label="text('recoveryAttempts')">{{ status.attempt }}</el-descriptions-item>
      <el-descriptions-item v-if="status.errorCode" :label="text('fulfillmentProblem')"><code>{{ status.errorCode }}</code></el-descriptions-item>
      <el-descriptions-item :label="text('fulfillmentReadAt')"><time data-testid="fulfillment-read-at" :datetime="status.readAtUtc">{{ formatDate(status.readAtUtc) }}</time></el-descriptions-item>
    </el-descriptions>
    <p v-if="status">{{ text('scheduleHint') }}</p>
    <el-button data-testid="subscription-fulfillment-refresh" :disabled="!allowed" :loading="loading" @click="load()">{{ text('refresh') }}</el-button>
    <el-button data-testid="subscription-fulfillment-retry" v-if="retryAllowed && !uncertain && status?.stateKey === 'Review'" :loading="loading" @click="load(true)">{{ text('retry') }}</el-button>
  </ArtFormDialog>
</template>
<style scoped>
.fulfillment-waiting { color: var(--el-text-color-primary); }
.fulfillment-snapshot :deep(.el-descriptions__label) { width: 40%; white-space: normal; }
.fulfillment-snapshot :deep(.el-descriptions__content) { overflow-wrap: anywhere; }
</style>
