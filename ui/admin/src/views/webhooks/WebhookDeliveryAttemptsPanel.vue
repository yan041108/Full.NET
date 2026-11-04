<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue';
import { ElAlert, ElButton, ElCard, ElDescriptions, ElDescriptionsItem, ElPagination, ElTag } from 'element-plus';
import type { WebhookDeliveryHistory, WebhookDeliveryAttempt } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../../auth/session';
import { useAdminI18n } from '../../i18n/adminI18n';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { listWebhookDeliveryAttempts } from '../../api/webhook-subscriptions';
const props = defineProps<{ delivery: WebhookDeliveryHistory }>();
const emit = defineEmits<{ close: [] }>();
const session = useSessionStore(), { t, locale } = useAdminI18n();
const permission = 'webhooks.deliveries.read';
const canRead = () => !session.switching && session.currentUser?.tenantId?.toLowerCase() === props.delivery.tenantId.toLowerCase() && session.can(permission) && session.can('webhooks.subscriptions.read');
const items = ref<WebhookDeliveryAttempt[]>([]), loading = ref(false), page = ref(1), total = ref(0), problem = ref(''), epoch = ref(0), heading = ref<HTMLElement>();
let request: ReturnType<typeof scope.begin>;
const scope = useAuthorizedViewScope(session, () => { epoch.value++; request = undefined; items.value = []; page.value = 1; total.value = 0; problem.value = ''; loading.value = false; }, () => load());
watch(() => JSON.stringify([props.delivery.id,props.delivery.subscriptionId,props.delivery.tenantId]), () => { scope.invalidate(); void load(); }, { flush: 'sync' });
watch(() => session.switching, switching => { scope.invalidate(); if (!switching) void load(); }, { flush: 'sync' });
const time = (value: string | null) => value ? new Date(value).toLocaleString(locale.value, { timeZone: 'UTC' }) + ' UTC' : '—';
const label = (value: WebhookDeliveryAttempt['outcomeKey']) => t(`webhooks.attempts.${value}`);
function failure(row: WebhookDeliveryAttempt) {
  if (row.errorCode === 'webhooks.delivery.http_error') return t('webhooks.history.httpError', { status: row.httpStatus ?? '' });
  return row.errorCode === 'webhooks.delivery.timeout' ? t('webhooks.history.timeout') : row.errorCode ? t('webhooks.history.otherError') : '—';
}
async function load(recover = true) {
  if (!canRead()) return;
  request?.cancel(); const pending = scope.begin(permission); if (!pending) return; request = pending; epoch.value++;
  const target = { ...props.delivery }, requestedPage = page.value; loading.value = true; problem.value = '';
  try {
    const result = await listWebhookDeliveryAttempts(target.subscriptionId, target.id, requestedPage, 20, pending.signal);
    if (!pending.current() || !canRead()) return;
    if (result.page !== requestedPage || result.pageSize !== 20 || result.items.some(row => row.tenantId.toLowerCase() !== target.tenantId.toLowerCase() || row.subscriptionId.toLowerCase() !== target.subscriptionId.toLowerCase() || row.deliveryId.toLowerCase() !== target.id.toLowerCase())) throw Error('client.invalid_webhook_attempt_identity');
    const last = Math.max(1, Math.ceil(result.total / 20)); if (recover && !result.items.length && requestedPage > last) { page.value = last; await load(false); return; }
    items.value = result.items; total.value = result.total; await nextTick(); if (pending.current()) heading.value?.focus();
  } catch (error) { if (pending.current()) problem.value = isFullNetProblemDetails(error) ? error.detail || error.title || t('webhooks.attempts.readFailed') : t('webhooks.attempts.readFailed'); }
  finally { if (pending.current()) loading.value = false; pending.finish(); }
}
const controls = computed(() => { const owner = epoch.value, target = props.delivery.id, owned = () => epoch.value === owner && props.delivery.id === target && canRead();
  return { refresh: () => { if (owned() && !loading.value) void load(); }, close: () => { if (owned()) { scope.invalidate(); emit('close'); } },
    page: (value: number) => { if (owned() && !loading.value && Number.isSafeInteger(value) && value > 0 && (value - 1) * 20 <= 2_147_483_647) { page.value = value; items.value = []; void load(); } } };
});
</script>
<template>
  <section v-if="canRead()" data-testid="webhook-attempts" class="webhook-attempts">
    <div class="webhook-attempts__header"><h3 ref="heading" tabindex="-1">{{ t('webhooks.attempts.title') }}</h3><div><ElButton data-testid="webhook-attempts-refresh" :loading="loading" v-on="{ click: controls.refresh }">{{ t('common.refresh') }}</ElButton><ElButton data-testid="webhook-attempts-close" v-on="{ click: controls.close }">{{ t('common.cancel') }}</ElButton></div></div>
    <div v-loading="loading" class="webhook-attempts__body">
    <p>{{ t('webhooks.attempts.hint') }}</p><ElAlert v-if="problem" data-testid="webhook-attempts-problem" :closable="false" type="error" :title="problem" />
    <p v-if="!loading && !problem && !items.length">{{ t('webhooks.attempts.empty') }}</p>
    <ul class="webhook-attempts__list"><li v-for="row in items" :key="row.id"><ElCard shadow="never">
      <p><ElTag :type="row.outcomeKey === 'delivered' ? 'success' : row.outcomeKey === 'failed' ? 'danger' : 'info'">{{ label(row.outcomeKey) }}</ElTag> <code>{{ row.id }}</code></p>
      <ElDescriptions :column="1" border><ElDescriptionsItem :label="t('webhooks.attempts.started')">{{ time(row.startedAtUtc) }}</ElDescriptionsItem><ElDescriptionsItem :label="t('webhooks.attempts.completed')">{{ time(row.completedAtUtc) }}</ElDescriptionsItem><ElDescriptionsItem :label="t('webhooks.attempts.http')">{{ row.httpStatus ?? '—' }}</ElDescriptionsItem><ElDescriptionsItem :label="t('webhooks.history.failure')">{{ failure(row) }}</ElDescriptionsItem></ElDescriptions>
      <p v-if="row.appliedToDelivery !== null">{{ t(row.appliedToDelivery ? 'webhooks.attempts.applied' : 'webhooks.attempts.notApplied') }}</p>
    </ElCard></li></ul>
    <ElPagination :current-page="page" :page-size="20" :total="total" :disabled="loading" layout="total, prev, pager, next" v-on="{ 'update:currentPage': controls.page }" />
    </div>
  </section>
</template>
<style scoped>
.webhook-attempts { overflow-wrap: anywhere; }
.webhook-attempts__body { min-height: 100px; }
.webhook-attempts__header { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center; gap: 12px; }
.webhook-attempts__list { list-style: none; padding: 0; display: flex; flex-direction: column; gap: 12px; }
.webhook-attempts :deep(.el-tag--info) { color: #4b5563; }
.webhook-attempts :deep(.el-tag--success) { color: #276518; }
.webhook-attempts :deep(.el-tag--danger) { color: #a32121; }
.webhook-attempts :deep(.el-pagination) { justify-content: center; flex-wrap: wrap; }
</style>
