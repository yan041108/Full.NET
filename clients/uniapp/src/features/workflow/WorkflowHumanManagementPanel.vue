<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import type { WorkflowTodoDetail, WorkflowTodoCountersignChainResponse, WorkflowManualReminderTargetsResponse } from '@fullnet/client-contracts';
import type { WorkflowTodoClient } from './workflow-todo-client';
import { classifyWorkflowTodoActionFailure } from './workflow-todo-action-failure';
import { createIdempotencyKey } from './idempotency-key';

const props = defineProps<{ detail: WorkflowTodoDetail; client: WorkflowTodoClient;
  permissions: { transfer: boolean; return: boolean; countersign: boolean; remind: boolean };
  comment: string; signatureFileId?: string; unsaved: boolean; disabled: boolean }>();
const emit = defineEmits<{ busy: [value: boolean]; completed: []; refresh: [] }>();
const { t } = useI18n();
type Action = 'transfer' | 'return' | 'countersign';
const action = ref<Action>('transfer'); const direction = ref<'before' | 'after'>('before');
const options = computed(() => (['transfer', 'return', 'countersign'] as const).filter(key =>
  props.detail.statusKey === 'active' && props.permissions[key] && props.detail.allowedActions.includes(key)));
const chain = ref<WorkflowTodoCountersignChainResponse>(); const reminders = ref<WorkflowManualReminderTargetsResponse>();
const chainLoaded = ref(false);
const rows = ref<Array<{ id: string; label: string }>>([]); const page = ref(1); const hasNext = ref(false);
const chosen = ref(''); const chosenLabel = ref(''); const members = ref<Array<{ id: string; label: string }>>([]);
const loading = ref(false); const failed = ref(false); const working = ref(false); const feedback = ref(''); const cancelComment = ref('');
let epoch = 0; let pending: { semantic: string; key: string } | undefined;
const selection = computed(() => chosen.value && !rows.value.some(item => item.id === chosen.value)
  ? [{ id: chosen.value, label: chosenLabel.value }, ...rows.value] : rows.value);
const names = computed(() => [t('workflow.todo.management.choose'), ...selection.value.map(item => item.label)]);
const selectionIndex = computed(() => Math.max(0, selection.value.findIndex(item => item.id === chosen.value) + 1));
const evidenceReady = computed(() => (!props.detail.commentRequired || !!props.comment.trim()) &&
  (action.value === 'countersign' || !props.detail.signRequired || !!props.signatureFileId) &&
  (action.value !== 'return' || !!props.comment.trim()));
const ready = computed(() => options.value.includes(action.value) && !props.disabled && !props.unsaved && !working.value && evidenceReady.value &&
  (action.value === 'countersign' ? chainLoaded.value && !chain.value && members.value.length > 0 : !!chosen.value));
const mayCancel = computed(() => props.permissions.countersign && props.detail.allowedActions.includes('countersign') && chain.value?.statusKey === 'active' &&
  (['active', 'before_countersign'].includes(props.detail.statusKey) || props.detail.statusKey === 'completed' && chain.value.directionKey === 'after'));
const reminder = computed(() => reminders.value?.items.find(item => item.stepId === props.detail.stepId && item.canRemind));

async function loadPage(next: number): Promise<void> {
  if (!options.value.includes(action.value) || loading.value || working.value || props.disabled) return;
  const current = epoch; const kind = action.value; loading.value = true; failed.value = false;
  try {
    if (kind === 'return') {
      const result = await props.client.listReturnTargets(props.detail.id, next);
      if (current !== epoch) return;
      rows.value = result.map(item => ({ id: item.stepId, label: `${item.nodeKey} · ${item.completedAtUtc}` })); hasNext.value = result.length === 20;
    } else {
      const result = await props.client.listManagementCandidates(props.detail.id, kind, next);
      if (current !== epoch) return;
      rows.value = result.items.filter(item => item.id !== props.detail.assigneeUserId).map(item => ({ id: item.id, label: item.displayName || item.username }));
      hasNext.value = result.page * 20 < result.total;
    }
    page.value = next;
  } catch { if (current === epoch) { failed.value = true; rows.value = []; } }
  finally { if (current === epoch) loading.value = false; }
}
async function initialize(): Promise<void> {
  const current = ++epoch; rows.value = []; chosen.value = ''; chosenLabel.value = ''; members.value = [];
  chain.value = undefined; chainLoaded.value = false; reminders.value = undefined; feedback.value = ''; cancelComment.value = ''; pending = undefined; page.value = 1; hasNext.value = false; loading.value = false;
  if (!options.value.includes(action.value) && options.value[0]) action.value = options.value[0];
  await Promise.all([
    loadPage(1),
    (async () => { if (!props.permissions.countersign || !props.detail.allowedActions.includes('countersign')) return;
      try { const value = await props.client.countersignChain(props.detail.id); if (current === epoch) { chain.value = value ?? undefined; chainLoaded.value = true; } }
      catch { if (current === epoch) feedback.value = t('workflow.todo.failed'); } })(),
    (async () => { if (!props.permissions.remind || !props.detail.allowedActions.includes('remind') || props.detail.statusKey !== 'active') return;
      try { const value = await props.client.reminderTargets(props.detail.instanceId); if (current === epoch) reminders.value = value; }
      catch { if (current === epoch) feedback.value = t('workflow.todo.failed'); } })()
  ]);
}
function choose(event: { detail: { value: string | number } }): void {
  if (working.value || props.disabled) return;
  const index = Number(event.detail.value); if (!Number.isInteger(index) || index < 0 || index > selection.value.length) return;
  const item = selection.value[index - 1]; chosen.value = item?.id ?? ''; chosenLabel.value = item?.label ?? '';
}
function addMember(): void {
  if (working.value || props.disabled || !chosen.value || members.value.length >= 10 || members.value.some(item => item.id === chosen.value)) return;
  members.value = [...members.value, { id: chosen.value, label: chosenLabel.value }]; chosen.value = ''; chosenLabel.value = '';
}
function operationKey(semantic: string): string {
  if (pending?.semantic !== semantic) pending = { semantic, key: createIdempotencyKey() };
  return pending.key;
}
async function execute(kind: Action | 'cancel' | 'remind'): Promise<void> {
  if (working.value || props.disabled || props.unsaved || (['transfer', 'return', 'countersign'].includes(kind) && !ready.value) ||
    kind === 'cancel' && (!mayCancel.value || props.detail.commentRequired && !cancelComment.value.trim()) || kind === 'remind' && !reminder.value) return;
  const current = epoch; const detail = props.detail;
  const reason = kind === 'cancel' ? cancelComment.value.trim() : props.comment.trim();
  let key: string;
  try { key = operationKey(JSON.stringify([kind, detail.id, detail.revision, detail.submissionRevision, chosen.value, direction.value,
    members.value.map(item => item.id), reason, props.signatureFileId, reminders.value?.instanceRevision, reminder.value?.stepRevision])); }
  catch { feedback.value = t('workflow.todo.idempotencyUnavailable'); return; }
  working.value = true; emit('busy', true); feedback.value = '';
  try {
    if (kind === 'transfer') await props.client.transfer(detail.id, { assigneeUserId: chosen.value, expectedRevision: detail.revision,
      expectedFormRevision: detail.submissionRevision, comment: reason, signatureFileId: props.signatureFileId, idempotencyKey: key });
    else if (kind === 'return') await props.client.returnTodo(detail.id, { targetStepId: chosen.value, expectedRevision: detail.revision,
      expectedFormRevision: detail.submissionRevision, fieldPatch: {}, comment: reason, signatureFileId: props.signatureFileId, idempotencyKey: key });
    else if (kind === 'countersign') await props.client.countersign(detail.id, { directionKey: direction.value,
      assigneeUserIds: members.value.map(item => item.id), expectedRevision: detail.revision, comment: reason, idempotencyKey: key });
    else if (kind === 'cancel') await props.client.cancelCountersign(detail.id, { expectedRevision: detail.revision, comment: reason, idempotencyKey: key });
    else await props.client.remind(detail.instanceId, { stepId: reminder.value!.stepId,
      expectedInstanceRevision: reminders.value!.instanceRevision, expectedStepRevision: reminder.value!.stepRevision, idempotencyKey: key });
    if (current !== epoch) return;
    pending = undefined; if (kind === 'remind') { emit('refresh'); } else emit('completed');
  } catch (error) {
    if (current !== epoch) return;
    const failure = classifyWorkflowTodoActionFailure(error); if (!failure.retainIdempotencyKey) pending = undefined;
    feedback.value = t('workflow.todo.failed'); if (failure.refreshTodo) emit('refresh');
  } finally { if (current === epoch) { working.value = false; emit('busy', false); } }
}
watch(() => [props.detail.id, props.detail.revision, props.permissions.transfer, props.permissions.return,
  props.permissions.countersign, props.permissions.remind], () => { void initialize(); }, { immediate: true });
watch(action, () => { void initialize(); });
onBeforeUnmount(() => { epoch++; });
</script>
<template>
  <view class="management">
    <text v-if="unsaved">{{ t('workflow.todo.management.unsaved') }}</text>
    <view v-if="options.length">
      <view><button v-for="kind in options" :key="kind" :disabled="working || disabled" :aria-pressed="action === kind" @click="action = kind">{{ t(`workflow.todo.management.${kind}`) }}</button></view>
      <picker :range="names" :value="selectionIndex" :disabled="working || disabled || loading" @change="choose"><view>{{ selection[selectionIndex - 1]?.label ?? names[0] }}</view></picker>
      <view><button :disabled="page <= 1 || working || disabled || loading" @click="loadPage(page - 1)">{{ t('workflow.todo.previousPage') }}</button>
        <text>{{ page }}</text><button :disabled="!hasNext || working || disabled || loading" @click="loadPage(page + 1)">{{ t('workflow.todo.nextPage') }}</button></view>
      <button v-if="failed" :disabled="working || disabled || loading" @click="loadPage(page)">{{ t('workflow.todo.delegateRetry') }}</button>
      <template v-if="action === 'countersign'">
        <view><button v-for="key in (['before', 'after'] as const)" :key="key" :disabled="working || disabled" :aria-pressed="direction === key" @click="direction = key">{{ t(`workflow.todo.management.${key}`) }}</button></view>
        <button :disabled="!chosen || members.length >= 10 || working || disabled" @click="addMember">{{ t('workflow.todo.management.add') }}</button>
        <view v-for="(member, index) in members" :key="member.id"><text>{{ index + 1 }}. {{ member.label }}</text><button :disabled="working || disabled" @click="members = members.filter(item => item.id !== member.id)">{{ t('workflow.todo.management.remove') }}</button></view>
      </template>
      <button data-testid="workflow-management-submit" :disabled="!ready" @click="execute(action)">{{ t(`workflow.todo.management.${action}`) }}</button>
    </view>
    <view v-if="chain"><text>{{ t('workflow.todo.management.chain') }}</text><text v-for="item in chain.items" :key="item.itemId">{{ item.sequenceNo }}. {{ item.assigneeUserId }} · {{ t(`workflow.todo.management.${item.statusKey}`) }}</text></view>
    <view v-if="mayCancel"><uni-easyinput v-model="cancelComment" type="textarea" :disabled="working || disabled" :placeholder="t('workflow.todo.comment')" />
      <button data-testid="workflow-management-cancel" :disabled="working || disabled || unsaved || detail.commentRequired && !cancelComment.trim()" @click="execute('cancel')">{{ t('workflow.todo.management.cancel') }}</button></view>
    <button v-if="reminder" data-testid="workflow-management-remind" :disabled="working || disabled || unsaved" @click="execute('remind')">{{ t('workflow.todo.management.remind') }}</button>
    <text v-if="feedback" role="alert">{{ feedback }}</text>
    <button v-if="feedback" :disabled="working || disabled" @click="initialize">{{ t('workflow.todo.delegateRetry') }}</button>
  </view>
</template>
<style scoped>
.management { display: flex; flex-direction: column; gap: 12px; }
.management text { display: block; }
</style>
