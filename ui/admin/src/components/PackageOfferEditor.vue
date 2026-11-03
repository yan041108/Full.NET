<script setup lang="ts">
import { computed, reactive, ref } from 'vue';
import { ElAlert, ElButton, ElForm, ElFormItem, ElInput, ElInputNumber, ElMessage, ElOption, ElSelect, ElSwitch } from 'element-plus';
import { isFullNetProblemDetails, isGuid, type TenantEntitlementCatalogResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { subscriptionBillingText } from '../i18n/subscription-billing';
import { getPackageOffer, listBillingFeatures, savePackageOffer } from '../api/subscription-billing';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';

const props = defineProps<{ packageId: string }>();
const session = useSessionStore();
const { locale } = useAdminI18n();
const text = (key: Parameters<typeof subscriptionBillingText>[1]) => subscriptionBillingText(locale.value, key);
const allowed = computed(() => ['tenancy.tenant_packages.read', 'tenancy.tenant_packages.update',
  'tenancy.tenant_entitlements.read', 'tenancy.tenant_entitlements.manage_bindings'].every(code => session.can(code)));
const open = ref(false);
const loading = ref(false);
const failure = ref('');
const features = ref<TenantEntitlementCatalogResponse[]>([]);
const form = reactive({ paymentTenantId: '', merchantConfigId: '', amountMinor: 1, currency: 'CNY', seatLimit: 0,
  storageLimit: 0, durationDays: 30, entitlementIds: [] as string[], isActive: true, version: 0 });
async function edit() {
  open.value = true; loading.value = true; failure.value = '';
  Object.assign(form, { paymentTenantId: '', merchantConfigId: '', amountMinor: 1, currency: 'CNY', seatLimit: 0,
    storageLimit: 0, durationDays: 30, entitlementIds: [], isActive: true, version: 0 });
  try {
    features.value = (await listBillingFeatures()).filter(item => item.isActive && item.entitlementType === 'Feature');
    try { Object.assign(form, await getPackageOffer(props.packageId)); }
    catch (error) { if (!isFullNetProblemDetails(error) || error.code !== 'tenancy.package_offer.not_found') throw error; }
  } catch { failure.value = text('failure'); }
  finally { loading.value = false; }
}
async function save() {
  if (loading.value || failure.value || !allowed.value) return;
  if (!isGuid(form.paymentTenantId) || !isGuid(form.merchantConfigId)
    || ![form.amountMinor, form.seatLimit, form.storageLimit, form.durationDays].every(Number.isSafeInteger)
    || form.amountMinor < 1 || form.seatLimit < 0 || form.storageLimit < 0 || form.durationDays < 1 || form.durationDays > 3660
    || !/^[A-Z]{3}$/.test(form.currency)) { ElMessage.warning(text('validation')); return; }
  loading.value = true;
  try { await savePackageOffer(props.packageId, { ...form }); open.value = false; ElMessage.success(text('saved')); }
  catch { failure.value = text('failure'); }
  finally { loading.value = false; }
}
</script>
<template>
  <el-button v-if="allowed" link type="primary" data-testid="package-offer-edit" @click="edit">{{ text('offer') }}</el-button>
  <ArtFormDialog v-model:open="open" trap-focus :title="text('offer')" width="min(520px, calc(100vw - 32px))" :saving="loading" :show-confirm="!failure" confirm-test-id="package-offer-save"
    :confirm-label="text('save')" :cancel-label="text('close')" @confirm="save">
    <el-alert v-if="failure" :title="failure" type="error" :closable="false" />
    <el-form v-loading="loading" label-position="top" :disabled="loading || !!failure">
      <el-form-item :label="text('seller')"><el-input v-model="form.paymentTenantId" /></el-form-item>
      <el-form-item :label="text('merchant')"><el-input v-model="form.merchantConfigId" /></el-form-item>
      <el-form-item :label="text('amount')"><el-input-number v-model="form.amountMinor" :min="1" :max="Number.MAX_SAFE_INTEGER" :precision="0" /></el-form-item>
      <el-form-item :label="text('currency')"><el-input v-model="form.currency" maxlength="3" /></el-form-item>
      <el-form-item :label="text('seats')"><el-input-number v-model="form.seatLimit" :min="0" :max="Number.MAX_SAFE_INTEGER" :precision="0" /></el-form-item>
      <el-form-item :label="text('storage')"><el-input-number v-model="form.storageLimit" :min="0" :max="Number.MAX_SAFE_INTEGER" :precision="0" /></el-form-item>
      <el-form-item :label="text('days')"><el-input-number v-model="form.durationDays" :min="1" :max="3660" :precision="0" /></el-form-item>
      <el-form-item :label="text('features')"><el-select v-model="form.entitlementIds" multiple filterable>
        <el-option v-for="feature in features" :key="feature.id" :label="feature.name" :value="feature.id" />
      </el-select></el-form-item>
      <el-form-item :label="text('active')"><el-switch v-model="form.isActive" /></el-form-item>
    </el-form>
  </ArtFormDialog>
</template>
