<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue';
import { ElButton, ElCard, ElDescriptions, ElDescriptionsItem, ElForm, ElFormItem, ElInput, ElInputNumber,
  ElMessageBox, ElOption, ElPagination, ElSelect, ElTable, ElTableColumn } from 'element-plus';
import { isFullNetProblemDetails, isMessagingOffset, isMessagingReplayOperation, type MessagingReplayState } from '@fullnet/client-contracts';
import { usePermission } from '../auth/permission';
import { useAdminI18n } from '../i18n/adminI18n';
import { showError, showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import { getMessagingDeliveryOverview, listMessagingReplayOperations, getMessagingReplayOperation,
  createMessagingReplayOperation, executeMessagingReplayOperation, cancelMessagingReplayOperation,
  type MessagingDeliveryOverview, type MessagingReplayOperation, type MessagingReplayCreateRequest } from '../api/messaging-delivery';

const { can } = usePermission();
const { t } = useAdminI18n();
const items = ref<MessagingReplayOperation[]>([]);
const overview = ref<MessagingDeliveryOverview>();
const page = ref(1), pageSize = ref(20), total = ref(0);
const loading = ref(false), saving = ref(false), executionPending = ref(false);
const errorCode = ref('');
const selected = ref<MessagingReplayOperation>();
const detailOpen = ref(false), createOpen = ref(false), changeOpen = ref(false);
const changeKind = ref<'execute' | 'cancel'>('execute');
const reason = ref('');
const form = reactive({ stream: '', consumer: '', partitions: '0', from: '', to: '', maxMessages: 100, reason: '' });
const pendingDraft = ref<MessagingReplayCreateRequest>();
const { tableMainRef, tableHeight, tableHeaderCellStyle, syncTableLayout } = useArtPagedTableInCard(loading);
const stateKeys = {
  prepared: 'messagingOps.state.prepared', running: 'messagingOps.state.running', partial: 'messagingOps.state.partial',
  completed: 'messagingOps.state.completed', cancel_requested: 'messagingOps.state.cancel_requested',
  cancelled: 'messagingOps.state.cancelled', result_unknown: 'messagingOps.state.result_unknown', blocked: 'messagingOps.state.blocked'
} as const satisfies Record<MessagingReplayState, string>;
const alive = ref(true);
let sequence = 0, detailSequence = 0;
let controller: AbortController | undefined, detailController: AbortController | undefined;
let timer: ReturnType<typeof setTimeout> | undefined;
const mayRead = () => alive.value && can('messaging.operations.read') && can('messaging.events.read');
const mayWrite = computed(() => mayRead() && can('messaging.kafka.range_replay'));
const streams = computed(() => overview.value?.streams.filter(item => item.isRegistered && item.deliveryOwner === 2 && item.topicCode && item.consumerNames.length) ?? []);
const chosenStream = computed(() => streams.value.find(item => streamKey(item) === form.stream));
function streamKey(item: { eventType: string; schemaVersion: number }): string { return item.eventType + '/' + item.schemaVersion; }
const busyState = (row: unknown) => isMessagingReplayOperation(row) && (row.state === 'running' || row.state === 'cancel_requested');
const canExecute = (row: unknown) => isMessagingReplayOperation(row) && mayWrite.value && overview.value?.replayEnabled && !busyState(row) && row.state !== 'completed' && !executionPending.value;
const stateLabel = (row: unknown) => isMessagingReplayOperation(row) ? t(stateKeys[row.state]) : t('messagingOps.unknown');
function fail(error: unknown): void {
  if (!alive.value) return;
  if (isFullNetProblemDetails(error)) showProblem(error, t('messagingOps.requestFailed'));
  else showError(t('messagingOps.requestFailed'));
}
onMounted(() => void load());
onUnmounted(() => { alive.value = false; sequence++; detailSequence++; controller?.abort(); detailController?.abort(); clearTimeout(timer); });
async function load(): Promise<void> {
  if (!mayRead()) return;
  controller?.abort(); controller = new AbortController(); const current = ++sequence;
  loading.value = true; errorCode.value = '';
  try {
    const [result, status] = await Promise.all([listMessagingReplayOperations(page.value, pageSize.value, controller.signal), getMessagingDeliveryOverview(controller.signal)]);
    if (current !== sequence || !mayRead()) return;
    items.value = result.items; total.value = result.total; overview.value = status;
  } catch (error) {
    if (current !== sequence || !mayRead()) return;
    items.value = []; total.value = 0; overview.value = undefined;
    errorCode.value = isFullNetProblemDetails(error) ? error.code ?? '' : 'client.request_failed';
  } finally { if (current === sequence) { loading.value = false; void syncTableLayout(); } }
}
async function refreshDetail(id: string): Promise<void> {
  if (!mayRead()) return;
  detailController?.abort(); detailController = new AbortController(); const current = ++detailSequence;
  clearTimeout(timer);
  try {
    const value = await getMessagingReplayOperation(id, detailController.signal);
    if (current !== detailSequence || !mayRead()) return;
    selected.value = value;
    // 仅轮询打开的活动记录；关闭页面不会把请求中止当作服务端取消成功。
    if (detailOpen.value && (busyState(value) || executionPending.value)) timer = setTimeout(() => void refreshDetail(id), 3000);
  } catch (error) { if (current === detailSequence && mayRead()) fail(error); }
}
function inspect(row: unknown): void {
  if (!mayRead() || !isMessagingReplayOperation(row)) return;
  selected.value = row; detailOpen.value = true; void refreshDetail(row.operationId);
}
function beginCreate(): void {
  if (!mayWrite.value || !overview.value?.replayEnabled || saving.value) return;
  if (!pendingDraft.value) { form.stream = ''; form.consumer = ''; form.from = ''; form.to = ''; form.partitions = '0'; form.reason = ''; form.maxMessages = Math.min(100, overview.value.replayMaximumSynchronousMessages); }
  createOpen.value = true;
}
async function create(): Promise<void> {
  if (!mayWrite.value || saving.value || !overview.value?.replayEnabled) return;
  if (!pendingDraft.value) {
    const stream = chosenStream.value;
    const parts = form.partitions.split(',').map(value => value.trim());
    if (!stream?.topicCode || !stream.consumerNames.includes(form.consumer) || !isMessagingOffset(form.from) || !isMessagingOffset(form.to)
      || BigInt(form.from) > BigInt(form.to) || BigInt(form.to) === 9223372036854775807n
      || parts.length > 32 || parts.some(value => !/^\d+$/.test(value) || !Number.isSafeInteger(Number(value)) || Number(value) > 2147483647)
      || new Set(parts.map(Number)).size !== parts.length || !Number.isSafeInteger(form.maxMessages) || form.maxMessages < 1 || form.maxMessages > overview.value.replayMaximumSynchronousMessages
      || !form.reason.trim() || form.reason.trim().length > 512) { showWarning(t('messagingOps.invalidReplay')); return; }
    pendingDraft.value = { requestId: crypto.randomUUID(), topicCode: stream.topicCode, partitions: parts.map(Number), replayConsumerName: form.consumer,
      fromTimestampUtc: null, toTimestampUtc: null, fromOffset: form.from, toOffset: form.to, maxMessages: form.maxMessages, reason: form.reason.trim() };
  }
  saving.value = true;
  try {
    const result = await createMessagingReplayOperation(pendingDraft.value);
    if (!mayRead()) return;
    pendingDraft.value = undefined; createOpen.value = false; selected.value = result; detailOpen.value = true;
    showSuccess(t('messagingOps.operationCreated')); await load();
  } catch (error) { fail(error); }
  finally { saving.value = false; }
}
function beginChange(kind: typeof changeKind.value, row: unknown): void {
  if (!isMessagingReplayOperation(row) || !mayWrite.value || saving.value || (kind === 'execute' && !canExecute(row))) return;
  selected.value = row; changeKind.value = kind; reason.value = ''; changeOpen.value = true;
}
async function change(): Promise<void> {
  const row = selected.value, kind = changeKind.value;
  if (!row || !mayWrite.value || saving.value || (kind === 'execute' && !canExecute(row))) return;
  if (!reason.value.trim() || reason.value.trim().length > 512) { showWarning(t('messagingOps.reasonRequired')); return; }
  saving.value = true;
  let executionStarted = false;
  try {
    await ElMessageBox.confirm(t(kind === 'execute' ? 'messagingOps.executeHint' : 'messagingOps.cancelHint') + ' ' + row.operationId,
      t('messagingOps.confirmTitle'), { type: 'warning', confirmButtonText: t('messagingOps.confirm'), cancelButtonText: t('messagingOps.cancel') });
    if (!mayWrite.value) return;
    if (kind === 'execute') {
      executionStarted = true;
      executionPending.value = true; saving.value = false; changeOpen.value = false;
      detailOpen.value = true; void refreshDetail(row.operationId);
    }
    const result = await (kind === 'execute' ? executeMessagingReplayOperation(row.operationId, reason.value) : cancelMessagingReplayOperation(row.operationId, reason.value));
    if (!mayRead()) return;
    if (selected.value?.operationId === result.operationId) selected.value = result;
    if (kind === 'cancel') changeOpen.value = false;
    showSuccess(t('messagingOps.operationSaved')); await load();
  } catch (error) { fail(error); if (mayRead()) await refreshDetail(row.operationId); }
  finally {
    if (kind === 'execute') { executionPending.value = false; if (!executionStarted) saving.value = false; }
    else saving.value = false;
  }
}
function closeDetail(): void { clearTimeout(timer); detailSequence++; detailController?.abort(); }
</script>

<template>
  <ElCard shadow="never" class="art-table-card art-crud-page" :aria-busy="loading || saving">
    <div class="messaging-toolbar"><h2>{{ t('messagingOps.operations') }}</h2><div>
      <ElButton v-if="mayWrite" :disabled="!overview?.replayEnabled || saving" @click="beginCreate">{{ t('messagingOps.createReplay') }}</ElButton>
      <ElButton :loading="loading" @click="load">{{ t('messagingOps.refresh') }}</ElButton>
    </div></div>
    <p class="art-page-caption">{{ t('messagingOps.replayProgressHint') }}</p>
    <p v-if="overview && !overview.replayEnabled" role="status">{{ t('messagingOps.replayDisabled') }}</p>
    <div v-if="errorCode" class="art-inline-alert" role="alert">{{ t('messagingOps.requestFailed') }} <code>{{ errorCode }}</code></div>
    <div ref="tableMainRef" class="art-crud-table-main">
      <ElTable v-loading="loading" :data="items" :height="tableHeight" :header-cell-style="tableHeaderCellStyle">
        <ElTableColumn prop="operationId" :label="t('messagingOps.recordId')" min-width="260" show-overflow-tooltip />
        <ElTableColumn prop="topicCode" :label="t('messagingOps.topic')" min-width="200" show-overflow-tooltip />
        <ElTableColumn :label="t('messagingOps.state')" min-width="140"><template #default="{ row }">{{ stateLabel(row) }}</template></ElTableColumn>
        <ElTableColumn prop="confirmedMessages" :label="t('messagingOps.confirmedMessages')" width="130" />
        <ElTableColumn prop="updatedAtUtc" :label="t('messagingOps.updatedAt')" min-width="200" />
        <ElTableColumn :label="t('messagingOps.actions')" min-width="200" fixed="right"><template #default="{ row }">
          <ElButton link @click="inspect(row)">{{ t('messagingOps.detail') }}</ElButton>
          <ElButton v-if="mayWrite && row.state !== 'completed' && !busyState(row)" link :disabled="!canExecute(row) || saving" @click="beginChange('execute', row)">{{ t('messagingOps.execute') }}</ElButton>
          <ElButton v-if="mayWrite && row.state !== 'completed' && row.state !== 'cancelled'" link :disabled="saving" @click="beginChange('cancel', row)">{{ t('messagingOps.cancelOperation') }}</ElButton>
        </template></ElTableColumn>
      </ElTable>
      <ElPagination v-model:current-page="page" v-model:page-size="pageSize" class="art-table-pagination" :total="total" layout="total, sizes, prev, pager, next" :page-sizes="[20, 50, 100]" @current-change="load" @size-change="page = 1; load()" />
    </div>
    <ArtFormDialog v-model:open="createOpen" :title="t('messagingOps.createReplay')" :saving="saving" :show-confirm="mayWrite" :confirm-label="t(pendingDraft ? 'messagingOps.retryCreate' : 'messagingOps.createReplay')" :cancel-label="t('messagingOps.close')" width="680px" @confirm="create">
      <p>{{ t('messagingOps.createReplayHint') }}</p><p v-if="pendingDraft" role="status">{{ t('messagingOps.pendingCreate') }} <code>{{ pendingDraft.requestId }}</code></p>
      <ElForm label-position="top" :disabled="!!pendingDraft || saving" @submit.prevent="create">
        <ElFormItem :label="t('messagingOps.messageType')" required><ElSelect v-model="form.stream" @change="form.consumer = ''"><ElOption v-for="stream in streams" :key="streamKey(stream)" :value="streamKey(stream)" :label="streamKey(stream)" /></ElSelect></ElFormItem>
        <ElFormItem :label="t('messagingOps.consumer')" required><ElSelect v-model="form.consumer"><ElOption v-for="consumer in chosenStream?.consumerNames ?? []" :key="consumer" :label="consumer" :value="consumer" /></ElSelect></ElFormItem>
        <ElFormItem :label="t('messagingOps.partitions')" required><ElInput v-model="form.partitions" placeholder="0,1" /></ElFormItem>
        <ElFormItem :label="t('messagingOps.fromOffset')" required><ElInput v-model="form.from" inputmode="numeric" /></ElFormItem>
        <ElFormItem :label="t('messagingOps.toOffset')" required><ElInput v-model="form.to" inputmode="numeric" /></ElFormItem>
        <ElFormItem :label="t('messagingOps.maxMessages')" required><ElInputNumber v-model="form.maxMessages" :min="1" :max="overview?.replayMaximumSynchronousMessages" :precision="0" /></ElFormItem>
        <ElFormItem :label="t('messagingOps.reason')" required><ElInput v-model="form.reason" type="textarea" :rows="3" :maxlength="512" show-word-limit /></ElFormItem>
      </ElForm>
    </ArtFormDialog>
    <ArtFormDialog v-model:open="detailOpen" :title="t('messagingOps.detail')" :show-confirm="false" :cancel-label="t('messagingOps.close')" width="800px" @cancel="closeDetail">
      <template v-if="selected"><p translate="no">{{ selected.operationId }}</p>
        <div v-if="selected.hasUnconfirmedMessage || selected.state === 'result_unknown'" role="status" class="art-inline-alert">{{ t('messagingOps.unknownOutcome') }}</div>
        <ElDescriptions :column="2" border>
          <ElDescriptionsItem :label="t('messagingOps.state')">{{ t(stateKeys[selected.state]) }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.confirmedMessages')">{{ selected.confirmedMessages }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.processedCount')">{{ selected.processedMessages }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.deduplicatedCount')">{{ selected.alreadyProcessedMessages }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.rejectedCount')">{{ selected.rejectedAttempts }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.reasonCode')">{{ selected.lastReasonCode ?? '—' }}</ElDescriptionsItem>
        </ElDescriptions>
        <ElTable :data="selected.partitions"><ElTableColumn prop="partition" :label="t('messagingOps.partition')" /><ElTableColumn prop="startOffset" :label="t('messagingOps.fromOffset')" /><ElTableColumn prop="endOffsetExclusive" :label="t('messagingOps.endExclusive')" /><ElTableColumn prop="nextOffset" :label="t('messagingOps.nextOffset')" /></ElTable>
        <ElButton @click="refreshDetail(selected.operationId)">{{ t('messagingOps.refresh') }}</ElButton>
      </template>
    </ArtFormDialog>
    <ArtFormDialog v-model:open="changeOpen" :title="t(changeKind === 'execute' ? 'messagingOps.execute' : 'messagingOps.cancelOperation')" :saving="saving" :show-confirm="mayWrite" :confirm-label="t('messagingOps.confirm')" :cancel-label="t('messagingOps.close')" @confirm="change">
      <p translate="no">{{ selected?.operationId }}</p><p>{{ t(changeKind === 'execute' ? 'messagingOps.executeHint' : 'messagingOps.cancelHint') }}</p>
      <ElForm @submit.prevent="change"><ElFormItem :label="t('messagingOps.reason')" required><ElInput v-model="reason" type="textarea" :rows="4" :maxlength="512" show-word-limit /></ElFormItem></ElForm>
    </ArtFormDialog>
  </ElCard>
</template>

<style scoped>
.messaging-toolbar { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 12px; }
.messaging-toolbar h2 { font-size: 16px; font-weight: 600; }
</style>
