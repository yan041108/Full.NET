<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElPagination,
  ElTag
} from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type NotificationDeliveryReceiptResponse,
  type NotificationDeliveryResponse
} from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import {
  getNotificationDelivery,
  listNotificationDeliveries,
  retryNotificationDelivery
} from '../api/notification-platform';

const statusKeys = [
  'persisted',
  'accepted',
  'sent',
  'delivered',
  'unknown',
  'read',
  'failed',
  'dead_lettered'
] as const;

type DeliveryStatusKey = typeof statusKeys[number];

/** Unknown 不是成功；死信没有独立 HTTP Endpoint，失败/死信/未知共用人工重试。 */
const { t } = useAdminI18n();
const items = ref<NotificationDeliveryResponse[]>([]);
const selected = ref<NotificationDeliveryResponse>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const retryReason = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listNotificationDeliveries(page.value, pageSize.value);
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'notificationDeliveries.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function selectItem(item: NotificationDeliveryResponse): Promise<void> {
  problem.value = undefined;
  try {
    selected.value = await getNotificationDelivery(item.id);
    retryReason.value = '';
  } catch (error: unknown) {
    problem.value = toProblem(error);
  }
}

function canRetryStatus(statusKey: string): boolean {
  return statusKey === 'failed' || statusKey === 'dead_lettered' || statusKey === 'unknown';
}

async function retryItem(): Promise<void> {
  const current = selected.value;
  if (!current || changing.value || !retryReason.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    selected.value = await retryNotificationDelivery(current.id, {
      revision: current.revision,
      reason: retryReason.value.trim()
    });
    ElMessage.success(t('notificationDeliveries.retrySuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function receiptLabel(receipt: NotificationDeliveryReceiptResponse): string {
  const mapped = t(`notificationDeliveries.receipt.mapped.${receipt.mappedStatusKey}` as 'notificationDeliveries.receipt.mapped.failed');
  const external = t(`notificationDeliveries.receipt.external.${receipt.externalStatusKey}` as 'notificationDeliveries.receipt.external.bounced');
  if (external !== receipt.externalStatusKey) {
    return `${mapped} · ${external}`;
  }
  return `${mapped} · ${receipt.externalStatusKey}`;
}

function receiptTone(mappedStatusKey: string): 'success' | 'warning' | 'info' | 'danger' | undefined {
  switch (mappedStatusKey) {
    case 'delivered':
    case 'accepted':
      return 'success';
    case 'sent':
      return 'warning';
    case 'failed':
      return 'danger';
    default:
      return 'info';
  }
}

function latestBounceReason(receipts: NotificationDeliveryReceiptResponse[]): string | undefined {
  const bounce = [...receipts]
    .reverse()
    .find(item => item.mappedStatusKey === 'failed'
      && (item.externalStatusKey === 'bounced' || item.externalStatusKey === 'rejected'));
  return bounce ? receiptLabel(bounce) : undefined;
}

function statusLabel(statusKey: string): string {
  return statusKeys.includes(statusKey as DeliveryStatusKey)
    ? t(`notificationDeliveries.status.${statusKey}` as 'notificationDeliveries.status.unknown')
    : statusKey;
}

function statusTone(statusKey: string): 'success' | 'warning' | 'info' | 'danger' | undefined {
  switch (statusKey) {
    case 'delivered':
    case 'read':
      return 'success';
    case 'sent':
      return 'warning';
    case 'accepted':
    case 'persisted':
      return 'info';
    case 'failed':
    case 'dead_lettered':
    case 'unknown':
      return 'danger';
    default:
      return undefined;
  }
}

function toProblem(
  error: unknown,
  fallbackCode: 'notificationDeliveries.loadFailed' | 'notificationDeliveries.operationFailed'
    = 'notificationDeliveries.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: fallbackCode, title: t(fallbackCode) };
}
</script>

<template>
  <section class="notification-deliveries-view art-page-stack art-full-height" :aria-busy="loading">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('notificationDeliveries.eyebrow') }}</p>
      <h1 data-route-heading tabindex="-1">{{ t('notificationDeliveries.title') }}</h1>
      <p>{{ t('notificationDeliveries.description') }}</p>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <ElCard class="art-card">
      <template #header>
        <h2>{{ t('notificationDeliveries.listTitle') }}</h2>
      </template>
      <p v-if="!items.length" class="art-empty-state">{{ t('notificationDeliveries.emptyList') }}</p>
      <ul v-else class="art-list">
        <li v-for="item in items" :key="item.id">
          <button type="button" data-testid="notification-deliveries-load" @click="selectItem(item)">
            <strong translate="no">{{ item.id }}</strong>
            <ElTag
              :type="statusTone(item.statusKey)"
              :data-testid="`notification-deliveries-status-${item.statusKey}`"
              :class="{ 'delivery-status--unknown': item.statusKey === 'unknown' }"
            >
              {{ statusLabel(item.statusKey) }}
            </ElTag>
          </button>
        </li>
      </ul>
      <ElPagination
        v-if="total > 0"
        background
        layout="prev, pager, next, total"
        :current-page="page"
        :page-size="pageSize"
        :total="total"
        @current-change="value => { page = value; void load(); }"
      />
    </ElCard>

    <ElCard v-if="selected" class="art-card">
      <template #header>
        <h2>{{ t('notificationDeliveries.detailTitle') }}</h2>
      </template>
      <p>
        {{ t('notificationDeliveries.fieldStatus') }}:
        <ElTag
          :type="statusTone(selected.statusKey)"
          :data-testid="`notification-deliveries-detail-status-${selected.statusKey}`"
          :class="{ 'delivery-status--unknown': selected.statusKey === 'unknown' }"
        >
          {{ statusLabel(selected.statusKey) }}
        </ElTag>
      </p>
      <ul class="art-list">
        <li v-for="attempt in selected.attempts" :key="attempt.id" data-testid="notification-deliveries-attempt">
          <span>#{{ attempt.attemptNumber }}</span>
          <ElTag>{{ attempt.resultCategoryKey ?? attempt.statusKey }}</ElTag>
        </li>
      </ul>
      <p
        v-if="latestBounceReason(selected.receipts ?? [])"
        data-testid="notification-deliveries-bounce-reason"
      >
        {{ t('notificationDeliveries.bounceReason') }}:
        {{ latestBounceReason(selected.receipts ?? []) }}
      </p>
      <section v-if="(selected.receipts ?? []).length > 0" class="delivery-receipts">
        <h3>{{ t('notificationDeliveries.receiptTimeline') }}</h3>
        <ul class="art-list">
          <li
            v-for="receipt in selected.receipts ?? []"
            :key="receipt.id"
            data-testid="notification-deliveries-receipt"
          >
            <time>{{ receipt.receivedAtUtc }}</time>
            <ElTag :type="receiptTone(receipt.mappedStatusKey)">
              {{ receiptLabel(receipt) }}
            </ElTag>
            <span>{{ receipt.processStatusKey }}</span>
          </li>
        </ul>
      </section>
      <PermissionGate v-if="canRetryStatus(selected.statusKey)" code="notifications.deliveries.retry">
        <div class="art-form-grid">
          <ElInput
            v-model="retryReason"
            data-testid="notification-deliveries-retry-reason"
            maxlength="128"
            :placeholder="t('notificationDeliveries.fieldReason')"
          />
          <ElButton
            data-testid="notification-deliveries-retry"
            type="primary"
            :disabled="changing || !retryReason.trim()"
            @click="retryItem"
          >
            {{ t('notificationDeliveries.retry') }}
          </ElButton>
        </div>
      </PermissionGate>
    </ElCard>
  </section>
</template>

<style scoped>
.delivery-status--unknown {
  --el-tag-bg-color: var(--el-fill-color-dark);
  --el-tag-border-color: var(--el-color-warning);
  --el-tag-text-color: var(--el-color-warning);
}
</style>
