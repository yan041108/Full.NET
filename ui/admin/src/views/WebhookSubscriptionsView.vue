<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import { ElAlert, ElButton, ElCard, ElForm, ElFormItem, ElInput, ElMessage, ElTable, ElTableColumn, ElTag } from 'element-plus';
import 'element-plus/theme-chalk/el-alert.css';
import { isFullNetProblemDetails, type WebhookSubscription } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listWebhookSubscriptions, createWebhookSubscription, setWebhookSubscriptionStatus } from '../api/webhook-subscriptions';

defineOptions({ name: 'WebhookSubscriptionsView' });
const session = useSessionStore(), { t } = useAdminI18n();
const read = 'webhooks.subscriptions.read', create = 'webhooks.subscriptions.manage', status = 'webhooks.subscriptions.set_status';
const tenant = () => !session.switching && !!session.currentUser?.tenantId && session.currentUser.scope !== 'host' && session.currentUser.actorScope !== 'host';
const canRead = () => tenant() && session.can(read), canCreate = () => canRead() && session.can(create), canStatus = () => canRead() && session.can(status);
const items = ref<WebhookSubscription[]>([]), loading = ref(false), changing = ref(false), problem = ref('');
const editorOpen = ref(false), confirmation = ref<{ task: WebhookSubscription; active: boolean; token: number }>();
const reviewOpen = ref(false), createBlocked = ref(false), createReviewed = ref(false), statusBlocks = ref(new Map<string, WebhookSubscription>());
const form = reactive({ eventType: 'fullnet.workflow.instance.completed', targetUrl: '', signingSecret: '' });
const receipt = ref<{ id?: string; event: string; target: string; unknown: boolean }>();
let confirmationSequence = 0, editorSequence = 0, listSequence = 0, reviewSequence = 0;
type Request = NonNullable<ReturnType<typeof scope.begin>>;
let action: { request: Request; kind: 'create' | 'status'; target: WebhookSubscription | { eventType: string; targetUrl: string }; dispatched: boolean } | undefined;
let listRequest: ReturnType<typeof scope.begin>;
const scope = useAuthorizedViewScope(session, () => {
  action = undefined; listRequest = undefined; items.value = []; loading.value = false; changing.value = false; problem.value = '';
  editorOpen.value = false; confirmation.value = undefined; reviewOpen.value = false; receipt.value = undefined;
  createBlocked.value = false; createReviewed.value = false; statusBlocks.value = new Map(); form.signingSecret = ''; form.targetUrl = '';
}, () => load());
watch(() => session.switching, switching => { scope.invalidate(); if (!switching) void load(); }, { flush: 'sync' });
const errorText = (error: unknown) => isFullNetProblemDetails(error) ? error.detail || error.title || t('webhooks.failed') : t('webhooks.failed');
const unknown = (error: unknown) => !isFullNetProblemDetails(error) || error.status >= 500 || error.status < 400 || error.status === 408;
const same = (a: WebhookSubscription, b: WebhookSubscription) => a.id.toLowerCase() === b.id.toLowerCase() && a.tenantId.toLowerCase() === b.tenantId.toLowerCase() && a.eventType === b.eventType && a.targetUrl === b.targetUrl;
const current = (row: WebhookSubscription) => items.value.find(value => same(value, row) && value.version === row.version && value.isActive === row.isActive);
function markUnknown() {
  if (!action?.dispatched) return;
  const target = action.target;
  receipt.value = { event: target.eventType, target: target.targetUrl, id: 'id' in target ? target.id : undefined, unknown: true };
  if (action.kind === 'create') { createBlocked.value = true; createReviewed.value = false; }
  else if ('id' in target) statusBlocks.value = new Map(statusBlocks.value).set(target.id.toLowerCase(), { ...target });
}
function closeAction() { if (action) { markUnknown(); action.request.cancel(); action = undefined; changing.value = false; } }
watch(editorOpen, open => { editorSequence++; if (!open) { if (action?.kind === 'create') closeAction(); form.signingSecret = ''; } }, { flush: 'sync' });
watch(reviewOpen, () => { reviewSequence++; }, { flush: 'sync' });
function finish(request: Request) { if (action?.request === request) { action = undefined; changing.value = false; } request.finish(); }
async function load(manual = false) {
  if (!canRead() || changing.value) return;
  listRequest?.cancel(); const request = scope.begin(read); if (!request) return; listRequest = request;
  loading.value = true; problem.value = ''; const sequence = ++listSequence;
  try {
    const result = await listWebhookSubscriptions(request.signal); if (!request.current()) return;
    if (result.some(row => row.tenantId.toLowerCase() !== session.currentUser!.tenantId!.toLowerCase())) throw Error('client.invalid_webhook_tenant');
    if (result.some(row => items.value.some(existing => existing.id.toLowerCase() === row.id.toLowerCase() && !same(existing, row)))) throw Error('client.invalid_webhook_subscription_identity');
    // 已确认修订是本页的事实下限；迟到或陈旧读取不能恢复暂停前状态。
    items.value = result.map(row => { const existing = items.value.find(value => same(value, row)); return existing && existing.version > row.version ? existing : row; });
    // 只有人工成功读取原来源且版本不回退，才允许再次明确确认状态；回执仍保留未知事实。
    if (manual) {
      statusBlocks.value = new Map([...statusBlocks.value].filter(([, baseline]) => !result.some(row => same(row, baseline) && row.version >= baseline.version)));
      createReviewed.value = createBlocked.value;
    }
    if (confirmation.value && !current(confirmation.value.task)) confirmation.value = undefined;
  } catch (error) { if (request.current()) problem.value = errorText(error); }
  finally { if (request.current() && sequence === listSequence) loading.value = false; request.finish(); }
}
function openCreate() { if (!canCreate() || changing.value || loading.value) return; confirmation.value = undefined; form.targetUrl = ''; form.signingSecret = ''; form.eventType = 'fullnet.workflow.instance.completed'; editorOpen.value = true; }
async function submitCreate() {
  if (!canCreate() || !editorOpen.value || changing.value || createBlocked.value) return;
  const snapshot = { eventType: form.eventType.trim(), targetUrl: form.targetUrl.trim(), signingSecret: form.signingSecret };
  try { if (!/^[\x21-\x7E]{1,128}$/u.test(snapshot.eventType) || new URL(snapshot.targetUrl).protocol !== 'https:' || snapshot.signingSecret.length < 16 || snapshot.signingSecret.length > 128) throw Error(); }
  catch { problem.value = t('webhooks.inputInvalid'); return; }
  const request = scope.begin(create); if (!request) return;
  action = { request, kind: 'create', target: { eventType: snapshot.eventType, targetUrl: snapshot.targetUrl }, dispatched: true };
  changing.value = true; problem.value = ''; listRequest?.cancel(); loading.value = false;
  try {
    const result = await createWebhookSubscription(snapshot, request.signal); if (!request.current()) return;
    if (result.tenantId.toLowerCase() !== session.currentUser!.tenantId!.toLowerCase() || items.value.some(row => row.id.toLowerCase() === result.id.toLowerCase())) throw Error('client.invalid_created_webhook');
    action!.dispatched = false; receipt.value = { id: result.id, event: result.eventType, target: result.targetUrl, unknown: false };
    items.value = [...items.value, result]; finish(request); editorOpen.value = false; ElMessage.success(t('webhooks.saved')); await load();
  } catch (error) { if (request.current()) { if (unknown(error)) markUnknown(); problem.value = errorText(error); } }
  finally { finish(request); }
}
function openStatus(row: WebhookSubscription) {
  if (!canStatus() || changing.value || loading.value || editorOpen.value || statusBlocks.value.has(row.id.toLowerCase()) || !current(row)) return;
  confirmation.value = { task: { ...row }, active: !row.isActive, token: ++confirmationSequence };
}
async function submitStatus(token: number) {
  const confirmed = confirmation.value;
  if (!confirmed || confirmed.token !== token || !canStatus() || changing.value || loading.value || !current(confirmed.task)) return;
  const request = scope.begin(status); if (!request) return;
  const target = { ...confirmed.task }; action = { request, kind: 'status', target, dispatched: true }; changing.value = true; problem.value = '';
  try {
    const result = await setWebhookSubscriptionStatus(target.id, { isActive: confirmed.active, version: target.version }, request.signal);
    if (!request.current()) return;
    if (!same(result, target) || result.isActive !== confirmed.active || result.version <= target.version) throw Error('client.invalid_webhook_status_result');
    action!.dispatched = false; receipt.value = { id: result.id, event: result.eventType, target: result.targetUrl, unknown: false };
    items.value = items.value.map(row => same(row, target) ? result : row); finish(request); confirmation.value = undefined; ElMessage.success(t('webhooks.statusSaved')); await load();
  } catch (error) { if (request.current()) { if (unknown(error)) markUnknown(); problem.value = errorText(error); } }
  finally { finish(request); }
}
const rows = computed(() => items.value.map(row => { const task = { ...row }; return { ...task, statusAction: () => openStatus(task) }; }));
const confirmationControls = computed(() => {
  const token = confirmation.value?.token;
  return { confirm: () => { if (token !== undefined) void submitStatus(token); }, update: (open: boolean) => { if (!open && confirmation.value?.token === token) { if (action?.kind === 'status') closeAction(); confirmation.value = undefined; } } };
});
const editorControls = computed(() => {
  const sequence = editorSequence, open = editorOpen.value, owned = () => open && editorSequence === sequence && editorOpen.value && canCreate();
  return { confirm: () => { if (owned()) void submitCreate(); }, update: (open: boolean) => { if (!open && owned()) editorOpen.value = false; },
    event: (value: string) => { if (owned() && !changing.value) form.eventType = value; }, target: (value: string) => { if (owned() && !changing.value) form.targetUrl = value; }, secret: (value: string) => { if (owned() && !changing.value) form.signingSecret = value; } };
});
const reviewControls = computed(() => {
  const sequence = listSequence, owner = reviewSequence, originalReceipt = receipt.value, tenantId = session.currentUser?.tenantId, open = reviewOpen.value;
  const owned = () => open && reviewOpen.value && owner === reviewSequence && receipt.value === originalReceipt && canCreate() && listSequence === sequence && session.currentUser?.tenantId === tenantId;
  return { confirm: () => { if (owned() && createBlocked.value && createReviewed.value && !loading.value && !changing.value) { createBlocked.value = false; reviewOpen.value = false; } }, update: (open: boolean) => { if (!open && owned()) reviewOpen.value = false; } };
});
</script>
<template>
  <section class="art-page-stack webhook-subscriptions-view">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('webhooks.title') }}</h1>
    <ElAlert v-if="!tenant()" :closable="false" type="info" :title="t('webhooks.tenantRequired')" />
    <template v-if="canRead()">
      <ElAlert v-if="problem" data-testid="webhook-problem" :closable="false" type="error" :title="problem" />
      <ElAlert v-if="receipt?.unknown" data-testid="webhook-unconfirmed" :closable="false" type="warning" :title="t('webhooks.unconfirmed')" />
      <div v-if="receipt" data-testid="webhook-receipt" role="status"><strong>{{ t(receipt.unknown ? 'webhooks.unknownReceipt' : 'webhooks.successReceipt') }}</strong> · {{ receipt.event }} · {{ receipt.target }} <code v-if="receipt.id">{{ receipt.id }}</code></div>
      <ElCard shadow="never">
        <template #header><div class="webhook-subscriptions-view__header"><span>{{ t('webhooks.title') }}</span><div><ElButton data-testid="webhook-refresh" :loading="loading" :disabled="changing" @click="load(true)">{{ t('common.refresh') }}</ElButton><ElButton v-if="canCreate()" data-testid="webhook-create" type="primary" :disabled="changing || loading" @click="openCreate">{{ t('webhooks.create') }}</ElButton></div></div></template>
        <p>{{ t('webhooks.statusHint') }}</p>
        <ElAlert v-if="createBlocked" :closable="false" type="warning" :title="t('webhooks.createBlocked')" />
        <ElButton v-if="createBlocked && canCreate()" data-testid="webhook-review-create" :disabled="!createReviewed || loading || changing" @click="reviewOpen = true">{{ t('webhooks.reviewCreate') }}</ElButton>
        <ElTable v-loading="loading" :data="rows" row-key="id" border>
          <ElTableColumn prop="eventType" :label="t('webhooks.event')" min-width="270" />
          <ElTableColumn prop="targetUrl" :label="t('webhooks.target')" min-width="280" />
          <ElTableColumn :label="t('webhooks.status')" width="110"><template #default="{ row }"><ElTag :type="row.isActive ? 'success' : 'info'">{{ t(row.isActive ? 'webhooks.active' : 'webhooks.paused') }}</ElTag></template></ElTableColumn>
          <ElTableColumn v-if="canStatus()" :label="t('users.columnActions')" width="150" fixed="right"><template #default="{ row }"><ElButton data-testid="webhook-status" :disabled="changing || loading || editorOpen || statusBlocks.has(row.id.toLowerCase())" @click="row.statusAction">{{ t(row.isActive ? 'webhooks.pause' : 'webhooks.resume') }}</ElButton></template></ElTableColumn>
          <template #empty>{{ t('webhooks.empty') }}</template>
        </ElTable>
      </ElCard>
      <ArtFormDialog :open="editorOpen" width="min(620px, 94vw)" :title="t('webhooks.create')" :saving="changing" :show-confirm="canCreate() && !createBlocked" :confirm-label="t('common.confirm')" :cancel-label="t('common.cancel')" confirm-test-id="webhook-create-submit" v-on="{ confirm: editorControls.confirm, 'update:open': editorControls.update }">
        <ElAlert v-if="problem" :closable="false" type="error" :title="problem" />
        <ElForm label-position="top">
          <ElFormItem :label="t('webhooks.event')" required><ElInput :model-value="form.eventType" data-testid="webhook-event" :disabled="changing" maxlength="128" v-on="{ 'update:modelValue': editorControls.event }" /></ElFormItem>
          <ElFormItem :label="t('webhooks.target')" required><ElInput :model-value="form.targetUrl" data-testid="webhook-target" :disabled="changing" maxlength="2048" autocomplete="off" v-on="{ 'update:modelValue': editorControls.target }" /></ElFormItem>
          <ElFormItem :label="t('webhooks.secret')" required><ElInput :model-value="form.signingSecret" data-testid="webhook-secret" type="password" :disabled="changing" maxlength="128" autocomplete="new-password" v-on="{ 'update:modelValue': editorControls.secret }" /></ElFormItem>
        </ElForm><p>{{ t('webhooks.secretHint') }}</p>
      </ArtFormDialog>
      <ArtFormDialog v-if="confirmation" :key="confirmation.token" :open="true" width="min(560px, 94vw)" :title="t(confirmation.active ? 'webhooks.resume' : 'webhooks.pause')" :saving="changing" :confirm-label="t('common.confirm')" :cancel-label="t('common.cancel')" confirm-test-id="webhook-status-confirm" v-on="{ confirm: confirmationControls.confirm, 'update:open': confirmationControls.update }"><p>{{ confirmation.task.eventType }}</p><p class="webhook-subscriptions-view__target">{{ confirmation.task.targetUrl }}</p><p>{{ t('webhooks.statusHint') }}</p></ArtFormDialog>
      <ArtFormDialog v-if="reviewOpen" :key="reviewSequence" :open="true" width="min(520px, 94vw)" :title="t('webhooks.reviewCreate')" :confirm-label="t('common.confirm')" :cancel-label="t('common.cancel')" confirm-test-id="webhook-review-confirm" v-on="{ 'update:open': reviewControls.update, confirm: reviewControls.confirm }"><p>{{ t('webhooks.reviewConfirm') }}</p></ArtFormDialog>
    </template>
  </section>
</template>
<style scoped>
.webhook-subscriptions-view__header { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; }
.webhook-subscriptions-view__target, [data-testid="webhook-receipt"] { overflow-wrap: anywhere; }
</style>
