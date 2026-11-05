<script setup lang="ts">
import { computed, reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElPagination,
  ElRadio,
  ElRadioGroup,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { PaymentOrderListItem, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import SubscriptionFulfillmentAction from '../components/SubscriptionFulfillmentAction.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { createPaymentOrder, listPaymentOrders, reconcilePaymentOrder, createPaymentRefund } from '../api/payment-orders';
import { formatPaymentAmount, parsePaymentAmount } from '../utils/payment-amount';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';

defineOptions({ name: 'PaymentOrdersView' });

const { t } = useAdminI18n();
const router = useRouter(); const session = useSessionStore();
const items = ref<PaymentOrderListItem[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const createOpen = ref(false);
// 同一创建窗口的网络重试必须复用请求标识，避免重复下单。
const createIdempotencyKey = ref('');
const refundOpen = ref(false);
const refundOrderId = ref('');
const refundForm = reactive({
  amountYuan: '',
  reason: ''
});
type RefundAttempt = { readonly order: Readonly<PaymentOrderListItem>; readonly body: Readonly<{ amountMinor: number | null; reason: string }>;
  readonly amountYuan: string; readonly submittedAtUtc: string; readonly state: 'pending' | 'unconfirmed' | 'accepted' };
const refundAttempts = reactive(new Map<string, RefundAttempt>());
const refundAttempt = computed(() => refundAttempts.get(refundOrderId.value));
const refundSaving = ref(false);
const validPaymentId = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
const canRefund = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can('payments.refunds.create'));
const canRead = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can('payments.orders.read'));
let listSequence = 0;
const refundScope = useAuthorizedViewScope(session, () => {
  listSequence++; items.value = []; total.value = 0; page.value = 1; loading.value = false; problem.value = undefined;
  refundOpen.value = false; refundOrderId.value = ''; refundSaving.value = false; refundAttempts.clear();
  Object.assign(refundForm, { amountYuan: '', reason: '' });
}, () => { void load(); });
const createForm = reactive({
  tenantId: '',
  channelKey: 'wechat_native',
  merchantConfigId: '',
  amountYuan: '',
  subject: '',
  description: ''
});
const lastCodeUrl = ref('');

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderCellStyle,
  updateTableHeight,
  watchLoading
} = useArtCrudTableLayout();

watchLoading(loading);

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

function formatAmount(minor: number): string {
  return formatPaymentAmount(minor) ?? t('paymentOrders.amountUnavailable');
}

async function load(): Promise<void> {
  if (!canRead.value) return;
  const operation = refundScope.begin('payments.orders.read'); if (!operation) return;
  const sequence = ++listSequence;
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listPaymentOrders({ page: page.value, pageSize: pageSize.value }, operation.signal);
    if (!operation.current() || sequence !== listSequence) return;
    items.value = result.items;
    total.value = result.total;
    await updateTableHeight();
  } catch (error: unknown) {
    if (!operation.current() || sequence !== listSequence) return;
    problem.value = toProblem(error, 'paymentOrders.loadFailed');
  } finally {
    if (operation.current() && sequence === listSequence) loading.value = false;
    operation.finish();
  }
}

function openCreate(): void {
  createIdempotencyKey.value = crypto.randomUUID();
  Object.assign(createForm, {
    tenantId: '',
    channelKey: 'wechat_native',
    merchantConfigId: '',
    amountYuan: '',
    subject: '',
    description: ''
  });
  lastCodeUrl.value = '';
  createOpen.value = true;
}

async function submitCreate(): Promise<void> {
  const amountMinor = parsePaymentAmount(createForm.amountYuan);
  if (!createForm.tenantId.trim() || amountMinor === undefined || !createForm.subject.trim()) {
    ElMessage.warning(t(amountMinor === undefined ? 'paymentOrders.amountValidationFailed' : 'paymentOrders.createValidationFailed'));
    return;
  }
  changing.value = true;
  try {
    const order = await createPaymentOrder({
      idempotencyKey: createIdempotencyKey.value,
      tenantId: createForm.tenantId.trim(),
      channelKey: createForm.merchantConfigId.trim() ? null : createForm.channelKey,
      merchantConfigId: createForm.merchantConfigId.trim() || null,
      amountMinor,
      currency: 'CNY',
      subject: createForm.subject.trim(),
      description: createForm.description.trim() || null
    });
    lastCodeUrl.value = order.codeUrl ?? '';
    ElMessage.success(t('paymentOrders.createSuccess'));
    createOpen.value = false;
    await load();
  } catch (error: unknown) {
    ElMessage.error(toProblem(error, 'paymentOrders.createFailed').title);
  } finally {
    changing.value = false;
  }
}

async function reconcileOrder(orderId: string): Promise<void> {
  changing.value = true;
  try {
    await reconcilePaymentOrder(orderId);
    ElMessage.success(t('paymentOrders.reconcileSuccess'));
    await load();
  } catch (error: unknown) {
    ElMessage.error(toProblem(error, 'paymentOrders.reconcileFailed').title);
  } finally {
    changing.value = false;
  }
}

function openRefund(orderId: string): void {
  if (changing.value || refundSaving.value || !canRefund.value || items.value.find(row => row.id === orderId)?.channelKey !== 'wechat_native') return;
  refundOrderId.value = orderId;
  const previous = refundAttempts.get(orderId);
  Object.assign(refundForm, { amountYuan: previous?.amountYuan ?? '', reason: previous?.body.reason ?? '' });
  refundOpen.value = true;
}
function readRefundRecords(orderId: string): void {
  if (changing.value || !session.can('payments.refunds.read') || !isGuid(orderId) || orderId === '00000000-0000-0000-0000-000000000000') return;
  refundOpen.value = false;
  void router?.push({ name: 'payment-refunds', query: { orderId } });
}

async function submitRefund(): Promise<void> {
  if (changing.value || refundSaving.value || refundAttempt.value || !canRefund.value) return;
  const order = items.value.find(row => row.id === refundOrderId.value);
  if (!order || order.channelKey !== 'wechat_native' || !validPaymentId(order.id) || !validPaymentId(order.tenantId)
    || !validPaymentId(order.merchantConfigId) || !order.outTradeNo || !order.currency) return;
  const reason = refundForm.reason.trim();
  if (!reason || reason.length > 128) {
    ElMessage.warning(t('paymentOrders.refundValidationFailed'));
    return;
  }
  const amountYuan = refundForm.amountYuan.trim();
  const amountMinor = amountYuan ? parsePaymentAmount(amountYuan) : null;
  const expectedAmount = amountMinor ?? order.amountMinor;
  if (amountMinor === undefined || !Number.isSafeInteger(expectedAmount) || expectedAmount <= 0
    || (amountMinor !== null && Number.isSafeInteger(order.amountMinor) && amountMinor > order.amountMinor)) {
    ElMessage.warning(t('paymentOrders.amountValidationFailed'));
    return;
  }
  const operation = refundScope.begin('payments.refunds.create'); if (!operation) return;
  // 一旦发出资金请求就冻结原正文；网络错误不能作为改参或再次提交的依据。
  const attempt: RefundAttempt = Object.freeze({ order: Object.freeze({ ...order }), body: Object.freeze({ amountMinor, reason }),
    amountYuan, submittedAtUtc: new Date().toISOString(), state: 'pending' });
  Object.assign(refundForm, { amountYuan, reason });
  refundAttempts.set(order.id, attempt); refundSaving.value = true;
  try {
    const receipt = await createPaymentRefund(order.id, attempt.body, operation.signal);
    if (!operation.current()) return;
    const sameId = (a: unknown, b: string) => validPaymentId(a) && a.toLowerCase() === b.toLowerCase();
    if (!validPaymentId(receipt.id)
      || !sameId(receipt.orderId, order.id) || !sameId(receipt.tenantId, order.tenantId)
      || !sameId(receipt.merchantConfigId, order.merchantConfigId) || receipt.outTradeNo !== order.outTradeNo
      || receipt.amountMinor !== expectedAmount || receipt.currency !== order.currency || receipt.reason !== reason
      || !receipt.outRefundNo?.trim() || !['created', 'provider_unknown', 'processing', 'succeeded', 'failed', 'closed'].includes(receipt.refundStateKey))
      throw new Error('client.payment_refund_mismatch');
    refundAttempts.set(order.id, Object.freeze({ ...attempt, state: 'accepted' }));
    ElMessage.success(t('paymentOrders.refundSuccess'));
    if (refundOrderId.value === order.id) refundOpen.value = false;
    await load();
  } catch {
    if (!operation.current()) return;
    refundAttempts.set(order.id, Object.freeze({ ...attempt, state: 'unconfirmed' }));
    ElMessage.warning(t('paymentOrders.refundAttemptUnconfirmed'));
  } finally {
    if (operation.current()) refundSaving.value = false;
    operation.finish();
  }
}

</script>

<template>
  <section class="payment-orders-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('paymentOrders.title') }}</h1>
    <el-alert type="info" :closable="false" :title="t('paymentOrders.refundChannelHint')" show-icon class="art-page-alert" data-testid="payment-refund-channel-hint" />
    <el-alert v-if="problem" type="error" :title="problem.title" show-icon class="art-page-alert" />
    <el-alert
      v-if="lastCodeUrl"
      type="success"
      :title="t('paymentOrders.payUrlReady')"
      :description="lastCodeUrl"
      show-icon
      class="art-page-alert"
    />

    <el-card class="art-full-height-card" shadow="never">
      <ArtTableHeader @refresh="load">
        <template #left>
          <PermissionGate code="payments.orders.create">
            <el-button type="primary" :icon="Plus" data-testid="payment-order-create" @click="openCreate">
              {{ t('paymentOrders.createOrder') }}
            </el-button>
          </PermissionGate>
        </template>
      </ArtTableHeader>

      <div ref="tableMainRef" class="art-table-main">
        <el-table
          :data="items"
          :height="tableHeight"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
        >
          <el-table-column prop="channelKey" :label="t('paymentOrders.fieldChannelKey')" width="140" />
          <el-table-column prop="outTradeNo" :label="t('paymentOrders.fieldOutTradeNo')" min-width="180" />
          <el-table-column :label="t('paymentOrders.fieldAmount')" width="120">
            <template #default="{ row }">{{ formatAmount(row.amountMinor) }} {{ row.currency }}</template>
          </el-table-column>
          <el-table-column prop="subject" :label="t('paymentOrders.fieldSubject')" min-width="140" />
          <el-table-column :label="t('paymentOrders.fieldStatus')" width="140">
            <template #default="{ row }">
              <el-tag>{{ row.tradeStateKey }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="createdAtUtc" :label="t('paymentOrders.fieldCreatedAt')" min-width="180" />
          <el-table-column prop="failMessage" :label="t('paymentOrders.fieldFailMessage')" min-width="160" />
          <el-table-column :label="t('paymentOrders.actions')" width="220" fixed="right">
            <template #default="{ row }">
              <PermissionGate code="payments.orders.reconcile">
                <el-button
                  link
                  type="primary"
                  :data-testid="`payment-order-reconcile-${row.id}`"
                  @click="reconcileOrder(row.id)"
                >
                  {{ t('paymentOrders.reconcile') }}
                </el-button>
              </PermissionGate>
              <PermissionGate code="payments.refunds.create">
                <el-button
                  link
                  type="warning"
                  :data-testid="`payment-order-refund-${row.id}`"
                  :disabled="changing || refundSaving || row.channelKey !== 'wechat_native'"
                  @click="openRefund(row.id)"
                >
                  {{ t('paymentOrders.refund') }}
                </el-button>
              </PermissionGate>
              <SubscriptionFulfillmentAction :order-id="row.id" />
              <PermissionGate code="payments.refunds.read"><ElButton link type="primary" :disabled="changing" :data-testid="`payment-order-refund-records-${row.id}`" @click="readRefundRecords(row.id)">{{ t('paymentOrders.readRefundRecords') }}</ElButton></PermissionGate>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <el-pagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        layout="total, prev, pager, next"
        :total="total"
        @current-change="load"
      />
    </el-card>

    <ArtFormDialog
      v-model:open="createOpen"
      :title="t('paymentOrders.createTitle')"
      :saving="changing"
      confirm-test-id="payment-order-create-confirm"
      @confirm="submitCreate"
    >
      <el-form label-width="120px">
        <el-form-item :label="t('paymentOrders.fieldTenantId')" required>
          <el-input v-model="createForm.tenantId" data-testid="payment-order-create-tenant" />
        </el-form-item>
        <el-form-item :label="t('paymentOrders.fieldChannelKey')">
          <ElRadioGroup v-model="createForm.channelKey" class="payment-channel-options" :aria-label="t('paymentOrders.fieldChannelKey')" :disabled="Boolean(createForm.merchantConfigId.trim())">
            <ElRadio value="wechat_native">{{ t('paymentOrders.channelWeChatNative') }}</ElRadio>
            <ElRadio value="alipay_page">{{ t('paymentOrders.channelAlipayPage') }}</ElRadio>
          </ElRadioGroup>
        </el-form-item>
        <el-form-item :label="t('paymentOrders.fieldMerchantConfigId')">
          <el-input v-model="createForm.merchantConfigId" data-testid="payment-order-create-merchant" />
        </el-form-item>
        <el-form-item :label="t('paymentOrders.fieldAmountYuan')" required>
          <el-input v-model="createForm.amountYuan" inputmode="decimal" maxlength="18" data-testid="payment-order-create-amount" />
        </el-form-item>
        <el-form-item :label="t('paymentOrders.fieldSubject')" required>
          <el-input v-model="createForm.subject" data-testid="payment-order-create-subject" />
        </el-form-item>
        <el-form-item :label="t('paymentOrders.fieldDescription')">
          <el-input v-model="createForm.description" type="textarea" :rows="3" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="refundOpen"
      :title="t('paymentOrders.refundTitle')"
      :saving="refundSaving"
      :confirm-disabled="changing || refundSaving || !!refundAttempt || !canRefund"
      :confirm-label="t('common.confirm')"
      :cancel-label="t('paymentRefunds.close')"
      confirm-test-id="payment-order-refund-confirm"
      @confirm="submitRefund"
    >
      <ElAlert v-if="refundAttempt" type="warning" :closable="false" :title="t('paymentOrders.refundAttemptLocked')"
        :description="t(refundAttempt.state === 'unconfirmed' ? 'paymentOrders.refundAttemptUnconfirmed' : 'paymentOrders.refundAttemptRecorded')"
        show-icon data-testid="payment-refund-attempt-status" />
      <dl v-if="refundAttempt" class="refund-attempt-summary" data-testid="payment-refund-original-attempt">
        <dt>{{ t('paymentRefunds.orderId') }}</dt><dd>{{ refundAttempt.order.id }}</dd>
        <dt>{{ t('paymentOrders.fieldOutTradeNo') }}</dt><dd>{{ refundAttempt.order.outTradeNo }}</dd>
        <dt>{{ t('paymentOrders.refundAttemptTime') }}</dt><dd>{{ refundAttempt.submittedAtUtc }}</dd>
      </dl>
      <el-form label-position="top">
        <el-form-item :label="t('paymentOrders.refundReason')" required>
          <el-input v-model="refundForm.reason" type="textarea" :rows="3" maxlength="128" :readonly="!!refundAttempt" data-testid="payment-order-refund-reason" />
        </el-form-item>
        <el-form-item :label="t('paymentOrders.refundAmountYuan')">
          <el-input v-model="refundForm.amountYuan" :readonly="!!refundAttempt" inputmode="decimal" maxlength="18" data-testid="payment-order-refund-amount" />
        </el-form-item>
      </el-form>
      <PermissionGate code="payments.refunds.read"><ElButton :disabled="changing || refundSaving" data-testid="payment-refund-check-records" @click="readRefundRecords(refundOrderId)">{{ t('paymentOrders.readRefundRecords') }}</ElButton></PermissionGate>
    </ArtFormDialog>
  </section>
</template>
<style scoped>
.payment-channel-options { display: flex; flex-wrap: wrap; gap: 4px 16px; }
.payment-channel-options .el-radio { margin-right: 0; }
.refund-attempt-summary { display: grid; gap: 4px; margin: 12px 0; }
.refund-attempt-summary dt { font-weight: 600; }
.refund-attempt-summary dd { margin: 0 0 8px; overflow-wrap: anywhere; }
</style>
