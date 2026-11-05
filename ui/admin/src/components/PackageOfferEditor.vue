<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import { ElAlert, ElButton, ElForm, ElFormItem, ElInput, ElInputNumber, ElOption, ElSelect, ElSwitch } from 'element-plus';
import { isFullNetProblemDetails, isGuid, type TenantEntitlementCatalogResponse, type TenantPackageOfferResponse, type UpsertTenantPackageOfferRequest } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { subscriptionBillingText } from '../i18n/subscription-billing';
import { getPackageOffer, listBillingFeatures, savePackageOffer } from '../api/subscription-billing';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';

const props = defineProps<{ packageId: string }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof subscriptionBillingText>[1]) => subscriptionBillingText(locale.value, key);
const allowed = computed(() => session.state === 'authenticated' && ['tenancy.tenant_packages.read', 'tenancy.tenant_packages.update',
  'tenancy.tenant_entitlements.read', 'tenancy.tenant_entitlements.manage_bindings'].every(code => session.can(code)));
const open = ref(false); const loading = ref(false); const ready = ref(false); const failure = ref('');
const features = ref<TenantEntitlementCatalogResponse[]>([]);
const defaults = () => ({ paymentTenantId: '', merchantConfigId: '', amountMinor: 1, currency: 'CNY', seatLimit: 0,
  storageLimit: 0, durationDays: 30, entitlementIds: [] as string[], isActive: true, version: 0 });
const form = reactive(defaults());
const attempted = ref<Readonly<UpsertTenantPackageOfferRequest>>();
const receipt = ref<'saving' | 'saved' | 'unknown' | 'rejected'>();
// undefined 尚未核对，null 是当前目标正式返回的不存在；不能把读取失败当作首次创建。
const current = ref<TenantPackageOfferResponse | null>();
const scope = useAuthorizedViewScope(session, () => {
  open.value = false; loading.value = false; ready.value = false; failure.value = ''; features.value = [];
  attempted.value = undefined; receipt.value = undefined; current.value = undefined; Object.assign(form, defaults());
}, () => {});
let pending: ReturnType<typeof scope.begin>;
watch(() => props.packageId, () => scope.invalidate(), { flush: 'sync' });
watch(open, value => {
  if (!value) {
    if (receipt.value === 'saving') receipt.value = 'unknown';
    pending?.cancel(); pending = undefined; loading.value = false; if (receipt.value !== 'saved') current.value = undefined;
  }
}, { flush: 'sync' });
const sameId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
function payload(value: UpsertTenantPackageOfferRequest): UpsertTenantPackageOfferRequest {
  return { paymentTenantId: value.paymentTenantId, merchantConfigId: value.merchantConfigId, amountMinor: value.amountMinor, currency: value.currency,
    seatLimit: value.seatLimit, storageLimit: value.storageLimit, durationDays: value.durationDays, entitlementIds: [...value.entitlementIds], isActive: value.isActive, version: value.version };
}
function sameContent(a: UpsertTenantPackageOfferRequest, b: UpsertTenantPackageOfferRequest) {
  return sameId(a.paymentTenantId, b.paymentTenantId) && sameId(a.merchantConfigId, b.merchantConfigId) && a.amountMinor === b.amountMinor
    && a.currency === b.currency && a.seatLimit === b.seatLimit && a.storageLimit === b.storageLimit && a.durationDays === b.durationDays
    && a.isActive === b.isActive && a.entitlementIds.length === b.entitlementIds.length
    && [...a.entitlementIds].map(id => id.toLowerCase()).sort().join() === [...b.entitlementIds].map(id => id.toLowerCase()).sort().join();
}
const matches = computed(() => !!attempted.value && !!current.value && sameContent(attempted.value, current.value));
async function readOffer(target: string, signal: AbortSignal) {
  try {
    const value = await getPackageOffer(target, signal);
    if (!sameId(value.packageId, target)) throw new Error('offer-target-mismatch');
    return value;
  } catch (error) {
    if (isFullNetProblemDetails(error) && error.status === 404 && error.code === 'tenancy.package_offer.not_found') return null;
    throw error;
  }
}
async function read() {
  if (!open.value || !allowed.value || loading.value) return;
  const operation = scope.begin('tenancy.tenant_packages.read'); if (!operation) return;
  const target = props.packageId; const checking = !!attempted.value;
  pending = operation; loading.value = true; failure.value = ''; current.value = undefined;
  try {
    const [catalog, quote] = await Promise.allSettled([listBillingFeatures(operation.signal), readOffer(target, operation.signal)]);
    if (!operation.current() || !open.value || !allowed.value || target !== props.packageId) return;
    if (catalog.status === 'rejected') throw catalog.reason;
    if (quote.status === 'rejected') throw quote.reason;
    features.value = catalog.value.filter(item => item.isActive && item.entitlementType === 'Feature');
    if (checking) current.value = quote.value;
    else { Object.assign(form, quote.value ? payload(quote.value) : defaults()); ready.value = true; }
  } catch (error) {
    if (operation.current() && open.value) failure.value = error instanceof Error && error.message === 'offer-target-mismatch' ? text('responseMismatch') : text('offerReadFailed');
  } finally { operation.finish(); if (pending === operation) { pending = undefined; loading.value = false; } }
}
function edit() {
  if (!allowed.value || loading.value) return;
  open.value = true;
  // 未确认保存保留原草稿和版本，关闭重开只提供人工核对，不重发 PUT。
  if (attempted.value && receipt.value !== 'saved') return;
  attempted.value = undefined; receipt.value = undefined; current.value = undefined; ready.value = false;
  Object.assign(form, defaults()); void read();
}
async function save() {
  if (!open.value || !allowed.value || loading.value || !ready.value || attempted.value) return;
  if (!isGuid(form.paymentTenantId) || !isGuid(form.merchantConfigId)
    || ![form.amountMinor, form.seatLimit, form.storageLimit, form.durationDays, form.version].every(Number.isSafeInteger)
    || form.amountMinor < 1 || form.seatLimit < 0 || form.storageLimit < 0 || form.durationDays < 1 || form.durationDays > 3660 || form.version < 0
    || !/^[A-Z]{3}$/.test(form.currency) || form.entitlementIds.length > 100 || new Set(form.entitlementIds.map(id => id.toLowerCase())).size !== form.entitlementIds.length
    || form.entitlementIds.some(id => !features.value.some(feature => sameId(feature.id, id)))) { failure.value = text('validation'); return; }
  const operation = scope.begin('tenancy.tenant_packages.update'); if (!operation) return;
  const target = props.packageId;
  const body = Object.freeze({ ...payload(form), entitlementIds: [...form.entitlementIds] });
  attempted.value = body; receipt.value = 'saving'; current.value = undefined; pending = operation; loading.value = true; failure.value = '';
  try {
    const value = await savePackageOffer(target, body, operation.signal);
    if (!operation.current() || !open.value || !allowed.value || target !== props.packageId) return;
    if (!sameId(value.packageId, target) || value.version !== body.version + 1 || !sameContent(body, value)) { receipt.value = 'unknown'; failure.value = text('responseMismatch'); return; }
    receipt.value = 'saved'; current.value = value;
  } catch (error) {
    if (!operation.current() || !open.value) return;
    const rejected = isFullNetProblemDetails(error) && error.status >= 400 && error.status < 500 && ![408, 429].includes(error.status);
    receipt.value = rejected ? 'rejected' : 'unknown'; failure.value = text(rejected ? 'offerConflict' : 'offerUnknown');
  } finally { operation.finish(); if (pending === operation) { pending = undefined; loading.value = false; } }
}
function adopt(keepDraft: boolean) {
  if (!open.value || !allowed.value || loading.value || !attempted.value || current.value === undefined) return;
  if (keepDraft) { Object.assign(form, payload(attempted.value)); form.version = current.value?.version ?? 0; }
  else Object.assign(form, current.value ? payload(current.value) : defaults());
  attempted.value = undefined; receipt.value = undefined; current.value = undefined; ready.value = true; failure.value = '';
}
type Field = 'paymentTenantId' | 'merchantConfigId' | 'amountMinor' | 'currency' | 'seatLimit' | 'storageLimit' | 'durationDays' | 'entitlementIds' | 'isActive';
const fields: [Field, Parameters<typeof text>[0]][] = [['paymentTenantId', 'seller'], ['merchantConfigId', 'merchant'], ['amountMinor', 'amount'], ['currency', 'currency'],
  ['seatLimit', 'seats'], ['storageLimit', 'storage'], ['durationDays', 'days'], ['entitlementIds', 'features'], ['isActive', 'active']];
function display(value: unknown): string {
  if (Array.isArray(value)) return value.map(id => features.value.find(feature => sameId(feature.id, id))?.name ?? id).join(', ') || '—';
  return typeof value === 'boolean' ? text(value ? 'offerEnabled' : 'offerDisabled') : value == null ? '—' : String(value);
}
</script>
<template>
  <ElButton v-if="allowed" link type="primary" data-testid="package-offer-edit" @click="edit">{{ text('offer') }}</ElButton>
  <ArtFormDialog v-model:open="open" trap-focus :title="text('offer')" width="min(620px, calc(100vw - 32px))" :saving="loading"
    :show-confirm="!attempted" :confirm-disabled="!allowed || !ready || loading" confirm-test-id="package-offer-save"
    :confirm-label="text('save')" :cancel-label="text('close')" @confirm="save">
    <p>{{ text('offerScopeHint') }}</p>
    <ElAlert v-if="failure" :title="failure" type="warning" :closable="false" />
    <p v-if="receipt" role="status" data-testid="package-offer-receipt">{{ text(receipt === 'saving' ? 'offerSaving' : receipt === 'saved' ? 'saved' : receipt === 'unknown' ? 'offerUnknown' : 'offerConflict') }}</p>
    <ElButton v-if="!ready || attempted" data-testid="package-offer-reload" :loading="loading" @click="read">{{ text(attempted ? 'offerCheck' : 'offerReload') }}</ElButton>
    <template v-if="attempted">
      <p v-if="current !== undefined" data-testid="package-offer-current-state">{{ text(current === null ? 'offerMissing' : matches ? 'offerCurrentMatches' : 'offerCurrentChanged') }} {{ text('offerVersion') }}: {{ current?.version ?? 0 }}</p>
      <div class="offer-comparison" role="region" :aria-label="text('offerComparison')" tabindex="0">
        <table><caption>{{ text('offerComparison') }}</caption><thead><tr><th scope="col">{{ text('offerField') }}</th><th scope="col">{{ text('offerDraft') }}</th><th scope="col">{{ text('offerCurrent') }}</th></tr></thead>
          <tbody><tr v-for="[field, label] in fields" :key="field"><th scope="row">{{ text(label) }}</th><td>{{ display(attempted[field]) }}</td><td>{{ current === undefined ? text('offerUnchecked') : display(current?.[field]) }}</td></tr></tbody>
        </table>
      </div>
      <template v-if="current !== undefined">
        <p>{{ text('offerRebaseHint') }}</p>
        <ElButton data-testid="package-offer-adopt-current" :disabled="loading || !allowed" @click="adopt(false)">{{ text('offerAdoptCurrent') }}</ElButton>
        <ElButton v-if="receipt !== 'saved' && !matches" data-testid="package-offer-keep-draft" :disabled="loading || !allowed" @click="adopt(true)">{{ text('offerKeepDraft') }}</ElButton>
      </template>
    </template>
    <!-- 数值组件只在挂载时写入 aria-disabled，授权数据就绪后再挂载，保持交互与辅助语义一致。 -->
    <ElForm v-else-if="ready && !loading" label-position="top" :disabled="!allowed">
      <ElFormItem :label="text('seller')"><ElInput v-model="form.paymentTenantId" data-testid="package-offer-seller" /></ElFormItem>
      <ElFormItem :label="text('merchant')"><ElInput v-model="form.merchantConfigId" data-testid="package-offer-merchant" /></ElFormItem>
      <ElFormItem :label="text('amount')"><ElInputNumber v-model="form.amountMinor" data-testid="package-offer-amount" :min="1" :max="Number.MAX_SAFE_INTEGER" :precision="0" /></ElFormItem>
      <ElFormItem :label="text('currency')"><ElInput v-model="form.currency" maxlength="3" /></ElFormItem>
      <ElFormItem :label="text('seats')"><ElInputNumber v-model="form.seatLimit" :min="0" :max="Number.MAX_SAFE_INTEGER" :precision="0" /></ElFormItem>
      <ElFormItem :label="text('storage')"><ElInputNumber v-model="form.storageLimit" :min="0" :max="Number.MAX_SAFE_INTEGER" :precision="0" /></ElFormItem>
      <ElFormItem :label="text('days')"><ElInputNumber v-model="form.durationDays" :min="1" :max="3660" :precision="0" /></ElFormItem>
      <ElFormItem :label="text('features')"><ElSelect v-model="form.entitlementIds" multiple filterable><ElOption v-for="feature in features" :key="feature.id" :label="feature.name" :value="feature.id" /></ElSelect></ElFormItem>
      <ElFormItem :label="text('active')"><ElSwitch v-model="form.isActive" /></ElFormItem>
    </ElForm>
  </ArtFormDialog>
</template>
<style scoped>
.offer-comparison { overflow-x: auto; margin: 12px 0; }
.offer-comparison table { width: 100%; border-collapse: collapse; table-layout: fixed; }
.offer-comparison th, .offer-comparison td { padding: 8px; text-align: start; vertical-align: top; overflow-wrap: anywhere; border-bottom: 1px solid var(--el-border-color); color: var(--el-text-color-primary); }
</style>
