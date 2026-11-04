<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem, ElDrawer, ElOption, ElPagination, ElSelect, ElTable, ElTableColumn, ElTag } from 'element-plus';
import 'element-plus/theme-chalk/el-descriptions.css';
import 'element-plus/theme-chalk/el-descriptions-item.css';
import type { WebhookSubscription, WebhookDeliveryHistory, WebhookDeliveryStatusKey } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../../auth/session';
import { useAdminI18n } from '../../i18n/adminI18n';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { getWebhookDeliveryHistory, listWebhookDeliveryHistory } from '../../api/webhook-subscriptions';

const props = defineProps<{ subscription: WebhookSubscription }>();
const emit = defineEmits<{ close: [] }>();
const session = useSessionStore(), { t, locale } = useAdminI18n();
const permission = 'webhooks.deliveries.read';
const canRead = () => !session.switching && session.currentUser?.tenantId?.toLowerCase() === props.subscription.tenantId.toLowerCase() && session.can(permission) && session.can('webhooks.subscriptions.read');
const items = ref<WebhookDeliveryHistory[]>([]), total = ref(0), page = ref(1), pageSize = ref(20), filter = ref<WebhookDeliveryStatusKey | ''>('');
const loading = ref(false), problem = ref(''), detailLoading = ref(false), detailProblem = ref(''), detailTarget = ref(''), detail = ref<WebhookDeliveryHistory>();
const epoch = ref(0), detailHeading = ref<HTMLElement>();
let listRequest: ReturnType<typeof scope.begin>, detailRequest: ReturnType<typeof scope.begin>;
let lastQuery: { page: number; pageSize: number; statusKey?: WebhookDeliveryStatusKey } | undefined;
const scope = useAuthorizedViewScope(session, () => {
  epoch.value++; items.value = []; total.value = 0; loading.value = false; problem.value = ''; detail.value = undefined;
  detailTarget.value = ''; detailProblem.value = ''; detailLoading.value = false; page.value = 1; pageSize.value = 20; filter.value = '';
  lastQuery = undefined;
}, () => load());
watch(() => JSON.stringify([props.subscription.id, props.subscription.tenantId]), () => { scope.invalidate(); void load(); }, { flush: 'sync' });
watch(() => session.switching, switching => { scope.invalidate(); if (!switching) void load(); }, { flush: 'sync' });
const errorText = (error: unknown) => isFullNetProblemDetails(error) ? error.detail || error.title || t('webhooks.history.readFailed') : t('webhooks.history.readFailed');
const owned = (row: WebhookDeliveryHistory) => row.tenantId.toLowerCase() === props.subscription.tenantId.toLowerCase() && row.subscriptionId.toLowerCase() === props.subscription.id.toLowerCase();
const statuses: WebhookDeliveryStatusKey[] = ['pending','processing','delivered','failed'];
const statusLabel = (value: WebhookDeliveryStatusKey) => t(`webhooks.history.${value}`);
const time = (value: string | null) => value ? new Date(value).toLocaleString(locale.value, { timeZone: 'UTC' }) + ' UTC' : '—';
function failure(row: WebhookDeliveryHistory) {
  if (row.lastErrorCode === 'webhooks.delivery.http_error') return t('webhooks.history.httpError', { status: row.lastHttpStatus ?? '' });
  if (row.lastErrorCode === 'webhooks.delivery.timeout') return t('webhooks.history.timeout');
  return row.lastErrorCode ? t('webhooks.history.otherError') : '—';
}
function close() { scope.invalidate(); emit('close'); }
async function load(recover = true) {
  if (!canRead()) return;
  epoch.value++; listRequest?.cancel(); detailRequest?.cancel(); detail.value = undefined; detailTarget.value = ''; detailProblem.value = ''; detailLoading.value = false;
  const request = scope.begin(permission); if (!request) return; listRequest = request;
  const target = props.subscription.id, query = { page: page.value, pageSize: pageSize.value, statusKey: filter.value || undefined };
  if (!lastQuery || lastQuery.page !== query.page || lastQuery.pageSize !== query.pageSize || lastQuery.statusKey !== query.statusKey) { items.value = []; total.value = 0; }
  lastQuery = query;
  loading.value = true; problem.value = '';
  try {
    const result = await listWebhookDeliveryHistory(target, query.page, query.pageSize, query.statusKey, request.signal);
    if (!request.current() || !canRead()) return;
    if (result.page !== query.page || result.pageSize !== query.pageSize || result.items.some(row => !owned(row) || query.statusKey && row.statusKey !== query.statusKey)) throw Error('client.invalid_webhook_history_page_identity');
    const last = Math.max(1, Math.ceil(result.total / query.pageSize));
    if (recover && !result.items.length && query.page > last) { page.value = last; await load(false); return; }
    items.value = result.items; total.value = result.total;
  } catch (error) { if (request.current()) problem.value = errorText(error); }
  finally { if (request.current()) loading.value = false; request.finish(); }
}
async function openDetail(row: WebhookDeliveryHistory) {
  if (!canRead() || loading.value || !items.value.some(value => value.id === row.id && value.version === row.version && owned(value))) return;
  detailRequest?.cancel(); const request = scope.begin(permission); if (!request) return; detailRequest = request;
  detailTarget.value = row.id; detail.value = undefined; detailLoading.value = true; detailProblem.value = '';
  try {
    const result = await getWebhookDeliveryHistory(props.subscription.id, row.id, request.signal);
    if (!request.current() || !canRead()) return;
    if (result.id.toLowerCase() !== row.id.toLowerCase() || !owned(result) || result.eventId.toLowerCase() !== row.eventId.toLowerCase() || result.version < row.version) throw Error('client.invalid_webhook_history_detail_identity');
    detail.value = result; await nextTick(); if (request.current()) detailHeading.value?.focus();
  } catch (error) { if (request.current()) detailProblem.value = errorText(error); }
  finally { if (request.current()) detailLoading.value = false; request.finish(); }
}
const controls = computed(() => {
  const version = epoch.value, target = props.subscription.id, valid = () => epoch.value === version && props.subscription.id === target && canRead();
  return { close: () => { if (valid()) close(); }, refresh: () => { if (valid() && !loading.value) void load(); },
    page: (value: number) => { if (valid() && !loading.value && Number.isSafeInteger(value) && value > 0) page.value = value; },
    resize: (value: number) => { if (valid() && !loading.value && [10,20,50].includes(value)) { pageSize.value = value; page.value = 1; void load(); } },
    change: (nextPage: number, nextSize: number) => {
      if (!valid() || loading.value || !Number.isSafeInteger(nextPage) || nextPage < 1 || ![10,20,50].includes(nextSize) || (nextPage - 1) * nextSize > 2_147_483_647) return;
      page.value = lastQuery?.pageSize !== nextSize ? 1 : nextPage; pageSize.value = nextSize;
      if (lastQuery?.page !== page.value || lastQuery.pageSize !== nextSize) void load();
    },
    filter: (value: string) => { if (valid() && !loading.value && (value === '' || statuses.includes(value as WebhookDeliveryStatusKey))) { filter.value = value as WebhookDeliveryStatusKey | ''; page.value = 1; void load(); } },
    update: (open: boolean) => { if (!open && valid()) close(); } };
});
const rows = computed(() => { const version = epoch.value; return items.value.map(row => ({ ...row, show: () => { if (version === epoch.value) void openDetail(row); } })); });
</script>
<template>
  <ElDrawer :model-value="true" size="min(1000px, 100vw)" :title="t('webhooks.history.title')" v-on="{ 'update:modelValue': controls.update }">
    <section v-if="canRead()" class="webhook-history" data-testid="webhook-history">
      <p class="webhook-history__identity">{{ subscription.eventType }} · {{ subscription.targetUrl }}</p><p>{{ t('webhooks.history.snapshotHint') }}</p>
      <ElAlert v-if="problem" data-testid="webhook-history-problem" :closable="false" type="error" :title="problem" />
      <div class="webhook-history__query"><ElSelect :model-value="filter" :disabled="loading" :aria-label="t('webhooks.status')" data-testid="webhook-history-filter" v-on="{ 'update:modelValue': controls.filter }"><ElOption :label="t('webhooks.history.all')" value="" /><ElOption v-for="status in statuses" :key="status" :label="statusLabel(status)" :value="status" /></ElSelect><ElButton data-testid="webhook-history-refresh" :loading="loading" v-on="{ click: controls.refresh }">{{ t('common.refresh') }}</ElButton></div>
      <ElTable v-loading="loading" :data="rows" row-key="id" border>
        <ElTableColumn prop="eventId" :label="t('webhooks.history.eventId')" min-width="290" />
        <ElTableColumn :label="t('webhooks.status')" width="120"><template #default="{ row }"><ElTag :type="row.statusKey === 'delivered' ? 'success' : row.statusKey === 'failed' ? 'danger' : 'info'">{{ statusLabel(row.statusKey) }}</ElTag></template></ElTableColumn>
        <ElTableColumn prop="attemptCount" :label="t('webhooks.history.attempts')" width="130" />
        <ElTableColumn :label="t('webhooks.history.created')" min-width="210"><template #default="{ row }">{{ time(row.createdAtUtc) }}</template></ElTableColumn>
        <ElTableColumn :label="t('users.columnActions')" width="120" fixed="right"><template #default="{ row }"><ElButton data-testid="webhook-history-detail" :disabled="loading || detailLoading" v-on="{ click: row.show }">{{ t('webhooks.history.detail') }}</ElButton></template></ElTableColumn>
        <template #empty>{{ t('webhooks.history.empty') }}</template>
      </ElTable>
      <div class="webhook-history__pagination">
        <ElSelect :model-value="pageSize" :aria-label="t('webhooks.history.pageSize')" :disabled="loading" class="webhook-history__page-size" data-testid="webhook-history-page-size" v-on="{ 'update:modelValue': controls.resize }"><ElOption v-for="size in [10,20,50]" :key="size" :label="t('webhooks.history.pageSizeValue', { size })" :value="size" /></ElSelect>
        <ElPagination :current-page="page" :page-size="pageSize" :total="total" :disabled="loading" layout="total, prev, pager, next" class="webhook-history__pages" v-on="{ 'update:currentPage': controls.page, change: controls.change }" />
      </div>
      <section v-if="detailTarget" v-loading="detailLoading" data-testid="webhook-history-detail-panel">
        <h2 ref="detailHeading" tabindex="-1">{{ t('webhooks.history.detail') }}</h2><ElAlert v-if="detailProblem" :closable="false" type="error" :title="detailProblem" data-testid="webhook-history-detail-problem" />
        <ElDescriptions v-if="detail" :column="1" border>
          <ElDescriptionsItem :label="t('webhooks.history.deliveryId')">{{ detail.id }}</ElDescriptionsItem><ElDescriptionsItem :label="t('webhooks.history.eventId')">{{ detail.eventId }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('webhooks.status')">{{ statusLabel(detail.statusKey) }}</ElDescriptionsItem><ElDescriptionsItem :label="t('webhooks.history.attempts')">{{ detail.attemptCount }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('webhooks.history.failure')">{{ failure(detail) }}</ElDescriptionsItem><ElDescriptionsItem :label="t('webhooks.history.next')">{{ time(detail.nextAttemptAtUtc) }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('webhooks.history.created')">{{ time(detail.createdAtUtc) }}</ElDescriptionsItem><ElDescriptionsItem :label="t('webhooks.history.updated')">{{ time(detail.updatedAtUtc) }}</ElDescriptionsItem>
        </ElDescriptions>
      </section>
    </section>
  </ElDrawer>
</template>
<style scoped>
.webhook-history { display: flex; flex-direction: column; gap: 16px; }
.webhook-history__identity, .webhook-history :deep(.el-descriptions__content) { overflow-wrap: anywhere; }
.webhook-history__query { display: flex; flex-wrap: wrap; gap: 12px; }
.webhook-history__query :deep(.el-select) { width: min(220px, 100%); }
.webhook-history__pages { justify-content: center; flex-wrap: wrap; }
.webhook-history__pagination { display: flex; flex-wrap: wrap; gap: 12px; justify-content: center; align-items: center; }
.webhook-history__page-size { width: 140px; }
</style>
