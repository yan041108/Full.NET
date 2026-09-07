<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type {
  FullNetProblemDetails,
  ReportingDefinition,
  ReportingExportTask,
  ReportingExportTaskParameterValue
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listReportingDefinitions } from '../api/reporting-definitions';
import {
  createReportingExportTask,
  downloadReportingExportTask,
  listReportingExportTasks
} from '../api/reporting-export-tasks';

defineOptions({ name: 'ReportingExportTasksView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<ReportingExportTask[]>([]);
const definitions = ref<ReportingDefinition[]>([]);
const loading = ref(false);
const creating = ref(false);
const problem = ref<FullNetProblemDetails>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const createDialogVisible = ref(false);
const selectedDefinitionId = ref('');
const parameterValues = reactive<Record<string, string>>({});

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

watchLoading(loading);

const canCreate = () => session.can('reporting.export_tasks.create');
const canRead = () => session.can('reporting.export_tasks.read');
const canDownload = () => session.can('reporting.export_tasks.download');

const selectedDefinition = computed(() =>
  definitions.value.find(item => item.id === selectedDefinitionId.value));

const parameterSchema = computed(() => selectedDefinition.value?.parameterSchema ?? []);

function statusTagType(statusKey: string): 'success' | 'danger' | 'info' {
  if (statusKey === 'succeeded') {
    return 'success';
  }
  if (statusKey === 'failed') {
    return 'danger';
  }
  return 'info';
}

function statusLabel(statusKey: string): string {
  const key = `reportingExportTasks.status.${statusKey}` as const;
  const translated = translateRuntimeMessage(t, key);
  return translated === key ? statusKey : translated;
}

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

function resetParameters(): void {
  for (const key of Object.keys(parameterValues)) {
    delete parameterValues[key];
  }
  for (const parameter of parameterSchema.value) {
    parameterValues[parameter.parameterKey] = parameter.defaultValue ?? '';
  }
}

async function loadDefinitions(): Promise<void> {
  definitions.value = (await listReportingDefinitions())
    .filter(item => item.isEnabled && item.latestPublishedVersionNumber > 0);
  selectedDefinitionId.value = definitions.value[0]?.id ?? '';
  resetParameters();
}

async function load(): Promise<void> {
  if (!canRead()) {
    return;
  }

  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listReportingExportTasks(page.value, pageSize.value);
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
    updateTableHeight();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingExportTasks.loadFailed');
  } finally {
    loading.value = false;
  }
}

function openCreateDialog(): void {
  createDialogVisible.value = true;
  resetParameters();
}

async function submitCreate(): Promise<void> {
  if (!selectedDefinitionId.value) {
    return;
  }

  creating.value = true;
  problem.value = undefined;
  try {
    const parameters: ReportingExportTaskParameterValue[] = parameterSchema.value.map(parameter => ({
      parameterKey: parameter.parameterKey,
      value: parameterValues[parameter.parameterKey] ?? null
    }));
    await createReportingExportTask({
      definitionId: selectedDefinitionId.value,
      formatKey: 'excel',
      parameters
    });
    ElMessage.success(t('reportingExportTasks.createSuccess'));
    createDialogVisible.value = false;
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingExportTasks.createFailed');
  } finally {
    creating.value = false;
  }
}

async function downloadTask(task: ReportingExportTask): Promise<void> {
  if (!canDownload() || task.statusKey !== 'succeeded') {
    return;
  }

  try {
    const blob = await downloadReportingExportTask(task.id);
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = task.outputFileName ?? 'reporting-export.xlsx';
    anchor.click();
    URL.revokeObjectURL(url);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingExportTasks.downloadFailed');
  }
}

onMounted(async () => {
  loading.value = true;
  try {
    await loadDefinitions();
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingExportTasks.loadFailed');
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <div class="reporting-export-tasks-view">
    <ElCard shadow="never" class="art-table-card">
      <div ref="tableMainRef" class="art-crud-table-main">
        <ArtTableHeader
          v-model:table-size="tableSize"
          v-model:zebra="tableZebra"
          v-model:border="tableBorder"
          v-model:header-background="tableHeaderBackground"
          :loading="loading"
          full-class="art-crud-table-main"
          layout="refresh,size,fullscreen"
          @refresh="load"
        >
          <template #left>
            <PermissionGate code="reporting.export_tasks.create">
              <ElButton
                type="primary"
                plain
                :icon="Plus"
                data-testid="reporting-export-create"
                @click="openCreateDialog"
              >
                {{ t('reportingExportTasks.create') }}
              </ElButton>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <ElAlert
          v-if="problem"
          type="error"
          :title="problem.title"
          show-icon
          class="mb-4"
        />

        <div class="art-table" :class="{ 'is-empty': items.length === 0 }">
          <ElTable
            v-loading="loading"
            :data="items"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
          >
          <ElTableColumn prop="definitionName" :label="t('reportingExportTasks.fieldDefinition')" min-width="160" />
          <ElTableColumn prop="definitionKey" :label="t('reportingExportTasks.fieldDefinitionKey')" min-width="140" />
          <ElTableColumn prop="versionNumber" :label="t('reportingExportTasks.fieldVersion')" width="90" />
          <ElTableColumn prop="formatKey" :label="t('reportingExportTasks.fieldFormat')" width="90" />
          <ElTableColumn :label="t('reportingExportTasks.fieldStatus')" width="120">
            <template #default="{ row }">
              <ElTag :type="statusTagType(row.statusKey)">
                {{ statusLabel(row.statusKey) }}
              </ElTag>
            </template>
          </ElTableColumn>
          <ElTableColumn prop="rowCount" :label="t('reportingExportTasks.fieldRowCount')" width="100" />
          <ElTableColumn prop="createdAtUtc" :label="t('reportingExportTasks.fieldCreatedAt')" min-width="180" />
          <!-- @vue-generic {ReportingExportTask} -->
          <ElTableColumn :label="t('reportingExportTasks.actions')" width="120" fixed="right">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="reporting.export_tasks.download">
                  <ElButton
                    v-if="row.statusKey === 'succeeded'"
                    link
                    type="primary"
                    data-testid="reporting-export-download"
                    @click="downloadTask(row)"
                  >
                    {{ t('reportingExportTasks.download') }}
                  </ElButton>
                </PermissionGate>
              </ArtTableActionGroup>
            </template>
          </ElTableColumn>
        </ElTable>
        </div>
      </div>

      <ElPagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        class="mt-4"
        layout="total, sizes, prev, pager, next"
        :total="total"
        @change="load"
        @size-change="load"
      />
    </ElCard>

    <ElDialog
      v-model="createDialogVisible"
      :title="t('reportingExportTasks.createTitle')"
      width="520px"
    >
      <ElForm label-width="120px">
        <ElFormItem :label="t('reportingExportTasks.fieldDefinition')">
          <ElSelect
            v-model="selectedDefinitionId"
            data-testid="reporting-export-definition"
            class="w-full"
            @change="resetParameters"
          >
            <ElOption
              v-for="definition in definitions"
              :key="definition.id"
              :label="definition.name"
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
      </ElForm>
      <template #footer>
        <ElButton @click="createDialogVisible = false">
          {{ t('common.cancel') }}
        </ElButton>
        <ElButton
          type="primary"
          :loading="creating"
          :disabled="!selectedDefinitionId"
          data-testid="reporting-export-submit"
          @click="submitCreate"
        >
          {{ t('reportingExportTasks.submitCreate') }}
        </ElButton>
      </template>
    </ElDialog>
  </div>
</template>
