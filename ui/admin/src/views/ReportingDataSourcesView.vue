<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElPagination,
  ElSelect,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type {
  FullNetProblemDetails,
  ReportingDataSource,
  ReportingDataSourceListItem,
  TestReportingDataSourceResult
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createReportingDataSource,
  deleteReportingDataSource,
  disableReportingDataSource,
  listReportingDataSources,
  testReportingDataSource,
  updateReportingDataSource
} from '../api/reporting-data-sources';

defineOptions({ name: 'ReportingDataSourcesView' });

type EditorMode = 'create' | 'edit';

const { t } = useAdminI18n();
const items = ref<ReportingDataSourceListItem[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref({ name: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editing = ref<ReportingDataSource | null>(null);
const editorFormRef = ref<FormInstance>();
const testResult = ref<TestReportingDataSourceResult | null>(null);
const editorForm = reactive({
  tenantId: '',
  name: '',
  providerKey: 'sql_server',
  serverHost: '',
  port: '1433',
  databaseName: '',
  username: '',
  password: '',
  trustServerCertificate: false,
  isEnabled: true
});

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

const searchItems = computed<ArtSearchBarItem[]>(() => [
  { key: 'name', label: t('reportingDataSources.fieldName'), type: 'input' }
]);

const providerOptions = computed(() => [
  { value: 'sql_server', label: t('reportingDataSources.providerSqlServer') },
  { value: 'mysql', label: t('reportingDataSources.providerMySql') }
]);

function rowIndex(index: number) {
  return (page.value - 1) * pageSize.value + index + 1;
}

function providerLabel(providerKey: string) {
  return providerKey === 'mysql'
    ? t('reportingDataSources.providerMySql')
    : t('reportingDataSources.providerSqlServer');
}

function testStatusTagType(statusKey: string | null): 'success' | 'danger' | 'info' {
  if (statusKey === 'succeeded') {
    return 'success';
  }
  if (statusKey === 'failed') {
    return 'danger';
  }
  return 'info';
}

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listReportingDataSources({
      page: page.value,
      pageSize: pageSize.value,
      nameContains: appliedFilters.value.name || undefined
    });
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
    await updateTableHeight();
  } catch (error) {
    problem.value = toProblem(error, 'reportingDataSources.loadFailed');
  } finally {
    loading.value = false;
  }
}

function applySearch() {
  appliedFilters.value = { name: searchForm.value.name?.trim() ?? '' };
  page.value = 1;
  void load();
}

function resetEditor() {
  editorForm.tenantId = '';
  editorForm.name = '';
  editorForm.providerKey = 'sql_server';
  editorForm.serverHost = '';
  editorForm.port = '1433';
  editorForm.databaseName = '';
  editorForm.username = '';
  editorForm.password = '';
  editorForm.trustServerCertificate = false;
  editorForm.isEnabled = true;
  testResult.value = null;
}

function openCreate() {
  editorMode.value = 'create';
  editing.value = null;
  resetEditor();
  editorOpen.value = true;
}

async function openEdit(row: ReportingDataSourceListItem) {
  editorMode.value = 'edit';
  changing.value = true;
  try {
    const { getReportingDataSource } = await import('../api/reporting-data-sources');
    const detail = await getReportingDataSource(row.id);
    editing.value = detail;
    editorForm.tenantId = detail.tenantId ?? '';
    editorForm.name = detail.name;
    editorForm.providerKey = detail.providerKey;
    editorForm.serverHost = detail.serverHost;
    editorForm.port = String(detail.port);
    editorForm.databaseName = detail.databaseName;
    editorForm.username = detail.username;
    editorForm.password = '';
    editorForm.trustServerCertificate = detail.trustServerCertificate;
    editorForm.isEnabled = detail.isEnabled;
    editorOpen.value = true;
  } catch (error) {
    ElMessage.error(toProblem(error, 'reportingDataSources.loadFailed').title);
  } finally {
    changing.value = false;
  }
}

async function submitEditor() {
  await editorFormRef.value?.validate();
  changing.value = true;
  try {
    const port = Number(editorForm.port);
    if (editorMode.value === 'create') {
      await createReportingDataSource({
        tenantId: editorForm.tenantId.trim() ? editorForm.tenantId.trim() : null,
        name: editorForm.name.trim(),
        providerKey: editorForm.providerKey,
        serverHost: editorForm.serverHost.trim(),
        port,
        databaseName: editorForm.databaseName.trim(),
        username: editorForm.username.trim(),
        password: editorForm.password,
        trustServerCertificate: editorForm.trustServerCertificate,
        isEnabled: editorForm.isEnabled
      });
      ElMessage.success(t('reportingDataSources.createSuccess'));
    } else if (editing.value) {
      await updateReportingDataSource(editing.value.id, {
        name: editorForm.name.trim(),
        providerKey: editorForm.providerKey,
        serverHost: editorForm.serverHost.trim(),
        port,
        databaseName: editorForm.databaseName.trim(),
        username: editorForm.username.trim(),
        password: editorForm.password.trim() ? editorForm.password : null,
        trustServerCertificate: editorForm.trustServerCertificate,
        isEnabled: editorForm.isEnabled,
        version: editing.value.version
      });
      ElMessage.success(t('reportingDataSources.updateSuccess'));
    }
    editorOpen.value = false;
    await load();
  } catch (error) {
    ElMessage.error(toProblem(error, 'reportingDataSources.saveFailed').title);
  } finally {
    changing.value = false;
  }
}

async function runTest(row: ReportingDataSourceListItem) {
  changing.value = true;
  testResult.value = null;
  try {
    testResult.value = await testReportingDataSource(row.id);
    if (testResult.value.succeeded) {
      ElMessage.success(testResult.value.message);
    } else {
      ElMessage.error(testResult.value.message);
    }
    await load();
  } catch (error) {
    ElMessage.error(toProblem(error, 'reportingDataSources.testFailed').title);
  } finally {
    changing.value = false;
  }
}

async function runDisable(row: ReportingDataSourceListItem) {
  await ElMessageBox.confirm(
    t('reportingDataSources.confirmDisable', { name: row.name }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    await disableReportingDataSource(row.id);
    ElMessage.success(t('reportingDataSources.disableSuccess'));
    await load();
  } catch (error) {
    ElMessage.error(toProblem(error, 'reportingDataSources.saveFailed').title);
  } finally {
    changing.value = false;
  }
}

async function runDelete(row: ReportingDataSourceListItem) {
  await ElMessageBox.confirm(
    t('reportingDataSources.confirmDelete', { name: row.name }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    await deleteReportingDataSource(row.id);
    ElMessage.success(t('reportingDataSources.deleteSuccess'));
    await load();
  } catch (error) {
    ElMessage.error(toProblem(error, 'reportingDataSources.saveFailed').title);
  } finally {
    changing.value = false;
  }
}

function toProblem(error: unknown, fallbackKey: string): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t(fallbackKey), status: 500, code: fallbackKey };
}

onMounted(load);
</script>

<template>
  <section class="reporting-data-sources-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('reportingDataSources.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

    <el-card class="art-page-card art-full-height-card" shadow="never">
      <ArtTableHeader>
        <ArtSearchBar
          v-model="searchForm"
          :items="searchItems"
          @search="applySearch"
        />
        <template #actions>
          <PermissionGate permission="reporting.data_sources.create">
            <el-button
              type="primary"
              :icon="Plus"
              data-testid="reporting-data-source-create"
              @click="openCreate"
            >
              {{ t('reportingDataSources.addDataSource') }}
            </el-button>
          </PermissionGate>
        </template>
      </ArtTableHeader>

      <div ref="tableMainRef" class="art-table-main">
        <el-table
          v-loading="loading"
          :data="items"
          :height="tableHeight"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
          :header-cell-class-name="tableHeaderBackground"
        >
          <el-table-column type="index" :index="rowIndex" width="56" />
          <el-table-column prop="name" :label="t('reportingDataSources.fieldName')" min-width="140" />
          <el-table-column :label="t('reportingDataSources.fieldProvider')" min-width="120">
            <template #default="{ row }">{{ providerLabel(row.providerKey) }}</template>
          </el-table-column>
          <el-table-column prop="maskedServerEndpoint" :label="t('reportingDataSources.fieldEndpoint')" min-width="160" />
          <el-table-column prop="maskedDatabaseName" :label="t('reportingDataSources.fieldDatabase')" min-width="120" />
          <el-table-column :label="t('reportingDataSources.fieldTestStatus')" min-width="120">
            <template #default="{ row }">
              <el-tag v-if="row.lastTestStatusKey" :type="testStatusTagType(row.lastTestStatusKey)">
                {{ row.lastTestStatusKey }}
              </el-tag>
              <span v-else>-</span>
            </template>
          </el-table-column>
          <el-table-column :label="t('reportingDataSources.fieldEnabled')" width="90">
            <template #default="{ row }">
              <el-tag :type="row.isEnabled ? 'success' : 'info'">
                {{ row.isEnabled ? t('reportingDataSources.statusEnabled') : t('reportingDataSources.statusDisabled') }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="t('reportingDataSources.actions')" width="260" fixed="right">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate permission="reporting.data_sources.test">
                  <ArtTableActionButton
                    :label="t('reportingDataSources.testConnection')"
                    test-id="reporting-data-source-test"
                    @click="runTest(row)"
                  />
                </PermissionGate>
                <PermissionGate permission="reporting.data_sources.update">
                  <ArtTableActionButton
                    :label="t('reportingDataSources.actionEdit')"
                    test-id="reporting-data-source-edit"
                    @click="openEdit(row)"
                  />
                </PermissionGate>
                <PermissionGate permission="reporting.data_sources.update">
                  <ArtTableActionButton
                    :label="t('reportingDataSources.actionDisable')"
                    test-id="reporting-data-source-disable"
                    @click="runDisable(row)"
                  />
                </PermissionGate>
                <PermissionGate permission="reporting.data_sources.delete">
                  <ArtTableActionButton
                    :label="t('reportingDataSources.actionDelete')"
                    test-id="reporting-data-source-delete"
                    @click="runDelete(row)"
                  />
                </PermissionGate>
              </ArtTableActionGroup>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <el-pagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        :total="total"
        layout="total, sizes, prev, pager, next"
        class="art-table-pagination"
        @current-change="load"
        @size-change="load"
      />
    </el-card>

    <ArtFormDialog
      v-model="editorOpen"
      :title="editorMode === 'create' ? t('reportingDataSources.createTitle') : t('reportingDataSources.editTitle')"
      :loading="changing"
      confirm-test-id="reporting-data-source-editor-submit"
      @confirm="submitEditor"
    >
      <el-form ref="editorFormRef" label-width="140px">
        <el-form-item :label="t('reportingDataSources.fieldName')" required>
          <el-input v-model="editorForm.name" />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldProvider')" required>
          <el-select v-model="editorForm.providerKey" style="width: 100%">
            <el-option
              v-for="option in providerOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldServerHost')" required>
          <el-input v-model="editorForm.serverHost" />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldPort')" required>
          <el-input v-model="editorForm.port" />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldDatabase')" required>
          <el-input v-model="editorForm.databaseName" />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldUsername')" required>
          <el-input v-model="editorForm.username" />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldPassword')" :required="editorMode === 'create'">
          <el-input v-model="editorForm.password" type="password" show-password />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldTrustServerCertificate')">
          <el-switch v-model="editorForm.trustServerCertificate" />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldEnabled')">
          <el-switch v-model="editorForm.isEnabled" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>
