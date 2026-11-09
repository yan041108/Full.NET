<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { PaymentOrder, PaymentOrderListItem, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import SubscriptionFulfillmentAction from '../components/SubscriptionFulfillmentAction.vue';
import PaymentOrderDetailDialog from '../components/PaymentOrderDetailDialog.vue';
import PaymentOrderCreateDialog from '../components/PaymentOrderCreateDialog.vue';
import PaymentOrderCreationLookupDialog from '../components/PaymentOrderCreationLookupDialog.vue';
import PaymentOrderReconcileDialog from '../components/PaymentOrderReconcileDialog.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { listPaymentOrders, createPaymentRefund } from '../api/payment-orders';
import { formatPaymentAmount, parsePaymentAmount } from '../utils/payment-amount';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { isNonnegativeInt64String } from '../utils/exact-integer';

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
const reconcileOpen = ref(false); const reconcileTarget = ref<Readonly<PaymentOrderListItem>>();
const refundOpen = ref(false);
const refundOrderId = ref('');
const refundForm = reactive({
  amountYuan: '',
  reason: ''
});
type RefundAttempt = { readonly order: Readonly<PaymentOrderListItem>; readonly body: Readonly<{ amountMinor: string | null; reason: string }>;
  readonly amountYuan: string; readonly submittedAtUtc: string; readonly state: 'pending' | 'unconfirmed' | 'accepted' };
const refundAttempts = reactive(new Map<string, RefundAttempt>());
const refundAttempt = computed(() => refundAttempts.get(refundOrderId.value));
const refundSaving = ref(false);
const orderIdInput = ref(''); const orderDetailId = ref(''); const orderDetailOpen = ref(false);
const validPaymentId = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
const canRefund = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can('payments.refunds.create'));
const canRead = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can('payments.orders.read'));
let listSequence = 0;
let listPending: { cancel(): void } | undefined;
const orderStates = ['created', 'awaiting_payment', 'succeeded', 'closed', 'failed', 'provider_unknown', 'refunding', 'refunded'] as const;
const filters = reactive({ tenantId: '', state: 'all', outTradeNo: '' });
let appliedFilters = { ...filters }; let invalidFilters = false;
const refundScope = useAuthorizedViewScope(session, () => {
  clearList(); page.value = 1;
  createOpen.value = false; creationLookupOpen.value = false; lastCodeUrl.value = ''; lastCreatedOrder.value = undefined; lastCreationReadAt.value = '';
  reconcileOpen.value = false; reconcileTarget.value = undefined; changing.value = false;
  Object.assign(filters, { tenantId: '', state: 'all', outTradeNo: '' }); appliedFilters = { ...filters }; invalidFilters = false;
  orderIdInput.value = ''; orderDetailId.value = ''; orderDetailOpen.value = false;
  refundOpen.value = false; refundOrderId.value = ''; refundSaving.value = false; refundAttempts.clear();
  Object.assign(refundForm, { amountYuan: '', reason: '' });
}, () => { void load(); });
const lastCodeUrl = ref('');
const lastCreatedOrder = ref<PaymentOrder>(); const lastCreationReadAt = ref(''); const creationLookupOpen = ref(false);
const narrowViewport = ref(false);
let viewportQuery: MediaQueryList | undefined;
function updateViewport() { narrowViewport.value = viewportQuery?.matches ?? false; }
onMounted(() => {
  if (typeof window.matchMedia !== 'function') return;
  viewportQuery = window.matchMedia('(max-width: 640px)'); updateViewport();
  viewportQuery.addEventListener?.('change', updateViewport);
});
onBeforeUnmount(() => viewportQuery?.removeEventListener?.('change', updateViewport));

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

function formatAmount(minor: unknown): string {
  return formatPaymentAmount(minor) ?? t('paymentOrders.amountUnavailable');
}

async function load(): Promise<void> {
  if (!canRead.value || invalidFilters) return;
  // 加载时保留分页总数，避免组件因临时归零把第二页夹回首页；旧行仍立即清空。
  clearList(false);
  const operation = refundScope.begin('payments.orders.read'); if (!operation) return;
  const sequence = listSequence; listPending = operation;
  const query = { page: page.value, pageSize: pageSize.value, tenantId: appliedFilters.tenantId || undefined,
    tradeStateKey: appliedFilters.state === 'all' ? undefined : appliedFilters.state, outTradeNoContains: appliedFilters.outTradeNo || undefined };
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listPaymentOrders(query, operation.signal);
    if (!operation.current() || sequence !== listSequence) return;
    if (result.page !== query.page || result.pageSize !== query.pageSize || !Number.isSafeInteger(result.total) || result.total < 0
      || result.items.length > query.pageSize || result.items.some(row => !validPaymentId(row.id) || !validPaymentId(row.tenantId)
        || (query.tenantId && row.tenantId.toLowerCase() !== query.tenantId.toLowerCase())
        || (query.tradeStateKey && row.tradeStateKey !== query.tradeStateKey)))
      throw { type: 'about:blank', status: 502, code: 'client.payment_order_page_mismatch', title: t('paymentOrders.filterMismatch') };
    items.value = result.items;
    total.value = result.total;
    await updateTableHeight();
  } catch (error: unknown) {
    if (!operation.current() || sequence !== listSequence) return;
    total.value = 0; page.value = 1;
    problem.value = toProblem(error, 'paymentOrders.loadFailed');
  } finally {
    if (operation.current() && sequence === listSequence) { loading.value = false; listPending = undefined; }
    operation.finish();
  }
}
function clearList(resetTotal = true) { listSequence++; listPending?.cancel(); listPending = undefined; items.value = []; if (resetTotal) total.value = 0; loading.value = false; problem.value = undefined; }
function searchOrders() {
  if (!canRead.value) return;
  clearList(); page.value = 1;
  const next = { tenantId: filters.tenantId.trim(), state: filters.state, outTradeNo: filters.outTradeNo.trim() };
  invalidFilters = (!!next.tenantId && !validPaymentId(next.tenantId))
    || (next.state !== 'all' && !orderStates.some(state => state === next.state)) || next.outTradeNo.length > 64;
  if (invalidFilters) { problem.value = { type: 'about:blank', status: 400, code: 'client.invalid_payment_order_filter', title: t('paymentOrders.invalidFilter') }; return; }
  appliedFilters = next; void load();
}
function resetOrderFilters() { Object.assign(filters, { tenantId: '', state: 'all', outTradeNo: '' }); searchOrders(); }
function changePage(value: number) { if (value !== page.value) { page.value = value; void load(); } }
function orderStateLabel(value: string) { const key = `paymentOrders.status.${value}`; const label = translateRuntimeMessage(t, key); return label === key ? value : label; }

function openCreate(): void {
  if (session.state === 'authenticated' && session.currentUser?.tenantId === null && session.currentUser.actorScope === 'host'
    && session.can('payments.orders.create')) createOpen.value = true;
}
function orderCreated(order: PaymentOrder, readAtUtc?: string) {
  lastCreatedOrder.value = order; lastCreationReadAt.value = readAtUtc ?? ''; lastCodeUrl.value = order.codeUrl ?? ''; void load();
}

function reconcileOrder(orderId: string): void {
  if (changing.value || refundSaving.value || !canRead.value || !session.can('payments.orders.reconcile')) return;
  const original = items.value.find(row => row.id === orderId); if (!original) return;
  reconcileTarget.value = Object.freeze({ ...original }); reconcileOpen.value = true;
}

function openRefund(orderId: string): void {
  if (changing.value || refundSaving.value || !canRefund.value || items.value.find(row => row.id === orderId)?.channelKey !== 'wechat_native') return;
  refundOrderId.value = orderId;
  const previous = refundAttempts.get(orderId);
  Object.assign(refundForm, { amountYuan: previous?.amountYuan ?? '', reason: previous?.body.reason ?? '' });
  refundOpen.value = true;
}
function readOrder(input = orderIdInput.value) {
  if (!canRead.value) return;
  const id = input.trim();
  if (!validPaymentId(id)) { problem.value = { type: 'about:blank', status: 400, code: 'client.invalid_payment_order_id', title: t('paymentOrders.invalidId') }; return; }
  if (problem.value?.code === 'client.invalid_payment_order_id') problem.value = undefined;
  orderIdInput.value = id; orderDetailId.value = id; orderDetailOpen.value = true;
}
function readRefundRecords(orderId: string): void {
  if (changing.value || !session.can('payments.refunds.read') || !isGuid(orderId) || orderId === '00000000-0000-0000-0000-000000000000') return;
  refundOpen.value = false;
  void router?.push({ name: 'payments-refunds', query: { orderId } });
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
  if (amountMinor === undefined || !isNonnegativeInt64String(expectedAmount) || expectedAmount === '0'
    || !isNonnegativeInt64String(order.amountMinor) || BigInt(expectedAmount) > BigInt(order.amountMinor)) {
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
      v-if="lastCodeUrl && lastCreatedOrder?.tradeStateKey === 'awaiting_payment'"
      type="success"
      :title="t('paymentOrders.payUrlReady')"
      :description="lastCodeUrl"
      show-icon
      class="art-page-alert"
    />

    <div v-if="lastCreatedOrder" role="status" class="payment-creation-receipt" data-testid="payment-order-creation-receipt">
      <strong>{{ t('paymentOrders.creationConfirmed') }}</strong>
      <p>{{ t('paymentOrders.orderId') }} <code>{{ lastCreatedOrder.id }}</code> {{ orderStateLabel(lastCreatedOrder.tradeStateKey) }}</p>
      <p v-if="lastCreationReadAt">{{ t('paymentOrders.creationServerReadAt') }} {{ lastCreationReadAt }}</p>
      <ElButton v-if="canRead" data-testid="payment-created-order-detail" @click="readOrder(lastCreatedOrder.id)">{{ t('paymentOrders.detail') }}</ElButton>
    </div>
    <ElForm v-if="canRead" label-position="top" class="payment-order-filters" @submit.prevent="searchOrders">
      <ElFormItem :label="t('paymentOrders.fieldTenantId')"><ElInput v-model="filters.tenantId" clearable data-testid="payment-orders-filter-tenant" /></ElFormItem>
      <ElFormItem :label="t('paymentOrders.fieldStatus')"><ElSelect v-model="filters.state" data-testid="payment-orders-filter-state"><ElOption value="all" :label="t('paymentOrders.allStates')" /><ElOption v-for="state in orderStates" :key="state" :value="state" :label="orderStateLabel(state)" /></ElSelect></ElFormItem>
      <ElFormItem :label="t('paymentOrders.filterOutTradeNo')"><ElInput v-model="filters.outTradeNo" clearable maxlength="64" data-testid="payment-orders-filter-trade" /></ElFormItem>
      <div class="payment-order-filter-actions"><ElButton native-type="submit" type="primary" data-testid="payment-orders-search">{{ t('paymentOrders.search') }}</ElButton><ElButton data-testid="payment-orders-reset" @click="resetOrderFilters">{{ t('paymentOrders.reset') }}</ElButton></div>
    </ElForm>
    <p v-if="canRead" class="payment-order-filter-hint">{{ t('paymentOrders.filtersHint') }}</p>
    <el-card class="art-full-height-card" shadow="never">
      <ArtTableHeader @refresh="load">
        <template #left>
          <ElForm v-if="canRead" class="payment-order-exact-picker" label-position="top" @submit.prevent="readOrder()">
            <ElFormItem :label="t('paymentOrders.orderId')"><ElInput v-model="orderIdInput" clearable data-testid="payment-order-id-input" /></ElFormItem>
            <ElButton native-type="submit" data-testid="payment-order-id-read">{{ t('paymentOrders.readById') }}</ElButton>
          </ElForm>
          <ElButton v-if="canRead" data-testid="payment-orders-creation-lookup" @click="creationLookupOpen = true">{{ t('paymentOrders.creationLookupTitle') }}</ElButton>
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
              <el-tag>{{ orderStateLabel(row.tradeStateKey) }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="createdAtUtc" :label="t('paymentOrders.fieldCreatedAt')" min-width="180" />
          <el-table-column prop="failMessage" :label="t('paymentOrders.fieldFailMessage')" min-width="160" />
          <el-table-column :label="t('paymentOrders.actions')" width="270" :fixed="narrowViewport ? undefined : 'right'">
            <template #default="{ row }">
              <ElButton v-if="canRead" link type="primary" :data-testid="`payment-order-detail-${row.id}`" @click="readOrder(row.id)">{{ t('paymentOrders.detail') }}</ElButton>
              <PermissionGate code="payments.orders.reconcile">
                <el-button
                  link
                  type="primary"
                  :data-testid="`payment-order-reconcile-${row.id}`"
                  :disabled="changing || refundSaving || !canRead"
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
        :current-page="page"
        v-model:page-size="pageSize"
        layout="total, prev, pager, next"
        :total="total"
        @current-change="changePage"
      />
    </el-card>

    <PaymentOrderCreateDialog v-model:open="createOpen" @created="orderCreated" />
  <PaymentOrderReconcileDialog v-model:open="reconcileOpen" :reference="reconcileTarget" @busy="changing = $event" @updated="load" />
    <PaymentOrderCreationLookupDialog v-model:open="creationLookupOpen" @open-order="readOrder" />

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
          <el-input v-model="refundForm.amountYuan" :readonly="!!refundAttempt" inputmode="decimal" data-testid="payment-order-refund-amount" />
        </el-form-item>
      </el-form>
      <PermissionGate code="payments.refunds.read"><ElButton :disabled="changing || refundSaving" data-testid="payment-refund-check-records" @click="readRefundRecords(refundOrderId)">{{ t('paymentOrders.readRefundRecords') }}</ElButton></PermissionGate>
    </ArtFormDialog>
    <PaymentOrderDetailDialog v-model:open="orderDetailOpen" :order-id="orderDetailId" />
  </section>
</template>
<style scoped>
.refund-attempt-summary { display: grid; gap: 4px; margin: 12px 0; }
.refund-attempt-summary dt { font-weight: 600; }
.refund-attempt-summary dd { margin: 0 0 8px; overflow-wrap: anywhere; }
.payment-order-exact-picker { display: flex; flex-wrap: wrap; align-items: end; gap: 8px; max-width: 100%; }
.payment-order-exact-picker .el-form-item { flex: 1 1 200px; min-width: 0; max-width: 100%; margin: 0; }
.payment-order-filters { display: grid; grid-template-columns: minmax(180px, 1fr) minmax(150px, 180px) minmax(180px, 1fr) auto; gap: 12px; align-items: end; }
.payment-order-filter-actions { display: flex; gap: 8px; margin-bottom: 18px; }
.payment-order-filter-hint { margin: 0; }
@media (max-width: 800px) { .payment-order-filters { grid-template-columns: 1fr; gap: 0; } }
.payment-creation-receipt { overflow-wrap: anywhere; }
</style>
