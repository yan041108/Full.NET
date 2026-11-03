<script setup lang="ts">
import { computed, reactive, ref } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem, ElForm, ElFormItem, ElInput, ElOption, ElSelect } from 'element-plus';
import type { HostTenantPackage, TenantPackageOfferResponse, TenantSubscriptionPaymentTerms } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { subscriptionBillingText } from '../i18n/subscription-billing';
import { createPaidSubscription, getPackageOffer } from '../api/subscription-billing';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import SubscriptionCheckoutAction from './SubscriptionCheckoutAction.vue';

const props = defineProps<{ tenantId: string; packages: HostTenantPackage[] }>();
const emit = defineEmits<{ created: [] }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof subscriptionBillingText>[1]) => subscriptionBillingText(locale.value, key);
const allowed = computed(() => session.can('tenancy.tenant_packages.read') && session.can('tenancy.tenant_subscriptions.manage'));
const open = ref(false); const loading = ref(false); const failure = ref('');
const attempted = ref(false);
const quote = ref<TenantPackageOfferResponse>(); const terms = ref<TenantSubscriptionPaymentTerms>();
const form = reactive({ packageId: '', effectiveFromUtc: '', idempotencyKey: '' });
function begin() {
  open.value = true; failure.value = ''; quote.value = undefined; terms.value = undefined; attempted.value = false;
  Object.assign(form, { packageId: '', effectiveFromUtc: new Date().toISOString(), idempotencyKey: crypto.randomUUID() });
}
async function readQuote() {
  if (!form.packageId) return;
  loading.value = true; failure.value = ''; quote.value = undefined;
  try { const value = await getPackageOffer(form.packageId); if (value.isActive) quote.value = value; else failure.value = text('failure'); }
  catch { failure.value = text('failure'); }
  finally { loading.value = false; }
}
async function create() {
  if (loading.value || !quote.value || terms.value) return;
  if (!/^\d{4}-\d{2}-\d{2}T.*(?:Z|[+-]\d{2}:\d{2})$/.test(form.effectiveFromUtc) || !Number.isFinite(Date.parse(form.effectiveFromUtc))) {
    failure.value = text('validation'); return;
  }
  loading.value = true; failure.value = ''; attempted.value = true;
  try {
    terms.value = await createPaidSubscription(props.tenantId, { ...form, offerVersion: quote.value.version }); emit('created');
  } catch { failure.value = text('failure'); }
  finally { loading.value = false; }
}
</script>
<template>
  <el-button v-if="allowed" type="primary" plain data-testid="paid-subscription-create" @click="begin">{{ text('purchase') }}</el-button>
  <ArtFormDialog v-model:open="open" trap-focus :title="text('purchase')" width="min(520px, calc(100vw - 32px))" :saving="loading" :show-confirm="!!quote && !terms" confirm-test-id="paid-subscription-confirm"
    :confirm-label="text('create')" :cancel-label="text('close')" @confirm="create">
    <el-alert v-if="failure" :title="failure" type="error" :closable="false" />
    <p>{{ text('paidHint') }}</p>
    <el-form v-if="!terms" label-position="top" :disabled="loading || attempted">
      <el-form-item :label="text('package')"><el-select v-model="form.packageId" @change="quote = undefined">
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
