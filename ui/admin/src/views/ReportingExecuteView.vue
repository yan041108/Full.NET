<script setup lang="ts">
import { computed, onActivated, onDeactivated, onBeforeUnmount, reactive, ref, watch } from 'vue';
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
  ReportingDefinitionVersion,
  ReportingDefinitionSummary,
  ReportingExecutionPage,
  ReportingExecutionParameterValue
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import { useSessionStore } from '../auth/session';
import { reportingExecutionText } from '../i18n/reporting-published-execution';
import { executeReportingDefinition, getReportingExecutionVersion } from '../api/reporting-executions';
import { listReportingDefinitions } from '../api/reporting-definitions';

defineOptions({ name: 'ReportingExecuteView' });

const { t, locale } = useAdminI18n();
const session = useSessionStore(), active = ref(true);
const text = (key: Parameters<typeof reportingExecutionText>[1]) => reportingExecutionText(locale.value, key);
const canRead = () => active.value && !session.switching && session.currentUser?.actorScope === 'host' && !session.currentUser.tenantId
  && session.can('reporting.executions.run') && session.can('reporting.definitions.read');
let generation = 0, listRequest: AbortController | undefined, versionRequest: AbortController | undefined, executionRequest: AbortController | undefined;
function current(scope: number) { return scope === generation && canRead(); }
const definitions = ref<ReportingDefinitionSummary[]>([]);
const selectedDefinition = ref<ReportingDefinitionVersion>();
const selectedSummary = ref<ReportingDefinitionSummary>();
const selectedDefinitionId = ref('');
const parameterValues = reactive<Record<string, string>>({});
const result = ref<ReportingExecutionPage>();
const page = ref(1);
const pageSize = ref(50);
const loading = ref(false);
const executing = ref(false);
const problem = ref<FullNetProblemDetails>();
type ExecutionInput = { definitionId: string; versionNumber: number; parameters: ReportingExecutionParameterValue[]; pageSize: number };
const executedInput = ref<ExecutionInput>();
const selectableDefinitions = computed(() => selectedSummary.value && !definitions.value.some(item => item.id === selectedSummary.value!.id) ? [selectedSummary.value, ...definitions.value] : definitions.value);
const inputParameters = () => parameterSchema.value.map(parameter => ({ parameterKey: parameter.parameterKey, value: parameterValues[parameter.parameterKey]?.trim() || null }));
const inputChanged = computed(() => !!executedInput.value && JSON.stringify(executedInput.value.parameters) !== JSON.stringify(inputParameters()));

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

/** 各读取拥有独立取消源，切上下文同时使所有迟到结果失效。 */
function reset() {
  generation++; listRequest?.abort(); versionRequest?.abort(); executionRequest?.abort();
  definitions.value = []; selectedDefinitionId.value = ''; selectedDefinition.value = undefined; selectedSummary.value = undefined;
  clearParameters(); result.value = undefined; executedInput.value = undefined; page.value = 1; loading.value = false; executing.value = false; problem.value = undefined;
}
function clearParameters() { for (const key of Object.keys(parameterValues)) delete parameterValues[key]; }
async function reloadPublished() {
  if (!canRead() || executing.value) return;
  const scope = generation;
  const previous = selectedDefinitionId.value; selectedDefinitionId.value = ''; selectedDefinition.value = undefined; selectedSummary.value = undefined;
  clearParameters(); result.value = undefined; executedInput.value = undefined;
  const rows = await searchDefinitions('');
  if (current(scope) && rows) selectedDefinitionId.value = rows.find(item => item.id === previous)?.id ?? rows[0]?.id ?? '';
}
async function searchDefinitions(keyword: string): Promise<ReportingDefinitionSummary[] | undefined> {
  if (!canRead()) return;
  const scope = generation, controller = new AbortController(); listRequest?.abort(); listRequest = controller; loading.value = true; problem.value = undefined;
  try {
    const value = await listReportingDefinitions({ nameContains: keyword, page: 1, pageSize: 20 }, controller.signal);
    if (!current(scope) || controller.signal.aborted) return;
    const rows = value.items.filter(item => item.isEnabled && item.latestPublishedVersionNumber > 0); definitions.value = rows; return rows;
  } catch (error) { if (current(scope) && !controller.signal.aborted) problem.value = toProblem(error, 'reportingExecute.loadFailed'); }
  finally { if (current(scope) && !controller.signal.aborted) loading.value = false; }
}

function resetParameters(): void {
  clearParameters();
  for (const parameter of parameterSchema.value) {
    parameterValues[parameter.parameterKey] = parameter.defaultValue ?? '';
  }
}

async function onDefinitionChanged(): Promise<void> {
  versionRequest?.abort(); executionRequest?.abort(); executing.value = false; selectedDefinition.value = undefined; clearParameters();
  result.value = undefined; executedInput.value = undefined; page.value = 1;
  const id = selectedDefinitionId.value, summary = selectableDefinitions.value.find(item => item.id === id); selectedSummary.value = summary;
  if (!canRead() || !summary || !id) return;
  const scope = generation, controller = new AbortController(); versionRequest = controller;
  try {
    const value = await getReportingExecutionVersion(id, summary.latestPublishedVersionNumber, controller.signal);
    if (current(scope) && !controller.signal.aborted && selectedDefinitionId.value === id) { selectedDefinition.value = value; resetParameters(); }
  } catch (error) { if (current(scope) && !controller.signal.aborted) problem.value = toProblem(error, 'reportingExecute.loadFailed'); }
}

async function runExecute(): Promise<void> {
  if (!canRead() || !selectedDefinition.value || executing.value) return;
  if (parameterSchema.value.some(parameter => parameter.isRequired && !parameterValues[parameter.parameterKey]?.trim())) {
    problem.value = { status: 400, code: 'client.validation_failed', title: text('required') }; return;
  }
  await execute({ definitionId: selectedDefinitionId.value, versionNumber: selectedDefinition.value.versionNumber, parameters: inputParameters(), pageSize: pageSize.value }, 1);
}
async function execute(input: ExecutionInput, nextPage: number) {
  if (!canRead() || executing.value || selectedDefinitionId.value !== input.definitionId) return;
  const scope = generation, controller = new AbortController(); executionRequest?.abort(); executionRequest = controller; executing.value = true;
  problem.value = undefined;
  try {
    const value = await executeReportingDefinition(input.definitionId, { versionNumber: input.versionNumber, parameters: input.parameters.map(parameter => ({ ...parameter })) }, nextPage, input.pageSize, controller.signal);
    if (!current(scope) || controller.signal.aborted || selectedDefinitionId.value !== input.definitionId || selectedDefinition.value?.versionNumber !== input.versionNumber) return;
    result.value = value; executedInput.value = input; page.value = nextPage;
    updateTableHeight();
  } catch (error: unknown) {
    if (current(scope) && !controller.signal.aborted) { result.value = undefined; executedInput.value = undefined; problem.value = toProblem(error, 'reportingExecute.executeFailed'); }
  } finally {
    if (current(scope) && !controller.signal.aborted) executing.value = false;
  }
}

async function onPageChanged(nextPage: number): Promise<void> {
  if (executedInput.value && Number.isSafeInteger(nextPage) && nextPage > 0) await execute(executedInput.value, nextPage);
}
watch(selectedDefinitionId, onDefinitionChanged, { flush: 'sync' });
watch(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.actorScope, session.currentUser?.sessionId, session.currentUser?.permissions.join('|'), session.switching, active.value], () => {
  reset(); if (canRead()) void reloadPublished();
}, { immediate: true, flush: 'sync' });
onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; reset(); });

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
    <ElAlert v-if="!canRead()" type="info" :closable="false" :title="text('hostOnly')" />
    <template v-if="canRead()">
    <ElAlert v-if="problem" type="error" :title="problem.title" show-icon class="mb-4" />
    <ElButton :loading="loading" :disabled="executing" data-testid="reporting-execute-reload" @click="reloadPublished">{{ text('reload') }}</ElButton>

    <ElCard v-loading="loading">
      <ArtTableHeader :title="t('reportingExecute.title')" />
      <ElForm label-position="top" class="execute-form">
        <ElFormItem :label="t('reportingExecute.fieldDefinition')">
          <ElSelect
            v-model="selectedDefinitionId"
            filterable remote :remote-method="searchDefinitions"
            data-testid="reporting-execute-definition"
            class="definition-select"
          >
            <ElOption
              v-for="definition in selectableDefinitions"
              :key="definition.id"
              :label="`${definition.name} (${definition.definitionKey})`"
              :value="definition.id"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem
          v-for="parameter in parameterSchema"
          :key="parameter.parameterKey"
          :label="parameter.displayName"
        >
          <ElInput v-model="parameterValues[parameter.parameterKey]" :disabled="executing" :data-parameter-key="parameter.parameterKey" :aria-required="parameter.isRequired" />
        </ElFormItem>
        <ElFormItem>
          <ElButton
            type="primary"
            data-testid="reporting-execute-run"
            :loading="executing"
            :disabled="!selectedDefinition || executing"
            @click="runExecute"
          >
            {{ t('reportingExecute.run') }}
          </ElButton>
        </ElFormItem>
      </ElForm>
      <p v-if="selectedDefinition" data-testid="reporting-execute-version">{{ text('version') }} {{ selectedDefinition.versionNumber }}</p>
      <p>{{ text('versionHelp') }}</p>
      <p v-if="!loading && !definitions.length && !selectedSummary">{{ text('empty') }}</p>
    </ElCard>

    <ElCard v-if="result" class="result-card">
      <p data-testid="reporting-execute-result-version">{{ result.definitionName }} · {{ text('resultVersion') }} {{ result.versionNumber }} · {{ text('page') }} {{ result.page }}</p>
      <ElAlert v-if="inputChanged" type="info" :closable="false" :title="text('changed')" data-testid="reporting-execute-input-changed" />
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
        :page-size="result.pageSize"
        :disabled="executing"
        :total="result.totalRows ?? result.page * result.pageSize + (result.hasMore ? 1 : 0)"
        @current-change="onPageChanged"
      />
    </ElCard>
    </template>
  </div>
</template>

<style scoped>
.execute-form {
  max-width: 720px;
}
.definition-select { width: 100%; }

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
