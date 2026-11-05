<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import { ElAlert, ElButton, ElForm, ElFormItem, ElInput, ElMessage, ElRadio, ElRadioGroup } from 'element-plus';
import { isFullNetProblemDetails, isGuid, type CreatePaymentOrderRequest, type FullNetProblemDetails, type PaymentOrder, type PaymentOrderCreationLookupResponse } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import PaymentOrderCreationLookupDialog from './PaymentOrderCreationLookupDialog.vue';
import { createPaymentOrder } from '../api/payment-orders';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import { parsePaymentAmount } from '../utils/payment-amount';
import { isNonnegativeInt64String } from '../utils/exact-integer';

const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; created: [order: PaymentOrder, readAtUtc?: string] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const allowed = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can('payments.orders.create'));
const visible = ref(false), busy = ref(false), lookupOpen = ref(false); const key = ref('');
const form = reactive({ tenantId: '', merchantConfigId: '', channelKey: 'wechat_native', amountYuan: '', subject: '', description: '' });
type Attempt = { readonly body: Readonly<CreatePaymentOrderRequest>; readonly submittedAt: string; readonly state: 'pending' | 'unconfirmed' | 'accepted' };
const attempt = ref<Attempt>(); const receipt = ref<PaymentOrder>(); const failure = ref<FullNetProblemDetails>();
const frozen = computed(() => !!attempt.value);
const validId = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
const sameId = (value: unknown, expected: string) => validId(value) && value.toLowerCase() === expected.toLowerCase();
function close() { visible.value = false; lookupOpen.value = false; emit('update:open', false); }
function clear() { key.value = ''; attempt.value = undefined; receipt.value = undefined; failure.value = undefined; busy.value = false;
  Object.assign(form, { tenantId: '', merchantConfigId: '', channelKey: 'wechat_native', amountYuan: '', subject: '', description: '' }); }
const scope = useAuthorizedViewScope(session, () => { close(); clear(); }, () => {});
function startAnother() {
  if (busy.value || !allowed.value || (attempt.value && attempt.value.state !== 'accepted')) return;
  clear(); key.value = crypto.randomUUID();
}
watch(() => props.open, open => {
  visible.value = open && allowed.value;
  if (visible.value && !key.value) startAnother();
}, { immediate: true });

function matches(value: PaymentOrder, body: Readonly<CreatePaymentOrderRequest>) {
  return validId(value.id) && sameId(value.tenantId, body.tenantId) && validId(value.merchantConfigId)
    && (!body.merchantConfigId || sameId(value.merchantConfigId, body.merchantConfigId))
    && ['wechat_native', 'alipay_page'].includes(value.channelKey) && (body.merchantConfigId || value.channelKey === body.channelKey)
    && value.amountMinor === body.amountMinor && isNonnegativeInt64String(value.amountMinor) && value.amountMinor !== '0'
    && value.currency === body.currency && value.subject === body.subject && value.description === (body.description ?? null)
    && typeof value.outTradeNo === 'string' && !!value.outTradeNo.trim() && Number.isSafeInteger(value.version) && value.version >= 1
    && ['created', 'awaiting_payment', 'succeeded', 'closed', 'failed', 'provider_unknown', 'refunding', 'refunded'].includes(value.tradeStateKey)
    && (value.codeUrl === null || typeof value.codeUrl === 'string');
}
function mismatch(): FullNetProblemDetails { return { type: 'about:blank', status: 502, code: 'client.creation_response_mismatch', title: t('paymentOrders.creationResponseMismatch') }; }
function confirm(value: PaymentOrder, readAtUtc?: string) {
  const original = attempt.value;
  if (!allowed.value || !original || !matches(value, original.body)) { failure.value = mismatch(); return; }
  attempt.value = Object.freeze({ ...original, state: 'accepted' }); receipt.value = value;
  failure.value = undefined; ElMessage.success(t('paymentOrders.createSuccess')); emit('created', value, readAtUtc); close();
}
function found(value: PaymentOrderCreationLookupResponse) {
  if (!attempt.value || !sameId(value.idempotencyKey, attempt.value.body.idempotencyKey)
    || !Number.isFinite(Date.parse(value.readAtUtc))) { failure.value = mismatch(); lookupOpen.value = false; return; }
  lookupOpen.value = false; confirm(value.order, value.readAtUtc);
}
async function send() {
  if (!visible.value || busy.value || !allowed.value || attempt.value?.state === 'accepted') return;
  const recovering = !!attempt.value;
  let body = attempt.value?.body;
  if (!body) {
    const amountMinor = parsePaymentAmount(form.amountYuan);
    if (amountMinor === undefined) { ElMessage.warning(t('paymentOrders.amountValidationFailed')); return; }
    if (!validId(form.tenantId.trim()) || !validId(key.value) || (form.merchantConfigId.trim() && !validId(form.merchantConfigId.trim()))
      || !form.subject.trim() || (!form.merchantConfigId.trim() && !['wechat_native', 'alipay_page'].includes(form.channelKey))) {
      ElMessage.warning(t('paymentOrders.createValidationFailed')); return;
    }
    body = Object.freeze({ idempotencyKey: key.value, tenantId: form.tenantId.trim(), merchantConfigId: form.merchantConfigId.trim() || null,
      channelKey: form.merchantConfigId.trim() ? null : form.channelKey, amountMinor, currency: 'CNY', subject: form.subject.trim(), description: form.description.trim() || null });
    Object.assign(form, { tenantId: body.tenantId, merchantConfigId: body.merchantConfigId ?? '', amountYuan: form.amountYuan.trim(), subject: body.subject, description: body.description ?? '' });
  }
  const operation = scope.begin('payments.orders.create'); if (!operation) return;
  attempt.value = Object.freeze({ body, submittedAt: attempt.value?.submittedAt ?? new Date().toISOString(), state: 'pending' });
  busy.value = true; failure.value = undefined;
  try {
    const value = await createPaymentOrder(body, operation.signal);
    if (!operation.current()) return;
    if (!matches(value, body)) throw mismatch();
    confirm(value);
  } catch (error) {
    if (!operation.current()) return;
    const problem = isFullNetProblemDetails(error) ? error : { type: 'about:blank', status: 500, code: 'client.request_failed', title: t('paymentOrders.creationUnconfirmed') };
    // 只有首个请求明确在意图写入前被拒绝，才允许修正；未知后的恢复不能据此丢弃原身份。
    if (!recovering && ['payments.order.invalid', 'payments.tenant.not_found', 'payments.merchant_config.unavailable'].includes(problem.code)) attempt.value = undefined;
    else attempt.value = Object.freeze({ ...attempt.value!, body, state: 'unconfirmed' });
    failure.value = problem;
  } finally { if (operation.current()) busy.value = false; operation.finish(); }
}
</script>

<template>
  <!-- 核对时卸载创建弹层，避免旧关闭动画的回调误关新打开的核对或恢复窗口。 -->
  <ArtFormDialog v-if="!lookupOpen" :open="visible && allowed" :title="t('paymentOrders.createTitle')" :saving="busy" :confirm-label="t(attempt ? 'paymentOrders.creationRestore' : 'common.confirm')"
    :confirm-disabled="busy || !allowed || attempt?.state === 'accepted'" :cancel-label="t('paymentRefunds.close')" confirm-test-id="payment-order-create-confirm" trap-focus
    width="min(560px, calc(100vw - 32px))" @update:open="close" @confirm="send">
    <p>{{ t('paymentOrders.creationSafetyHint') }}</p>
    <ElAlert v-if="failure" :closable="false" type="warning" :title="failure.title" data-testid="payment-creation-problem" />
    <p v-if="attempt" data-testid="payment-creation-original-key">{{ t('paymentOrders.creationRequestKey') }} <code>{{ attempt.body.idempotencyKey }}</code></p>
    <p v-if="attempt?.state === 'unconfirmed'" role="status" data-testid="payment-creation-unconfirmed">{{ t('paymentOrders.creationUnconfirmed') }}</p>
    <p v-if="receipt" data-testid="payment-creation-confirmed-order">{{ t('paymentOrders.orderId') }} <code>{{ receipt.id }}</code> {{ receipt.tradeStateKey }}</p>
    <ElForm label-position="top">
      <ElFormItem :label="t('paymentOrders.fieldTenantId')" required><ElInput v-model="form.tenantId" :readonly="frozen" data-testid="payment-order-create-tenant" /></ElFormItem>
      <ElFormItem :label="t('paymentOrders.fieldChannelKey')"><ElRadioGroup v-model="form.channelKey" :disabled="frozen || Boolean(form.merchantConfigId.trim())" :aria-label="t('paymentOrders.fieldChannelKey')" class="creation-channels"><ElRadio value="wechat_native">{{ t('paymentOrders.channelWeChatNative') }}</ElRadio><ElRadio value="alipay_page">{{ t('paymentOrders.channelAlipayPage') }}</ElRadio></ElRadioGroup><p v-if="form.merchantConfigId.trim()">{{ t('paymentOrders.creationMerchantHint') }}</p></ElFormItem>
      <ElFormItem :label="t('paymentOrders.fieldMerchantConfigId')"><ElInput v-model="form.merchantConfigId" :readonly="frozen" data-testid="payment-order-create-merchant" /></ElFormItem>
      <ElFormItem :label="t('paymentOrders.fieldAmountYuan')" required><ElInput v-model="form.amountYuan" :readonly="frozen" inputmode="decimal" data-testid="payment-order-create-amount" /></ElFormItem>
      <ElFormItem :label="t('paymentOrders.fieldSubject')" required><ElInput v-model="form.subject" :readonly="frozen" data-testid="payment-order-create-subject" /></ElFormItem>
      <ElFormItem :label="t('paymentOrders.fieldDescription')"><ElInput v-model="form.description" :readonly="frozen" type="textarea" :rows="2" data-testid="payment-order-create-description" /></ElFormItem>
    </ElForm>
    <ElButton v-if="attempt && session.can('payments.orders.read')" :disabled="busy" data-testid="payment-creation-lookup" @click="lookupOpen = true">{{ t('paymentOrders.creationLookupRead') }}</ElButton>
    <ElButton v-if="attempt?.state === 'accepted'" data-testid="payment-order-create-another" @click="startAnother">{{ t('paymentOrders.creationAnother') }}</ElButton>
  </ArtFormDialog>
  <PaymentOrderCreationLookupDialog v-if="lookupOpen" v-model:open="lookupOpen" :tenant-id="attempt?.body.tenantId" :idempotency-key="attempt?.body.idempotencyKey" fixed-identity @found="found" />
</template>

<style scoped>
.creation-channels { display: flex; flex-wrap: wrap; gap: 4px 16px; }
.creation-channels .el-radio { margin-right: 0; }
code { overflow-wrap: anywhere; }
</style>
