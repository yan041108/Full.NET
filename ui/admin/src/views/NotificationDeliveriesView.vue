<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
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
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import {
  getNotificationDelivery,
  listNotificationDeliveries,
  retryNotificationDelivery,
  getNotificationDeliverySummary,
  reconcileNotificationDelivery,
  deadLetterNotificationDelivery
} from '../api/notification-platform';
import { getNotificationIntent, revokeNotificationIntent } from '../api/notification-intents';
import { useSessionStore } from '../auth/session';

const statusKeys = [
  'persisted',
  'accepted',
  'sent',
  'delivered',
  'unknown',
  'read',
  'failed',
  'dead_lettered',
  'suppressed'
] as const;

type DeliveryStatusKey = typeof statusKeys[number];

/** Unknown 保留待对账语义；人工重试、转死信与业务撤回使用各自精确权限。 */
const { t, locale } = useAdminI18n();
const session = useSessionStore();
const intentRevision = ref<number>();
const intentLookupId = ref('');
const loadedIntentId = ref<string>();
const revocationResult = ref('');
const summary = ref<Array<{ channelKey: string; providerTypeKey: string; statusKey: string; count: number; receiptModeKey: string; oldestCreatedAtUtc: string }>>([]);
const summaryUnavailable = ref(false);
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
const { tableMainRef, tableHeight, syncTableLayout } = useArtPagedTableInCard(loading);
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

onMounted(() => { void load(); void loadSummary(); });

async function loadSummary(): Promise<void> {
  if (!session.can('notifications.deliveries.read')) return;
  try { summary.value = [...await getNotificationDeliverySummary()]; summaryUnavailable.value = false; }
  catch { summaryUnavailable.value = true; }
}

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
    void syncTableLayout();
  }
}

async function selectItem(item: NotificationDeliveryResponse): Promise<void> {
  selectedId.value = item.id;
  detailLoading.value = true;
  problem.value = undefined;
  attachmentCount.value = null;
  intentRevision.value = undefined;
  loadedIntentId.value = undefined;
  revocationResult.value = '';
  retryForm.reason = '';
  retryFormRef.value?.clearValidate();
  try {
    selected.value = await getNotificationDelivery(item.id);
    if (session.can('notifications.intents.read')) {
      const intent = await getNotificationIntent(item.intentId);
      attachmentCount.value = intent.attachments.length;
      intentRevision.value = intent.revision;
      loadedIntentId.value = intent.id;
    }
  } catch (error: unknown) {
    selected.value = undefined;
    problem.value = toProblem(error);
  } finally {
    detailLoading.value = false;
  }
}

async function revokeIntent(): Promise<void> {
  if (!loadedIntentId.value || intentRevision.value === undefined || changing.value || !session.can('notifications.intents.revoke')) return;
  const intentId = loadedIntentId.value;
  const revision = intentRevision.value;
  const answer = await ElMessageBox.prompt(t('notificationDeliveries.revokePrompt'), t('notificationDeliveries.revoke'), {
    inputValidator: (value: string) => value.trim().length > 0 && value.trim().length <= 128
  }).catch(() => undefined);
  if (!answer || !session.can('notifications.intents.revoke') || loadedIntentId.value !== intentId) return;
  changing.value = true;
  try {
    const result = await revokeNotificationIntent(intentId, revision, answer.value.trim());
    revocationResult.value = t('notificationDeliveries.revokedResult', { count: result.crossedSendBoundary });
    intentRevision.value = undefined;
    await load();
  } catch (error) { problem.value = toProblem(error); }
  finally { changing.value = false; }
}

async function loadIntentForRevocation(): Promise<void> {
  if (changing.value || !session.can('notifications.intents.read') || !session.can('notifications.intents.revoke')) return;
  const id = intentLookupId.value.trim();
  if (!/^[0-9a-f-]{36}$/i.test(id)) return;
  changing.value = true;
  intentRevision.value = undefined;
  loadedIntentId.value = undefined;
  try {
    const intent = await getNotificationIntent(id);
    loadedIntentId.value = intent.id;
    intentRevision.value = intent.statusKey === 'revoked' ? undefined : intent.revision;
  } catch (error) { problem.value = toProblem(error); }
  finally { changing.value = false; }
}

function canRetryStatus(statusKey: string): boolean {
  return statusKey === 'failed' || statusKey === 'dead_lettered';
}

async function deadLetter(): Promise<void> {
  const row = selected.value;
  if (!row || changing.value || !session.can('notifications.deliveries.dead_letter')) return;
  const answer = await ElMessageBox.prompt(t('notificationDeliveries.deadLetterPrompt'), t('notificationDeliveries.deadLetter'), {
    inputValidator: (value: string) => value.trim().length > 0 && value.trim().length <= 128
  }).catch(() => undefined);
  if (!answer || !session.can('notifications.deliveries.dead_letter') || selected.value?.id !== row.id) return;
  changing.value = true;
  try { selected.value = await deadLetterNotificationDelivery(row.id, row.revision, answer.value.trim()); await load(); await loadSummary(); }
  catch (error) { problem.value = toProblem(error); }
  finally { changing.value = false; }
}

async function reconcile(outcomeKey: 'confirmed_accepted' | 'confirmed_not_called'): Promise<void> {
  const row = selected.value;
  if (!row || changing.value || !session.can('notifications.deliveries.reconcile')) return;
  const answer = await ElMessageBox.prompt(t('notificationDeliveries.reconcilePrompt'),
    t(outcomeKey === 'confirmed_accepted' ? 'notificationDeliveries.confirmCalled' : 'notificationDeliveries.confirmNotCalled'), {
      inputValidator: (value: string) => value.trim().length > 0 && value.trim().length <= 128
    }).catch(() => undefined);
  if (!answer || !session.can('notifications.deliveries.reconcile') || selected.value?.id !== row.id) return;
  changing.value = true;
  try {
    selected.value = await reconcileNotificationDelivery(row.id, row.revision, outcomeKey, answer.value.trim());
    await load(); await loadSummary();
  } catch (error) { problem.value = toProblem(error); }
  finally { changing.value = false; }
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
    <ElCard>
      <h2>{{ t('notificationDeliveries.summary') }}</h2>
      <p v-if="revocationResult" role="status">{{ revocationResult }}</p>
      <ElButton @click="loadSummary">{{ t('notificationDeliveries.refreshSummary') }}</ElButton>
      <p v-if="summaryUnavailable" role="status">{{ t('notificationDeliveries.summaryUnavailable') }}</p>
      <ElTable :data="summary">
        <ElTableColumn prop="channelKey" :label="t('notificationDeliveries.fieldChannel')" />
        <ElTableColumn prop="providerTypeKey" :label="t('notificationDeliveries.provider')" />
        <ElTableColumn :label="t('notificationDeliveries.fieldStatus')"><template #default="{ row }">{{ statusLabel(row.statusKey) }}</template></ElTableColumn>
        <ElTableColumn prop="count" :label="t('notificationDeliveries.count')" />
        <ElTableColumn prop="oldestCreatedAtUtc" :label="t('notificationDeliveries.oldest')" />
        <ElTableColumn prop="receiptModeKey" :label="t('notificationDeliveries.receiptCoverage')" />
      </ElTable>
      <p>{{ t('notificationDeliveries.receiptCoverageHint') }}</p>
      <PermissionGate code="notifications.intents.read">
        <PermissionGate code="notifications.intents.revoke">
          <ElInput v-model="intentLookupId" :placeholder="t('notificationDeliveries.intentLookup')" />
          <ElButton :disabled="changing" @click="loadIntentForRevocation">{{ t('notificationDeliveries.loadIntent') }}</ElButton>
          <ElButton :disabled="changing || intentRevision === undefined" @click="revokeIntent">{{ t('notificationDeliveries.revoke') }}</ElButton>
        </PermissionGate>
      </PermissionGate>
    </ElCard>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <div class="notification-deliveries-layout art-split-layout">
      <ElCard class="notification-deliveries-list art-table-card" shadow="never">
        <template #header>
          <h2>{{ t('notificationDeliveries.listTitle') }}</h2>
        </template>

        <div ref="tableMainRef" class="art-crud-table-main">
        <ElTable
          v-loading="loading"
          :data="items"
          :height="tableHeight"
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
                @click.stop="void selectItem(row as NotificationDeliveryResponse)"
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

        <ElPagination
          v-if="total > 0"
          class="art-table-pagination"
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
          <p v-if="revocationResult" role="status">{{ revocationResult }}</p>
          <PermissionGate v-if="intentRevision !== undefined" code="notifications.intents.revoke">
            <ElButton :disabled="changing" @click="revokeIntent">{{ t('notificationDeliveries.revoke') }}</ElButton>
          </PermissionGate>
          <PermissionGate v-if="selected.statusKey === 'unknown' || selected.statusKey === 'failed'" code="notifications.deliveries.dead_letter">
            <ElButton :disabled="changing" @click="deadLetter">{{ t('notificationDeliveries.deadLetter') }}</ElButton>
          </PermissionGate>
          <PermissionGate v-if="selected.statusKey === 'unknown' || selected.statusKey === 'dead_lettered'" code="notifications.deliveries.reconcile">
            <ElButton data-testid="notification-reconcile-accepted" :disabled="changing" @click="reconcile('confirmed_accepted')">{{ t('notificationDeliveries.confirmCalled') }}</ElButton>
            <ElButton data-testid="notification-reconcile-not-called" :disabled="changing" @click="reconcile('confirmed_not_called')">{{ t('notificationDeliveries.confirmNotCalled') }}</ElButton>
          </PermissionGate>
          <p v-if="selected.suppressionReasonCode" translate="no">{{ selected.suppressionReasonCode }}</p>
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
