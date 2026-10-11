<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
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
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getReportingDataSource,
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
const session = useSessionStore();
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


const confirmation = ref<{ title: string; message: string; resolve: (confirmed: boolean) => void }>();
function finishConfirmation(confirmed: boolean): void {
  const pending = confirmation.value; confirmation.value = undefined; pending?.resolve(confirmed);
}
const host = () => session.currentUser?.scope === 'host' && session.currentUser.tenantId === null;
const scope = useAuthorizedViewScope(session, () => {
  // 确认提示与编辑凭据属于当前会话，失效时立即清除并解除等待。
  finishConfirmation(false);
  items.value = []; total.value = 0; page.value = 1; problem.value = undefined;
  searchForm.value = {}; appliedFilters.value = { name: '' }; editorOpen.value = false;
  editing.value = null; resetEditor(); loading.value = false; changing.value = false;
}, load);
type ViewRequest = NonNullable<ReturnType<typeof scope.begin>>;
const begin = (permission: string) => host() ? scope.begin(permission) : undefined;
function canEnter(permission: string): boolean {
  const request = begin(permission); request?.finish(); return !!request;
}

let listRequest: ReturnType<typeof scope.begin>;
let editorRequest: ReturnType<typeof scope.begin>;
watch(editorOpen, open => {
  if (!open) { editorRequest?.cancel(); editing.value = null; resetEditor(); changing.value = false; }
}, { flush: 'sync' });
async function withAction(permission: string, fallback: Parameters<typeof t>[0],
  action: (request: ViewRequest) => Promise<void>, editor = false): Promise<void> {
  if (changing.value) return;
  const request = begin(permission); if (!request) return;
  if (editor) editorRequest = request;
  changing.value = true;
  try { await action(request); }
  catch (error) { if (request.current()) ElMessage.error(toProblem(error, fallback).title); }
  finally { if (request.current()) changing.value = false; request.finish(); }
}

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
  listRequest?.cancel(); const request = begin('reporting.data_sources.read'); listRequest = request;
  if (!request) return;
  loading.value = true; problem.value = undefined;
  try {
    const result = await listReportingDataSources({ page: page.value, pageSize: pageSize.value,
      nameContains: appliedFilters.value.name || undefined }, request.signal);
    if (!request.current()) return;
    items.value = result.items; page.value = result.page; pageSize.value = result.pageSize; total.value = result.total;
    await updateTableHeight();
  } catch (error) { if (request.current()) problem.value = toProblem(error, 'reportingDataSources.loadFailed'); }
  finally { if (request.current()) loading.value = false; request.finish(); }
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
  if (changing.value || !canEnter('reporting.data_sources.create')) return;
  editorMode.value = 'create'; editing.value = null; resetEditor(); editorOpen.value = true;
}

async function openEdit(row: ReportingDataSourceListItem) {
  if (!session.can('reporting.data_sources.read')) return;
  await withAction('reporting.data_sources.update', 'reportingDataSources.loadFailed', async request => {
    const detail = await getReportingDataSource(row.id, request.signal);
    if (!request.current()) return;
    editorMode.value = 'edit'; editing.value = detail;
    editorForm.tenantId = detail.tenantId ?? ''; editorForm.name = detail.name; editorForm.providerKey = detail.providerKey;
    editorForm.serverHost = detail.serverHost; editorForm.port = String(detail.port); editorForm.databaseName = detail.databaseName;
    editorForm.username = detail.username; editorForm.password = ''; editorForm.trustServerCertificate = detail.trustServerCertificate;
    editorForm.isEnabled = detail.isEnabled; editorOpen.value = true;
  }, true);
}

async function submitEditor() {
  if (!editorOpen.value || !editorFormRef.value) return;
  await withAction(editorMode.value === 'create' ? 'reporting.data_sources.create' : 'reporting.data_sources.update',
    'reportingDataSources.saveFailed', async request => {
      // 校验开始前取得租约并锁定提交，关闭或撤权后不能继续写入。
      const valid = await editorFormRef.value!.validate().catch(() => false);
      if (!valid || !request.current()) return;
      const payload = { name: editorForm.name.trim(), providerKey: editorForm.providerKey,
        serverHost: editorForm.serverHost.trim(), port: Number(editorForm.port), databaseName: editorForm.databaseName.trim(),
        username: editorForm.username.trim(), trustServerCertificate: editorForm.trustServerCertificate, isEnabled: editorForm.isEnabled };
      if (editorMode.value === 'create') {
        await createReportingDataSource({ ...payload, tenantId: editorForm.tenantId.trim() || null,
          password: editorForm.password }, request.signal);
      } else if (editing.value) {
        await updateReportingDataSource(editing.value.id, { ...payload, password: editorForm.password.trim() ? editorForm.password : null,
          version: editing.value.version }, request.signal);
      } else return;
      if (!request.current()) return;
      ElMessage.success(t(editorMode.value === 'create' ? 'reportingDataSources.createSuccess' : 'reportingDataSources.updateSuccess'));
      editorOpen.value = false; await load();
    }, true);
}

async function runTest(row: ReportingDataSourceListItem) {
  await withAction('reporting.data_sources.test', 'reportingDataSources.testFailed', async request => {
    testResult.value = null;
    const result = await testReportingDataSource(row.id, request.signal);
    if (!request.current()) return;
    testResult.value = result;
    if (result.succeeded) ElMessage.success(result.message); else ElMessage.error(result.message);
    await load();
  });
}

async function runDisable(row: ReportingDataSourceListItem) {
  await withAction('reporting.data_sources.update', 'reportingDataSources.saveFailed', async request => {
    const confirmed = await new Promise<boolean>(resolve => { confirmation.value = {
      title: t('reportingDataSources.actionDisable'), message: t('reportingDataSources.confirmDisable', { name: row.name }), resolve }; });
    if (!confirmed || !request.current()) return;
    await disableReportingDataSource(row.id, request.signal);
    if (!request.current()) return;
    ElMessage.success(t('reportingDataSources.disableSuccess')); await load();
  });
}

async function runDelete(row: ReportingDataSourceListItem) {
  await withAction('reporting.data_sources.delete', 'reportingDataSources.saveFailed', async request => {
    const confirmed = await new Promise<boolean>(resolve => { confirmation.value = {
      title: t('reportingDataSources.actionDelete'), message: t('reportingDataSources.confirmDelete', { name: row.name }), resolve }; });
    if (!confirmed || !request.current()) return;
    await deleteReportingDataSource(row.id, request.signal);
    if (!request.current()) return;
    ElMessage.success(t('reportingDataSources.deleteSuccess')); await load();
  });
}

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t(fallbackKey), status: 500, code: fallbackKey };
}


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
      <ArtSearchBar
        v-model="searchForm"
        :items="searchItems"
        @search="applySearch"
        @reset="applySearch"
      />
      <ArtTableHeader @refresh="load">
        <template #left>
          <PermissionGate code="reporting.data_sources.create">
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
          :header-cell-class-name="tableHeaderBackground ? 'art-table-header-background' : ''"
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
          <!-- @vue-generic {ReportingDataSourceListItem} -->
          <el-table-column :label="t('reportingDataSources.actions')" width="260" fixed="right">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="reporting.data_sources.test">
                  <ArtTableActionButton type="view"
                    :title="t('reportingDataSources.testConnection')"
                    test-id="reporting-data-source-test"
                    @click="runTest(row)"
                  />
                </PermissionGate>
                <PermissionGate code="reporting.data_sources.update">
                  <ArtTableActionButton type="edit"
                    :title="t('reportingDataSources.actionEdit')"
                    test-id="reporting-data-source-edit"
                    @click="openEdit(row)"
                  />
                </PermissionGate>
                <PermissionGate code="reporting.data_sources.update">
                  <ArtTableActionButton type="delete"
                    :title="t('reportingDataSources.actionDisable')"
                    test-id="reporting-data-source-disable"
                    @click="runDisable(row)"
                  />
                </PermissionGate>
                <PermissionGate code="reporting.data_sources.delete">
                  <ArtTableActionButton type="delete"
                    :title="t('reportingDataSources.actionDelete')"
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


    <ArtFormDialog v-if="confirmation" :open="true" :title="confirmation.title"
      :confirm-label="t('users.confirm')" :cancel-label="t('common.cancel')" confirm-test-id="reporting-data-source-confirm"
      @confirm="finishConfirmation(true)" @update:open="open => { if (!open) finishConfirmation(false); }">
      <p>{{ confirmation.message }}</p>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('reportingDataSources.createTitle') : t('reportingDataSources.editTitle')"
      :saving="changing"
      confirm-test-id="reporting-data-source-editor-submit"
      @confirm="submitEditor"
    >
      <el-form ref="editorFormRef" :model="editorForm" label-width="140px">
        <el-form-item :label="t('reportingDataSources.fieldName')" prop="name" required>
          <el-input v-model="editorForm.name" />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldProvider')" prop="providerKey" required>
          <el-select v-model="editorForm.providerKey" style="width: 100%">
            <el-option
              v-for="option in providerOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldServerHost')" prop="serverHost" required>
          <el-input v-model="editorForm.serverHost" />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldPort')" prop="port" required>
          <el-input v-model="editorForm.port" />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldDatabase')" prop="databaseName" required>
          <el-input v-model="editorForm.databaseName" />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldUsername')" prop="username" required>
          <el-input v-model="editorForm.username" />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldPassword')" prop="password" :required="editorMode === 'create'">
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
