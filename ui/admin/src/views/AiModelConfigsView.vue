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
  ElTabPane,
  ElTabs,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type {
  AiModelConfigListItem,
  AiTenantQuotaListItem,
  FullNetProblemDetails
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
  createAiModelConfig,
  disableAiModelConfig,
  getAiModelConfig,
  listAiModelConfigs,
  listAiTenantQuotas,
  testAiModelConfig,
  updateAiModelConfig,
  upsertAiTenantQuota
} from '../api/ai-model-configs';

defineOptions({ name: 'AiModelConfigsView' });

type EditorMode = 'create' | 'edit';

const { t } = useAdminI18n();
const activeTab = ref('models');
const modelItems = ref<AiModelConfigListItem[]>([]);
const quotaItems = ref<AiTenantQuotaListItem[]>([]);
const modelTotal = ref(0);
const quotaTotal = ref(0);
const modelPage = ref(1);
const quotaPage = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref({ name: '' });
const editorOpen = ref(false);
const quotaEditorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editing = ref<AiModelConfigListItem | null>(null);
const editingQuota = ref<AiTenantQuotaListItem | null>(null);
const editorFormRef = ref<FormInstance>();
const quotaFormRef = ref<FormInstance>();
const editorForm = reactive({
  tenantId: '',
  name: '',
  providerKey: 'openai_compatible',
  endpointBaseUrl: 'https://api.openai.com/v1',
  modelId: '',
  apiKey: '',
  organizationId: '',
  isDefault: false,
  isEnabled: true
});
const quotaForm = reactive({
  tenantId: '',
  monthlyTokenLimit: '',
  monthlyRequestLimit: '',
  isEnabled: true,
  version: 0
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
  { key: 'name', label: t('aiModelConfigs.fieldName'), type: 'input' }
]);

const providerOptions = computed(() => [
  { value: 'openai_compatible', label: t('aiModelConfigs.providerOpenAiCompatible') },
  { value: 'ollama', label: t('aiModelConfigs.providerOllama') }
]);

function providerLabel(providerKey: string) {
  return providerKey === 'ollama'
    ? t('aiModelConfigs.providerOllama')
    : t('aiModelConfigs.providerOpenAiCompatible');
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

function rowIndex(index: number) {
  return (modelPage.value - 1) * pageSize.value + index + 1;
}

async function loadModels() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listAiModelConfigs({
      page: modelPage.value,
      pageSize: pageSize.value,
      nameContains: appliedFilters.value.name || undefined
    });
    modelItems.value = result.items;
    modelPage.value = result.page;
    pageSize.value = result.pageSize;
    modelTotal.value = result.total;
    await updateTableHeight();
  } catch (error) {
    problem.value = toProblem(error, 'aiModelConfigs.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function loadQuotas() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listAiTenantQuotas({
      page: quotaPage.value,
      pageSize: pageSize.value
    });
    quotaItems.value = result.items;
    quotaPage.value = result.page;
    quotaTotal.value = result.total;
  } catch (error) {
    problem.value = toProblem(error, 'aiModelConfigs.quotaLoadFailed');
  } finally {
    loading.value = false;
  }
}

async function load() {
  if (activeTab.value === 'models') {
    await loadModels();
  } else {
    await loadQuotas();
  }
}

function applySearch() {
  appliedFilters.value = { name: searchForm.value.name?.trim() ?? '' };
  modelPage.value = 1;
  void loadModels();
}

function resetEditor() {
  editorForm.tenantId = '';
  editorForm.name = '';
  editorForm.providerKey = 'openai_compatible';
  editorForm.endpointBaseUrl = 'https://api.openai.com/v1';
  editorForm.modelId = '';
  editorForm.apiKey = '';
  editorForm.organizationId = '';
  editorForm.isDefault = false;
  editorForm.isEnabled = true;
}

function onProviderChange(providerKey: string) {
  editorForm.endpointBaseUrl = providerKey === 'ollama'
    ? 'http://127.0.0.1:11434'
    : 'https://api.openai.com/v1';
}

function openCreate() {
  editorMode.value = 'create';
  editing.value = null;
  resetEditor();
  editorOpen.value = true;
}

async function openEdit(row: AiModelConfigListItem) {
  editorMode.value = 'edit';
  changing.value = true;
  try {
    const detail = await getAiModelConfig(row.id);
    editing.value = row;
    editorForm.tenantId = detail.tenantId ?? '';
    editorForm.name = detail.name;
    editorForm.providerKey = detail.providerKey;
    editorForm.endpointBaseUrl = detail.endpointBaseUrl;
    editorForm.modelId = detail.modelId;
    editorForm.apiKey = '';
    editorForm.organizationId = detail.organizationId ?? '';
    editorForm.isDefault = detail.isDefault;
    editorForm.isEnabled = detail.isEnabled;
    editorOpen.value = true;
  } catch (error) {
    ElMessage.error(toProblem(error, 'aiModelConfigs.loadFailed').title);
  } finally {
    changing.value = false;
  }
}

async function submitEditor() {
  await editorFormRef.value?.validate();
  changing.value = true;
  try {
    if (editorMode.value === 'create') {
      await createAiModelConfig({
        tenantId: editorForm.tenantId.trim() ? editorForm.tenantId.trim() : null,
        name: editorForm.name.trim(),
        providerKey: editorForm.providerKey,
        endpointBaseUrl: editorForm.endpointBaseUrl.trim(),
        modelId: editorForm.modelId.trim(),
        apiKey: editorForm.apiKey.trim() ? editorForm.apiKey.trim() : null,
        organizationId: editorForm.organizationId.trim() ? editorForm.organizationId.trim() : null,
        isDefault: editorForm.isDefault,
        isEnabled: editorForm.isEnabled
      });
      ElMessage.success(t('aiModelConfigs.createSuccess'));
    } else if (editing.value) {
      const detail = await getAiModelConfig(editing.value.id);
      await updateAiModelConfig(editing.value.id, {
        name: editorForm.name.trim(),
        providerKey: editorForm.providerKey,
        endpointBaseUrl: editorForm.endpointBaseUrl.trim(),
        modelId: editorForm.modelId.trim(),
        apiKey: editorForm.apiKey.trim() ? editorForm.apiKey.trim() : null,
        clearApiKey: false,
        organizationId: editorForm.organizationId.trim() ? editorForm.organizationId.trim() : null,
        isDefault: editorForm.isDefault,
        isEnabled: editorForm.isEnabled,
        version: detail.version
      });
      ElMessage.success(t('aiModelConfigs.updateSuccess'));
    }
    editorOpen.value = false;
    await loadModels();
  } catch (error) {
    ElMessage.error(toProblem(error, 'aiModelConfigs.saveFailed').title);
  } finally {
    changing.value = false;
  }
}

async function runTest(row: AiModelConfigListItem) {
  changing.value = true;
  try {
    const result = await testAiModelConfig(row.id);
    if (result.succeeded) {
      ElMessage.success(result.message);
    } else {
      ElMessage.error(result.message);
    }
    await loadModels();
  } catch (error) {
    ElMessage.error(toProblem(error, 'aiModelConfigs.testFailed').title);
  } finally {
    changing.value = false;
  }
}

async function runDisable(row: AiModelConfigListItem) {
  await ElMessageBox.confirm(
    t('aiModelConfigs.confirmDisable', { name: row.name }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    await disableAiModelConfig(row.id);
    ElMessage.success(t('aiModelConfigs.disableSuccess'));
    await loadModels();
  } catch (error) {
    ElMessage.error(toProblem(error, 'aiModelConfigs.saveFailed').title);
  } finally {
    changing.value = false;
  }
}

function openQuotaEditor(row?: AiTenantQuotaListItem) {
  editingQuota.value = row ?? null;
  quotaForm.tenantId = row?.tenantId ?? '';
  quotaForm.monthlyTokenLimit = row?.monthlyTokenLimit?.toString() ?? '';
  quotaForm.monthlyRequestLimit = row?.monthlyRequestLimit?.toString() ?? '';
  quotaForm.isEnabled = row?.isEnabled ?? true;
  quotaForm.version = row?.version ?? 0;
  quotaEditorOpen.value = true;
}

async function submitQuotaEditor() {
  await quotaFormRef.value?.validate();
  if (!quotaForm.tenantId.trim()) {
    ElMessage.error(t('aiModelConfigs.quotaTenantRequired'));
    return;
  }
  changing.value = true;
  try {
    await upsertAiTenantQuota(quotaForm.tenantId.trim(), {
      monthlyTokenLimit: quotaForm.monthlyTokenLimit.trim()
        ? Number(quotaForm.monthlyTokenLimit)
        : null,
      monthlyRequestLimit: quotaForm.monthlyRequestLimit.trim()
        ? Number(quotaForm.monthlyRequestLimit)
        : null,
      isEnabled: quotaForm.isEnabled,
      version: quotaForm.version
    });
    ElMessage.success(t('aiModelConfigs.quotaSaveSuccess'));
    quotaEditorOpen.value = false;
    await loadQuotas();
  } catch (error) {
    ElMessage.error(toProblem(error, 'aiModelConfigs.quotaSaveFailed').title);
  } finally {
    changing.value = false;
  }
}

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

onMounted(load);
</script>

<template>
  <section class="ai-model-configs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('aiModelConfigs.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail"
      show-icon
      class="art-page-alert"
    />

    <el-card class="art-page-card art-full-height-card" shadow="never">
      <el-tabs v-model="activeTab" @tab-change="load">
        <el-tab-pane :label="t('aiModelConfigs.tabModels')" name="models">
          <ArtTableHeader>
            <ArtSearchBar
              v-model="searchForm"
              :items="searchItems"
              @search="applySearch"
            />
            <template #left>
              <PermissionGate code="ai.models.create">
                <el-button
                  type="primary"
                  :icon="Plus"
                  data-testid="ai-model-config-create"
                  @click="openCreate"
                >
                  {{ t('aiModelConfigs.addModel') }}
                </el-button>
              </PermissionGate>
            </template>
          </ArtTableHeader>

          <div ref="tableMainRef" class="art-table-main">
            <el-table
              v-loading="loading"
              :data="modelItems"
              :height="tableHeight"
              :size="tableSize"
              :stripe="tableZebra"
              :border="tableBorder"
              :header-cell-style="tableHeaderCellStyle"
              :header-cell-class-name="tableHeaderBackground ? 'art-table-header-background' : ''"
            >
              <el-table-column type="index" :index="rowIndex" width="56" />
              <el-table-column prop="name" :label="t('aiModelConfigs.fieldName')" min-width="140" />
              <el-table-column :label="t('aiModelConfigs.fieldProvider')" min-width="140">
                <template #default="{ row }">{{ providerLabel(row.providerKey) }}</template>
              </el-table-column>
              <el-table-column prop="modelId" :label="t('aiModelConfigs.fieldModelId')" min-width="140" />
              <el-table-column prop="maskedEndpointBaseUrl" :label="t('aiModelConfigs.fieldEndpoint')" min-width="180" />
              <el-table-column :label="t('aiModelConfigs.fieldDefault')" width="90">
                <template #default="{ row }">
                  <el-tag v-if="row.isDefault" type="success">{{ t('aiModelConfigs.defaultTag') }}</el-tag>
                  <span v-else>-</span>
                </template>
              </el-table-column>
              <el-table-column :label="t('aiModelConfigs.fieldTestStatus')" min-width="120">
                <template #default="{ row }">
                  <el-tag v-if="row.lastTestStatusKey" :type="testStatusTagType(row.lastTestStatusKey)">
                    {{ row.lastTestStatusKey }}
                  </el-tag>
                  <span v-else>-</span>
                </template>
              </el-table-column>
              <!-- @vue-generic {AiModelConfigListItem} -->
          <el-table-column :label="t('aiModelConfigs.actions')" width="220" fixed="right">
                <template #default="{ row }">
                  <ArtTableActionGroup>
                    <PermissionGate code="ai.models.test">
                      <ArtTableActionButton type="view" @click="runTest(row)">
                        {{ t('aiModelConfigs.testConnection') }}
                      </ArtTableActionButton>
                    </PermissionGate>
                    <PermissionGate code="ai.models.update">
                      <ArtTableActionButton type="edit" @click="openEdit(row)">
                        {{ t('aiModelConfigs.actionEdit') }}
                      </ArtTableActionButton>
                    </PermissionGate>
                    <PermissionGate code="ai.models.update">
                      <ArtTableActionButton type="delete" @click="runDisable(row)">
                        {{ t('aiModelConfigs.actionDisable') }}
                      </ArtTableActionButton>
                    </PermissionGate>
                  </ArtTableActionGroup>
                </template>
              </el-table-column>
            </el-table>
          </div>

          <el-pagination
            v-model:current-page="modelPage"
            v-model:page-size="pageSize"
            layout="total, prev, pager, next"
            :total="modelTotal"
            class="art-table-pagination"
            @current-change="loadModels"
            @size-change="loadModels"
          />
        </el-tab-pane>

        <el-tab-pane :label="t('aiModelConfigs.tabQuotas')" name="quotas">
          <ArtTableHeader>
            <template #left>
              <PermissionGate code="ai.quotas.update">
                <el-button type="primary" :icon="Plus" @click="openQuotaEditor()">
                  {{ t('aiModelConfigs.addQuota') }}
                </el-button>
              </PermissionGate>
            </template>
          </ArtTableHeader>

          <el-table v-loading="loading" :data="quotaItems" border>
            <el-table-column prop="tenantId" :label="t('aiModelConfigs.fieldTenantId')" min-width="220" />
            <el-table-column prop="quotaMonthKey" :label="t('aiModelConfigs.fieldQuotaMonth')" width="120" />
            <el-table-column prop="usedTokensThisMonth" :label="t('aiModelConfigs.fieldUsedTokens')" min-width="120" />
            <el-table-column prop="monthlyTokenLimit" :label="t('aiModelConfigs.fieldTokenLimit')" min-width="120" />
            <el-table-column prop="usedRequestsThisMonth" :label="t('aiModelConfigs.fieldUsedRequests')" min-width="120" />
            <el-table-column prop="monthlyRequestLimit" :label="t('aiModelConfigs.fieldRequestLimit')" min-width="120" />
            <!-- @vue-generic {AiTenantQuotaListItem} -->
          <el-table-column :label="t('aiModelConfigs.actions')" width="120" fixed="right">
              <template #default="{ row }">
                <PermissionGate code="ai.quotas.update">
                  <el-button link type="primary" @click="openQuotaEditor(row)">
                    {{ t('aiModelConfigs.actionEdit') }}
                  </el-button>
                </PermissionGate>
              </template>
            </el-table-column>
          </el-table>

          <el-pagination
            v-model:current-page="quotaPage"
            v-model:page-size="pageSize"
            layout="total, prev, pager, next"
            :total="quotaTotal"
            class="art-table-pagination"
            @current-change="loadQuotas"
            @size-change="loadQuotas"
          />
        </el-tab-pane>
      </el-tabs>
    </el-card>

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('aiModelConfigs.createTitle') : t('aiModelConfigs.editTitle')"
      :confirm-loading="changing"
      @confirm="submitEditor"
    >
      <el-form ref="editorFormRef" :model="editorForm" label-width="120px">
        <el-form-item :label="t('aiModelConfigs.fieldTenantId')">
          <el-input v-model="editorForm.tenantId" :disabled="editorMode === 'edit'" />
        </el-form-item>
        <el-form-item :label="t('aiModelConfigs.fieldName')" required>
          <el-input v-model="editorForm.name" />
        </el-form-item>
        <el-form-item :label="t('aiModelConfigs.fieldProvider')" required>
          <el-select v-model="editorForm.providerKey" @change="onProviderChange">
            <el-option
              v-for="item in providerOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('aiModelConfigs.fieldEndpoint')" required>
          <el-input v-model="editorForm.endpointBaseUrl" />
        </el-form-item>
        <el-form-item :label="t('aiModelConfigs.fieldModelId')" required>
          <el-input v-model="editorForm.modelId" />
        </el-form-item>
        <el-form-item :label="t('aiModelConfigs.fieldApiKey')">
          <el-input v-model="editorForm.apiKey" type="password" show-password autocomplete="new-password" />
        </el-form-item>
        <el-form-item v-if="editorForm.providerKey === 'openai_compatible'" :label="t('aiModelConfigs.fieldOrganizationId')">
          <el-input v-model="editorForm.organizationId" />
        </el-form-item>
        <el-form-item :label="t('aiModelConfigs.fieldDefault')">
          <el-switch v-model="editorForm.isDefault" />
        </el-form-item>
        <el-form-item :label="t('aiModelConfigs.fieldEnabled')">
          <el-switch v-model="editorForm.isEnabled" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="quotaEditorOpen"
      :title="t('aiModelConfigs.quotaEditTitle')"
      :confirm-loading="changing"
      @confirm="submitQuotaEditor"
    >
      <el-form ref="quotaFormRef" :model="quotaForm" label-width="140px">
        <el-form-item :label="t('aiModelConfigs.fieldTenantId')" required>
          <el-input v-model="quotaForm.tenantId" :disabled="!!editingQuota" />
        </el-form-item>
        <el-form-item :label="t('aiModelConfigs.fieldTokenLimit')">
          <el-input v-model="quotaForm.monthlyTokenLimit" placeholder="empty = unlimited" />
        </el-form-item>
        <el-form-item :label="t('aiModelConfigs.fieldRequestLimit')">
          <el-input v-model="quotaForm.monthlyRequestLimit" placeholder="empty = unlimited" />
        </el-form-item>
        <el-form-item :label="t('aiModelConfigs.fieldEnabled')">
          <el-switch v-model="quotaForm.isEnabled" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>
  </section>
</template>
