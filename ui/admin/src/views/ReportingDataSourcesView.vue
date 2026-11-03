<script setup lang="ts">
import { computed, onActivated, onDeactivated, onBeforeUnmount, reactive, ref, watch } from 'vue';
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
import { useSessionStore } from '../auth/session';
import { reportingSourceText } from '../i18n/reporting-source-configuration';
import {
  createReportingDataSource,
  getReportingDataSource,
  deleteReportingDataSource,
  disableReportingDataSource,
  listReportingDataSources,
  testReportingDataSource,
  updateReportingDataSource
} from '../api/reporting-data-sources';

defineOptions({ name: 'ReportingDataSourcesView' });

type EditorMode = 'create' | 'edit';

const { t, locale } = useAdminI18n();
const session = useSessionStore(), active = ref(true), writeUnconfirmed = ref(false);
const text = (key: Parameters<typeof reportingSourceText>[1]) => reportingSourceText(locale.value, key);
let generation = 0, editorEpoch = 0, cancellation = new AbortController(), listRequest: AbortController | undefined, detailRequest: AbortController | undefined;
const canRead = () => active.value && !session.switching && session.currentUser?.actorScope === 'host' && !session.currentUser.tenantId && session.can('reporting.data_sources.read');
const can = (action: string) => canRead() && session.can(`reporting.data_sources.${action}`);
function current(scope: number) { return scope === generation && canRead(); }
function unknown(error: unknown) { return !isFullNetProblemDetails(error) || error.status === 409 || (error.status ?? 500) >= 500; }
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

async function load(manual = false, afterWrite = false) {
  if (!canRead() || (changing.value && !afterWrite)) return;
  const scope = generation, controller = new AbortController(); listRequest?.abort(); listRequest = controller;
  if (manual && !editorOpen.value) detailRequest?.abort();
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listReportingDataSources({
      page: page.value,
      pageSize: pageSize.value,
      nameContains: appliedFilters.value.name || undefined
    }, controller.signal);
    if (!current(scope) || controller.signal.aborted) return;
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
    if (manual && !editorOpen.value) writeUnconfirmed.value = false;
    await updateTableHeight();
  } catch (error) {
    if (current(scope) && !controller.signal.aborted) problem.value = toProblem(error, 'reportingDataSources.loadFailed');
  } finally {
    if (current(scope) && !controller.signal.aborted) loading.value = false;
  }
}

function applySearch() {
  if (!canRead() || changing.value) return;
  detailRequest?.abort();
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
  if (!can('create') || changing.value || writeUnconfirmed.value) return;
  detailRequest?.abort();
  editorMode.value = 'create';
  editing.value = null;
  resetEditor();
  editorOpen.value = true;
}

async function openEdit(row: ReportingDataSourceListItem) {
  if (!can('update') || changing.value || writeUnconfirmed.value) return;
  await readEditor(row.id);
}
async function readEditor(id: string) {
  if (!can('update') || changing.value) return;
  const scope = generation, controller = new AbortController(); detailRequest?.abort(); detailRequest = controller;
  editorMode.value = 'edit';
  try {
    const detail = await getReportingDataSource(id, controller.signal);
    if (!current(scope) || !can('update') || controller.signal.aborted) return;
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
    writeUnconfirmed.value = false;
  } catch (error) {
    if (current(scope) && !controller.signal.aborted) problem.value = toProblem(error, 'reportingDataSources.loadFailed');
  }
}
async function reloadEditor() {
  const scope = generation, id = editing.value?.id; if (!id || !can('update') || changing.value) return;
  try { await ElMessageBox.confirm(text('discard'), { type: 'warning' }); } catch { return; }
  if (current(scope) && editorOpen.value && editing.value?.id === id) await readEditor(id);
}

async function submitEditor() {
  const scope = generation, editor = editorEpoch, action = editorMode.value === 'create' ? 'create' : 'update';
  if (!can(action) || changing.value || writeUnconfirmed.value || !editorOpen.value) return;
  const valid = await editorFormRef.value?.validate().catch(() => false);
  if (!valid || !current(scope) || editor !== editorEpoch || !editorOpen.value || changing.value || !can(action)) return;
  const port = Number(editorForm.port);
  if (![editorForm.name, editorForm.serverHost, editorForm.databaseName, editorForm.username].every(value => !!value.trim())
    || !Number.isInteger(port) || port < 1 || port > 65535 || (editorMode.value === 'create' && !editorForm.password)) {
    problem.value = { status: 400, code: 'client.validation_failed', title: text('invalid') }; return;
  }
  await write(action, async signal => {
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
      }, signal);
      if (!current(scope)) return;
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
      }, signal);
      if (!current(scope)) return;
      ElMessage.success(t('reportingDataSources.updateSuccess'));
    }
    editorOpen.value = false;
  });
}

async function runTest(row: ReportingDataSourceListItem) {
  if (!can('test') || changing.value || writeUnconfirmed.value) return;
  const scope = generation;
  testResult.value = null;
  await write('test', async signal => {
    const result = await testReportingDataSource(row.id, signal); if (!current(scope)) return;
    testResult.value = result;
    if (testResult.value.succeeded) {
      ElMessage.success(testResult.value.message);
    } else {
      ElMessage.error(testResult.value.message);
    }
  });
}

async function runDisable(row: ReportingDataSourceListItem) {
  const scope = generation; if (!can('update') || changing.value || writeUnconfirmed.value) return;
  try { await ElMessageBox.confirm(
    t('reportingDataSources.confirmDisable', { name: row.name }),
    { type: 'warning' }
  ); } catch { return; }
  if (!current(scope)) return;
  await write('update', async signal => {
    await disableReportingDataSource(row.id, signal); if (!current(scope)) return;
    ElMessage.success(t('reportingDataSources.disableSuccess'));
  });
}

async function runDelete(row: ReportingDataSourceListItem) {
  const scope = generation; if (!can('delete') || changing.value || writeUnconfirmed.value) return;
  try { await ElMessageBox.confirm(
    t('reportingDataSources.confirmDelete', { name: row.name }),
    { type: 'warning' }
  ); } catch { return; }
  if (!current(scope)) return;
  await write('delete', async signal => {
    await deleteReportingDataSource(row.id, signal); if (!current(scope)) return;
    ElMessage.success(t('reportingDataSources.deleteSuccess'));
  });
}
/** 连接测试也会写回测试元数据；未知结果与维护写入采用相同的人工确认边界。 */
async function write(action: string, execute: (signal: AbortSignal) => Promise<void>) {
  if (!can(action) || changing.value || writeUnconfirmed.value) return;
  const scope = generation; listRequest?.abort(); detailRequest?.abort(); loading.value = false; changing.value = true; problem.value = undefined;
  try { await execute(cancellation.signal); if (current(scope)) await load(false, true); }
  catch (error) { if (current(scope)) { writeUnconfirmed.value = unknown(error); problem.value = toProblem(error, action === 'test' ? 'reportingDataSources.testFailed' : 'reportingDataSources.saveFailed'); } }
  finally { if (current(scope)) changing.value = false; }
}

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t(fallbackKey), status: 500, code: fallbackKey };
}

function reset() {
  generation++; cancellation.abort(); cancellation = new AbortController(); listRequest?.abort(); detailRequest?.abort();
  items.value = []; total.value = 0; page.value = 1; pageSize.value = 20; loading.value = false; changing.value = false; problem.value = undefined;
  searchForm.value = {}; appliedFilters.value = { name: '' }; editorOpen.value = false; editing.value = null; writeUnconfirmed.value = false; resetEditor();
}
watch(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.actorScope, session.currentUser?.sessionId, session.currentUser?.permissions.join('|'), session.switching, active.value], () => {
  reset(); if (canRead()) void load();
}, { immediate: true, flush: 'sync' });
watch(editorOpen, value => { editorEpoch++; if (!value) { detailRequest?.abort(); editorForm.password = ''; } }, { flush: 'sync' });
onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; reset(); });
</script>

<template>
  <section class="reporting-data-sources-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('reportingDataSources.title') }}</h1>
    <ElAlert v-if="!canRead()" type="info" :closable="false" :title="text('hostOnly')" />
    <template v-if="canRead()">
    <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="text('unconfirmed')" data-testid="reporting-source-unconfirmed" />
    <ElButton :disabled="changing" :loading="loading" data-testid="reporting-source-refresh" @click="load(true)">{{ text('refresh') }}</ElButton>

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
        <template #left>
          <ArtSearchBar v-model="searchForm" :items="searchItems" @search="applySearch" />
          <PermissionGate code="reporting.data_sources.create">
            <el-button
              type="primary"
              :icon="Plus"
              data-testid="reporting-data-source-create"
              :disabled="changing || writeUnconfirmed"
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
                    :disabled="changing || writeUnconfirmed"
                    @click="runTest(row)"
                  />
                </PermissionGate>
                <PermissionGate code="reporting.data_sources.update">
                  <ArtTableActionButton type="edit"
                    :title="t('reportingDataSources.actionEdit')"
                    test-id="reporting-data-source-edit"
                    :disabled="changing || writeUnconfirmed"
                    @click="openEdit(row)"
                  />
                </PermissionGate>
                <PermissionGate code="reporting.data_sources.update">
                  <ArtTableActionButton type="delete"
                    :title="t('reportingDataSources.actionDisable')"
                    test-id="reporting-data-source-disable"
                    :disabled="changing || writeUnconfirmed || !row.isEnabled"
                    @click="runDisable(row)"
                  />
                </PermissionGate>
                <PermissionGate code="reporting.data_sources.delete">
                  <ArtTableActionButton type="delete"
                    :title="t('reportingDataSources.actionDelete')"
                    test-id="reporting-data-source-delete"
                    :disabled="changing || writeUnconfirmed"
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
        :disabled="changing"
        layout="total, sizes, prev, pager, next"
        class="art-table-pagination"
        @current-change="() => { detailRequest?.abort(); void load(); }"
        @size-change="() => { detailRequest?.abort(); void load(); }"
      />
    </el-card>

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('reportingDataSources.createTitle') : t('reportingDataSources.editTitle')"
      :saving="changing"
      :show-confirm="!writeUnconfirmed && can(editorMode === 'create' ? 'create' : 'update')"
      width="min(640px, calc(100vw - 32px))"
      confirm-test-id="reporting-data-source-editor-submit"
      @confirm="submitEditor"
    >
      <el-form ref="editorFormRef" :model="editorForm" label-position="top" :disabled="changing">
        <ElAlert v-if="problem" type="error" :closable="false" :title="problem.detail ?? problem.title" />
        <ElAlert v-if="writeUnconfirmed" type="warning" :closable="false" :title="text('unconfirmed')" />
        <ElButton v-if="editing" :disabled="changing" data-testid="reporting-source-reload" @click="reloadEditor">{{ text('reload') }}</ElButton>
        <el-form-item prop="name" :label="t('reportingDataSources.fieldName')" required>
          <el-input v-model="editorForm.name" data-testid="reporting-source-name" />
        </el-form-item>
        <el-form-item prop="providerKey" :label="t('reportingDataSources.fieldProvider')" required>
          <el-select v-model="editorForm.providerKey" style="width: 100%">
            <el-option
              v-for="option in providerOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item prop="serverHost" :label="t('reportingDataSources.fieldServerHost')" required>
          <el-input v-model="editorForm.serverHost" data-testid="reporting-source-host" />
        </el-form-item>
        <el-form-item prop="port" :label="t('reportingDataSources.fieldPort')" required>
          <el-input v-model="editorForm.port" data-testid="reporting-source-port" />
        </el-form-item>
        <el-form-item prop="databaseName" :label="t('reportingDataSources.fieldDatabase')" required>
          <el-input v-model="editorForm.databaseName" data-testid="reporting-source-database" />
        </el-form-item>
        <el-form-item prop="username" :label="t('reportingDataSources.fieldUsername')" required>
          <el-input v-model="editorForm.username" data-testid="reporting-source-username" />
        </el-form-item>
        <el-form-item prop="password" :label="t('reportingDataSources.fieldPassword')" :required="editorMode === 'create'">
          <el-input v-model="editorForm.password" type="password" show-password data-testid="reporting-source-password" />
        </el-form-item>
        <p>{{ text('passwordHelp') }}</p>
        <el-form-item :label="t('reportingDataSources.fieldTrustServerCertificate')">
          <el-switch v-model="editorForm.trustServerCertificate" :aria-label="t('reportingDataSources.fieldTrustServerCertificate')" />
        </el-form-item>
        <el-form-item :label="t('reportingDataSources.fieldEnabled')">
          <el-switch v-model="editorForm.isEnabled" :aria-label="t('reportingDataSources.fieldEnabled')" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
    </template>
  </section>
</template>
