<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElForm,
  ElFormItem,
  ElInput,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { PaymentOrder, PaymentRefund, PaymentRefundListItem, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { getPaymentRefund, listPaymentRefunds } from '../api/payment-refunds';
import { formatPaymentAmount } from '../utils/payment-amount';
import { isNonnegativeInt64String } from '../utils/exact-integer';
import PermissionGate from '../components/PermissionGate.vue';
import PaymentOrderDetailDialog from '../components/PaymentOrderDetailDialog.vue';
import PaymentOrderReconcileDialog from '../components/PaymentOrderReconcileDialog.vue';

defineOptions({ name: 'PaymentRefundsView' });

const { t } = useAdminI18n();
const session = useSessionStore(); const route = useRoute(); const router = useRouter();
const permission = 'payments.refunds.read';
const allowed = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can(permission));
const states = ['created', 'provider_unknown', 'processing', 'succeeded', 'failed', 'closed'] as const;
const orderFilter = ref(''); const stateFilter = ref('all'); const invalidRoute = ref(false);
const refundIdInput = ref(''); const detailId = ref(''); const detailOpen = ref(false);
const detail = ref<PaymentRefund>(); const detailLoading = ref(false); const detailProblem = ref<FullNetProblemDetails>();
const originalOrderOpen = ref(false); const originalOrderId = ref('');
const originalOrderReference = ref<Pick<PaymentRefund, 'tenantId' | 'merchantConfigId' | 'outTradeNo' | 'currency'>>();
const originalRefund = ref<Readonly<PaymentRefund>>();
const reconcileOpen = ref(false);
const reconcileTarget = ref<Readonly<PaymentOrder>>();
const items = ref<PaymentRefundListItem[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
let listSequence = 0, detailSequence = 0;
let appliedFilters = { orderId: '', state: 'all' };
let listPending: { cancel(): void } | undefined, detailPending: { cancel(): void } | undefined;
const validId = (value: unknown): value is string => isGuid(value) && value !== '00000000-0000-0000-0000-000000000000';
const sameId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
const mismatch = (): FullNetProblemDetails => ({ code: 'client.payment_refund_mismatch', type: 'about:blank', status: 502, title: t('paymentRefunds.responseMismatch') });
const scope = useAuthorizedViewScope(session, () => {
  listSequence++; detailSequence++; listPending?.cancel(); detailPending?.cancel(); listPending = undefined; detailPending = undefined;
  items.value = []; total.value = 0; page.value = 1; loading.value = false; problem.value = undefined;
  orderFilter.value = ''; stateFilter.value = 'all'; refundIdInput.value = ''; invalidRoute.value = false;
  appliedFilters = { orderId: '', state: 'all' };
  detail.value = undefined; detailId.value = ''; detailOpen.value = false; detailLoading.value = false; detailProblem.value = undefined;
  originalOrderOpen.value = false; originalOrderId.value = ''; originalOrderReference.value = undefined;
  originalRefund.value = undefined; reconcileOpen.value = false; reconcileTarget.value = undefined;
}, () => { readRoute(); void load(); });

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
  const sequence = ++listSequence; listPending?.cancel(); listPending = undefined; loading.value = false; items.value = []; problem.value = undefined;
  if (!allowed.value) return;
  const { orderId, state } = appliedFilters;
  if (invalidRoute.value || (orderId && !validId(orderId)) || (state !== 'all' && !(states as readonly string[]).includes(state))) {
    total.value = 0; problem.value = { code: 'client.invalid_refund_query', type: 'about:blank', status: 400, title: t('paymentRefunds.invalidId') }; return;
  }
  const operation = scope.begin(permission); if (!operation) return;
  const query = { page: page.value, pageSize: pageSize.value, orderId: orderId || undefined, refundStateKey: state === 'all' ? undefined : state };
  listPending = operation;
  loading.value = true;
  try {
    const result = await listPaymentRefunds(query, operation.signal);
    if (!operation.current() || sequence !== listSequence || !allowed.value) return;
    if (result.page !== query.page || result.pageSize !== query.pageSize || !Number.isSafeInteger(result.total) || result.total < result.items.length
      || result.items.length > query.pageSize || result.items.some(row => !validId(row.id) || !validId(row.orderId) || !validId(row.tenantId)
        || (query.orderId && !sameId(row.orderId, query.orderId)) || (query.refundStateKey && row.refundStateKey !== query.refundStateKey))) throw mismatch();
    items.value = result.items;
    total.value = result.total;
    await updateTableHeight();
  } catch (error: unknown) {
    if (!operation.current() || sequence !== listSequence) return;
    items.value = []; total.value = 0;
    problem.value = toProblem(error, 'paymentRefunds.loadFailed');
  } finally {
    operation.finish(); if (sequence === listSequence) { loading.value = false; listPending = undefined; }
  }
}

function readRoute() {
  const value = route?.query.orderId;
  orderFilter.value = typeof value === 'string' ? value : '';
  invalidRoute.value = value !== undefined && !validId(value);
  appliedFilters = { orderId: orderFilter.value.trim(), state: 'all' };
}
watch(() => route?.query.orderId, () => { scope.invalidate(); readRoute(); void load(); }, { flush: 'sync' });
function search() { invalidRoute.value = false; appliedFilters = { orderId: orderFilter.value.trim(), state: stateFilter.value }; page.value = 1; void load(); }
function reset() {
  if (router && route?.query.orderId !== undefined) { void router.replace({ name: 'payments-refunds', query: {} }); return; }
  orderFilter.value = ''; stateFilter.value = 'all'; search();
}
function changePage(value: number) { if (value !== page.value) { page.value = value; void load(); } }
function stateLabel(value: string) {
  const key = `paymentRefunds.status.${value}`; const translated = translateRuntimeMessage(t, key); return translated === key ? value : translated;
}
function closeDetail() { detailSequence++; detailPending?.cancel(); detailPending = undefined; detailOpen.value = false; detailLoading.value = false; detail.value = undefined; detailProblem.value = undefined; }
function readOriginalOrder() {
  const source = detail.value;
  if (!allowed.value || !session.can('payments.orders.read') || !source || !validId(source.orderId)) return;
  originalOrderId.value = source.orderId;
  originalOrderReference.value = { tenantId: source.tenantId, merchantConfigId: source.merchantConfigId, outTradeNo: source.outTradeNo, currency: source.currency };
  originalRefund.value = Object.freeze({ ...source });
  closeDetail(); originalOrderOpen.value = true;
}
function matchesOriginalOrder(order: PaymentOrder, refund: Readonly<PaymentRefund>): boolean {
  return sameId(order.id, refund.orderId) && sameId(order.tenantId, refund.tenantId)
    && sameId(order.merchantConfigId, refund.merchantConfigId)
    && order.outTradeNo === refund.outTradeNo && order.currency === refund.currency;
}
function beginOriginalReconcile(order: PaymentOrder) {
  const source = originalRefund.value;
  if (!allowed.value || session.switching || !session.can('payments.orders.read') || !session.can('payments.orders.reconcile')
    || !originalOrderOpen.value || source?.refundStateKey !== 'processing'
    || order.tradeStateKey !== 'refunding' || order.channelKey !== 'wechat_native'
    || !matchesOriginalOrder(order, source)) return;
  reconcileTarget.value = Object.freeze({ ...order });
  originalOrderOpen.value = false;
  reconcileOpen.value = true;
}
function refreshOriginalRefund(order: PaymentOrder) {
  const source = originalRefund.value;
  if (!allowed.value || session.switching || !session.can('payments.orders.reconcile')
    || !reconcileOpen.value || !source || !matchesOriginalOrder(order, source)) return;
  reconcileOpen.value = false;
  reconcileTarget.value = undefined;
  void readDetail(source.id, source);
}
async function readDetail(input = refundIdInput.value, original?: Readonly<PaymentRefund>) {
  if (!allowed.value) return;
  const id = input.trim();
  if (!validId(id)) { problem.value = { code: 'client.invalid_refund_id', type: 'about:blank', status: 400, title: t('paymentRefunds.invalidId') }; return; }
  if (!original) {
    originalOrderOpen.value = false; originalRefund.value = undefined;
    reconcileOpen.value = false; reconcileTarget.value = undefined;
  }
  const sequence = ++detailSequence; detailPending?.cancel(); detail.value = undefined; detailProblem.value = undefined;
  detailId.value = id; refundIdInput.value = id; detailOpen.value = true;
  const operation = scope.begin(permission); if (!operation) return;
  detailPending = operation; detailLoading.value = true;
  try {
    const value = await getPaymentRefund(id, operation.signal);
    if (!operation.current() || sequence !== detailSequence || !allowed.value) return;
    if (!validId(value.id) || !sameId(value.id, id) || !validId(value.orderId) || !validId(value.tenantId) || !validId(value.merchantConfigId)
      || typeof value.outRefundNo !== 'string' || typeof value.outTradeNo !== 'string' || typeof value.reason !== 'string'
      || typeof value.currency !== 'string' || !/^[A-Z]{3}$/.test(value.currency) || typeof value.refundStateKey !== 'string'
      || !isNonnegativeInt64String(value.amountMinor) || value.amountMinor === '0'
      || !Number.isSafeInteger(value.version) || value.version < 1 || !Number.isFinite(Date.parse(value.createdAtUtc))
      || (original && (!sameId(value.orderId, original.orderId) || !sameId(value.tenantId, original.tenantId)
        || !sameId(value.merchantConfigId, original.merchantConfigId) || value.currency !== original.currency
        || value.outTradeNo !== original.outTradeNo || value.outRefundNo !== original.outRefundNo
        || value.version < original.version))) throw mismatch();
    detail.value = value;
  } catch (error) {
    if (operation.current() && sequence === detailSequence) { detail.value = undefined; detailProblem.value = toProblem(error, 'paymentRefunds.detailFailed'); }
  } finally { operation.finish(); if (sequence === detailSequence) { detailLoading.value = false; detailPending = undefined; } }
}
</script>

<template>
  <section class="payment-refunds-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('paymentRefunds.title') }}</h1>
    <el-alert v-if="problem" type="error" :title="problem.title" show-icon class="art-page-alert" />
    <ElAlert v-if="!allowed" :title="t('paymentRefunds.hostRequired')" type="info" :closable="false" />

    <template v-if="allowed">
    <ElForm label-position="top" class="refund-filters" @submit.prevent="search">
      <ElFormItem :label="t('paymentRefunds.orderId')"><ElInput v-model="orderFilter" clearable data-testid="refunds-filter-order" /></ElFormItem>
      <ElFormItem :label="t('paymentRefunds.fieldStatus')"><ElSelect v-model="stateFilter" data-testid="refunds-filter-state"><ElOption value="all" :label="t('paymentRefunds.allStates')" /><ElOption v-for="state in states" :key="state" :value="state" :label="stateLabel(state)" /></ElSelect></ElFormItem>
      <div class="refund-filter-actions"><ElButton type="primary" data-testid="refunds-search" @click="search">{{ t('paymentRefunds.search') }}</ElButton><ElButton data-testid="refunds-reset" @click="reset">{{ t('paymentRefunds.reset') }}</ElButton></div>
    </ElForm>
    <el-card class="art-full-height-card" shadow="never">
      <ArtTableHeader :loading="loading" @refresh="load"><template #left><ElForm label-position="top" class="refund-exact-picker" @submit.prevent="readDetail()"><ElFormItem :label="t('paymentRefunds.refundId')"><ElInput v-model="refundIdInput" data-testid="refunds-exact-id" /></ElFormItem><ElButton data-testid="refunds-read-id" @click="readDetail()">{{ t('paymentRefunds.readId') }}</ElButton></ElForm></template></ArtTableHeader>

      <div ref="tableMainRef" class="art-table-main">
        <el-table
          :data="items"
          row-key="id"
          :height="tableHeight"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
        >
          <el-table-column prop="outRefundNo" :label="t('paymentRefunds.fieldOutRefundNo')" min-width="180" />
          <el-table-column prop="outTradeNo" :label="t('paymentRefunds.fieldOutTradeNo')" min-width="180" />
          <el-table-column :label="t('paymentRefunds.fieldAmount')" width="120">
            <template #default="{ row }">{{ formatAmount(row.amountMinor) }} {{ row.currency }}</template>
          </el-table-column>
          <el-table-column prop="reason" :label="t('paymentRefunds.fieldReason')" min-width="140" />
          <el-table-column :label="t('paymentRefunds.fieldStatus')" width="140">
            <template #default="{ row }">
              <el-tag>{{ stateLabel(row.refundStateKey) }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="createdAtUtc" :label="t('paymentRefunds.fieldCreatedAt')" min-width="180" />
          <el-table-column prop="failMessage" :label="t('paymentRefunds.fieldFailMessage')" min-width="160" />
          <ElTableColumn :label="t('paymentOrders.actions')" width="100" fixed="right"><template #default="{ row }"><ElButton link type="primary" :data-testid="`refund-detail-${row.id}`" @click="readDetail(row.id)">{{ t('paymentRefunds.detail') }}</ElButton></template></ElTableColumn>
        </el-table>
      </div>

      <el-pagination
        data-testid="refunds-pagination"
        :current-page="page"
        v-model:page-size="pageSize"
        layout="total, prev, pager, next"
        :total="total"
        @current-change="changePage"
      />
    </el-card>
    <ArtFormDialog :open="detailOpen && allowed" :title="t('paymentRefunds.detailTitle')" :cancel-label="t('paymentRefunds.close')" :show-confirm="false" trap-focus width="min(760px, calc(100vw - 32px))" @update:open="closeDetail">
      <p class="refund-identifiers">{{ t('paymentRefunds.refundId') }} <code data-testid="refund-detail-id">{{ detailId }}</code></p>
      <p>{{ t('paymentRefunds.readOnlyHint') }}</p>
      <ElButton :loading="detailLoading" data-testid="refund-detail-refresh" @click="readDetail(detailId)">{{ t('paymentRefunds.refresh') }}</ElButton>
      <PermissionGate code="payments.orders.read"><ElButton v-if="detail" data-testid="refund-read-original-order" @click="readOriginalOrder">{{ t('paymentOrders.readOriginal') }}</ElButton></PermissionGate>
      <ElAlert v-if="detailProblem" type="warning" :closable="false" :title="detailProblem.title" data-testid="refund-detail-problem" />
      <ElDescriptions v-if="detail" :column="1" border class="refund-identifiers" data-testid="refund-detail-snapshot">
        <ElDescriptionsItem :label="t('paymentRefunds.orderId')"><code>{{ detail.orderId }}</code></ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentRefunds.fieldOutTradeNo')">{{ detail.outTradeNo }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentRefunds.fieldOutRefundNo')">{{ detail.outRefundNo }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentRefunds.fieldStatus')">{{ stateLabel(detail.refundStateKey) }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentRefunds.fieldAmount')"><span data-testid="refund-detail-amount">{{ formatAmount(detail.amountMinor) }} {{ detail.currency }}</span></ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentRefunds.fieldReason')">{{ detail.reason }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentRefunds.providerId')">{{ detail.providerRefundId || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentRefunds.fieldCreatedAt')">{{ detail.createdAtUtc }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentRefunds.updatedAt')">{{ detail.updatedAtUtc || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentRefunds.completedAt')">{{ detail.completedAtUtc || '—' }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('paymentRefunds.fieldFailMessage')">{{ detail.failMessage || '—' }}</ElDescriptionsItem>
      </ElDescriptions>
    </ArtFormDialog>
    <PaymentOrderDetailDialog v-model:open="originalOrderOpen" :order-id="originalOrderId" :reference="originalOrderReference"
      :show-refund-reconcile="originalRefund?.refundStateKey === 'processing'" @reconcile="beginOriginalReconcile" />
    <PaymentOrderReconcileDialog v-model:open="reconcileOpen" :reference="reconcileTarget" refund-only @updated="refreshOriginalRefund" />
    </template>
  </section>
</template>
<style scoped>
.refund-filters { display: grid; grid-template-columns: minmax(200px, 1fr) minmax(150px, 200px) auto; align-items: end; gap: 12px; }
.refund-filter-actions { display: flex; gap: 8px; margin-bottom: 18px; }
.refund-identifiers { overflow-wrap: anywhere; }
.refund-exact-picker { display: flex; flex-wrap: wrap; align-items: end; gap: 8px; max-width: 100%; }
.refund-exact-picker .el-form-item { flex: 1 1 200px; min-width: 0; max-width: 100%; margin: 0; }
@media (max-width: 640px) { .refund-filters { grid-template-columns: 1fr; gap: 0; } }
</style>
