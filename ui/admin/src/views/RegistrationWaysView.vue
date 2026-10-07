<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, onMounted, reactive, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElPagination,
  ElSelect,
  ElOption,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, RegistrationMode, RegistrationPolicy, RegistrationWay } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import { useSessionStore } from '../auth/session';
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
const policyLoading = ref(false);
const session = useSessionStore();
let disposed = false;
let inactive = false;
let viewGeneration = 0;
let policyRequest = 0;
let listRequest = 0;
// 会话与页面生命周期变化后，旧请求不能修改新页面状态或显示成功提示。
function invalidateView() {
  viewGeneration++; policyRequest++; listRequest++;
  policy.value = null; items.value = []; total.value = 0;
  policyLoading.value = false; policySaving.value = false; loading.value = false;
}
onBeforeUnmount(() => { disposed = true; invalidateView(); });
onDeactivated(() => { inactive = true; invalidateView(); });
onActivated(() => {
  if (!inactive) return;
  inactive = false; void loadPolicy(); void load();
});
watch([() => session.currentUser?.id, () => session.currentUser?.sessionId], () => {
  invalidateView();
  if (!disposed && !inactive) { void loadPolicy(); void load(); }
}, { flush: 'sync' });
const policyModeLabel = computed(() => policy.value ? t(([
  'registrationWays.modeDisabled', 'registrationWays.modeInvitationOnly', 'registrationWays.modeOpen'
] as const)[policy.value.registrationMode]) : t('registrationWays.policyUnavailable'));
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
  if (disposed || inactive || policyLoading.value || policySaving.value) return;
  const request = ++policyRequest;
  policyLoading.value = true;
  try {
    const result = await getRegistrationPolicy();
    if (!disposed && !inactive && request === policyRequest) policy.value = result;
  } catch (error) {
    if (!disposed && !inactive && request === policyRequest) showProblem(error, t('registrationWays.policyLoadFailed'));
  } finally {
    if (request === policyRequest) policyLoading.value = false;
  }
}

async function savePolicy(mode: RegistrationMode) {
  if (!policy.value || disposed || inactive || policySaving.value || policyLoading.value
      || (mode !== 0 && mode !== 1 && mode !== 2)) return;
  const request = ++policyRequest;
  policySaving.value = true;
  try {
    const result = await updateRegistrationPolicy({
      registrationMode: mode,
      isPublicRegistrationEnabled: mode === 2,
      version: policy.value.version
    });
    if (!disposed && !inactive && request === policyRequest) {
      policy.value = result;
      showSuccess(t('registrationWays.policyUpdateSuccess'));
    }
  } catch (error) {
    if (!disposed && !inactive && request === policyRequest) showProblem(error, t('registrationWays.policyUpdateFailed'));
  } finally {
    if (request === policyRequest) policySaving.value = false;
  }
}

async function load() {
  if (disposed || inactive) return;
  const generation = viewGeneration;
  const request = ++listRequest;
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listRegistrationWays({
      page: page.value,
      pageSize: pageSize.value,
      tenantId: appliedFilters.value.tenantId,
      nameContains: appliedFilters.value.name
    });
    if (disposed || inactive || generation !== viewGeneration || request !== listRequest) return;
    items.value = result.items;
    total.value = result.total;
    updateTableHeight();
  } catch (error) {
    if (!disposed && !inactive && generation === viewGeneration && request === listRequest) problem.value = error as FullNetProblemDetails;
  } finally {
    if (generation === viewGeneration && request === listRequest) loading.value = false;
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

onMounted(() => { void loadPolicy(); void load(); });
</script>

<template>
  <section class="registration-ways-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="registration-ways-title">{{ t('registrationWays.title') }}</h1>
    <ElCard class="registration-policy-card" shadow="never">
      <template #header>
        <span>{{ t('registrationWays.policyTitle') }}</span>
      </template>
      <div class="registration-policy-row">
        <span data-testid="registration-policy-current">{{ policyModeLabel }}</span>
        <PermissionGate code="identity.registration_policy.update">
          <ElSelect
            :model-value="policy?.registrationMode"
            :disabled="!policy || policyLoading || policySaving"
            :loading="policySaving || policyLoading"
            :aria-label="t('registrationWays.policyTitle')"
            data-testid="registration-policy-mode"
            @change="savePolicy"
          >
            <ElOption :value="0" :label="t('registrationWays.modeDisabled')" />
            <ElOption :value="1" :label="t('registrationWays.modeInvitationOnly')" />
            <ElOption :value="2" :label="t('registrationWays.modeOpen')" />
          </ElSelect>
        </PermissionGate>
        <ElButton :disabled="policyLoading || policySaving" data-testid="registration-policy-retry" @click="loadPolicy">
          {{ t('registrationWays.policyRefresh') }}
        </ElButton>
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
      <ArtTableHeader>
        <template #left>
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
.registration-ways-title {
  margin: 0;
  font-size: 1.25rem;
}
.registration-policy-card {
  margin-bottom: 12px;
}

.registration-policy-row {
  display: flex;
  align-items: center;
  gap: 12px;
}
</style>
