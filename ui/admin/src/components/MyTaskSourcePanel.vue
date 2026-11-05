<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { ElAlert, ElButton, ElCard, ElForm, ElFormItem, ElOption, ElSelect, ElTable, ElTableColumn, ElTag } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import { useBoundedPolling } from '../composables/useBoundedPolling';
import { readMyTaskSource, importTaskItem, reportingTaskItem, sourceDetailRoutes, sourceReadPermissions, sourceStatusKeys,
  taskActions, taskPending, type MyTaskAction, type MyTaskItem, type MyTaskSource } from '../tasks/myTasks';
import { getImportExportTask, executeImportExportTask, resumeImportExportTask, retryImportExportTask, downloadImportExportTaskErrorReceipt } from '../api/import-export-tasks';
import { getReportingExportTask, downloadReportingExportTask } from '../api/reporting-export-tasks';

const props = defineProps<{ source: MyTaskSource }>();
const session = useSessionStore(), router = useRouter(), { t } = useAdminI18n();
const items = ref<MyTaskItem[]>([]), status = ref('all'), loading = ref(false), actionLoading = ref(false), unavailable = ref(false);
const cursors = ref<string[]>(['']), pageIndex = ref(0), nextCursor = ref<string | null>(null);
let generation = 0, readVersion = 0; let requests = new AbortController(), readRequest: AbortController | undefined;
const scope = () => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId, session.currentUser?.permissions.join('|')].join('|');
const eligible = () => !!session.currentUser?.tenantId && !session.switching && session.can('platform.my_tasks.read')
  && session.currentUser.permissions.includes(sourceReadPermissions.get(props.source)!);
const heading = computed(() => t(props.source === 'imports' ? 'taskCenter.imports' : props.source === 'agents' ? 'taskCenter.agents' : 'taskCenter.reports'));
const states = computed(() => sourceStatusKeys.get(props.source) ?? []);
const currentCursor = () => cursors.value[pageIndex.value] || undefined;
async function read(signal: AbortSignal) {
  const actor = session.currentUser!;
  return readMyTaskSource(props.source, { userId: actor.id, tenantId: actor.tenantId! }, currentCursor(), status.value === 'all' ? undefined : status.value, signal);
}
const polling = useBoundedPolling({
  enabled: () => eligible() && !loading.value && !actionLoading.value && !unavailable.value,
  pending: () => items.value.some(taskPending),
  key: () => scope() + '|' + status.value + '|' + currentCursor(), intervalMs: 5000, read,
  accept: page => { items.value = page.items; nextCursor.value = page.nextCursor; },
  failed: () => { unavailable.value = true; items.value = []; nextCursor.value = null; }
});
const refreshPaused = polling.paused;
function reset() {
  generation++; readVersion++; requests.abort(); requests = new AbortController(); readRequest?.abort(); polling.stop();
  items.value = []; cursors.value = ['']; pageIndex.value = 0; nextCursor.value = null; loading.value = false; actionLoading.value = false; unavailable.value = false;
}
async function load() {
  if (!eligible()) return;
  const current = generation, version = ++readVersion;
  readRequest?.abort(); const controller = new AbortController(); readRequest = controller;
  const timeout = window.setTimeout(() => {
    if (current !== generation || version !== readVersion) return;
    readVersion++; controller.abort(); loading.value = false; unavailable.value = true;
    items.value = []; nextCursor.value = null; polling.stop();
  }, 30_000);
  polling.stop(); loading.value = true; unavailable.value = false;
  try {
    const page = await read(controller.signal);
    if (current === generation && version === readVersion && eligible()) { items.value = page.items; nextCursor.value = page.nextCursor; }
  } catch {
    if (current === generation && version === readVersion) { unavailable.value = true; items.value = []; nextCursor.value = null; }
  } finally {
    window.clearTimeout(timeout);
    if (current === generation && version === readVersion) { loading.value = false; polling.restart(); }
  }
}
function refresh() { if (actionLoading.value) return; cursors.value = ['']; pageIndex.value = 0; void load(); }
function changeStatus() { if (!actionLoading.value) refresh(); }
function next() {
  if (!nextCursor.value || loading.value || actionLoading.value) return;
  cursors.value = [...cursors.value.slice(0, pageIndex.value + 1), nextCursor.value]; pageIndex.value++; void load();
}
function previous() { if (pageIndex.value > 0 && !loading.value && !actionLoading.value) { pageIndex.value--; void load(); } }
function statusLabel(key: string) {
  const message = props.source === 'agents' ? `taskCenter.agentStatus.${key}`
    : `${props.source === 'imports' ? 'importExportTasks' : 'reportingExportTasks'}.status.${key}`;
  const translated = translateRuntimeMessage(t, message); return translated === message ? key : translated;
}
function tagType(key: string) {
  if (key.includes('failed')) return 'danger'; if (key === 'execution_partial') return 'warning';
  return key.includes('succeeded') || key === 'completed' ? 'success' : 'info';
}
function download(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob), anchor = document.createElement('a');
  anchor.href = url; anchor.download = filename; anchor.click(); URL.revokeObjectURL(url);
}
async function act(task: MyTaskItem, action: MyTaskAction) {
  if (!eligible() || actionLoading.value || !taskActions(task, session.can).includes(action)) return;
  if (action === 'detail' || action === 'cancel') {
    await router.push({ name: sourceDetailRoutes.get(props.source)!, query: { taskId: task.id } }); return;
  }
  const current = generation, actor = session.currentUser!; actionLoading.value = true; polling.stop();
  try {
    // 操作前读取来源权威状态，状态变化只刷新，不猜测成功或重放写入。
    if (props.source === 'imports') {
      const detail = await getImportExportTask(task.id, requests.signal);
      if (current !== generation || !eligible()) return;
      if (detail.requestedByUserId !== actor.id || detail.tenantId !== actor.tenantId || !taskActions(importTaskItem(detail), session.can).includes(action))
        throw new Error('client.task_state_changed');
      if (action === 'errors') {
        const blob = await downloadImportExportTaskErrorReceipt(task.id, requests.signal);
        if (current === generation && eligible() && session.can('import_export.import_tasks.execute')) download(blob, detail.sourceFileName?.replace(/\.xlsx$/i, '-errors.xlsx') ?? 'import-errors.xlsx');
      } else {
        const execute = action === 'execute' ? executeImportExportTask : action === 'resume' ? resumeImportExportTask : retryImportExportTask;
        await execute(task.id, requests.signal);
        if (current === generation && eligible()) showSuccess(t('importExportTasks.actionSuccess'));
      }
    } else if (props.source === 'reports') {
      const detail = await getReportingExportTask(task.id, requests.signal);
      if (current !== generation || !eligible()) return;
      if (detail.requestedByUserId !== actor.id || !taskActions(reportingTaskItem(detail), session.can).includes(action)) throw new Error('client.task_state_changed');
      const blob = await downloadReportingExportTask(task.id, requests.signal);
      if (current === generation && eligible() && session.can('reporting.export_tasks.download')) download(blob, detail.outputFileName ?? 'reporting-export.xlsx');
    }
  } catch (error) { if (current === generation) showProblem(error, t('taskCenter.actionFailed')); }
  finally { if (current === generation) { actionLoading.value = false; await load(); } }
}
watch(() => [scope(), eligible()], () => { reset(); if (eligible()) void load(); }, { immediate: true, flush: 'sync' });
onBeforeUnmount(reset);
</script>

<template>
  <ElCard shadow="never" :data-testid="`my-tasks-${source}`" class="my-task-source">
    <template #header><div class="source-header"><h2>{{ heading }}</h2><ElButton :loading="loading" :disabled="actionLoading" data-testid="my-tasks-refresh" @click="refresh">{{ t('common.refresh') }}</ElButton></div></template>
    <ElForm inline @submit.prevent>
      <ElFormItem :label="t('taskCenter.status')">
        <ElSelect v-model="status" :aria-label="t('taskCenter.status')" :disabled="loading || actionLoading" data-testid="my-tasks-status" style="width: 180px" @change="changeStatus">
          <ElOption value="all" :label="t('taskCenter.allStates')" /><ElOption v-for="key in states" :key="key" :value="key" :label="statusLabel(key)" />
        </ElSelect>
      </ElFormItem>
    </ElForm>
    <ElAlert v-if="unavailable" type="error" :closable="false" :title="t('taskCenter.sourceUnavailable')" class="mb-4" />
    <ElAlert v-else-if="refreshPaused" type="info" :closable="false" :title="t('taskCenter.refreshPaused')" class="mb-4" />
    <div class="art-table"><ElTable v-loading="loading" :data="items" row-key="id" :empty-text="t('taskCenter.empty')">
      <ElTableColumn prop="title" :label="t('taskCenter.task')" min-width="220" show-overflow-tooltip />
      <ElTableColumn prop="businessType" :label="t('taskCenter.businessType')" min-width="160" show-overflow-tooltip />
      <ElTableColumn prop="statusKey" :label="t('taskCenter.status')" width="130"><template #default="{ row }"><ElTag :type="tagType(row.statusKey)" :class="{ 'task-canceled-tag': row.statusKey === 'canceled' }">{{ statusLabel(row.statusKey) }}</ElTag></template></ElTableColumn>
      <ElTableColumn :label="t('taskCenter.progress')" min-width="110"><template #default="{ row }">{{ row.progress ? `${row.progress.processed} / ${row.progress.total}` : t('taskCenter.unknown') }}</template></ElTableColumn>
      <ElTableColumn :label="t('taskCenter.expiry')" min-width="130"><template #default>{{ t('taskCenter.notProvided') }}</template></ElTableColumn>
      <ElTableColumn prop="createdAtUtc" :label="t('taskCenter.createdAt')" min-width="190" show-overflow-tooltip />
      <!-- @vue-generic {MyTaskItem} -->
      <ElTableColumn :label="t('taskCenter.actions')" :min-width="source === 'agents' ? 100 : 220" fixed="right"><template #default="{ row }">
        <ElButton v-for="action in taskActions(row, session.can)" :key="action" link type="primary" :disabled="actionLoading || loading || unavailable"
          :data-testid="`my-tasks-${action}`" @click="act(row, action)">{{ translateRuntimeMessage(t, `taskCenter.action.${action}`) }}</ElButton>
      </template></ElTableColumn>
    </ElTable></div>
    <div class="source-pagination">
      <ElButton :disabled="pageIndex === 0 || loading || actionLoading" data-testid="my-tasks-previous" @click="previous">{{ t('taskCenter.previous') }}</ElButton>
      <span>{{ t('taskCenter.page', { page: pageIndex + 1 }) }}</span>
      <ElButton :disabled="!nextCursor || loading || actionLoading" data-testid="my-tasks-next" @click="next">{{ t('taskCenter.next') }}</ElButton>
    </div>
  </ElCard>
</template>

<style scoped>
.task-canceled-tag { color: var(--el-text-color-primary); background-color: var(--el-fill-color-light); border-color: var(--el-border-color); }
.my-task-source { min-width: 0; }
.source-header, .source-pagination { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
.source-header h2 { margin: 0; font-size: 16px; }
.source-pagination { justify-content: flex-end; flex-wrap: wrap; margin-top: 16px; }
.mb-4 { margin-bottom: 16px; }
</style>
