<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem, ElForm, ElFormItem, ElInput, ElOption, ElSelect } from 'element-plus';
import { isFullNetProblemDetails, type CreatePaidTenantSubscriptionRequest, type HostTenantPackage, type TenantPackageOfferResponse, type TenantSubscriptionPaymentTerms } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { subscriptionBillingText } from '../i18n/subscription-billing';
import { createPaidSubscription, getPackageOffer } from '../api/subscription-billing';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import SubscriptionCheckoutAction from './SubscriptionCheckoutAction.vue';
const props = defineProps<{ tenantId: string; packages: HostTenantPackage[]; disabled?: boolean; source?: { subscriptionId: string; packageId: string } }>();
const emit = defineEmits<{ created: []; dialogOpen: [value: boolean] }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof subscriptionBillingText>[1]) => subscriptionBillingText(locale.value, key);
const allowed = computed(() => !props.disabled && session.state === 'authenticated' && session.can('tenancy.tenant_packages.read') && session.can('tenancy.tenant_subscriptions.manage')
  && (!props.source || props.packages.some(pkg => pkg.isActive && pkg.id.toLowerCase() === props.source!.packageId.toLowerCase())));
const open = ref(false); const loading = ref(false); const failure = ref(''); const attempted = ref(false);
const quoteRejected = ref(false);
const quote = ref<TenantPackageOfferResponse>(); const terms = ref<TenantSubscriptionPaymentTerms>();
const form = reactive({ packageId: '', effectiveFromUtc: '', idempotencyKey: '' });
let frozen: Readonly<CreatePaidTenantSubscriptionRequest> | undefined;
const scope = useAuthorizedViewScope(session, () => {
  open.value = false; loading.value = false; failure.value = ''; attempted.value = false;
  quote.value = undefined; terms.value = undefined; frozen = undefined; quoteRejected.value = false;
  Object.assign(form, { packageId: '', effectiveFromUtc: '', idempotencyKey: '' });
}, () => {});
let pending: ReturnType<typeof scope.begin>;
// 列表重读会重建来源对象；只有实际身份改变才失效，避免清掉刚创建的新账单。
watch([() => props.tenantId, () => props.source?.subscriptionId, () => props.source?.packageId], () => scope.invalidate(), { flush: 'sync' });
watch(open, value => { if (!value) { pending?.cancel(); pending = undefined; loading.value = false; } }, { flush: 'sync' });
watch(open, value => emit('dialogOpen', value), { flush: 'sync' });
const sameId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
function begin() {
  if (!allowed.value) return;
  // 未确认请求保留原报价、时间和幂等键，关闭重开不能意外创建第二张账单。
  if (!attempted.value) {
    failure.value = ''; quote.value = undefined; terms.value = undefined;
    Object.assign(form, { packageId: props.source?.packageId ?? '', effectiveFromUtc: new Date().toISOString(), idempotencyKey: crypto.randomUUID() });
  }
  open.value = true;
}
async function readQuote() {
  if (!open.value || !allowed.value || loading.value || attempted.value || !form.packageId) return;
  const operation = scope.begin('tenancy.tenant_packages.read'); if (!operation) return;
  const packageId = form.packageId; const tenantId = props.tenantId;
  pending = operation; loading.value = true; failure.value = ''; quote.value = undefined;
  try {
    const value = await getPackageOffer(packageId, operation.signal);
    if (!operation.current() || !open.value || !allowed.value || tenantId !== props.tenantId || packageId !== form.packageId) return;
    if (value.isActive && sameId(value.packageId, packageId)) quote.value = value;
    else failure.value = text('failure');
  } catch { if (operation.current() && open.value) failure.value = text('failure'); }
  finally { operation.finish(); if (pending === operation) { pending = undefined; loading.value = false; } }
}
async function create() {
  if (!open.value || !allowed.value || loading.value || !quote.value || terms.value) return;
  if (!frozen && (!/^\d{4}-\d{2}-\d{2}T.*(?:Z|[+-]\d{2}:\d{2})$/.test(form.effectiveFromUtc) || !Number.isFinite(Date.parse(form.effectiveFromUtc)))) {
    failure.value = text('validation'); return;
  }
  const operation = scope.begin('tenancy.tenant_subscriptions.manage'); if (!operation) return;
  const tenantId = props.tenantId; const offer = quote.value;
  frozen ??= Object.freeze({ ...form, offerVersion: offer.version });
  const body = frozen; pending = operation; loading.value = true; failure.value = ''; attempted.value = true;
  try {
    const value = await createPaidSubscription(tenantId, body, operation.signal);
    if (!operation.current() || !open.value || !allowed.value || tenantId !== props.tenantId) return;
    if (!sameId(value.tenantId, tenantId) || !sameId(value.packageId, body.packageId) || value.offerVersion !== body.offerVersion
      || (props.source && sameId(value.subscriptionId, props.source.subscriptionId))
      || Date.parse(value.effectiveFromUtc) !== Date.parse(body.effectiveFromUtc) || value.amountMinor !== offer.amountMinor
      || value.currency !== offer.currency || !sameId(value.merchantConfigId, offer.merchantConfigId) || !sameId(value.paymentTenantId, offer.paymentTenantId)) {
      failure.value = text('responseMismatch'); return;
    }
    terms.value = value; emit('created');
  } catch (error) {
    if (!operation.current() || !open.value) return;
    quoteRejected.value = isFullNetProblemDetails(error) && error.status === 409 && error.code === 'tenancy.package_offer.version_conflict';
    failure.value = text(quoteRejected.value ? 'offerChanged' : isFullNetProblemDetails(error) && error.status >= 400 && error.status < 500 && ![408, 429].includes(error.status) ? 'invoiceRejected' : 'invoiceUnknown');
  }
  finally { operation.finish(); if (pending === operation) { pending = undefined; loading.value = false; } }
}
function reconfirmQuote() {
  if (!open.value || !allowed.value || loading.value || !quoteRejected.value) return;
  // 仅正式报价冲突确认原请求未创建时，允许重新读取报价并使用新请求身份。
  quoteRejected.value = false; attempted.value = false; frozen = undefined; quote.value = undefined; failure.value = '';
  form.idempotencyKey = crypto.randomUUID(); void readQuote();
}
</script>
<template>
  <el-button v-if="allowed" type="primary" plain :data-testid="source ? 'subscription-repurchase' : 'paid-subscription-create'" @click="begin">{{ text(source ? 'repurchase' : 'purchase') }}</el-button>
  <ArtFormDialog v-model:open="open" trap-focus :title="text(source ? 'repurchase' : 'purchase')" width="min(520px, calc(100vw - 32px))" :saving="loading" :show-confirm="!!quote && !terms" confirm-test-id="paid-subscription-confirm"
    :confirm-disabled="!allowed || loading" :confirm-label="text(attempted ? 'recoverInvoice' : 'create')" :cancel-label="text('close')" @confirm="create">
    <el-alert v-if="failure" :title="failure" type="error" :closable="false" />
    <el-button v-if="quoteRejected" data-testid="subscription-reconfirm-quote" :disabled="loading || !allowed" @click="reconfirmQuote">{{ text('reconfirmQuote') }}</el-button>
    <p>{{ text('paidHint') }}</p>
    <template v-if="source"><p>{{ text('repurchasePolicy') }}</p><p>{{ text('sourceSubscription') }} <code data-testid="repurchase-source">{{ source.subscriptionId }}</code></p></template>
    <p v-if="attempted && !terms" role="status" data-testid="subscription-invoice-recovery">{{ text('invoiceRecovery') }}</p>
    <el-form v-if="!terms" label-position="top" :disabled="loading || attempted">
      <el-form-item :label="text('package')"><el-select data-testid="paid-subscription-package" v-model="form.packageId" :disabled="!!source" @change="quote = undefined">
        <el-option v-for="pkg in packages" :key="pkg.id" :label="pkg.name" :value="pkg.id" />
      </el-select></el-form-item>
      <el-form-item :label="text('start')"><el-input v-model="form.effectiveFromUtc" /></el-form-item>
      <el-button :disabled="!form.packageId" @click="readQuote">{{ text('quote') }}</el-button>
    </el-form>
    <el-descriptions v-if="quote" :column="1" border>
      <el-descriptions-item :label="text('amount')">{{ quote.amountMinor }} {{ quote.currency }}</el-descriptions-item>
      <el-descriptions-item :label="text('days')">{{ quote.durationDays }}</el-descriptions-item>
      <el-descriptions-item :label="text('seats')">{{ quote.seatLimit }}</el-descriptions-item>
      <el-descriptions-item :label="text('storage')">{{ quote.storageLimit }}</el-descriptions-item>
    </el-descriptions>
    <el-descriptions v-if="terms" :column="1" border>
      <el-descriptions-item :label="text('invoice')"><code>{{ terms.subscriptionId }}</code></el-descriptions-item>
      <el-descriptions-item :label="text('end')">{{ terms.effectiveToUtc }}</el-descriptions-item>
    </el-descriptions>
    <SubscriptionCheckoutAction v-if="terms" :tenant-id="tenantId" :subscription-id="terms.subscriptionId" />
  </ArtFormDialog>
</template>
