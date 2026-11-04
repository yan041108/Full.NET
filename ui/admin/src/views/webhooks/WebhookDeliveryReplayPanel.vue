<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDialog } from 'element-plus';
import type { WebhookSubscription, WebhookDeliveryHistory, WebhookDeliveryReplayReceipt } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../../auth/session';
import { useAdminI18n } from '../../i18n/adminI18n';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { replayWebhookDelivery, getWebhookDeliveryReplayReceipt } from '../../api/webhook-subscriptions';

const props = defineProps<{ subscription: WebhookSubscription; delivery: WebhookDeliveryHistory }>();
const emit = defineEmits<{ queued: [receipt: WebhookDeliveryReplayReceipt] }>();
const session = useSessionStore(), { t } = useAdminI18n();
const read = 'webhooks.deliveries.read', write = 'webhooks.deliveries.replay';
const canRead = () => !session.switching && session.currentUser?.tenantId?.toLowerCase() === props.delivery.tenantId.toLowerCase() && props.subscription.tenantId.toLowerCase() === props.delivery.tenantId.toLowerCase() && props.subscription.id.toLowerCase() === props.delivery.subscriptionId.toLowerCase() && session.can(read) && session.can('webhooks.subscriptions.read');
const eligible = () => canRead() && session.can(write) && props.subscription.isActive && props.delivery.statusKey === 'failed' && props.delivery.version > 0 && props.delivery.version <= 2_147_483_644;
type Operation = { operationId: string; version: number; tenantId: string; subscriptionId: string; deliveryId: string; eventId: string; attemptCount: number };
const operation = ref<Operation>(), receipt = ref<WebhookDeliveryReplayReceipt>(), confirmation = ref(false), busy = ref(false), unknown = ref(false), problem = ref(''), epoch = ref(0);
const identity = () => JSON.stringify([session.currentUser?.id,session.currentUser?.sessionId,session.currentUser?.tenantId,props.delivery.id,props.delivery.subscriptionId]);
let owner = identity(), confirmedVersion = 0, pending: ReturnType<typeof scope.begin>;
// 同一主体撤权时保留原操作供只读核对；换主体、资源或开始切换时彻底丢弃旧接入。
const reset = () => { epoch.value++; confirmation.value = false; busy.value = false; problem.value = ''; pending = undefined;
  if (identity() !== owner || session.switching) { operation.value = undefined; receipt.value = undefined; unknown.value = false; }
  else if (operation.value && !receipt.value) unknown.value = true;
  owner = identity();
};
const scope = useAuthorizedViewScope(session, reset, () => {});
watch(identity, () => scope.invalidate(), { flush: 'sync' });
watch(() => session.switching, () => scope.invalidate(), { flush: 'sync' });
watch(() => JSON.stringify([props.delivery.version,props.delivery.statusKey,props.subscription.isActive]), () => { epoch.value++; confirmation.value = false; }, { flush: 'sync' });
function close() { pending?.cancel(); if (busy.value && operation.value && !receipt.value) unknown.value = true; busy.value = false; confirmation.value = false; epoch.value++; }
function accept(value: WebhookDeliveryReplayReceipt, target: Operation) {
  if (value.tenantId.toLowerCase() !== target.tenantId.toLowerCase() || value.eventId.toLowerCase() !== target.eventId.toLowerCase() || value.operationId.toLowerCase() !== target.operationId.toLowerCase()
    || value.subscriptionId.toLowerCase() !== target.subscriptionId.toLowerCase() || value.deliveryId.toLowerCase() !== target.deliveryId.toLowerCase() || value.previousVersion !== target.version || value.attemptCount !== target.attemptCount) throw Error('client.invalid_webhook_replay_identity');
  receipt.value = value; unknown.value = false; confirmation.value = false; emit('queued', value);
}
async function submit() {
  if (!confirmation.value || busy.value || !eligible() || props.delivery.version !== confirmedVersion || receipt.value) return;
  const request = scope.begin(write); if (!request) return; pending = request;
  operation.value ??= { operationId: crypto.randomUUID(), version: confirmedVersion, tenantId: props.delivery.tenantId, subscriptionId: props.delivery.subscriptionId, deliveryId: props.delivery.id, eventId: props.delivery.eventId, attemptCount: props.delivery.attemptCount };
  const target = { ...operation.value }; if (target.version !== confirmedVersion) { request.finish(); return; }
  busy.value = true; problem.value = ''; epoch.value++;
  try { const value = await replayWebhookDelivery(target.subscriptionId, target.deliveryId, { operationId: target.operationId, version: target.version }, request.signal); if (request.current() && canRead()) accept(value, target); }
  catch (error) { if (request.current() && canRead()) { problem.value = isFullNetProblemDetails(error) ? error.detail || error.title || t('webhooks.replay.failed') : t('webhooks.replay.failed');
    if (isFullNetProblemDetails(error) && [400,403,404].includes(error.status ?? 0)) { operation.value = undefined; unknown.value = false; } else unknown.value = true;
  } }
  finally { if (request.current()) busy.value = false; request.finish(); }
}
async function readReceipt() {
  if (!operation.value || busy.value || !canRead()) return; const request = scope.begin(read); if (!request) return; pending = request;
  const target = { ...operation.value }; busy.value = true; problem.value = ''; epoch.value++;
  try { const value = await getWebhookDeliveryReplayReceipt(target.subscriptionId,target.deliveryId,target.operationId,request.signal); if (request.current() && canRead()) accept(value,target); }
  catch (error) { if (request.current() && canRead()) problem.value = isFullNetProblemDetails(error) ? error.detail || error.title || t('webhooks.replay.readFailed') : t('webhooks.replay.readFailed'); }
  finally { if (request.current()) busy.value = false; request.finish(); }
}
const controls = computed(() => { const generation = epoch.value, version = props.delivery.version, resource = props.delivery.id;
  const owned = () => generation === epoch.value && resource === props.delivery.id && canRead();
  return { open: () => { if (owned() && !busy.value && eligible() && !receipt.value && (!operation.value || operation.value.version === version)) { confirmedVersion = version; confirmation.value = true; epoch.value++; } },
    next: () => { if (owned() && !busy.value && eligible() && receipt.value && version > receipt.value.version) { operation.value = undefined; receipt.value = undefined; unknown.value = false; problem.value = ''; confirmedVersion = version; confirmation.value = true; epoch.value++; } },
    confirm: () => { if (owned()) void submit(); }, close: () => { if (owned()) close(); }, update: (open: boolean) => { if (!open && owned()) close(); },
    read: () => { if (owned()) void readReceipt(); } };
});
</script>
<template>
  <section v-if="canRead()" data-testid="webhook-replay" class="webhook-replay">
    <ElButton v-if="eligible() && !operation && !receipt" data-testid="webhook-replay-open" v-on="{ click: controls.open }">{{ t('webhooks.replay.open') }}</ElButton>
    <ElAlert v-if="receipt" data-testid="webhook-replay-receipt" type="success" :closable="false" :title="t('webhooks.replay.queued')"><p>{{ receipt.operationId }}</p><p>{{ t('webhooks.replay.receiptHint') }}</p></ElAlert>
    <ElButton v-if="receipt && eligible() && delivery.version > receipt.version" data-testid="webhook-replay-next" v-on="{ click: controls.next }">{{ t('webhooks.replay.next') }}</ElButton>
    <ElAlert v-if="unknown" data-testid="webhook-replay-unknown" type="warning" :closable="false" :title="t('webhooks.replay.unknown')"><p>{{ operation?.operationId }}</p></ElAlert>
    <ElAlert v-if="problem" data-testid="webhook-replay-problem" type="error" :closable="false" :title="problem" />
    <div v-if="operation && !receipt && !confirmation" class="webhook-replay__actions">
      <ElButton data-testid="webhook-replay-read" :disabled="busy" v-on="{ click: controls.read }">{{ t('webhooks.replay.read') }}</ElButton>
      <ElButton v-if="eligible() && operation.version === delivery.version" data-testid="webhook-replay-retry" :disabled="busy" v-on="{ click: controls.open }">{{ t('webhooks.replay.retryOriginal') }}</ElButton>
    </div>
    <ElDialog :model-value="confirmation" :title="t('webhooks.replay.confirmTitle')" width="min(540px, calc(100vw - 24px))" :close-on-click-modal="false" v-on="{ 'update:modelValue': controls.update }">
      <p>{{ t('webhooks.replay.confirmHint') }}</p><p>{{ subscription.eventType }} · {{ subscription.targetUrl }}</p><p>{{ delivery.id }}</p><p>{{ delivery.eventId }}</p>
      <p>{{ t('webhooks.replay.version', { version: confirmedVersion }) }}</p>
      <template #footer><ElButton data-testid="webhook-replay-cancel" v-on="{ click: controls.close }">{{ t('common.cancel') }}</ElButton><ElButton data-testid="webhook-replay-confirm" type="primary" :disabled="!eligible() || delivery.version !== confirmedVersion" :loading="busy" v-on="{ click: controls.confirm }">{{ t('webhooks.replay.open') }}</ElButton></template>
    </ElDialog>
  </section>
</template>
<style scoped>
.webhook-replay { display: flex; flex-direction: column; gap: 12px; overflow-wrap: anywhere; }
.webhook-replay__actions { display: flex; flex-wrap: wrap; gap: 8px; }
.webhook-replay :deep(.el-alert--success), .webhook-replay :deep(.el-alert--success .el-alert__description) { color: #276518; }
.webhook-replay :deep(.el-alert--warning), .webhook-replay :deep(.el-alert--warning .el-alert__description) { color: #795207; }
.webhook-replay :deep(.el-alert--error), .webhook-replay :deep(.el-alert--error .el-alert__description) { color: #a32121; }
</style>
