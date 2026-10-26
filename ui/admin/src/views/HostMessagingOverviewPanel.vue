<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue';
import { ElButton, ElCard, ElDescriptions, ElDescriptionsItem, ElForm, ElFormItem, ElInput,
  ElMessageBox, ElTable, ElTableColumn } from 'element-plus';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import 'element-plus/theme-chalk/el-descriptions.css';
import type { MessageKey } from '@fullnet/admin-i18n';
import HostMessagingShadowCaptureDialog from './HostMessagingShadowCaptureDialog.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import { usePermission } from '../auth/permission';
import { useAdminI18n } from '../i18n/adminI18n';
import { showError, showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { getMessagingDeliveryOverview, verifyMessagingCutoverEvidence, changeMessagingDeliveryOwner,
  getLatestMessagingRollback, recoverMessagingRollback, type MessagingDeliveryOverview, type MessagingDeliveryStream,
  type MessagingRollbackOperation, type MessagingCutoverEvidence } from '../api/messaging-delivery';

const { can } = usePermission();
const { t } = useAdminI18n();
const overview = ref<MessagingDeliveryOverview>();
const loading = ref(false);
const captureStream = ref<MessagingDeliveryStream>();
const layout = useArtCrudTableLayout({ bottomOffset: 0 });
layout.watchLoading(loading);
const { tableMainRef, tableHeight, tableHeaderCellStyle } = layout;
const syncTableLayout = () => nextTick(layout.updateTableHeight);
const saving = ref(false);
const errorCode = ref('');
const selected = ref<MessagingDeliveryStream>();
const rollback = ref<MessagingRollbackOperation>();
const evidence = ref<MessagingCutoverEvidence>();
const noRollback = ref(false);
const action = ref<'verify' | 'cutover' | 'rollback' | 'recover'>('verify');
const dialog = ref(false);
const statusDialog = ref(false);
const reason = ref('');
const alive = ref(true);
let sequence = 0;
let rollbackSequence = 0;
let controller: AbortController | undefined;
const mayRead = () => alive.value && can('messaging.operations.read') && can('messaging.events.read');
const mayChange = computed(() => mayRead() && can(action.value === 'verify' || action.value === 'cutover'
  ? 'messaging.delivery.cutover' : 'messaging.delivery.rollback'));
const actionLabels = { verify: 'messagingOps.verify', cutover: 'messagingOps.cutover', rollback: 'messagingOps.rollback', recover: 'messagingOps.recoverRollback' } as const;
const phases = ['messagingOps.rollback.prepared', 'messagingOps.rollback.pauseRequested', 'messagingOps.rollback.paused',
  'messagingOps.rollback.recovering', 'messagingOps.rollback.recovered', 'messagingOps.rollback.completed'] as const;
const owner = (value: number | null) => value === 0 ? 'Legacy' : value === 1 ? 'Shadow CDC' : value === 2 ? 'CDC Kafka' : t('messagingOps.unknown');
const reasonKeys: Readonly<Record<string, MessageKey>> = {
  stream_not_registered: 'messagingOps.blocker.unregistered', subscription_not_registered: 'messagingOps.blocker.subscription',
  cutover_disabled: 'messagingOps.blocker.disabled', ownership_unavailable: 'messagingOps.blocker.ownerUnknown',
  cutover_owner_unsupported: 'messagingOps.blocker.ownerUnsupported', control_state_unavailable: 'messagingOps.blocker.controlUnknown',
  ownership_record_required: 'messagingOps.blocker.ownerRecord', rollback_preparing: 'messagingOps.blocker.rollback',
  legacy_backlog_unavailable: 'messagingOps.blocker.backlogUnknown', legacy_backlog_not_drained: 'messagingOps.blocker.backlog',
  shadow_evidence_not_observed: 'messagingOps.blocker.shadow', target_runtime_not_observed: 'messagingOps.blocker.runtime'
};
const explain = (code: string) => reasonKeys[code] ? t(reasonKeys[code]) : code;
const count = (value: number | null | undefined) => value == null ? t('messagingOps.unknown') : String(value);
const streamKey = (value: MessagingDeliveryStream) => value.eventType + '/' + value.schemaVersion;
function fail(error: unknown): void {
  if (!alive.value) return;
  if (isFullNetProblemDetails(error)) showProblem(error, t('messagingOps.requestFailed'));
  else showError(t('messagingOps.requestFailed'));
}
onMounted(() => void load());
onUnmounted(() => { alive.value = false; sequence++; rollbackSequence++; controller?.abort(); });
async function load(): Promise<void> {
  if (!mayRead()) return;
  controller?.abort(); controller = new AbortController();
  const current = ++sequence;
  loading.value = true; errorCode.value = '';
  try {
    const result = await getMessagingDeliveryOverview(controller.signal);
    if (current === sequence && mayRead()) overview.value = result;
  } catch (error) {
    if (current !== sequence || !mayRead()) return;
    overview.value = undefined;
    errorCode.value = isFullNetProblemDetails(error) ? error.code ?? '' : 'client.request_failed';
  } finally { if (current === sequence) { loading.value = false; void syncTableLayout(); } }
}
function streamFor(value: unknown): MessagingDeliveryStream | undefined {
  if (typeof value !== 'object' || value === null || !('eventType' in value) || !('schemaVersion' in value)) return;
  return overview.value?.streams.find(item => item.eventType === value.eventType && item.schemaVersion === value.schemaVersion);
}
function begin(kind: typeof action.value, value: unknown): void {
  const stream = streamFor(value);
  if (!stream || !mayRead() || saving.value) return;
  action.value = kind;
  if (!mayChange.value) return;
  selected.value = stream; reason.value = ''; evidence.value = undefined; dialog.value = true;
}
async function inspectRollback(value: unknown): Promise<void> {
  const stream = streamFor(value);
  if (!stream || !mayRead() || saving.value) return;
  selected.value = stream; rollback.value = undefined; noRollback.value = false;
  const current = ++rollbackSequence;
  try {
    const result = await getLatestMessagingRollback(stream);
    if (!mayRead() || current !== rollbackSequence) return;
    rollback.value = result; statusDialog.value = true;
  } catch (error) {
    if (!mayRead() || current !== rollbackSequence) return;
    if (isFullNetProblemDetails(error) && error.status === 404 && error.code === 'messaging.delivery.rollback_operation_not_found') {
      noRollback.value = true; statusDialog.value = true;
    } else fail(error);
  }
}
async function confirm(): Promise<void> {
  const stream = selected.value;
  if (!stream || !mayChange.value || saving.value) return;
  if (!reason.value.trim() || reason.value.trim().length > 512) { showWarning(t('messagingOps.reasonRequired')); return; }
  saving.value = true;
  try {
    await ElMessageBox.confirm(t('messagingOps.controlConfirm') + ' ' + stream.eventType + ' / ' + stream.schemaVersion,
      t(actionLabels[action.value]), { type: 'warning', confirmButtonText: t('messagingOps.confirm'), cancelButtonText: t('messagingOps.cancel') });
    if (!mayChange.value) return;
    if (action.value === 'verify') evidence.value = await verifyMessagingCutoverEvidence(stream, reason.value);
    else if (action.value === 'recover') {
      if (!rollback.value) return;
      rollback.value = await recoverMessagingRollback(rollback.value.generation, reason.value);
    } else await changeMessagingDeliveryOwner(action.value, stream, reason.value);
    if (!mayRead()) return;
    showSuccess(t('messagingOps.operationSaved'));
    if (action.value !== 'verify') dialog.value = false;
    await load();
  } catch (error) { if (error !== 'cancel' && error !== 'close') fail(error); }
  finally { saving.value = false; }
}
</script>

<template>
  <ElCard shadow="never" class="messaging-overview art-table-card" :aria-busy="loading || saving">
    <div class="messaging-toolbar"><h2>{{ t('messagingOps.overview') }}</h2><ElButton :loading="loading" @click="load">{{ t('messagingOps.refresh') }}</ElButton></div>
    <div v-if="errorCode" class="art-inline-alert" role="alert">{{ t('messagingOps.requestFailed') }} <code>{{ errorCode }}</code></div>
    <template v-if="overview">
      <p class="art-page-caption">{{ t('messagingOps.observationHint') }}</p>
      <div v-if="overview.observationState !== 'available' || overview.hasMoreStreams" class="art-inline-alert" role="status">{{ t('messagingOps.partialObservation') }}</div>
      <ElDescriptions :column="3" border>
        <ElDescriptionsItem :label="t('messagingOps.pending')">{{ count(overview.backlog?.pendingCount) }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('messagingOps.dueRetry')">{{ count(overview.backlog?.dueRetryCount) }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('messagingOps.leases')">{{ count(overview.backlog?.activeLeaseCount) }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('messagingOps.cutover')">{{ t(overview.cutoverEnabled ? 'messagingOps.enabled' : 'messagingOps.disabled') }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('messagingOps.runtime')">{{ t('messagingOps.notObserved') }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('messagingOps.unattributed')">{{ count(overview.unattributedKafkaDeadLetters) }}</ElDescriptionsItem>
      </ElDescriptions>
      <div ref="tableMainRef" class="art-crud-table-main messaging-stream-table">
      <ElTable :data="overview.streams" :row-key="streamKey" :height="tableHeight" :header-cell-style="tableHeaderCellStyle">
        <ElTableColumn :label="t('messagingOps.messageType')" min-width="230"><template #default="{ row }"><span translate="no">{{ row.eventType }} / {{ row.schemaVersion }}</span><p class="art-page-caption messaging-topic" :title="row.topicCode ?? undefined" translate="no">{{ row.topicCode ?? '—' }}</p></template></ElTableColumn>
        <ElTableColumn :label="t('messagingOps.owner')" min-width="130"><template #default="{ row }">{{ owner(row.deliveryOwner) }}</template></ElTableColumn>
        <ElTableColumn :label="t('messagingOps.pending')" width="110"><template #default="{ row }">{{ count(row.legacyBacklog?.pendingCount) }}</template></ElTableColumn>
        <ElTableColumn :label="t('messagingOps.legacyDeadLetters')" width="120"><template #default="{ row }">{{ count(row.legacyBacklog?.deadLetterCount) }}</template></ElTableColumn>
        <ElTableColumn :label="t('messagingOps.projectedDeadLetters')" width="130"><template #default="{ row }">{{ count(row.kafkaProjectedDeadLetterCount) }}</template></ElTableColumn>
        <ElTableColumn :label="t('messagingOps.retained')" width="130"><template #default="{ row }">{{ count(row.retainedEventCount) }}</template></ElTableColumn>
        <ElTableColumn :label="t('messagingOps.blockingReasons')" min-width="260"><template #default="{ row }"><details class="messaging-reasons"><summary>{{ row.blockingReasons.length }} {{ t('messagingOps.statusNotes') }}</summary><ul><li v-for="code in row.blockingReasons" :key="code" :title="code">{{ explain(code) }}</li></ul></details></template></ElTableColumn>
        <ElTableColumn :label="t('messagingOps.actions')" min-width="210" fixed="right"><template #default="{ row }">
          <div class="messaging-stream-actions">
            <ElButton v-if="row.isRegistered" link :disabled="saving" @click="captureStream = streamFor(row)">{{ t('messagingOps.capture.title') }}</ElButton>
            <ElButton v-if="can('messaging.delivery.cutover') && row.isRegistered && row.deliveryOwner === 0" link :disabled="!overview.cutoverEnabled || saving" @click="begin('verify', row)">{{ t('messagingOps.verify') }}</ElButton>
            <ElButton v-if="can('messaging.delivery.cutover') && row.isRegistered && row.deliveryOwner === 0" link :disabled="!overview.cutoverEnabled || saving" @click="begin('cutover', row)">{{ t('messagingOps.cutover') }}</ElButton>
            <ElButton v-if="can('messaging.delivery.rollback') && row.deliveryOwner === 2" link :disabled="saving" @click="begin('rollback', row)">{{ t('messagingOps.rollback') }}</ElButton>
            <ElButton link :disabled="saving" @click="inspectRollback(row)">{{ t('messagingOps.rollbackStatus') }}</ElButton>
          </div>
        </template></ElTableColumn>
      </ElTable>
      </div>
    </template>
    <HostMessagingShadowCaptureDialog v-if="captureStream && overview" :stream="captureStream" :policy="overview.shadowCapturePolicy" @close="captureStream = undefined" @changed="load" />
    <ArtFormDialog v-model:open="dialog" :title="t(actionLabels[action])" :saving="saving" :show-confirm="mayChange && !evidence" :confirm-label="t('messagingOps.confirm')" :cancel-label="t('messagingOps.close')" @confirm="confirm">
      <p translate="no">{{ selected?.eventType }} / {{ selected?.schemaVersion }}</p>
      <p>{{ t(action === 'recover' ? 'messagingOps.recoverRollbackHint' : 'messagingOps.controlHint') }}</p>
      <ElForm v-if="!evidence" @submit.prevent="confirm"><ElFormItem :label="t('messagingOps.reason')" required><ElInput v-model="reason" type="textarea" :maxlength="512" show-word-limit :rows="4" /></ElFormItem></ElForm>
      <ElDescriptions v-else :column="1" border>
        <ElDescriptionsItem :label="t('messagingOps.evidence')">{{ evidence.id }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('messagingOps.samples')">{{ evidence.sampleCount }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="t('messagingOps.expiresAt')">{{ evidence.expiresAtUtc }}</ElDescriptionsItem>
      </ElDescriptions>
    </ArtFormDialog>
    <ArtFormDialog v-model:open="statusDialog" :title="t('messagingOps.rollbackStatus')" :show-confirm="false" :cancel-label="t('messagingOps.close')">
      <p v-if="noRollback">{{ t('messagingOps.noRollback') }}</p>
      <template v-if="rollback"><p translate="no">{{ rollback.generation }}</p><p>{{ t(phases[rollback.phase]!) }}</p><p>{{ rollback.updatedAtUtc }}</p>
        <ElButton v-if="can('messaging.delivery.rollback') && rollback.phase !== 4 && rollback.phase !== 5 && selected" :disabled="saving" @click="begin('recover', selected)">{{ t('messagingOps.recoverRollback') }}</ElButton>
      </template>
    </ArtFormDialog>
  </ElCard>
</template>

<style scoped>
.messaging-overview { overflow: auto; }
.messaging-toolbar { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.messaging-toolbar h2 { margin: 0; font-size: 16px; font-weight: 600; }
.messaging-stream-table { margin-top: 20px; }
.messaging-reasons { font-size: 12px; overflow-wrap: anywhere; }
.messaging-reasons summary { cursor: pointer; color: var(--el-color-primary); }
.messaging-reasons ul { margin: 8px 0; padding-inline-start: 16px; }
.messaging-topic { margin: 4px 0 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.messaging-stream-table :deep(td.el-table__cell) { vertical-align: top; }
.messaging-stream-actions { display: flex; flex-wrap: wrap; gap: 8px; }
.messaging-stream-actions :deep(.el-button) { margin: 0; }
</style>
