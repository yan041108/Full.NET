<script setup lang="ts">
import { computed, inject, onActivated, onBeforeUnmount, onDeactivated, onMounted, reactive, ref, watch } from 'vue';
import { routeLocationKey, routerKey } from 'vue-router';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn
} from 'element-plus';
import type {
  FullNetProblemDetails,
  ReportingPublishedDefinition,
  ReportingExecutionPage,
  ReportingExecutionParameterValue
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import { useAdminI18n } from '../i18n/adminI18n';
import { executeReportingDefinition } from '../api/reporting-executions';
import { listReportingPublishedDefinitions } from '../api/reporting-definitions';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { isTenantSessionContext } from '../auth/tenant-session-context';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { createReportingExportTask } from '../api/reporting-export-tasks';
import { showSuccess } from '../feedback/fullNetMessage';

defineOptions({ name: 'ReportingExecuteView' });

const { t } = useAdminI18n();
const session = useSessionStore();
const router = inject(routerKey, undefined); const route = inject(routeLocationKey, undefined);
const pageActive = ref(false), owner = ref(0);
onMounted(() => { pageActive.value = true; }); onActivated(() => { pageActive.value = true; });
onDeactivated(() => { pageActive.value = false; }); onBeforeUnmount(() => { pageActive.value = false; });
const canRun = () => pageActive.value && (!route || route.name === 'reporting-execute') && session.can('reporting.executions.run')
  && (session.currentUser?.scope === 'host' && session.currentUser.tenantId === null || isTenantSessionContext(session.currentUser));
const canExport = computed(() => canRun() && !!router && route?.name === 'reporting-execute'
  && session.can('reporting.export_tasks.create') && session.can('reporting.export_tasks.read'));
const definitions = ref<ReportingPublishedDefinition[]>([]);
const selectedDefinitionVersionKey = ref('');
// 参数机器键可以与对象成员同名，Map 同时保证特殊键读取参与响应式失效。
const parameterValues = reactive(new Map<string, string>());
const result = ref<ReportingExecutionPage>();
const page = ref(1);
const pageSize = ref(50);
const loading = ref(false);
const executing = ref(false);
const exporting = ref(false);
const problem = ref<FullNetProblemDetails>();
const definitionsProblem = ref<FullNetProblemDetails>(), navigationProblem = ref<FullNetProblemDetails>();
const busy = computed(() => loading.value || executing.value || exporting.value);
type Receipt = { sequence: number; definitionId: string; versionNumber: number; taskId?: string; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed'; errorCode?: string };
const receipt = ref<Receipt>(); let receiptSequence = 0, recoverySequence = 0;
const exportBlocks = ref(new Map<string, Receipt>());
const recoveryConfirmation = ref<{ token: number; confirm: () => void; update: (open: boolean) => void }>();
let exportingIntent: { key: string; receipt: Receipt } | undefined;

// 同一定义的每个不可变发布版本都是独立授权资源，选择身份同时包含定义和版本。
const versionKey = (definition: ReportingPublishedDefinition) => definition.definitionId.toLowerCase() + ':' + definition.versionNumber;
const selectedDefinition = computed(() =>
  definitions.value.find(item => versionKey(item) === selectedDefinitionVersionKey.value));

const parameterSchema = computed(() => selectedDefinition.value?.parameterSchema ?? []);
const intent = computed(() => JSON.stringify([selectedDefinition.value, parameterSchema.value.map(parameter => [parameter.parameterKey, parameterValues.get(parameter.parameterKey) ?? ''])]));
// 重复写入按实际提交值匹配，不能用空白差异绕过结果不确定时的人工核对。
const exportKey = computed(() => selectedDefinition.value ? JSON.stringify([versionKey(selectedDefinition.value), 'excel', currentParameters()]) : '');
const exportBlocked = computed(() => exportBlocks.value.has(exportKey.value));

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  syncTableLayout
} = useArtPagedTableInCard(executing);

const scope = useAuthorizedViewScope(session, () => {
  definitions.value = []; selectedDefinitionVersionKey.value = ''; result.value = undefined; problem.value = undefined;
  page.value = 1; loading.value = false; executing.value = false; exporting.value = false; resetParameters();
  definitionsProblem.value = undefined; navigationProblem.value = undefined; receipt.value = undefined;
  exportBlocks.value = new Map(); exportingIntent = undefined; recoveryConfirmation.value = undefined; owner.value++;
}, loadDefinitions);
let definitionsRequest: ReturnType<typeof scope.begin>;
let executeRequest: ReturnType<typeof scope.begin>;
let exportRequest: ReturnType<typeof scope.begin>;
function cancelExecution(): void { executeRequest?.cancel(); executeRequest = undefined; executing.value = false; }
function cancelExport(): void {
  if (exportRequest?.current() && exportingIntent) {
    const value: Receipt = { ...exportingIntent.receipt, outcome: 'unconfirmed' };
    exportBlocks.value = new Map(exportBlocks.value).set(exportingIntent.key, value); receipt.value = value;
  }
  exportRequest?.cancel(); exportRequest = undefined; exportingIntent = undefined; exporting.value = false;
}
watch(selectedDefinitionVersionKey, resetParameters, { flush: 'sync' });
watch(() => JSON.stringify(selectedDefinition.value), resetParameters, { flush: 'sync' });
watch(intent, () => {
  cancelExecution(); cancelExport(); result.value = undefined; problem.value = undefined; navigationProblem.value = undefined;
  page.value = 1; owner.value++; recoveryConfirmation.value = undefined;
}, { flush: 'sync' });
watch(() => route?.name, name => {
  if (name === 'reporting-execute') { if (pageActive.value) void loadDefinitions(); return; }
  cancelExecution(); cancelExport(); definitionsRequest?.cancel(); loading.value = false; owner.value++;
  definitions.value = []; selectedDefinitionVersionKey.value = ''; result.value = undefined; recoveryConfirmation.value = undefined;
}, { flush: 'sync' });
const controls = computed(() => {
  const ticket = owner.value, query = intent.value, output = result.value;
  const owned = () => ticket === owner.value && query === intent.value && canRun();
  return { reload: () => { if (owned() && !busy.value) void loadDefinitions(); }, run: () => { if (owned()) void runExecute(); }, export: () => { if (owned()) void exportCurrent(); },
    select: (value: string) => { if (owned() && !busy.value) selectedDefinitionVersionKey.value = value; },
    parameters: parameterSchema.value.map(parameter => ({ ...parameter, input: (value: string) => { if (owned() && !busy.value) parameterValues.set(parameter.parameterKey, value); } })),
    page: (value: number, size: number) => { if (owned() && output && result.value === output && size === output.pageSize) void onPageChanged(value); },
    recover: () => { if (owned()) acknowledgeExport(); }, open: () => { if (owned() && receipt.value?.outcome === 'succeeded') void openExport(receipt.value); } };
});

async function loadDefinitions(): Promise<void> {
  if (!canRun() || executing.value || exporting.value) return;
  definitionsRequest?.cancel(); const request = scope.begin('reporting.executions.run'); if (!request) return; definitionsRequest = request;
  loading.value = true; definitionsProblem.value = undefined; owner.value++;
  try {
    const values = await listReportingPublishedDefinitions(request.signal);
    if (!request.current()) return;
    if (new Set(values.map(versionKey)).size !== values.length || values.some(row => !isGuid(row.definitionId) || row.definitionId === '00000000-0000-0000-0000-000000000000' || !Number.isSafeInteger(row.versionNumber) || row.versionNumber < 1
      || row.parameterSchema.some(parameter => !parameter.parameterKey.trim()) || new Set(row.parameterSchema.map(parameter => parameter.parameterKey)).size !== row.parameterSchema.length)) throw new Error('client.invalid_reporting_definition_list');
    const previous = selectedDefinitionVersionKey.value;
    definitions.value = values;
    selectedDefinitionVersionKey.value = definitions.value.some(item => versionKey(item) === previous) ? previous : definitions.value[0] ? versionKey(definitions.value[0]) : '';
  } catch (error: unknown) {
    if (request.current()) definitionsProblem.value = toProblem(error, 'reportingExecute.loadFailed');
  } finally {
    if (request.current()) loading.value = false; request.finish();
  }
}

function resetParameters(): void {
  parameterValues.clear();
  for (const parameter of parameterSchema.value) {
    parameterValues.set(parameter.parameterKey, parameter.defaultValue ?? '');
  }
}

async function runExecute(): Promise<void> {
  const definition = selectedDefinition.value;
  if (!definition || !canRun() || busy.value || !Number.isSafeInteger(page.value) || page.value < 1 || page.value > 2147483647) {
    return;
  }
  const query = { definitionId: definition.definitionId, versionNumber: definition.versionNumber, queryPortKey: definition.queryPortKey, parameters: currentParameters(), page: page.value, pageSize: pageSize.value };
  const request = scope.begin('reporting.executions.run'); if (!request) return; executeRequest = request; owner.value++;
  executing.value = true;
  problem.value = undefined;
  // 新查询失败时不得把上一次结果继续呈现为本次输出。
  result.value = undefined;
  try {
    const value = await executeReportingDefinition(
      query.definitionId,
      { versionNumber: query.versionNumber, parameters: query.parameters },
      query.page,
      query.pageSize,
      request.signal
    );
    if (!request.current()) return;
    // 运行时守卫只验证形状；内容交付前还须匹配本次选择的精确发布身份。
    if (value.definitionId.toLowerCase() !== query.definitionId.toLowerCase() || value.versionNumber !== query.versionNumber || value.queryPortKey !== query.queryPortKey
      || value.page !== query.page || value.pageSize !== query.pageSize)
      throw new Error('client.invalid_reporting_execution_identity');
    result.value = value;
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'reportingExecute.executeFailed');
  } finally {
    if (executeRequest === request) { if (request.current()) executing.value = false; executeRequest = undefined; } request.finish(); void syncTableLayout();
  }
}

// 两个入口共用参数规范化；导出是完整查询的后台执行，不是当前分页的客户端截图。
function currentParameters(): ReportingExecutionParameterValue[] {
  return parameterSchema.value.map(parameter => ({ parameterKey: parameter.parameterKey,
    value: parameterValues.get(parameter.parameterKey)?.trim() || null }));
}
async function exportCurrent(): Promise<void> {
  const definition = selectedDefinition.value;
  if (!definition || !canExport.value || busy.value || exportBlocked.value) return;
  const request = scope.begin('reporting.export_tasks.create'); if (!request) return; exportRequest = request;
  exporting.value = true; problem.value = undefined; navigationProblem.value = undefined; owner.value++;
  receipt.value = { sequence: ++receiptSequence, definitionId: definition.definitionId, versionNumber: definition.versionNumber, outcome: 'processing' };
  const attempt = { key: exportKey.value, receipt: { ...receipt.value } }; exportingIntent = attempt;
  try {
    const task = await createReportingExportTask({ definitionId: definition.definitionId, versionNumber: definition.versionNumber,
      formatKey: 'excel', parameters: currentParameters() }, request.signal);
    if (!request.current() || !canExport.value) return;
    if (task.definitionId.toLowerCase() !== definition.definitionId.toLowerCase() || task.versionNumber !== definition.versionNumber || task.formatKey !== 'excel'
      || !isGuid(task.id) || task.id === '00000000-0000-0000-0000-000000000000' || task.requestedByUserId.toLowerCase() !== session.currentUser?.id.toLowerCase())
      throw new Error('client.invalid_reporting_export_task_identity');
    receipt.value = { ...attempt.receipt, taskId: task.id, outcome: 'succeeded' };
    exportBlocks.value = new Map(exportBlocks.value).set(attempt.key, receipt.value);
    // 成功回包已经确认创建；后续导航失败不能降级写入事实，也不能自动再发 POST。
    exportingIntent = undefined; exportRequest = undefined; exporting.value = false;
    showSuccess(t('reportingExportTasks.createSuccess'));
    await openExport(receipt.value);
  } catch (error) {
    if (exportRequest === request && request.current()) {
      const failure = toProblem(error, 'reportingExportTasks.createFailed');
      const unknown = !isFullNetProblemDetails(error) || error.status < 400 || error.status >= 500 || error.status === 408;
      receipt.value = { ...attempt.receipt, outcome: unknown ? 'unconfirmed' : 'failed', errorCode: failure.code };
      if (unknown) exportBlocks.value = new Map(exportBlocks.value).set(attempt.key, receipt.value);
      problem.value = failure;
    }
  } finally { if (exportRequest === request) { if (request.current()) exporting.value = false; exportRequest = undefined; exportingIntent = undefined; } request.finish(); }
}
async function openExport(value: Receipt): Promise<void> {
  if (!canExport.value || !value.taskId || value.outcome !== 'succeeded' || receipt.value?.sequence !== value.sequence) return;
  const ticket = owner.value; navigationProblem.value = undefined;
  try { const failure = await router!.push({ name: 'reporting-export-tasks', query: { taskId: value.taskId } }); if (failure) throw failure; }
  catch (error) { if (canExport.value && owner.value === ticket && receipt.value?.sequence === value.sequence) navigationProblem.value = toProblem(error, 'reportingExecute.navigationFailed'); }
}
function acknowledgeExport(): void {
  if (!canExport.value || busy.value || !exportBlocked.value) return;
  const key = exportKey.value, sequence = exportBlocks.value.get(key)!.sequence, ticket = owner.value, token = ++recoverySequence;
  const owned = () => canExport.value && !busy.value && ticket === owner.value && key === exportKey.value && recoveryConfirmation.value?.token === token && exportBlocks.value.get(key)?.sequence === sequence;
  recoveryConfirmation.value = { token, confirm: () => { if (owned()) { const next = new Map(exportBlocks.value); next.delete(key); exportBlocks.value = next; recoveryConfirmation.value = undefined; } }, update: open => { if (!open && owned()) recoveryConfirmation.value = undefined; } };
}

async function onPageChanged(nextPage: number): Promise<void> {
  if (busy.value || !canRun() || !Number.isSafeInteger(nextPage) || nextPage < 1 || nextPage > 2147483647 || nextPage === page.value) return;
  page.value = nextPage;
  await runExecute();
}

function cellValue(row: { values: Record<string, string | null> }, columnKey: string): string {
  return Object.hasOwn(row.values, columnKey) ? row.values[columnKey] ?? '' : '';
}

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { status: 500, code: 'client.unexpected_error', title: t(fallbackKey) };
}
</script>

<template>
  <div class="reporting-execute-view art-page-stack art-full-height">
    <ElAlert v-if="problem" type="error" :title="problem.title" show-icon class="mb-4" />
    <ElAlert v-if="navigationProblem" data-testid="reporting-execute-navigation-problem" type="error" :title="navigationProblem.title" show-icon />
    <div v-if="receipt" role="status" data-testid="reporting-execute-export-receipt">
      <p>{{ t('reportingExportTasks.receiptTarget', { definition: receipt.definitionId, version: String(receipt.versionNumber) }) }} · {{ t(receipt.outcome === 'succeeded' ? 'reportingExportTasks.resultSucceeded' : receipt.outcome === 'failed' ? 'reportingExportTasks.resultFailed' : receipt.outcome === 'processing' ? 'reportingExportTasks.resultProcessing' : 'reportingExportTasks.resultUnconfirmed') }}</p>
      <code v-if="receipt.taskId" translate="no">{{ receipt.taskId }}</code> <code v-if="receipt.errorCode" translate="no">{{ receipt.errorCode }}</code>
      <ElButton v-if="canExport && receipt.outcome === 'succeeded'" data-testid="reporting-execute-open-export" v-on="{ click: controls.open }">{{ t('reportingExecute.openExport') }}</ElButton>
    </div>

    <ElCard v-loading="loading">
      <ArtTableHeader :title="t('reportingExecute.title')" />
      <ElAlert v-if="definitionsProblem" data-testid="reporting-execute-definitions-problem" type="error" :title="definitionsProblem.title" show-icon />
      <ElButton v-if="canRun()" data-testid="reporting-execute-definitions-refresh" :disabled="busy" :loading="loading" v-on="{ click: controls.reload }">{{ t('reportingExecute.reloadDefinitions') }}</ElButton>
      <ElForm label-width="140px" class="execute-form">
        <ElFormItem :label="t('reportingExecute.fieldDefinition')" required>
          <ElSelect
            :model-value="selectedDefinitionVersionKey"
            filterable
            data-testid="reporting-execute-definition"
            :disabled="busy"
            v-on="{ 'update:modelValue': controls.select }"
          >
            <ElOption
              v-for="definition in definitions"
              :key="versionKey(definition)"
              :label="`${definition.name} (${definition.definitionKey}) · v${definition.versionNumber}`"
              :value="versionKey(definition)"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem
          v-for="parameter in controls.parameters"
          :key="parameter.parameterKey"
          :label="parameter.displayName"
          :required="parameter.isRequired"
        >
          <ElInput :model-value="parameterValues.get(parameter.parameterKey) ?? ''" :disabled="busy" v-on="{ 'update:modelValue': parameter.input }" />
        </ElFormItem>
        <ElFormItem>
          <PermissionGate code="reporting.executions.run"><ElButton
            type="primary"
            data-testid="reporting-execute-run"
            :loading="executing"
            :disabled="!selectedDefinition || loading || exporting"
            v-on="{ click: controls.run }"
          >
            {{ t('reportingExecute.run') }}
          </ElButton></PermissionGate>
          <ElButton v-if="canExport" data-testid="reporting-execute-export" :loading="exporting"
            :disabled="!selectedDefinition || executing || loading || exportBlocked" v-on="{ click: controls.export }">{{ t('reportingExecute.export') }}</ElButton>
          <ElButton v-if="canExport && exportBlocked" data-testid="reporting-execute-export-recovery" :disabled="busy" v-on="{ click: controls.recover }">{{ t('reportingExecute.reviewExport') }}</ElButton>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <ElCard v-if="result" class="result-card art-table-card art-full-height">
      <ArtTableHeader data-testid="reporting-execute-result-header" :title="t('reportingExecute.resultTitle')" />
      <div ref="tableMainRef" class="art-crud-table-main">
        <ElTable
          v-loading="executing"
          :data="result.rows"
          :height="tableHeight"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
        >
          <!-- @vue-generic {{ values: Record<string, string | null> }} -->
          <ElTableColumn
            v-for="column in result.columns"
            :key="column.columnKey"
            :prop="column.columnKey"
            :label="column.displayName"
            min-width="160"
          >
            <template #default="{ row }">
              {{ cellValue(row, column.columnKey) }}
            </template>
          </ElTableColumn>
        </ElTable>
        <ElPagination
          v-if="result.hasMore || result.page > 1"
          class="art-table-pagination"
          layout="prev, pager, next"
          :current-page="result.page"
          :disabled="busy"
          :page-size="result.pageSize"
          :total="result.totalRows ?? result.page * result.pageSize + (result.hasMore ? 1 : 0)"
          v-on="{ change: controls.page }"
        />
      </div>
    </ElCard>
    <ElDialog :model-value="!!recoveryConfirmation" :title="t('reportingExecute.reviewExport')" width="480px" v-on="{ 'update:modelValue': recoveryConfirmation?.update }">
      <p>{{ t('reportingExecute.exportRecoveryHint') }}</p>
      <template #footer><ElButton type="warning" data-testid="reporting-execute-export-recovery-confirm" v-on="{ click: recoveryConfirmation?.confirm }">{{ t('reportingExecute.exportRecoveryConfirm') }}</ElButton></template>
    </ElDialog>
  </div>
</template>

<style scoped>
.execute-form {
  max-width: 720px;
}

.result-card {
  margin-top: 16px;
}

.mb-4 {
  margin-bottom: 16px;
}

</style>
