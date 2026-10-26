<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref } from 'vue';
import { ElButton, ElCard, ElDescriptions, ElDescriptionsItem, ElForm, ElFormItem, ElInput,
  ElMessageBox, ElPagination, ElTable, ElTableColumn } from 'element-plus';
import type { FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isOperationalDeadLetter } from '@fullnet/client-contracts';

import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import { ART_TABLE_ACTION_COLUMN_WIDTH } from '../framework/art-design/components/artTableActions';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import { usePermission } from '../auth/permission';
import { useAdminI18n } from '../i18n/adminI18n';
import { showError, showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { getOperationalDeadLetter, listOperationalDeadLetters, recoverOperationalDeadLetter,
  type DeadLetterSource, type OperationalDeadLetter } from '../api/messaging-operations';

defineOptions({ name: 'HostMessagingDeadLettersPanel' });
const props = defineProps<{ source: DeadLetterSource }>();
const alive = ref(true);
const { t } = useAdminI18n();
const { can } = usePermission();
const source = computed(() => props.source);
const items = ref<OperationalDeadLetter[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const saving = ref(false);
const problem = ref<FullNetProblemDetails>();
const unknownError = ref(false);
const filters = reactive({ messageType: '', schemaVersion: '', errorCode: '', consumerName: '', fromUtc: '', toUtc: '' });
const selected = ref<OperationalDeadLetter>();
const detailOpen = ref(false);
const recoveryOpen = ref(false);
const reason = ref('');
const mayRead = computed(() => alive.value && can('messaging.operations.read') && can('messaging.dead_letters.read'));
const mayRecover = computed(() => mayRead.value && can(source.value === 'legacy'
  ? 'messaging.outbox_dead_letters.requeue' : 'messaging.dead_letters.replay'));
const { tableMainRef, tableHeight, tableHeaderCellStyle, syncTableLayout } = useArtPagedTableInCard(loading);
let sequence = 0;
let requestController: AbortController | undefined;

onMounted(() => { if (mayRead.value) void load(); });
onUnmounted(() => { alive.value = false; sequence++; requestController?.abort(); });

async function load(): Promise<void> {
  if (!mayRead.value) return;
  requestController?.abort();
  requestController = new AbortController();
  const current = ++sequence;
  loading.value = true;
  problem.value = undefined;
  unknownError.value = false;
  try {
    const result = await listOperationalDeadLetters(source.value,
      { page: page.value, pageSize: pageSize.value, ...filters,
        consumerName: source.value === 'kafka' ? filters.consumerName : undefined }, requestController.signal);
    if (current !== sequence) return;
    items.value = result.items;
    total.value = result.total;
  } catch (error) {
    if (current !== sequence) return;
    items.value = [];
    total.value = 0;
    if (isFullNetProblemDetails(error)) problem.value = error;
    else unknownError.value = true;
  } finally {
    if (current === sequence) { loading.value = false; void syncTableLayout(); }
  }
}

function search(): void { page.value = 1; void load(); }

function fail(error: unknown): void {
  if (isFullNetProblemDetails(error)) showProblem(error, t('messagingOps.requestFailed'));
  else showError(t('messagingOps.requestFailed'));
}
async function detail(row: unknown): Promise<void> {
  if (!mayRead.value || !isOperationalDeadLetter(row)) return;
  try {
    const result = await getOperationalDeadLetter(source.value, row.id);
    if (!mayRead.value || result.source !== source.value) return;
    selected.value = result;
    detailOpen.value = true;
  } catch (error) { fail(error); }
}
function beginRecovery(row: unknown): void {
  if (!mayRead.value || !isOperationalDeadLetter(row)) return;
  if (!mayRecover.value || saving.value || !row.canReplay || row.source !== source.value) return;
  selected.value = row;
  reason.value = '';
  recoveryOpen.value = true;
}
async function recover(): Promise<void> {
  const row = selected.value;
  if (!row || !mayRecover.value || saving.value) return;
  if (!reason.value.trim() || reason.value.trim().length > 512) {
    showWarning(t('messagingOps.reasonRequired')); return;
  }
  const targetSource = source.value;
  saving.value = true;
  try {
    await ElMessageBox.confirm(t('messagingOps.confirmBody') + ' ' + row.id,
      t('messagingOps.confirmTitle'), { type: 'warning', confirmButtonText: t('messagingOps.confirm'), cancelButtonText: t('messagingOps.cancel') });
    if (!mayRecover.value) return;
    const outcome = await recoverOperationalDeadLetter(targetSource, row.id, reason.value);
    showSuccess(t(outcome === 'requeued' ? 'messagingOps.requeued' : outcome === 'already_processed'
      ? 'messagingOps.alreadyProcessed' : 'messagingOps.processed'));
    recoveryOpen.value = false;
    if (source.value === targetSource) await load();
  } catch (error) { if (error !== 'cancel' && error !== 'close') fail(error); }
  finally { saving.value = false; }
}
</script>

<template>
      <ElCard shadow="never" class="messaging-dead-letters-panel art-table-card art-crud-page" :aria-busy="loading || saving">
      <div v-if="problem || unknownError" class="art-inline-alert" role="alert">
        <strong v-if="problem" translate="no">{{ problem.code }}</strong>
        <span>{{ problem?.title ?? t('messagingOps.requestFailed') }}</span>
      </div>
<p class="art-page-caption">{{ t(source === 'legacy' ? 'messagingOps.legacyHint' : 'messagingOps.kafkaHint') }}</p>
        <ElForm inline @submit.prevent="search">
          <ElFormItem :label="t('messagingOps.messageType')"><ElInput v-model="filters.messageType" clearable /></ElFormItem>
          <ElFormItem :label="t('messagingOps.schemaVersion')"><ElInput v-model="filters.schemaVersion" clearable /></ElFormItem>
          <ElFormItem :label="t('messagingOps.errorCode')"><ElInput v-model="filters.errorCode" clearable /></ElFormItem>
          <ElFormItem v-if="source === 'kafka'" :label="t('messagingOps.consumer')"><ElInput v-model="filters.consumerName" clearable /></ElFormItem>
          <ElFormItem :label="t('messagingOps.fromUtc')"><ElInput v-model="filters.fromUtc" placeholder="2026-10-24T00:00:00Z" clearable /></ElFormItem>
          <ElFormItem :label="t('messagingOps.toUtc')"><ElInput v-model="filters.toUtc" placeholder="2026-10-24T23:59:59Z" clearable /></ElFormItem>
          <ElButton native-type="submit" :loading="loading">{{ t('messagingOps.search') }}</ElButton>
        </ElForm>
        <div ref="tableMainRef" class="art-crud-table-main">
          <ElTable v-loading="loading" :data="items" :height="tableHeight" :header-cell-style="tableHeaderCellStyle">
            <ElTableColumn prop="messageId" :label="t('messagingOps.messageId')" min-width="260" show-overflow-tooltip />
            <ElTableColumn prop="messageType" :label="t('messagingOps.messageType')" min-width="230" show-overflow-tooltip />
            <ElTableColumn v-if="source === 'kafka'" prop="consumerName" :label="t('messagingOps.consumer')" min-width="200" />
            <ElTableColumn prop="attempts" :label="t('messagingOps.attempts')" width="100" />
            <ElTableColumn prop="errorCode" :label="t('messagingOps.errorCode')" min-width="200" show-overflow-tooltip />
            <ElTableColumn prop="lastFailedAtUtc" :label="t('messagingOps.failedAt')" min-width="200" />
            <ElTableColumn :label="t('messagingOps.actions')" :width="ART_TABLE_ACTION_COLUMN_WIDTH" fixed="right">
              <template #default="{ row }"><ArtTableActionGroup>
                <ArtTableActionButton type="view" :title="t('messagingOps.detail')" @click="detail(row)" />
                <ArtTableActionButton v-if="mayRecover && row.canReplay" type="edit" :title="t(source === 'legacy' ? 'messagingOps.requeue' : 'messagingOps.replay')" @click="beginRecovery(row)" />
              </ArtTableActionGroup></template>
            </ElTableColumn>
          </ElTable>
          <ElPagination v-model:current-page="page" v-model:page-size="pageSize" class="art-table-pagination"
            layout="total, sizes, prev, pager, next" :page-sizes="[20, 50, 100]" :total="total" @current-change="load" @size-change="search" />
        </div>
      </ElCard>
      <ArtFormDialog v-model:open="detailOpen" :title="t('messagingOps.detail')" :show-confirm="false" :cancel-label="t('messagingOps.close')" width="760px">
        <ElDescriptions v-if="selected" :column="1" border>
          <ElDescriptionsItem :label="t('messagingOps.recordId')">{{ selected.id }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.messageId')">{{ selected.messageId ?? '—' }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.messageType')">{{ selected.messageType ?? '—' }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.consumer')">{{ selected.consumerName ?? '—' }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.errorCode')">{{ selected.errorCode ?? '—' }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.firstFailedAt')">{{ selected.firstFailedAtUtc }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.failedAt')">{{ selected.lastFailedAtUtc }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.sourcePosition')">{{ selected.sourceTopic ?? '—' }} / {{ selected.sourcePartition ?? '—' }} / {{ selected.sourceOffset ?? '—' }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.dlqPosition')">{{ selected.dlqTopic ?? '—' }} / {{ selected.dlqPartition ?? '—' }} / {{ selected.dlqOffset ?? '—' }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.replayedAt')">{{ selected.replayedAtUtc ?? '—' }}</ElDescriptionsItem>
          <ElDescriptionsItem :label="t('messagingOps.replayable')">{{ t(selected.canReplay ? 'messagingOps.replayableYes' : 'messagingOps.replayableNo') }}</ElDescriptionsItem>
        </ElDescriptions>
      </ArtFormDialog>
      <ArtFormDialog v-model:open="recoveryOpen" :title="t(source === 'legacy' ? 'messagingOps.requeue' : 'messagingOps.replay')"
        :saving="saving" :confirm-label="t('messagingOps.confirm')" :cancel-label="t('messagingOps.cancel')" @confirm="recover">
        <p translate="no">{{ selected?.messageId ?? selected?.id }}</p>
        <ElForm @submit.prevent="recover"><ElFormItem :label="t('messagingOps.reason')" required>
          <ElInput v-model="reason" type="textarea" :maxlength="512" show-word-limit :rows="4" />
        </ElFormItem></ElForm>
      </ArtFormDialog>
</template>
