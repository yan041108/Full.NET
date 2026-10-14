<script setup lang="ts">
import { computed, inject, reactive, ref, watch } from 'vue';
import { routeLocationKey, routerKey } from 'vue-router';
import {
  ElAlert,
  ElButton,
  ElCard,
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
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import { executeReportingDefinition } from '../api/reporting-executions';
import { listReportingPublishedDefinitions } from '../api/reporting-definitions';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { createReportingExportTask } from '../api/reporting-export-tasks';
import { showSuccess } from '../feedback/fullNetMessage';

defineOptions({ name: 'ReportingExecuteView' });

const { t } = useAdminI18n();
const session = useSessionStore();
const router = inject(routerKey, undefined); const route = inject(routeLocationKey, undefined);
const canExport = computed(() => !!router && route?.name === 'reporting-execute' && session.can('reporting.executions.run')
  && session.can('reporting.export_tasks.create') && session.can('reporting.export_tasks.read'));
const definitions = ref<ReportingPublishedDefinition[]>([]);
const selectedDefinitionVersionKey = ref('');
const parameterValues = reactive<Record<string, string>>({});
const result = ref<ReportingExecutionPage>();
const page = ref(1);
const pageSize = ref(50);
const loading = ref(false);
const executing = ref(false);
const exporting = ref(false);
const problem = ref<FullNetProblemDetails>();

// 同一定义的每个不可变发布版本都是独立授权资源，选择身份同时包含定义和版本。
const versionKey = (definition: ReportingPublishedDefinition) => definition.definitionId + ':' + definition.versionNumber;
const selectedDefinition = computed(() =>
  definitions.value.find(item => versionKey(item) === selectedDefinitionVersionKey.value));

const parameterSchema = computed(() => selectedDefinition.value?.parameterSchema ?? []);

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  updateTableHeight,
  watchLoading
} = useArtCrudTableLayout();

watchLoading(executing);
const scope = useAuthorizedViewScope(session, () => {
  definitions.value = []; selectedDefinitionVersionKey.value = ''; result.value = undefined; problem.value = undefined;
  page.value = 1; loading.value = false; executing.value = false; exporting.value = false; resetParameters();
}, loadDefinitions);
let executeRequest: ReturnType<typeof scope.begin>;
let exportRequest: ReturnType<typeof scope.begin>;
watch(() => route?.name, name => {
  if (name === 'reporting-execute') return;
  executeRequest?.cancel(); exportRequest?.cancel(); executing.value = false; exporting.value = false; result.value = undefined;
}, { flush: 'sync' });

async function loadDefinitions(): Promise<void> {
  const request = scope.begin('reporting.executions.run'); if (!request) return;
  loading.value = true;
  try {
    const values = await listReportingPublishedDefinitions(request.signal);
    if (!request.current()) return;
    definitions.value = values.filter((item, index, all) => all.findIndex(candidate => versionKey(candidate) === versionKey(item)) === index);
    selectedDefinitionVersionKey.value = definitions.value[0] ? versionKey(definitions.value[0]) : '';
    resetParameters();
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'reportingExecute.loadFailed');
  } finally {
    if (request.current()) loading.value = false; request.finish();
  }
}

function resetParameters(): void {
  for (const key of Object.keys(parameterValues)) {
    delete parameterValues[key];
  }
  for (const parameter of parameterSchema.value) {
    parameterValues[parameter.parameterKey] = parameter.defaultValue ?? '';
  }
}

function onDefinitionChanged(): void {
  executeRequest?.cancel(); executing.value = false; problem.value = undefined;
  exportRequest?.cancel(); exporting.value = false;
  resetParameters();
  result.value = undefined;
  page.value = 1;
}

async function runExecute(): Promise<void> {
  const definition = selectedDefinition.value;
  if (!definition || executing.value || exporting.value) {
    return;
  }
  const request = scope.begin('reporting.executions.run'); if (!request) return; executeRequest = request;
  executing.value = true;
  problem.value = undefined;
  // 新查询失败时不得把上一次结果继续呈现为本次输出。
  result.value = undefined;
  try {
    const parameters = currentParameters();
    const value = await executeReportingDefinition(
      definition.definitionId,
      { versionNumber: definition.versionNumber, parameters },
      page.value,
      pageSize.value,
      request.signal
    );
    if (!request.current()) return;
    // 运行时守卫只验证形状；内容交付前还须匹配本次选择的精确发布身份。
    if (value.definitionId !== definition.definitionId || value.versionNumber !== definition.versionNumber)
      throw new Error('client.invalid_reporting_execution_identity');
    result.value = value;
    updateTableHeight();
  } catch (error: unknown) {
    if (request.current()) problem.value = toProblem(error, 'reportingExecute.executeFailed');
  } finally {
    if (request.current()) executing.value = false; request.finish();
  }
}

// 两个入口共用参数规范化；导出是完整查询的后台执行，不是当前分页的客户端截图。
function currentParameters(): ReportingExecutionParameterValue[] {
  return parameterSchema.value.map(parameter => ({ parameterKey: parameter.parameterKey,
    value: parameterValues[parameter.parameterKey]?.trim() || null }));
}
async function exportCurrent(): Promise<void> {
  const definition = selectedDefinition.value;
  if (!definition || !canExport.value || executing.value || exporting.value || loading.value) return;
  const request = scope.begin('reporting.export_tasks.create'); if (!request) return; exportRequest = request;
  exporting.value = true; problem.value = undefined;
  try {
    const task = await createReportingExportTask({ definitionId: definition.definitionId, versionNumber: definition.versionNumber,
      formatKey: 'excel', parameters: currentParameters() }, request.signal);
    if (!request.current() || !canExport.value) return;
    if (task.definitionId !== definition.definitionId || task.versionNumber !== definition.versionNumber || task.formatKey !== 'excel')
      throw new Error('client.invalid_reporting_export_task_identity');
    showSuccess(t('reportingExportTasks.createSuccess'));
    void router!.push({ name: 'reporting-export-tasks', query: { taskId: task.id } });
  } catch (error) {
    if (request.current()) problem.value = toProblem(error, 'reportingExportTasks.createFailed');
  } finally { if (request.current()) exporting.value = false; request.finish(); }
}

async function onPageChanged(nextPage: number): Promise<void> {
  if (executing.value || exporting.value || !session.can('reporting.executions.run')) return;
  page.value = nextPage;
  await runExecute();
}

function cellValue(row: { values: Record<string, string | null> }, columnKey: string): string {
  return row.values[columnKey] ?? '';
}

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { status: 500, code: 'client.unexpected_error', title: t(fallbackKey) };
}
</script>

<template>
  <div class="reporting-execute-view">
    <ElAlert v-if="problem" type="error" :title="problem.title" show-icon class="mb-4" />

    <ElCard v-loading="loading">
      <ArtTableHeader :title="t('reportingExecute.title')" />
      <ElForm label-width="140px" class="execute-form">
        <ElFormItem :label="t('reportingExecute.fieldDefinition')" required>
          <ElSelect
            v-model="selectedDefinitionVersionKey"
            filterable
            data-testid="reporting-execute-definition"
            :disabled="executing || exporting"
            @change="onDefinitionChanged"
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
          v-for="parameter in parameterSchema"
          :key="parameter.parameterKey"
          :label="parameter.displayName"
          :required="parameter.isRequired"
        >
          <ElInput v-model="parameterValues[parameter.parameterKey]" :disabled="executing || exporting" />
        </ElFormItem>
        <ElFormItem>
          <PermissionGate code="reporting.executions.run"><ElButton
            type="primary"
            data-testid="reporting-execute-run"
            :loading="executing"
            :disabled="!selectedDefinition || exporting"
            @click="runExecute"
          >
            {{ t('reportingExecute.run') }}
          </ElButton></PermissionGate>
          <ElButton v-if="canExport" data-testid="reporting-execute-export" :loading="exporting"
            :disabled="!selectedDefinition || executing || loading" @click="exportCurrent">{{ t('reportingExecute.export') }}</ElButton>
        </ElFormItem>
      </ElForm>
    </ElCard>

    <ElCard v-if="result" class="result-card">
      <ArtTableHeader :title="t('reportingExecute.resultTitle')" />
      <div ref="tableMainRef" class="table-main">
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
      </div>
      <ElPagination
        v-if="result.hasMore || result.page > 1"
        class="mt-3"
        layout="prev, pager, next"
        :current-page="result.page"
        :disabled="executing"
        :page-size="result.pageSize"
        :total="result.totalRows ?? result.page * result.pageSize + (result.hasMore ? 1 : 0)"
        @current-change="onPageChanged"
      />
    </ElCard>
  </div>
</template>

<style scoped>
.execute-form {
  max-width: 720px;
}

.result-card {
  margin-top: 16px;
}

.table-main {
  margin-top: 12px;
}

.mb-4 {
  margin-bottom: 16px;
}

.mt-3 {
  margin-top: 12px;
}
</style>
