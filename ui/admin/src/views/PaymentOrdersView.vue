<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { PaymentOrderListItem, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { createPaymentOrder, listPaymentOrders, reconcilePaymentOrder, createPaymentRefund } from '../api/payment-orders';

defineOptions({ name: 'PaymentOrdersView' });

const { t } = useAdminI18n();
const items = ref<PaymentOrderListItem[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const createOpen = ref(false);
const refundOpen = ref(false);
const refundOrderId = ref('');
const refundForm = reactive({
  amountYuan: '',
  reason: ''
});
const createForm = reactive({
  tenantId: '',
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

function toProblem(error: unknown, fallbackKey: string): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { title: t(fallbackKey), status: 500, type: 'about:blank' };
}

function formatAmount(minor: number): string {
  return (minor / 100).toFixed(2);
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listPaymentOrders({ page: page.value, pageSize: pageSize.value });
    items.value = result.items;
    total.value = result.total;
    await updateTableHeight();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'paymentOrders.loadFailed');
  } finally {
    loading.value = false;
  }
}

function openCreate(): void {
  Object.assign(createForm, {
    tenantId: '',
    merchantConfigId: '',
    amountYuan: '',
    subject: '',
    description: ''
  });
  lastCodeUrl.value = '';
  createOpen.value = true;
}

async function submitCreate(): Promise<void> {
  const amount = Number.parseFloat(createForm.amountYuan);
  if (!createForm.tenantId.trim() || Number.isNaN(amount) || amount <= 0 || !createForm.subject.trim()) {
    ElMessage.warning(t('paymentOrders.createValidationFailed'));
    return;
  }
  changing.value = true;
  try {
    const order = await createPaymentOrder({
      tenantId: createForm.tenantId.trim(),
      merchantConfigId: createForm.merchantConfigId.trim() || null,
      amountMinor: Math.round(amount * 100),
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
  refundOrderId.value = orderId;
  Object.assign(refundForm, { amountYuan: '', reason: '' });
  refundOpen.value = true;
}

async function submitRefund(): Promise<void> {
  if (!refundForm.reason.trim()) {
    ElMessage.warning(t('paymentOrders.refundValidationFailed'));
    return;
  }
  const amountYuan = refundForm.amountYuan.trim();
  const amountMinor = amountYuan ? Math.round(Number.parseFloat(amountYuan) * 100) : null;
  if (amountYuan && (Number.isNaN(amountMinor) || amountMinor <= 0)) {
    ElMessage.warning(t('paymentOrders.refundValidationFailed'));
    return;
  }
  changing.value = true;
  try {
    await createPaymentRefund(refundOrderId.value, {
      amountMinor,
      reason: refundForm.reason.trim()
    });
    ElMessage.success(t('paymentOrders.refundSuccess'));
    refundOpen.value = false;
    await load();
  } catch (error: unknown) {
    ElMessage.error(toProblem(error, 'paymentOrders.refundFailed').title);
  } finally {
    changing.value = false;
  }
}

onMounted(() => {
  void load();
});
</script>

<template>
  <section class="payment-orders-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('paymentOrders.title') }}</h1>
    <el-alert v-if="problem" type="error" :title="problem.title" show-icon class="art-page-alert" />
    <el-alert
      v-if="lastCodeUrl"
      type="success"
      :title="t('paymentOrders.codeUrlReady')"
      :description="lastCodeUrl"
      show-icon
      class="art-page-alert"
    />

    <el-card class="art-full-height-card" shadow="never">
      <ArtTableHeader>
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
              <PermissionGate code="payments.orders.refund">
                <el-button
                  link
                  type="warning"
                  :data-testid="`payment-order-refund-${row.id}`"
                  @click="openRefund(row.id)"
                >
                  {{ t('paymentOrders.refund') }}
                </el-button>
              </PermissionGate>
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
      v-model="createOpen"
      :title="t('paymentOrders.createTitle')"
      :loading="changing"
      @submit="submitCreate"
    >
      <el-form label-width="120px">
        <el-form-item :label="t('paymentOrders.fieldTenantId')" required>
          <el-input v-model="createForm.tenantId" />
        </el-form-item>
        <el-form-item :label="t('paymentOrders.fieldMerchantConfigId')">
          <el-input v-model="createForm.merchantConfigId" />
        </el-form-item>
        <el-form-item :label="t('paymentOrders.fieldAmountYuan')" required>
          <el-input v-model="createForm.amountYuan" />
        </el-form-item>
        <el-form-item :label="t('paymentOrders.fieldSubject')" required>
          <el-input v-model="createForm.subject" />
        </el-form-item>
        <el-form-item :label="t('paymentOrders.fieldDescription')">
          <el-input v-model="createForm.description" type="textarea" :rows="3" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>

    <ArtFormDialog
      v-model="refundOpen"
      :title="t('paymentOrders.refundTitle')"
      :loading="changing"
      @submit="submitRefund"
    >
      <el-form label-width="120px">
        <el-form-item :label="t('paymentOrders.refundReason')" required>
          <el-input v-model="refundForm.reason" type="textarea" :rows="3" />
        </el-form-item>
        <el-form-item :label="t('paymentOrders.refundAmountYuan')">
          <el-input v-model="refundForm.amountYuan" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>
