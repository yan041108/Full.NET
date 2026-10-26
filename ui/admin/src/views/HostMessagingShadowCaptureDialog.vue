<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { ElButton, ElDescriptions, ElDescriptionsItem, ElForm, ElFormItem, ElInput, ElInputNumber, ElMessageBox } from 'element-plus';
import 'element-plus/theme-chalk/el-descriptions.css';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { usePermission } from '../auth/permission';
import { useAdminI18n } from '../i18n/adminI18n';
import { showError, showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { getMessagingShadowCapture, startMessagingShadowCapture, closeMessagingShadowCapture,
  type MessagingDeliveryStream, type MessagingShadowCapturePolicy, type MessagingShadowCapture, type MessagingShadowCaptureStart } from '../api/messaging-delivery';

const props = defineProps<{ stream: MessagingDeliveryStream; policy: MessagingShadowCapturePolicy }>();
const emit = defineEmits<{ close: []; changed: [] }>();
const { can } = usePermission();
const { t } = useAdminI18n();
const open = ref(true);
const alive = ref(true);
const loading = ref(false);
const saving = ref(false);
const loaded = ref(false);
const current = ref<MessagingShadowCapture>();
const reason = ref('');
const maxEvents = ref(Math.min(100, props.policy.maximumEvents ?? 1));
const seconds = ref(Math.min(60, props.policy.maximumWindowSeconds ?? 1));
const pending = ref<MessagingShadowCaptureStart>();
let timer: ReturnType<typeof setTimeout> | undefined;
let controller: AbortController | undefined;
let sequence = 0;
const mayRead = computed(() => alive.value && open.value && can('messaging.operations.read') && can('messaging.events.read'));
const mayWrite = computed(() => mayRead.value && can('messaging.delivery.cutover'));
const matches = computed(() => props.policy.enabled && props.policy.eventType === props.stream.eventType && props.policy.schemaVersion === props.stream.schemaVersion);
const mayStart = computed(() => mayWrite.value && loaded.value && matches.value && props.stream.deliveryOwner === 0
  && !props.stream.blockingReasons.includes('ownership_record_required') && current.value?.state !== 'active');
const endings = { message_budget: 'messagingOps.capture.messageBudget', byte_budget: 'messagingOps.capture.byteBudget', time_budget: 'messagingOps.capture.timeBudget', operator_closed: 'messagingOps.capture.operatorClosed', metadata_missing: 'messagingOps.capture.metadataMissing' } as const;
const endReason = (value: string | null) => value && value in endings ? t(endings[value as keyof typeof endings]) : value ?? '—';
const states = { active: 'messagingOps.capture.active', closed: 'messagingOps.capture.closed', blocked: 'messagingOps.capture.blocked', expired: 'messagingOps.capture.expired' } as const;
function fail(error: unknown): void {
  if (!mayRead.value) return;
  if (isFullNetProblemDetails(error)) showProblem(error, t('messagingOps.requestFailed')); else showError(t('messagingOps.requestFailed'));
}
function stop(): void { clearTimeout(timer); controller?.abort(); sequence++; }
watch(mayRead, value => { if (!value) { stop(); current.value = undefined; } });
watch(open, value => { if (!value) { stop(); emit('close'); } });
onUnmounted(() => { alive.value = false; stop(); });
onMounted(() => void load());
async function load(): Promise<void> {
  if (!mayRead.value || saving.value) return;
  stop(); const own = sequence; controller = new AbortController(); loading.value = true;
  try {
    const result = await getMessagingShadowCapture(props.stream, controller.signal);
    if (own === sequence && mayRead.value) { current.value = result; loaded.value = true; }
  } catch (error) {
    if (own !== sequence || !mayRead.value) return;
    if (isFullNetProblemDetails(error) && error.status === 404 && error.code === 'messaging.shadow_capture.not_found') {
      current.value = undefined; loaded.value = true;
    } else { loaded.value = false; fail(error); }
  } finally {
    if (own === sequence) { loading.value = false;
      if (mayRead.value && current.value?.state === 'active') timer = setTimeout(() => void load(), 3000);
    }
  }
}
async function change(kind: 'start' | 'close'): Promise<void> {
  if (saving.value || !mayWrite.value || kind === 'start' && !mayStart.value || kind === 'close' && !['active', 'expired'].includes(current.value?.state ?? '')) return;
  if (!reason.value.trim() || reason.value.trim().length > 512) { showWarning(t('messagingOps.reasonRequired')); return; }
  saving.value = true; stop();
  try {
    await ElMessageBox.confirm(t('messagingOps.capture.confirm') + ' ' + props.stream.eventType, t('messagingOps.capture.title'),
      { type: 'warning', confirmButtonText: t('messagingOps.confirm'), cancelButtonText: t('messagingOps.cancel') });
    if (!mayWrite.value || kind === 'start' && !mayStart.value) return;
    if (kind === 'start') {
      const body = { requestId: pending.value?.requestId ?? crypto.randomUUID(), expectedGeneration: current.value?.generation ?? null,
        eventType: props.stream.eventType, schemaVersion: props.stream.schemaVersion, maxEvents: maxEvents.value,
        windowSeconds: seconds.value, reason: reason.value.trim() };
      // 同一请求重试保留完整意图；更改输入才创建新键，旧代次不能覆盖新的采集窗口。
      if (pending.value && JSON.stringify({ ...pending.value, requestId: body.requestId }) !== JSON.stringify(body)) body.requestId = crypto.randomUUID();
      pending.value = body;
      const result = await startMessagingShadowCapture(body);
      if (mayRead.value) current.value = result;
      pending.value = undefined;
    } else if (current.value) {
      const result = await closeMessagingShadowCapture(current.value.generation, reason.value);
      if (mayRead.value) current.value = result;
    }
    if (mayRead.value) { showSuccess(t('messagingOps.operationSaved')); emit('changed'); }
  } catch (error) { if (error !== 'cancel' && error !== 'close') fail(error); }
  finally { saving.value = false; if (mayRead.value) void load(); }
}
</script>

<template>
  <ArtFormDialog v-model:open="open" :title="t('messagingOps.capture.title')" :show-confirm="false" :saving="saving" :cancel-label="t('messagingOps.close')" width="660px">
    <p translate="no">{{ stream.eventType }} / {{ stream.schemaVersion }}</p>
    <p>{{ t('messagingOps.capture.hint') }}</p>
    <p v-if="!matches" class="art-inline-alert">{{ t('messagingOps.capture.disabled') }}</p>
    <ElButton :loading="loading" :disabled="saving" @click="load">{{ t('messagingOps.refresh') }}</ElButton>
    <p v-if="loaded && !current">{{ t('messagingOps.capture.none') }}</p>
    <ElDescriptions v-if="current" :column="1" border>
      <ElDescriptionsItem :label="t('messagingOps.state')">{{ t(states[current.state]) }}</ElDescriptionsItem>
      <ElDescriptionsItem :label="t('messagingOps.capture.generation')"><span translate="no">{{ current.generation }}</span></ElDescriptionsItem>
      <ElDescriptionsItem :label="t('messagingOps.samples')">{{ current.capturedCount }} / {{ current.maxEvents }}</ElDescriptionsItem>
      <ElDescriptionsItem :label="t('messagingOps.capture.bytes')">{{ current.capturedBytes }} / {{ current.maxPayloadBytes }}</ElDescriptionsItem>
      <ElDescriptionsItem :label="t('messagingOps.capture.gaps')">{{ current.missingMetadataCount }}</ElDescriptionsItem>
      <ElDescriptionsItem :label="t('messagingOps.expiresAt')">{{ current.expiresAtUtc }}</ElDescriptionsItem>
      <ElDescriptionsItem :label="t('messagingOps.capture.endReason')"><span :title="current.endReason ?? undefined">{{ endReason(current.endReason) }}</span></ElDescriptionsItem>
    </ElDescriptions>
    <ElForm v-if="mayWrite && (mayStart || current?.state === 'active' || current?.state === 'expired')" @submit.prevent>
      <template v-if="mayStart">
        <ElFormItem :label="t('messagingOps.maxMessages')"><ElInputNumber v-model="maxEvents" :min="1" :max="policy.maximumEvents ?? 1" :disabled="saving" /></ElFormItem>
        <ElFormItem :label="t('messagingOps.capture.seconds')"><ElInputNumber v-model="seconds" :min="1" :max="policy.maximumWindowSeconds ?? 1" :disabled="saving" /></ElFormItem>
      </template>
      <ElFormItem :label="t('messagingOps.reason')" required><ElInput v-model="reason" type="textarea" :rows="3" :maxlength="512" :disabled="saving" /></ElFormItem>
      <ElButton v-if="mayStart" type="primary" :loading="saving" @click="change('start')">{{ t('messagingOps.capture.start') }}</ElButton>
      <ElButton v-if="current?.state === 'active' || current?.state === 'expired'" :loading="saving" @click="change('close')">{{ t('messagingOps.capture.close') }}</ElButton>
    </ElForm>
  </ArtFormDialog>
</template>
