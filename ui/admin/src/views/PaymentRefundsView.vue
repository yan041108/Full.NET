<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  ElAlert,
  ElCard,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { PaymentRefundListItem, FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import { listPaymentRefunds } from '../api/payment-refunds';

defineOptions({ name: 'PaymentRefundsView' });

const { t } = useAdminI18n();
const items = ref<PaymentRefundListItem[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();

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
    const result = await listPaymentRefunds({ page: page.value, pageSize: pageSize.value });
    items.value = result.items;
    total.value = result.total;
    await updateTableHeight();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'paymentRefunds.loadFailed');
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  void load();
});
</script>

<template>
  <section class="payment-refunds-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('paymentRefunds.title') }}</h1>
    <el-alert v-if="problem" type="error" :title="problem.title" show-icon class="art-page-alert" />

    <el-card class="art-full-height-card" shadow="never">
      <ArtTableHeader />

      <div ref="tableMainRef" class="art-table-main">
        <el-table
          :data="items"
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
              <el-tag>{{ row.refundStateKey }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="createdAtUtc" :label="t('paymentRefunds.fieldCreatedAt')" min-width="180" />
          <el-table-column prop="failMessage" :label="t('paymentRefunds.fieldFailMessage')" min-width="160" />
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
  </section>
</template>
