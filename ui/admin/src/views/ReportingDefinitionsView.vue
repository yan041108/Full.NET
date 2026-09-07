<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElDrawer,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
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
  ReportingDefinition,
  ReportingDefinitionVersion,
  ReportingGroup,
  ReportingParameterSchemaEntry,
  ReportingQueryPortDefinition
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
import { listReportingDataSources } from '../api/reporting-data-sources';
import {
  createReportingDefinition,
  createReportingGroup,
  deleteReportingDefinition,
  deleteReportingGroup,
  listReportingDefinitions,
  listReportingDefinitionVersions,
  listReportingGroups,
  listReportingQueryPorts,
  publishReportingDefinition,
  updateReportingDefinition,
  updateReportingGroup
} from '../api/reporting-definitions';

defineOptions({ name: 'ReportingDefinitionsView' });

type GroupEditorMode = 'create' | 'edit';
type DefinitionEditorMode = 'create' | 'edit';

const { t } = useAdminI18n();
const groups = ref<ReportingGroup[]>([]);
const definitions = ref<ReportingDefinition[]>([]);
const queryPorts = ref<ReportingQueryPortDefinition[]>([]);
const dataSourceOptions = ref<Array<{ value: string; label: string; providerKey: string }>>([]);
const selectedGroupId = ref<string>();
const versions = ref<ReportingDefinitionVersion[]>([]);
const loading = ref(false);
const acting = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref({ name: '' });
const groupEditorOpen = ref(false);
const groupEditorMode = ref<GroupEditorMode>('create');
const editingGroup = ref<ReportingGroup | null>(null);
const groupFormRef = ref<FormInstance>();
const groupForm = reactive({
  name: '',
  sortOrder: '0',
  isEnabled: true
});
const definitionEditorOpen = ref(false);
const definitionEditorMode = ref<DefinitionEditorMode>('create');
const editingDefinition = ref<ReportingDefinition | null>(null);
const definitionFormRef = ref<FormInstance>();
const definitionForm = reactive({
  definitionKey: '',
  name: '',
  description: '',
  dataSourceId: '',
  queryPortKey: '',
  layoutConfigJson: '{}',
  isEnabled: true,
  parameterSchema: [] as ReportingParameterSchemaEntry[]
});
const publishNote = ref('');
const versionsDrawerOpen = ref(false);

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
  { key: 'name', label: t('reportingDefinitions.fieldName'), type: 'input' }
]);

const filteredDefinitions = computed(() => {
  const keyword = appliedFilters.value.name.trim().toLowerCase();
  let rows = definitions.value;
  if (selectedGroupId.value) {
    rows = rows.filter(item => item.groupId === selectedGroupId.value);
  }
  if (keyword) {
    rows = rows.filter(item =>
      item.name.toLowerCase().includes(keyword)
      || item.definitionKey.toLowerCase().includes(keyword));
  }
  return rows;
});

const selectedQueryPort = computed(() =>
  queryPorts.value.find(port => port.queryPortKey === definitionForm.queryPortKey));

const compatibleDataSources = computed(() => {
  const port = selectedQueryPort.value;
  if (!port) {
    return dataSourceOptions.value;
  }
  return dataSourceOptions.value.filter(option =>
    port.supportedProviderKeys.includes(option.providerKey));
});

watch(() => definitionForm.queryPortKey, queryPortKey => {
  const port = queryPorts.value.find(item => item.queryPortKey === queryPortKey);
  if (!port) {
    definitionForm.parameterSchema = [];
    return;
  }
  definitionForm.parameterSchema = port.parameters.map(parameter => ({
    parameterKey: parameter.parameterKey,
    displayName: parameter.displayName,
    dataTypeKey: parameter.dataTypeKey,
    isRequired: parameter.isRequired,
    defaultValue: parameter.defaultValue
  }));
});

onMounted(() => {
  void loadPage();
});

async function loadPage(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [groupRows, definitionRows, portRows, dataSourcePage] = await Promise.all([
      listReportingGroups(),
      listReportingDefinitions(),
      listReportingQueryPorts(),
      listReportingDataSources({ page: 1, pageSize: 200, isEnabled: true })
    ]);
    groups.value = groupRows;
    definitions.value = definitionRows;
    queryPorts.value = portRows;
    dataSourceOptions.value = dataSourcePage.items.map(item => ({
      value: item.id,
      label: item.name,
      providerKey: item.providerKey
    }));
    if (!selectedGroupId.value && groups.value.length > 0) {
      selectedGroupId.value = groups.value[0]?.id;
    }
    updateTableHeight();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingDefinitions.loadFailed');
  } finally {
    loading.value = false;
  }
}

function selectGroup(groupId: string): void {
  selectedGroupId.value = groupId;
}

function handleSearch(params: Record<string, string | undefined>): void {
  appliedFilters.value = { name: params.name ?? '' };
}

function resetSearch(): void {
  appliedFilters.value = { name: '' };
}

function openCreateGroup(): void {
  groupEditorMode.value = 'create';
  editingGroup.value = null;
  groupForm.name = '';
  groupForm.sortOrder = '0';
  groupForm.isEnabled = true;
  groupEditorOpen.value = true;
}

function openEditGroup(group: ReportingGroup): void {
  groupEditorMode.value = 'edit';
  editingGroup.value = group;
  groupForm.name = group.name;
  groupForm.sortOrder = String(group.sortOrder);
  groupForm.isEnabled = group.isEnabled;
  groupEditorOpen.value = true;
}

async function submitGroup(): Promise<void> {
  if (!groupFormRef.value || acting.value) {
    return;
  }
  const valid = await groupFormRef.value.validate().catch(() => false);
  if (!valid) {
    return;
  }
  acting.value = true;
  problem.value = undefined;
  try {
    const sortOrder = Number.parseInt(groupForm.sortOrder, 10) || 0;
    if (groupEditorMode.value === 'create') {
      const created = await createReportingGroup({
        name: groupForm.name.trim(),
        sortOrder,
        isEnabled: groupForm.isEnabled
      });
      groups.value = [...groups.value, created];
      selectedGroupId.value = created.id;
      ElMessage.success(t('reportingDefinitions.groupCreateSuccess'));
    } else if (editingGroup.value) {
      const updated = await updateReportingGroup(editingGroup.value.id, {
        name: groupForm.name.trim(),
        sortOrder,
        isEnabled: groupForm.isEnabled,
        version: editingGroup.value.version
      });
      groups.value = groups.value.map(item => item.id === updated.id ? updated : item);
      ElMessage.success(t('reportingDefinitions.groupUpdateSuccess'));
    }
    groupEditorOpen.value = false;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingDefinitions.saveFailed');
  } finally {
    acting.value = false;
  }
}

async function confirmDeleteGroup(group: ReportingGroup): Promise<void> {
  try {
    await ElMessageBox.confirm(
      t('reportingDefinitions.confirmDeleteGroup', { name: group.name }),
      { type: 'warning' }
    );
  } catch {
    return;
  }
  acting.value = true;
  try {
    await deleteReportingGroup(group.id);
    groups.value = groups.value.filter(item => item.id !== group.id);
    definitions.value = definitions.value.filter(item => item.groupId !== group.id);
    if (selectedGroupId.value === group.id) {
      selectedGroupId.value = groups.value[0]?.id;
    }
    ElMessage.success(t('reportingDefinitions.groupDeleteSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingDefinitions.saveFailed');
  } finally {
    acting.value = false;
  }
}

function openCreateDefinition(): void {
  if (!selectedGroupId.value) {
    ElMessage.warning(t('reportingDefinitions.selectGroupFirst'));
    return;
  }
  definitionEditorMode.value = 'create';
  editingDefinition.value = null;
  definitionForm.definitionKey = '';
  definitionForm.name = '';
  definitionForm.description = '';
  definitionForm.dataSourceId = compatibleDataSources.value[0]?.value ?? '';
  definitionForm.queryPortKey = queryPorts.value[0]?.queryPortKey ?? '';
  definitionForm.layoutConfigJson = '{}';
  definitionForm.isEnabled = true;
  definitionEditorOpen.value = true;
}

function openEditDefinition(definition: ReportingDefinition): void {
  definitionEditorMode.value = 'edit';
  editingDefinition.value = definition;
  definitionForm.definitionKey = definition.definitionKey;
  definitionForm.name = definition.name;
  definitionForm.description = definition.description ?? '';
  definitionForm.dataSourceId = definition.dataSourceId;
  definitionForm.queryPortKey = definition.queryPortKey;
  definitionForm.layoutConfigJson = definition.layoutConfigJson;
  definitionForm.isEnabled = definition.isEnabled;
  definitionForm.parameterSchema = definition.parameterSchema.map(item => ({ ...item }));
  definitionEditorOpen.value = true;
}

async function submitDefinition(): Promise<void> {
  if (!definitionFormRef.value || acting.value || !selectedGroupId.value) {
    return;
  }
  const valid = await definitionFormRef.value.validate().catch(() => false);
  if (!valid) {
    return;
  }
  acting.value = true;
  problem.value = undefined;
  try {
    const payload = {
      groupId: selectedGroupId.value,
      dataSourceId: definitionForm.dataSourceId,
      name: definitionForm.name.trim(),
      description: definitionForm.description.trim() || null,
      queryPortKey: definitionForm.queryPortKey,
      parameterSchema: definitionForm.parameterSchema,
      layoutConfigJson: definitionForm.layoutConfigJson,
      isEnabled: definitionForm.isEnabled
    };
    if (definitionEditorMode.value === 'create') {
      const created = await createReportingDefinition({
        ...payload,
        definitionKey: definitionForm.definitionKey.trim()
      });
      definitions.value = [...definitions.value, created];
      ElMessage.success(t('reportingDefinitions.createSuccess'));
    } else if (editingDefinition.value) {
      const updated = await updateReportingDefinition(editingDefinition.value.id, {
        ...payload,
        version: editingDefinition.value.version
      });
      definitions.value = definitions.value.map(item => item.id === updated.id ? updated : item);
      editingDefinition.value = updated;
      ElMessage.success(t('reportingDefinitions.updateSuccess'));
    }
    definitionEditorOpen.value = false;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingDefinitions.saveFailed');
  } finally {
    acting.value = false;
  }
}

async function confirmDeleteDefinition(definition: ReportingDefinition): Promise<void> {
  try {
    await ElMessageBox.confirm(
      t('reportingDefinitions.confirmDeleteDefinition', { name: definition.name }),
      { type: 'warning' }
    );
  } catch {
    return;
  }
  acting.value = true;
  try {
    await deleteReportingDefinition(definition.id);
    definitions.value = definitions.value.filter(item => item.id !== definition.id);
    ElMessage.success(t('reportingDefinitions.deleteSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingDefinitions.saveFailed');
  } finally {
    acting.value = false;
  }
}

async function publishCurrentDefinition(definition: ReportingDefinition): Promise<void> {
  acting.value = true;
  problem.value = undefined;
  try {
    await publishReportingDefinition(definition.id, {
      changeNote: publishNote.value.trim() || null,
      version: definition.version
    });
    const refreshed = await listReportingDefinitions({ groupId: definition.groupId });
    definitions.value = definitions.value.map(item => {
      const updated = refreshed.find(row => row.id === item.id);
      return updated ?? item;
    });
    ElMessage.success(t('reportingDefinitions.publishSuccess'));
    publishNote.value = '';
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingDefinitions.saveFailed');
  } finally {
    acting.value = false;
  }
}

async function openVersions(definition: ReportingDefinition): Promise<void> {
  acting.value = true;
  try {
    versions.value = await listReportingDefinitionVersions(definition.id);
    editingDefinition.value = definition;
    versionsDrawerOpen.value = true;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'reportingDefinitions.loadFailed');
  } finally {
    acting.value = false;
  }
}

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { status: 500, code: 'client.unexpected_error', title: t(fallbackKey) };
}
</script>

<template>
  <div class="reporting-definitions-view">
    <ElAlert v-if="problem" type="error" :title="problem.title" show-icon class="mb-4" />

    <div class="layout-grid">
      <ElCard :header="t('reportingDefinitions.groupsTitle')" class="groups-card">
        <PermissionGate code="reporting.groups.create">
          <ElButton
            type="primary"
            :icon="Plus"
            class="mb-3"
            data-testid="reporting-group-create"
            @click="openCreateGroup"
          >
            {{ t('reportingDefinitions.addGroup') }}
          </ElButton>
        </PermissionGate>
        <ElTable :data="groups" size="small" highlight-current-row @row-click="row => selectGroup(row.id)">
          <ElTableColumn prop="name" :label="t('reportingDefinitions.fieldGroupName')" />
          <ElTableColumn prop="sortOrder" :label="t('reportingDefinitions.fieldSortOrder')" width="90" />
          <ElTableColumn :label="t('reportingDefinitions.fieldEnabled')" width="90">
            <template #default="{ row }">
              <ElTag :type="row.isEnabled ? 'success' : 'info'">
                {{ row.isEnabled ? t('reportingDefinitions.statusEnabled') : t('reportingDefinitions.statusDisabled') }}
              </ElTag>
            </template>
          </ElTableColumn>
          <!-- @vue-generic {ReportingGroup} -->
          <ElTableColumn :label="t('reportingDefinitions.actions')" width="140">
            <template #default="{ row }">
              <ArtTableActionGroup>
                <PermissionGate code="reporting.groups.update">
                  <ArtTableActionButton type="edit" @click="openEditGroup(row)">
                    {{ t('reportingDefinitions.actionEdit') }}
                  </ArtTableActionButton>
                </PermissionGate>
                <PermissionGate code="reporting.groups.delete">
                  <ArtTableActionButton type="delete" @click="confirmDeleteGroup(row)">
                    {{ t('reportingDefinitions.actionDelete') }}
                  </ArtTableActionButton>
                </PermissionGate>
              </ArtTableActionGroup>
            </template>
          </ElTableColumn>
        </ElTable>
      </ElCard>

      <ElCard class="definitions-card">
        <ArtTableHeader :title="t('reportingDefinitions.title')">
          <template #actions>
            <PermissionGate code="reporting.definitions.create">
              <ElButton
                type="primary"
                :icon="Plus"
                data-testid="reporting-definition-create"
                @click="openCreateDefinition"
              >
                {{ t('reportingDefinitions.addDefinition') }}
              </ElButton>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <ArtSearchBar
          v-model="searchForm"
          :items="searchItems"
          @search="handleSearch"
          @reset="resetSearch"
        />

        <div ref="tableMainRef" class="table-main">
          <ElTable
            v-loading="loading"
            :data="filteredDefinitions"
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            :header-cell-class-name="() => (tableHeaderBackground ? 'is-background' : '')"
          >
            <ElTableColumn prop="definitionKey" :label="t('reportingDefinitions.fieldDefinitionKey')" min-width="180" />
            <ElTableColumn prop="name" :label="t('reportingDefinitions.fieldName')" min-width="160" />
            <ElTableColumn prop="queryPortKey" :label="t('reportingDefinitions.fieldQueryPort')" min-width="200" />
            <ElTableColumn :label="t('reportingDefinitions.fieldPublishedVersion')" width="120">
              <template #default="{ row }">
                {{ row.latestPublishedVersionNumber || '-' }}
              </template>
            </ElTableColumn>
            <!-- @vue-generic {ReportingDefinition} -->
          <ElTableColumn :label="t('reportingDefinitions.actions')" width="260" fixed="right">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate code="reporting.definitions.update">
                    <ArtTableActionButton type="edit" @click="openEditDefinition(row)">
                      {{ t('reportingDefinitions.actionEdit') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <PermissionGate code="reporting.definitions.publish">
                    <ArtTableActionButton type="view" @click="publishCurrentDefinition(row)">
                      {{ t('reportingDefinitions.actionPublish') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <ArtTableActionButton type="view" @click="openVersions(row)">
                    {{ t('reportingDefinitions.actionVersions') }}
                  </ArtTableActionButton>
                  <PermissionGate code="reporting.definitions.delete">
                    <ArtTableActionButton type="delete" @click="confirmDeleteDefinition(row)">
                      {{ t('reportingDefinitions.actionDelete') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </ElTableColumn>
          </ElTable>
        </div>
      </ElCard>
    </div>

    <ArtFormDialog
      v-model:open="groupEditorOpen"
      :title="groupEditorMode === 'create' ? t('reportingDefinitions.createGroupTitle') : t('reportingDefinitions.editGroupTitle')"
      :saving="acting"
      @confirm="submitGroup"
    >
      <ElForm ref="groupFormRef" :model="groupForm" label-width="120px">
        <ElFormItem :label="t('reportingDefinitions.fieldGroupName')" prop="name" required>
          <ElInput v-model="groupForm.name" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldSortOrder')" prop="sortOrder" required>
          <ElInput v-model="groupForm.sortOrder" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldEnabled')">
          <ElSwitch v-model="groupForm.isEnabled" />
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="definitionEditorOpen"
      :title="definitionEditorMode === 'create' ? t('reportingDefinitions.createTitle') : t('reportingDefinitions.editTitle')"
      :saving="acting"
      width="760px"
      @confirm="submitDefinition"
    >
      <ElForm ref="definitionFormRef" :model="definitionForm" label-width="140px">
        <ElFormItem
          v-if="definitionEditorMode === 'create'"
          :label="t('reportingDefinitions.fieldDefinitionKey')"
          prop="definitionKey"
          required
        >
          <ElInput v-model="definitionForm.definitionKey" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldName')" prop="name" required>
          <ElInput v-model="definitionForm.name" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldDescription')">
          <ElInput v-model="definitionForm.description" type="textarea" :rows="2" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldQueryPort')" prop="queryPortKey" required>
          <ElSelect v-model="definitionForm.queryPortKey" filterable>
            <ElOption
              v-for="port in queryPorts"
              :key="port.queryPortKey"
              :label="port.displayName"
              :value="port.queryPortKey"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldDataSource')" prop="dataSourceId" required>
          <ElSelect v-model="definitionForm.dataSourceId" filterable>
            <ElOption
              v-for="option in compatibleDataSources"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldLayoutConfig')">
          <ElInput v-model="definitionForm.layoutConfigJson" type="textarea" :rows="3" />
        </ElFormItem>
        <ElFormItem :label="t('reportingDefinitions.fieldEnabled')">
          <ElSwitch v-model="definitionForm.isEnabled" />
        </ElFormItem>
        <ElFormItem v-if="definitionForm.parameterSchema.length > 0" :label="t('reportingDefinitions.fieldParameterSchema')">
          <ElTable :data="definitionForm.parameterSchema" size="small">
            <ElTableColumn prop="parameterKey" :label="t('reportingDefinitions.fieldParameterKey')" />
            <ElTableColumn :label="t('reportingDefinitions.fieldParameterDisplayName')">
              <template #default="{ row }">
                <ElInput v-model="row.displayName" />
              </template>
            </ElTableColumn>
            <ElTableColumn :label="t('reportingDefinitions.fieldParameterDefault')">
              <template #default="{ row }">
                <ElInput v-model="row.defaultValue" />
              </template>
            </ElTableColumn>
          </ElTable>
        </ElFormItem>
        <ElFormItem v-if="definitionEditorMode === 'edit'" :label="t('reportingDefinitions.fieldChangeNote')">
          <ElInput v-model="publishNote" type="textarea" :rows="2" />
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>

    <ElDrawer v-model="versionsDrawerOpen" :title="t('reportingDefinitions.versionsTitle')" size="40%">
      <ElTable :data="versions" size="small">
        <ElTableColumn prop="versionNumber" :label="t('reportingDefinitions.fieldVersionNumber')" width="100" />
        <ElTableColumn prop="queryPortKey" :label="t('reportingDefinitions.fieldQueryPort')" />
        <ElTableColumn prop="publishedAtUtc" :label="t('reportingDefinitions.fieldPublishedAt')" min-width="180" />
        <ElTableColumn prop="changeNote" :label="t('reportingDefinitions.fieldChangeNote')" />
      </ElTable>
    </ElDrawer>
  </div>
</template>

<style scoped>
.layout-grid {
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: 16px;
}

.groups-card,
.definitions-card {
  min-height: 520px;
}

.table-main {
  margin-top: 12px;
}

.mb-3 {
  margin-bottom: 12px;
}

.mb-4 {
  margin-bottom: 16px;
}
</style>
