<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag,
  type FormInstance,
  type FormRules
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
import { getNotificationIntent } from '../api/notification-intents';

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
const { t, locale } = useAdminI18n();
const items = ref<NotificationDeliveryResponse[]>([]);
const selectedId = ref<string>();
const selected = ref<NotificationDeliveryResponse>();
const detailLoading = ref(false);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const retryFormRef = ref<FormInstance>();
const retryForm = reactive({ reason: '' });
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const attachmentCount = ref<number | null>(null);

const retryRules = computed<FormRules>(() => {
  const message = t('notificationDeliveries.validationRequired');
  return {
    reason: [
      {
        validator: (_rule: unknown, value: unknown, callback: (error?: Error) => void) => {
          if (typeof value !== 'string' || !value.trim()) {
            callback(new Error(message));
            return;
          }
          callback();
        },
        trigger: ['blur', 'change'] as const
      }
    ]
  };
});

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
  selectedId.value = item.id;
  detailLoading.value = true;
  problem.value = undefined;
  attachmentCount.value = null;
  retryForm.reason = '';
  retryFormRef.value?.clearValidate();
  try {
    selected.value = await getNotificationDelivery(item.id);
    const intent = await getNotificationIntent(item.intentId);
    attachmentCount.value = intent.attachments.length;
  } catch (error: unknown) {
    selected.value = undefined;
    problem.value = toProblem(error);
  } finally {
    detailLoading.value = false;
  }
}

function canRetryStatus(statusKey: string): boolean {
  return statusKey === 'failed' || statusKey === 'dead_lettered' || statusKey === 'unknown';
}

async function validateRetryForm(): Promise<boolean> {
  const form = retryFormRef.value;
  if (!form) {
    return false;
  }
  if (!retryForm.reason.trim()) {
    try {
      await form.validate();
    } catch {
      /* 展示字段错误 */
    }
    return false;
  }
  try {
    await form.validate();
    return true;
  } catch {
    return false;
  }
}

async function retryItem(): Promise<void> {
  const current = selected.value;
  if (!current || changing.value || !(await validateRetryForm())) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    selected.value = await retryNotificationDelivery(current.id, {
      revision: current.revision,
      reason: retryForm.reason.trim()
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

function showsSmsSentNotice(delivery: NotificationDeliveryResponse): boolean {
  return delivery.channelKey === 'sms' && delivery.statusKey === 'sent';
}

function showsEmailSentNotice(delivery: NotificationDeliveryResponse): boolean {
  return delivery.channelKey === 'email' && delivery.statusKey === 'sent';
}

function formatDateTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleString(locale.value, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false
  });
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
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('notificationDeliveries.title') }}</h1>
    <p class="art-sr-heading">{{ t('notificationDeliveries.description') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <div class="notification-deliveries-layout">
      <ElCard class="notification-deliveries-list art-table-card" shadow="never">
        <template #header>
          <h2>{{ t('notificationDeliveries.listTitle') }}</h2>
        </template>

        <ElTable
          v-loading="loading"
          :data="items"
          class="notification-deliveries-table"
          highlight-current-row
          row-key="id"
          empty-text=""
          :current-row-key="selectedId"
          @row-click="row => void selectItem(row)"
        >
          <ElTableColumn
            :label="t('notificationDeliveries.fieldDeliveryId')"
            min-width="200"
            show-overflow-tooltip
          >
            <template #default="{ row }">
              <button
                type="button"
                class="notification-deliveries-table__id"
                data-testid="notification-deliveries-load"
                translate="no"
                @click.stop="void selectItem(row)"
              >
                {{ row.id }}
              </button>
            </template>
          </ElTableColumn>

          <ElTableColumn :label="t('notificationDeliveries.fieldChannel')" width="100" show-overflow-tooltip>
            <template #default="{ row }">
              <span translate="no">{{ row.channelKey }}</span>
            </template>
          </ElTableColumn>

          <ElTableColumn :label="t('notificationDeliveries.fieldStatus')" width="120" align="center">
            <template #default="{ row }">
              <ElTag
                size="small"
                :type="statusTone(row.statusKey)"
                :data-testid="`notification-deliveries-status-${row.statusKey}`"
                :class="{ 'delivery-status--unknown': row.statusKey === 'unknown' }"
              >
                {{ statusLabel(row.statusKey) }}
              </ElTag>
            </template>
          </ElTableColumn>

          <ElTableColumn :label="t('notificationDeliveries.fieldCreatedAt')" width="168">
            <template #default="{ row }">
              <span translate="no">{{ formatDateTime(row.createdAtUtc) }}</span>
            </template>
          </ElTableColumn>

          <template #empty>
            <p class="art-empty-state">{{ t('notificationDeliveries.emptyList') }}</p>
          </template>
        </ElTable>

        <div v-if="total > 0" class="notification-deliveries-list__pagination">
          <ElPagination
            background
            layout="prev, pager, next, total"
            :current-page="page"
            :page-size="pageSize"
            :total="total"
            @current-change="value => { page = value; void load(); }"
          />
        </div>
      </ElCard>

      <ElCard
        class="notification-deliveries-detail art-form-card"
        shadow="never"
        :aria-busy="detailLoading || changing"
      >
        <template #header>
          <h2>{{ t('notificationDeliveries.detailTitle') }}</h2>
        </template>

        <p v-if="!selected && !detailLoading" class="art-empty-state notification-deliveries-detail__empty">
          {{ t('notificationDeliveries.selectDetail') }}
        </p>

        <div v-else-if="selected" class="notification-deliveries-detail__body">
          <dl class="notification-deliveries-detail__meta">
            <div>
              <dt>{{ t('notificationDeliveries.fieldStatus') }}</dt>
              <dd>
                <ElTag
                  :type="statusTone(selected.statusKey)"
                  :data-testid="`notification-deliveries-detail-status-${selected.statusKey}`"
                  :class="{ 'delivery-status--unknown': selected.statusKey === 'unknown' }"
                >
                  {{ statusLabel(selected.statusKey) }}
                </ElTag>
              </dd>
            </div>
            <div>
              <dt>{{ t('notificationDeliveries.fieldChannel') }}</dt>
              <dd translate="no">{{ selected.channelKey }}</dd>
            </div>
            <div v-if="attachmentCount !== null">
              <dt>{{ t('notificationDeliveries.fieldAttachmentCount') }}</dt>
              <dd data-testid="notification-deliveries-attachment-count">{{ attachmentCount }}</dd>
            </div>
          </dl>

          <p
            v-if="showsEmailSentNotice(selected)"
            class="art-inline-alert"
            data-testid="notification-deliveries-email-sent-notice"
          >
            {{ t('notificationDeliveries.emailSentNotice') }}
          </p>
          <p
            v-if="showsSmsSentNotice(selected)"
            class="art-inline-alert"
            data-testid="notification-deliveries-sms-sent-notice"
          >
            {{ t('notificationDeliveries.smsSentNotice') }}
          </p>

          <section v-if="selected.attempts.length > 0" class="notification-deliveries-attempts">
            <h3>{{ t('notificationDeliveries.attemptsTitle') }}</h3>
            <ul class="notification-deliveries-attempt-list">
              <li v-for="attempt in selected.attempts" :key="attempt.id" data-testid="notification-deliveries-attempt">
                <span>#{{ attempt.attemptNumber }}</span>
                <ElTag size="small">{{ attempt.resultCategoryKey ?? attempt.statusKey }}</ElTag>
              </li>
            </ul>
          </section>

          <p
            v-if="latestBounceReason(selected.receipts ?? [])"
            data-testid="notification-deliveries-bounce-reason"
          >
            {{ t('notificationDeliveries.bounceReason') }}:
            {{ latestBounceReason(selected.receipts ?? []) }}
          </p>

          <section v-if="(selected.receipts ?? []).length > 0" class="delivery-receipts">
            <h3>{{ t('notificationDeliveries.receiptTimeline') }}</h3>
            <ul class="notification-deliveries-receipt-list">
              <li
                v-for="receipt in selected.receipts ?? []"
                :key="receipt.id"
                data-testid="notification-deliveries-receipt"
              >
                <time translate="no">{{ formatDateTime(receipt.receivedAtUtc) }}</time>
                <ElTag size="small" :type="receiptTone(receipt.mappedStatusKey)">
                  {{ receiptLabel(receipt) }}
                </ElTag>
                <span translate="no">{{ receipt.processStatusKey }}</span>
              </li>
            </ul>
          </section>

          <PermissionGate v-if="canRetryStatus(selected.statusKey)" code="notifications.deliveries.retry">
            <ElForm
              ref="retryFormRef"
              :model="retryForm"
              :rules="retryRules"
              label-position="top"
              class="notification-deliveries-retry-form"
              @submit.prevent
            >
              <ElFormItem prop="reason" :label="t('notificationDeliveries.fieldReason')" required>
                <ElInput
                  v-model="retryForm.reason"
                  data-testid="notification-deliveries-retry-reason"
                  maxlength="128"
                  type="textarea"
                  :rows="2"
                />
              </ElFormItem>
              <ElFormItem>
                <ElButton
                  data-testid="notification-deliveries-retry"
                  type="primary"
                  :disabled="changing"
                  @click="retryItem"
                >
                  {{ t('notificationDeliveries.retry') }}
                </ElButton>
              </ElFormItem>
            </ElForm>
          </PermissionGate>
        </div>
      </ElCard>
    </div>
  </section>
</template>

<style scoped>
.notification-deliveries-view {
  min-height: 0;
}

.notification-deliveries-layout {
  display: flex;
  flex: 1;
  gap: 12px;
  min-height: 0;
}

.notification-deliveries-list {
  flex: 1 1 0;
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.notification-deliveries-list :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 0;
}

.notification-deliveries-list :deep(.el-card__header) {
  padding: 12px 16px;
}

.notification-deliveries-list :deep(.el-card__header) h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}

.notification-deliveries-table {
  flex: 1;
  min-height: 200px;
}

.notification-deliveries-table :deep(.el-table__row) {
  cursor: pointer;
}

.notification-deliveries-table__id {
  padding: 0;
  border: none;
  background: none;
  color: var(--el-color-primary);
  font: inherit;
  text-align: left;
  cursor: pointer;
}

.notification-deliveries-list__pagination {
  display: flex;
  justify-content: flex-end;
  padding-top: 12px;
}

.notification-deliveries-detail {
  flex: 1 1 0;
  min-width: 0;
  min-height: 0;
  overflow: auto;
}

.notification-deliveries-detail :deep(.el-card__header) {
  padding: 12px 16px;
}

.notification-deliveries-detail :deep(.el-card__header) h2 {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
}

.notification-deliveries-detail :deep(.el-card__body) {
  padding: 12px 16px 16px;
}

.notification-deliveries-detail__empty {
  margin: 24px 0;
}

.notification-deliveries-detail__meta {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px 16px;
  margin: 0 0 16px;
}

.notification-deliveries-detail__meta div {
  margin: 0;
}

.notification-deliveries-detail__meta dt {
  margin: 0 0 4px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
  font-weight: 400;
}

.notification-deliveries-detail__meta dd {
  margin: 0;
  font-size: 14px;
}

.notification-deliveries-attempts h3,
.delivery-receipts h3 {
  margin: 16px 0 8px;
  font-size: 14px;
  font-weight: 600;
}

.notification-deliveries-attempt-list,
.notification-deliveries-receipt-list {
  margin: 0;
  padding: 0;
  list-style: none;
}

.notification-deliveries-attempt-list li,
.notification-deliveries-receipt-list li {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  padding: 6px 0;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.notification-deliveries-retry-form {
  margin-top: 16px;
  padding-top: 16px;
  border-top: 1px solid var(--el-border-color-lighter);
}

.notification-deliveries-retry-form :deep(.el-form-item) {
  margin-bottom: 12px;
}

.notification-deliveries-retry-form :deep(.el-form-item:last-child) {
  margin-bottom: 0;
}

.delivery-status--unknown {
  --el-tag-bg-color: var(--el-fill-color-dark);
  --el-tag-border-color: var(--el-color-warning);
  --el-tag-text-color: var(--el-color-warning);
}
</style>
