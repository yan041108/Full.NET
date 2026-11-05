<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem, ElForm, ElFormItem, ElInput, ElOption, ElSelect } from 'element-plus';
import { isFullNetProblemDetails, type CreatePaidTenantSubscriptionRequest, type CreatePaidTenantSubscriptionResponse, type HostTenantPackage, type TenantPackageOfferResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { subscriptionBillingText } from '../i18n/subscription-billing';
import { createPaidSubscription, getPackageOffer, getSubscriptionRenewalPreview } from '../api/subscription-billing';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import SubscriptionCheckoutAction from './SubscriptionCheckoutAction.vue';
import SubscriptionQuotaImpact from './SubscriptionQuotaImpact.vue';
const props = defineProps<{ tenantId: string; packages: HostTenantPackage[]; disabled?: boolean; source?: { subscriptionId: string; packageId: string }; renewal?: boolean; changePackage?: boolean }>();
const emit = defineEmits<{ created: []; dialogOpen: [value: boolean] }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof subscriptionBillingText>[1]) => subscriptionBillingText(locale.value, key);
const scheduled = computed(() => props.renewal || props.changePackage);
const targetPackages = computed(() => props.changePackage ? props.packages.filter(pkg => pkg.isActive && pkg.id.toLowerCase() !== props.source?.packageId.toLowerCase()) : props.packages);
const open = ref(false); const loading = ref(false); const failure = ref(''); const attempted = ref(false);
const allowed = computed(() => !props.disabled && !(props.renewal && props.changePackage) && session.state === 'authenticated'
  && session.can('tenancy.tenant_packages.read') && session.can('tenancy.tenant_subscriptions.manage')
  && (!scheduled.value || (!!props.source && session.can('tenancy.tenant_subscriptions.read')))
  // 原请求可能已被接受；目录退出销售不应阻止按原身份核对，而新提案仍要求活动目标。
  && (attempted.value || (props.changePackage ? targetPackages.value.length > 0
    : !props.source || props.packages.some(pkg => pkg.isActive && pkg.id.toLowerCase() === props.source!.packageId.toLowerCase()))));
const quoteRejected = ref(false);
const targetRejected = ref(false);
const renewalSourceVersion = ref<number>(); const previewReadAt = ref('');
const quote = ref<TenantPackageOfferResponse>(); const terms = ref<CreatePaidTenantSubscriptionResponse>();
const form = reactive({ packageId: '', effectiveFromUtc: '', idempotencyKey: '' });
let frozen: Readonly<CreatePaidTenantSubscriptionRequest> | undefined;
const scope = useAuthorizedViewScope(session, () => {
  open.value = false; loading.value = false; failure.value = ''; attempted.value = false;
  quote.value = undefined; terms.value = undefined; frozen = undefined; quoteRejected.value = false;
  targetRejected.value = false;
  renewalSourceVersion.value = undefined; previewReadAt.value = '';
  Object.assign(form, { packageId: '', effectiveFromUtc: '', idempotencyKey: '' });
}, () => {});
let pending: ReturnType<typeof scope.begin>;
// 列表重读会重建来源对象；只有实际身份改变才失效，避免清掉刚创建的新账单。
watch([() => props.tenantId, () => props.source?.subscriptionId, () => props.source?.packageId, () => props.renewal, () => props.changePackage], () => scope.invalidate(), { flush: 'sync' });
watch(open, value => { if (!value) { pending?.cancel(); pending = undefined; loading.value = false; } }, { flush: 'sync' });
watch(open, value => emit('dialogOpen', value), { flush: 'sync' });
const sameId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
function begin() {
  if (!allowed.value) return;
  // 未确认请求保留原报价、时间和幂等键，关闭重开不能意外创建第二张账单。
  if (!attempted.value) {
    failure.value = ''; quote.value = undefined; terms.value = undefined;
    renewalSourceVersion.value = undefined; previewReadAt.value = '';
    Object.assign(form, { packageId: props.changePackage ? '' : props.source?.packageId ?? '', effectiveFromUtc: scheduled.value ? '' : new Date().toISOString(), idempotencyKey: crypto.randomUUID() });
  }
  open.value = true;
}
async function readQuote() {
  if (!open.value || !allowed.value || loading.value || attempted.value || !form.packageId) return;
  const operation = scope.begin('tenancy.tenant_packages.read'); if (!operation) return;
  const packageId = form.packageId; const tenantId = props.tenantId;
  pending = operation; loading.value = true; failure.value = ''; quote.value = undefined;
  if (scheduled.value) { renewalSourceVersion.value = undefined; previewReadAt.value = ''; form.effectiveFromUtc = ''; }
  try {
    const preview = scheduled.value && props.source
      ? props.changePackage ? await getSubscriptionRenewalPreview(tenantId, props.source.subscriptionId, operation.signal, packageId)
        : await getSubscriptionRenewalPreview(tenantId, props.source.subscriptionId, operation.signal) : undefined;
    const value = preview ? preview.offer : await getPackageOffer(packageId, operation.signal);
    if (!operation.current() || !open.value || !allowed.value || tenantId !== props.tenantId || packageId !== form.packageId) return;
    if (preview && (!sameId(preview.tenantId, tenantId) || !sameId(preview.sourceSubscriptionId, props.source!.subscriptionId)
      || !sameId(preview.sourcePackageId, props.source!.packageId)
      || !Number.isInteger(preview.sourceVersion) || preview.sourceVersion < 1
      || !Number.isFinite(Date.parse(preview.readAtUtc)) || !Number.isFinite(Date.parse(preview.effectiveFromUtc))
      || Date.parse(preview.effectiveFromUtc) <= Date.parse(preview.readAtUtc))) { failure.value = text('responseMismatch'); return; }
    if (value.isActive && sameId(value.packageId, packageId)) {
      quote.value = value;
      if (preview) { form.effectiveFromUtc = preview.effectiveFromUtc; renewalSourceVersion.value = preview.sourceVersion; previewReadAt.value = preview.readAtUtc; }
    }
    else failure.value = text('failure');
  } catch { if (operation.current() && open.value) failure.value = text('failure'); }
  finally { operation.finish(); if (pending === operation) { pending = undefined; loading.value = false; } }
}
async function create() {
  if (!open.value || !allowed.value || loading.value || !quote.value || terms.value) return;
  if (scheduled.value && !frozen && !renewalSourceVersion.value) return;
  if (!frozen && (!/^\d{4}-\d{2}-\d{2}T.*(?:Z|[+-]\d{2}:\d{2})$/.test(form.effectiveFromUtc) || !Number.isFinite(Date.parse(form.effectiveFromUtc)))) {
    failure.value = text('validation'); return;
  }
  const operation = scope.begin('tenancy.tenant_subscriptions.manage'); if (!operation) return;
  const tenantId = props.tenantId; const offer = quote.value;
  frozen ??= Object.freeze({ ...form, offerVersion: offer.version,
    ...(scheduled.value ? { renewalOfSubscriptionId: props.source!.subscriptionId, renewalSourceVersion: renewalSourceVersion.value! } : {}) });
  const body = frozen; pending = operation; loading.value = true; failure.value = ''; attempted.value = true;
  try {
    const value = await createPaidSubscription(tenantId, body, operation.signal);
    if (!operation.current() || !open.value || !allowed.value || tenantId !== props.tenantId) return;
    if (!sameId(value.tenantId, tenantId) || !sameId(value.packageId, body.packageId) || value.offerVersion !== body.offerVersion
      || (props.source && sameId(value.subscriptionId, props.source.subscriptionId))
      || (body.renewalOfSubscriptionId && (!value.renewalOfSubscriptionId || !sameId(value.renewalOfSubscriptionId, body.renewalOfSubscriptionId)
        || value.renewalSourceVersion !== body.renewalSourceVersion))
      || (!body.renewalOfSubscriptionId && (value.renewalOfSubscriptionId != null || value.renewalSourceVersion != null))
      || Date.parse(value.effectiveFromUtc) !== Date.parse(body.effectiveFromUtc) || value.amountMinor !== offer.amountMinor
      || value.currency !== offer.currency || !sameId(value.merchantConfigId, offer.merchantConfigId) || !sameId(value.paymentTenantId, offer.paymentTenantId)) {
      failure.value = text('responseMismatch'); return;
    }
    terms.value = value; emit('created');
  } catch (error) {
    if (!operation.current() || !open.value) return;
    targetRejected.value = !!props.changePackage && isFullNetProblemDetails(error) && [400, 409, 422].includes(error.status)
      && error.code === 'tenancy.tenant_package.inactive';
    quoteRejected.value = targetRejected.value || (isFullNetProblemDetails(error) && error.status === 409 && (error.code === 'tenancy.package_offer.version_conflict'
      || (scheduled.value && error.code === 'tenancy.tenant_subscription.version_conflict')));
    failure.value = text(targetRejected.value ? 'packageChangeRejected' : quoteRejected.value ? scheduled.value ? 'renewalChanged' : 'offerChanged' : isFullNetProblemDetails(error) && error.status >= 400 && error.status < 500 && ![408, 429].includes(error.status) ? 'invoiceRejected' : 'invoiceUnknown');
  }
  finally { operation.finish(); if (pending === operation) { pending = undefined; loading.value = false; } }
}
function reconfirmQuote() {
  if (!open.value || !allowed.value || loading.value || !quoteRejected.value) return;
  // 仅正式来源/报价版本冲突确认原请求未创建时，允许重读提案并使用新身份。
  const reselect = targetRejected.value;
  quoteRejected.value = false; targetRejected.value = false; attempted.value = false; frozen = undefined; quote.value = undefined; failure.value = '';
  form.idempotencyKey = crypto.randomUUID();
  if (reselect) { form.packageId = ''; changeTarget(); } else void readQuote();
}
function changeTarget() {
  quote.value = undefined;
  if (scheduled.value) { renewalSourceVersion.value = undefined; previewReadAt.value = ''; form.effectiveFromUtc = ''; }
}
function reselectTarget() {
  if (!open.value || !allowed.value || loading.value || attempted.value || !props.changePackage) return;
  form.packageId = ''; changeTarget();
}
</script>
<template>
  <el-button v-if="allowed" type="primary" plain :data-testid="changePackage ? 'subscription-package-change' : renewal ? 'subscription-renewal' : source ? 'subscription-repurchase' : 'paid-subscription-create'" @click="begin">{{ text(changePackage ? 'packageChange' : renewal ? 'renewal' : source ? 'repurchase' : 'purchase') }}</el-button>
  <ArtFormDialog v-model:open="open" trap-focus :title="text(changePackage ? 'packageChange' : renewal ? 'renewal' : source ? 'repurchase' : 'purchase')" width="min(520px, calc(100vw - 32px))" :saving="loading" :show-confirm="!!quote && !terms" confirm-test-id="paid-subscription-confirm"
    :confirm-disabled="!allowed || loading" :confirm-label="text(attempted ? 'recoverInvoice' : 'create')" :cancel-label="text('close')" @confirm="create">
    <el-alert v-if="failure" :title="failure" type="error" :closable="false" />
    <el-button v-if="quoteRejected" data-testid="subscription-reconfirm-quote" :disabled="loading || !allowed" @click="reconfirmQuote">{{ text(targetRejected ? 'packageChangeReselect' : 'reconfirmQuote') }}</el-button>
    <p>{{ text('paidHint') }}</p>
    <template v-if="source"><p>{{ text(changePackage ? 'packageChangePolicy' : renewal ? 'renewalPolicy' : 'repurchasePolicy') }}</p><p>{{ text('sourceSubscription') }} <code data-testid="repurchase-source">{{ source.subscriptionId }}</code></p></template>
    <p v-if="changePackage && source">{{ text('sourcePackage') }} <code data-testid="package-change-source">{{ source.packageId }}</code></p>
    <p v-if="scheduled && previewReadAt" data-testid="renewal-preview-read-at">{{ text('fulfillmentReadAt') }} {{ previewReadAt }}</p>
    <p v-if="attempted && !terms" role="status" data-testid="subscription-invoice-recovery">{{ text('invoiceRecovery') }}</p>
    <el-form v-if="!terms" label-position="top" :disabled="loading || attempted">
      <el-form-item v-if="!changePackage || (!attempted && !quote)" :label="text(changePackage ? 'targetPackage' : 'package')">
      <div v-if="source && !changePackage" data-testid="repurchase-package" style="overflow-wrap: anywhere">
        {{ packages.find(pkg => pkg.id.toLowerCase() === source?.packageId.toLowerCase())?.name }} <code>{{ source.packageId }}</code>
      </div>
      <el-select v-else data-testid="paid-subscription-package" v-model="form.packageId" @change="changeTarget">
        <el-option v-for="pkg in targetPackages" :key="pkg.id" :label="pkg.name" :value="pkg.id" />
      </el-select></el-form-item>
      <el-form-item :label="text('start')"><code v-if="scheduled && attempted" data-testid="paid-subscription-start">{{ form.effectiveFromUtc }}</code><el-input v-else v-model="form.effectiveFromUtc" :readonly="scheduled" data-testid="paid-subscription-start" /></el-form-item>
      <el-button :disabled="!form.packageId" @click="readQuote">{{ text('quote') }}</el-button>
      <el-button v-if="changePackage && quote && !attempted" data-testid="package-change-target-reset" :disabled="loading" @click="reselectTarget">{{ text('packageChangeReselect') }}</el-button>
    </el-form>
    <el-descriptions v-if="quote" :column="1" border>
      <el-descriptions-item v-if="changePackage" :label="text('targetPackage')">{{ terms?.subject ?? packages.find(pkg => sameId(pkg.id, quote!.packageId))?.name }} <code>{{ quote.packageId }}</code></el-descriptions-item>
      <el-descriptions-item :label="text('amount')">{{ quote.amountMinor }} {{ quote.currency }}</el-descriptions-item>
      <el-descriptions-item :label="text('days')">{{ quote.durationDays }}</el-descriptions-item>
      <el-descriptions-item :label="text('seats')">{{ quote.seatLimit }}</el-descriptions-item>
      <el-descriptions-item :label="text('storage')">{{ quote.storageLimit }}</el-descriptions-item>
    </el-descriptions>
    <SubscriptionQuotaImpact v-if="open && changePackage && quote && !attempted && !terms && !quoteRejected" :tenant-id="tenantId" :offer="quote" :disabled="loading" />
    <el-descriptions v-if="terms" :column="1" border>
      <el-descriptions-item :label="text('invoice')"><code>{{ terms.subscriptionId }}</code></el-descriptions-item>
      <el-descriptions-item :label="text('start')">{{ terms.effectiveFromUtc }}</el-descriptions-item>
      <el-descriptions-item :label="text('end')">{{ terms.effectiveToUtc }}</el-descriptions-item>
    </el-descriptions>
    <SubscriptionCheckoutAction v-if="terms" :tenant-id="tenantId" :subscription-id="terms.subscriptionId" />
  </ArtFormDialog>
</template>
