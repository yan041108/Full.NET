<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
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
  ReportingDefinition,
  ReportingExecutionPage,
  ReportingExecutionParameterValue
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import { executeReportingDefinition } from '../api/reporting-executions';
import { listReportingDefinitions } from '../api/reporting-definitions';

defineOptions({ name: 'ReportingExecuteView' });

const { t } = useAdminI18n();
const definitions = ref<ReportingDefinition[]>([]);
const selectedDefinitionId = ref('');
const parameterValues = reactive<Record<string, string>>({});
const result = ref<ReportingExecutionPage>();
const page = ref(1);
const pageSize = ref(50);
const loading = ref(false);
const executing = ref(false);
const problem = ref<FullNetProblemDetails>();

const selectedDefinition = computed(() =>
  definitions.value.find(item => item.id === selectedDefinitionId.value));

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

onMounted(async () => {
  loading.value = true;
  try {
    definitions.value = (await listReportingDefinitions())
      .filter(item => item.isEnabled && item.latestPublishedVersionNumber > 0);
    selectedDefinitionId.value = definitions.value[0]?.id ?? '';
    resetParameters();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingExecute.loadFailed');
  } finally {
    loading.value = false;
  }
});

function resetParameters(): void {
  for (const key of Object.keys(parameterValues)) {
    delete parameterValues[key];
  }
  for (const parameter of parameterSchema.value) {
    parameterValues[parameter.parameterKey] = parameter.defaultValue ?? '';
  }
}

function onDefinitionChanged(): void {
  resetParameters();
  result.value = undefined;
  page.value = 1;
}

async function runExecute(): Promise<void> {
  if (!selectedDefinitionId.value || executing.value) {
    return;
  }
  executing.value = true;
  problem.value = undefined;
  try {
    const parameters: ReportingExecutionParameterValue[] = parameterSchema.value.map(parameter => ({
      parameterKey: parameter.parameterKey,
      value: parameterValues[parameter.parameterKey]?.trim() || null
    }));
    result.value = await executeReportingDefinition(
      selectedDefinitionId.value,
      { parameters },
      page.value,
      pageSize.value
    );
    updateTableHeight();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingExecute.executeFailed');
  } finally {
    executing.value = false;
  }
}

async function onPageChanged(nextPage: number): Promise<void> {
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
        <ElFormItem :label="t('reportingExecute.fieldDefinition')">
          <ElSelect
            v-model="selectedDefinitionId"
            filterable
            data-testid="reporting-execute-definition"
            @change="onDefinitionChanged"
          >
            <ElOption
              v-for="definition in definitions"
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
          <ElInput v-model="parameterValues[parameter.parameterKey]" />
        </ElFormItem>
        <ElFormItem>
          <ElButton
            type="primary"
            data-testid="reporting-execute-run"
            :loading="executing"
            :disabled="!selectedDefinitionId"
            @click="runExecute"
          >
            {{ t('reportingExecute.run') }}
          </ElButton>
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
