<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElPagination,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, RegistrationPolicy, RegistrationWay } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createRegistrationWay,
  deleteRegistrationWay,
  getRegistrationPolicy,
  listRegistrationWays,
  updateRegistrationPolicy,
  updateRegistrationWay
} from '../api/registration-ways';

defineOptions({ name: 'RegistrationWaysView' });

type EditorMode = 'create' | 'edit';

const { t } = useAdminI18n();
const policy = ref<RegistrationPolicy | null>(null);
const policySaving = ref(false);
const items = ref<RegistrationWay[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const searchForm = ref<Record<string, string | undefined>>({});
const appliedFilters = ref({ tenantId: '', name: '' });
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingWay = ref<RegistrationWay | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  tenantId: '',
  name: '',
  code: '',
  isEnabled: true,
  roleId: '',
  organizationUnitId: '',
  positionId: '',
  sortOrder: '0',
  remark: ''
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
  { key: 'tenantId', label: t('registrationWays.fieldTenantId'), type: 'input' },
  { key: 'name', label: t('registrationWays.fieldName'), type: 'input' }
]);

function rowIndex(index: number) {
  return (page.value - 1) * pageSize.value + index + 1;
}

async function loadPolicy() {
  policy.value = await getRegistrationPolicy();
}

async function savePolicy(enabled: boolean) {
  if (!policy.value) {
    return;
  }
  policySaving.value = true;
  try {
    policy.value = await updateRegistrationPolicy({
      isPublicRegistrationEnabled: enabled,
      version: policy.value.version
    });
    ElMessage.success(t('registrationWays.policyUpdateSuccess'));
  } finally {
    policySaving.value = false;
  }
}

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listRegistrationWays({
      page: page.value,
      pageSize: pageSize.value,
      tenantId: appliedFilters.value.tenantId,
      nameContains: appliedFilters.value.name
    });
    items.value = result.items;
    total.value = result.total;
    updateTableHeight();
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    loading.value = false;
  }
}

function applySearch(values: Record<string, string | undefined>) {
  appliedFilters.value = {
    tenantId: values.tenantId?.trim() ?? '',
    name: values.name?.trim() ?? ''
  };
  page.value = 1;
  void load();
}

function resetSearch() {
  searchForm.value = {};
  appliedFilters.value = { tenantId: '', name: '' };
  page.value = 1;
  void load();
}

function openCreate() {
  editorMode.value = 'create';
  editingWay.value = null;
  Object.assign(editorForm, {
    tenantId: appliedFilters.value.tenantId,
    name: '',
    code: '',
    isEnabled: true,
    roleId: '',
    organizationUnitId: '',
    positionId: '',
    sortOrder: '0',
    remark: ''
  });
  editorOpen.value = true;
}

function openEdit(way: RegistrationWay) {
  editorMode.value = 'edit';
  editingWay.value = way;
  Object.assign(editorForm, {
    tenantId: way.tenantId,
    name: way.name,
    code: way.code,
    isEnabled: way.isEnabled,
    roleId: way.roleId,
    organizationUnitId: way.organizationUnitId,
    positionId: way.positionId ?? '',
    sortOrder: String(way.sortOrder),
    remark: way.remark ?? ''
  });
  editorOpen.value = true;
}

async function submitEditor() {
  changing.value = true;
  try {
    if (editorMode.value === 'create') {
      await createRegistrationWay({
        tenantId: editorForm.tenantId.trim(),
        name: editorForm.name.trim(),
        code: editorForm.code.trim(),
        isEnabled: editorForm.isEnabled,
        roleId: editorForm.roleId.trim(),
        organizationUnitId: editorForm.organizationUnitId.trim(),
        positionId: editorForm.positionId.trim() || null,
        sortOrder: Number(editorForm.sortOrder) || 0,
        remark: editorForm.remark.trim() || null
      });
      ElMessage.success(t('registrationWays.createSuccess'));
    } else if (editingWay.value) {
      await updateRegistrationWay(editingWay.value.id, {
        name: editorForm.name.trim(),
        code: editorForm.code.trim(),
        isEnabled: editorForm.isEnabled,
        roleId: editorForm.roleId.trim(),
        organizationUnitId: editorForm.organizationUnitId.trim(),
        positionId: editorForm.positionId.trim() || null,
        sortOrder: Number(editorForm.sortOrder) || 0,
        remark: editorForm.remark.trim() || null,
        version: editingWay.value.version
      });
      ElMessage.success(t('registrationWays.updateSuccess'));
    }
    editorOpen.value = false;
    await load();
  } finally {
    changing.value = false;
  }
}

async function confirmDelete(way: RegistrationWay) {
  await ElMessageBox.confirm(
    t('registrationWays.confirmDelete', { name: way.name }),
    { type: 'warning' }
  );
  changing.value = true;
  try {
    await deleteRegistrationWay(way.id);
    ElMessage.success(t('registrationWays.deleteSuccess'));
    await load();
  } finally {
    changing.value = false;
  }
}

onMounted(async () => {
  await loadPolicy();
  await load();
});
</script>

<template>
  <section class="registration-ways-view art-page-stack art-full-height" :aria-busy="loading">
    <ElCard class="registration-policy-card" shadow="never">
      <template #header>
        <span>{{ t('registrationWays.policyTitle') }}</span>
      </template>
      <div class="registration-policy-row">
        <span>{{ t('registrationWays.policyPublicEnabled') }}</span>
        <PermissionGate code="identity.registration_policy.update">
          <ElSwitch
            :model-value="policy?.isPublicRegistrationEnabled ?? false"
            :loading="policySaving"
            data-testid="registration-policy-toggle"
            @change="value => savePolicy(value === true)"
          />
        </PermissionGate>
        <ElTag type="info">{{ t('registrationWays.policyDefaultDisabled') }}</ElTag>
      </div>
    </ElCard>

    <ArtSearchBar
      v-model="searchForm"
      :items="searchItems"
      :loading="loading"
      @search="applySearch"
      @reset="resetSearch"
    />

    <ElCard ref="tableMainRef" class="art-table-card art-full-height" shadow="never">
      <ArtTableHeader :title="t('registrationWays.title')">
        <template #actions>
          <PermissionGate code="identity.registration_ways.create">
            <ElButton
              type="primary"
              :icon="Plus"
              data-testid="registration-ways-action-create"
              @click="openCreate"
            >
              {{ t('registrationWays.create') }}
            </ElButton>
          </PermissionGate>
        </template>
      </ArtTableHeader>

      <ElTable
        v-loading="loading"
        :data="items"
        :size="tableSize"
        :stripe="tableZebra"
        :border="tableBorder"
        :height="tableHeight"
        :header-cell-style="tableHeaderCellStyle"
        :header-cell-class-name="tableHeaderBackground ? 'art-table-header-background' : ''"
      >
        <ElTableColumn type="index" :index="rowIndex" width="64" />
        <ElTableColumn prop="name" :label="t('registrationWays.fieldName')" min-width="140" />
        <ElTableColumn prop="code" :label="t('registrationWays.fieldCode')" min-width="140" />
        <ElTableColumn prop="tenantId" :label="t('registrationWays.fieldTenantId')" min-width="220" />
        <ElTableColumn :label="t('registrationWays.status')" width="100">
          <template #default="{ row }">
            <ElTag :type="row.isEnabled ? 'success' : 'info'">
              {{ row.isEnabled ? t('registrationWays.statusEnabled') : t('registrationWays.statusDisabled') }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="sortOrder" :label="t('registrationWays.fieldSortOrder')" width="90" />
        <!-- @vue-generic {RegistrationWay} -->
          <ElTableColumn fixed="right" width="180">
          <template #default="{ row }">
            <ArtTableActionGroup>
              <PermissionGate code="identity.registration_ways.update">
                <ArtTableActionButton type="edit"
                  :title="t('registrationWays.edit')"
                  test-id="registration-ways-action-edit"
                  @click="openEdit(row)"
                />
              </PermissionGate>
              <PermissionGate code="identity.registration_ways.delete">
                <ArtTableActionButton type="delete"
                  :title="t('registrationWays.delete')"
                  test-id="registration-ways-action-delete"
                  @click="confirmDelete(row)"
                />
              </PermissionGate>
            </ArtTableActionGroup>
          </template>
        </ElTableColumn>
      </ElTable>

      <ElPagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        class="art-table-pagination"
        layout="total, sizes, prev, pager, next"
        :total="total"
        @change="load"
      />
    </ElCard>

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('registrationWays.createTitle') : t('registrationWays.editTitle')"
      :saving="changing"
      confirm-test-id="registration-ways-editor-submit"
      @confirm="submitEditor"
    >
      <ElForm ref="editorFormRef" label-position="top" class="registration-ways-editor-form">
        <ElFormItem v-if="editorMode === 'create'" :label="t('registrationWays.fieldTenantId')" required>
          <ElInput v-model="editorForm.tenantId" />
        </ElFormItem>
        <ElFormItem :label="t('registrationWays.fieldName')" required>
          <ElInput v-model="editorForm.name" />
        </ElFormItem>
        <ElFormItem :label="t('registrationWays.fieldCode')" required>
          <ElInput v-model="editorForm.code" />
        </ElFormItem>
        <ElFormItem :label="t('registrationWays.fieldRoleId')" required>
          <ElInput v-model="editorForm.roleId" />
        </ElFormItem>
        <ElFormItem :label="t('registrationWays.fieldOrganizationUnitId')" required>
          <ElInput v-model="editorForm.organizationUnitId" />
        </ElFormItem>
        <ElFormItem :label="t('registrationWays.fieldPositionId')">
          <ElInput v-model="editorForm.positionId" />
        </ElFormItem>
        <ElFormItem :label="t('registrationWays.fieldSortOrder')">
          <ElInput v-model="editorForm.sortOrder" />
        </ElFormItem>
        <ElFormItem :label="t('registrationWays.fieldRemark')">
          <ElInput v-model="editorForm.remark" type="textarea" :rows="3" />
        </ElFormItem>
        <ElFormItem :label="t('registrationWays.status')">
          <ElSwitch v-model="editorForm.isEnabled" />
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.registration-policy-card {
  margin-bottom: 12px;
}

.registration-policy-row {
  display: flex;
  align-items: center;
  gap: 12px;
}
</style>
