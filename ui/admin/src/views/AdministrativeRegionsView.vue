<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElInputNumber,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElRadio,
  ElRadioGroup,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElUpload
} from 'element-plus';
import { Plus, Upload } from '@element-plus/icons-vue';
import type { FormInstance, UploadFile } from 'element-plus';
import type {
  AdministrativeRegion,
  AdministrativeRegionDatasetManifest,
  AdministrativeRegionTreeNode,
  FullNetProblemDetails,
  ImportAdministrativeRegionsPreview,
  ImportAdministrativeRegionsRequest
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import AdministrativeRegionCascader from '../components/AdministrativeRegionCascader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  applyAdministrativeRegionImport,
  createAdministrativeRegion,
  deleteAdministrativeRegion,
  getAdministrativeRegion,
  getAdministrativeRegionTree,
  getLatestAdministrativeRegionDatasetManifest,
  previewAdministrativeRegionImport,
  updateAdministrativeRegion
} from '../api/administrative-regions';

defineOptions({ name: 'AdministrativeRegionsView' });

type EditorMode = 'create' | 'edit';

interface TreeRow extends AdministrativeRegionTreeNode {
  version?: number;
}

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const inHostContext = computed(() => !session.currentUser?.tenantId);
const treeRows = ref<TreeRow[]>([]);
const manifest = ref<AdministrativeRegionDatasetManifest | null>(null);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editingRegion = ref<AdministrativeRegion | null>(null);
const editorParentId = ref<string | null>(null);
const editorFormRef = ref<FormInstance>();
const editorForm = reactive({
  code: '',
  name: '',
  level: 1,
  displayOrder: 100,
  regionType: '',
  remark: ''
});
const fieldErrors = reactive({ code: '', name: '', level: '' });
const importOpen = ref(false);
const importPreview = ref<ImportAdministrativeRegionsPreview | null>(null);
const importPayload = ref<ImportAdministrativeRegionsRequest | null>(null);
const importMergeMode = ref<'merge' | 'replace'>('merge');
const cascaderDemo = ref<string[]>([]);

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  watchLoading
} = useArtCrudTableLayout();

const manifestLabel = computed(() => {
  if (!manifest.value) {
    return t('administrativeRegions.manifestEmpty');
  }
  return t('administrativeRegions.manifestCurrent', {
    version: manifest.value.datasetVersion,
    count: manifest.value.recordCount
  });
});

watchLoading(loading);

onMounted(() => {
  if (inHostContext.value) {
    void load();
  }
});

async function load(): Promise<void> {
  if (!inHostContext.value) {
    return;
  }
  loading.value = true;
  problem.value = undefined;
  try {
    const [tree, latestManifest] = await Promise.all([
      getAdministrativeRegionTree(undefined, 5),
      getLatestAdministrativeRegionDatasetManifest()
    ]);
    treeRows.value = tree;
    manifest.value = latestManifest;
  } catch (error: unknown) {
    problem.value = resolveProblem(error);
  } finally {
    loading.value = false;
  }
}

function openCreate(parentId: string | null = null): void {
  editorMode.value = 'create';
  editingRegion.value = null;
  editorParentId.value = parentId;
  editorForm.code = '';
  editorForm.name = '';
  editorForm.level = parentId ? 2 : 1;
  editorForm.displayOrder = 100;
  editorForm.regionType = '';
  editorForm.remark = '';
  clearFieldErrors();
  editorOpen.value = true;
}

async function openEdit(row: TreeRow): Promise<void> {
  changing.value = true;
  try {
    const region = await getAdministrativeRegion(row.id);
    editorMode.value = 'edit';
    editingRegion.value = region;
    editorParentId.value = region.parentId;
    editorForm.code = region.code;
    editorForm.name = region.name;
    editorForm.level = region.level;
    editorForm.displayOrder = region.displayOrder;
    editorForm.regionType = region.regionType ?? '';
    editorForm.remark = region.remark ?? '';
    clearFieldErrors();
    editorOpen.value = true;
  } catch (error: unknown) {
    problem.value = resolveProblem(error, 'administrativeRegions.operationFailed');
  } finally {
    changing.value = false;
  }
}

function clearFieldErrors(): void {
  fieldErrors.code = '';
  fieldErrors.name = '';
  fieldErrors.level = '';
}

function validateEditor(): boolean {
  clearFieldErrors();
  let valid = true;
  if (editorMode.value === 'create' && !editorForm.code.trim()) {
    fieldErrors.code = t('administrativeRegions.codeRequired');
    valid = false;
  }
  if (!editorForm.name.trim()) {
    fieldErrors.name = t('administrativeRegions.nameRequired');
    valid = false;
  }
  if (editorForm.level < 1 || editorForm.level > 5) {
    fieldErrors.level = t('administrativeRegions.levelInvalid');
    valid = false;
  }
  return valid;
}

async function submitEditor(): Promise<void> {
  if (!validateEditor()) {
    return;
  }
  changing.value = true;
  try {
    if (editorMode.value === 'create') {
      await createAdministrativeRegion({
        parentId: editorParentId.value,
        code: editorForm.code.trim(),
        name: editorForm.name.trim(),
        level: editorForm.level,
        displayOrder: editorForm.displayOrder,
        regionType: editorForm.regionType.trim() || null,
        remark: editorForm.remark.trim() || null
      });
      ElMessage.success(t('administrativeRegions.createSuccess'));
    } else if (editingRegion.value) {
      await updateAdministrativeRegion(editingRegion.value.id, {
        parentId: editingRegion.value.parentId,
        name: editorForm.name.trim(),
        level: editorForm.level,
        displayOrder: editorForm.displayOrder,
        regionType: editorForm.regionType.trim() || null,
        remark: editorForm.remark.trim() || null,
        version: editingRegion.value.version
      });
      ElMessage.success(t('administrativeRegions.updateSuccess'));
    }
    editorOpen.value = false;
    await load();
  } catch (error: unknown) {
    problem.value = resolveProblem(error, 'administrativeRegions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function confirmDelete(row: TreeRow): Promise<void> {
  try {
    await ElMessageBox.confirm(
      t('administrativeRegions.confirmDelete', { name: row.name }),
      t('administrativeRegions.delete'),
      { type: 'warning' }
    );
  } catch {
    return;
  }
  changing.value = true;
  try {
    const region = await getAdministrativeRegion(row.id);
    await deleteAdministrativeRegion(region.id, region.version);
    ElMessage.success(t('administrativeRegions.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = resolveProblem(error, 'administrativeRegions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function handleImportFile(uploadFile: UploadFile): Promise<void> {
  if (!uploadFile.raw) {
    return;
  }
  const text = await uploadFile.raw.text();
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    ElMessage.error(t('administrativeRegions.importInvalidJson'));
    return;
  }
  if (!isImportFilePayload(parsed)) {
    ElMessage.error(t('administrativeRegions.importInvalidShape'));
    return;
  }
  const payload: ImportAdministrativeRegionsRequest = {
    datasetKey: parsed.datasetKey ?? 'china.administrative',
    datasetVersion: parsed.datasetVersion ?? `import.${new Date().toISOString().slice(0, 10)}`,
    sourceDigest: parsed.sourceDigest ?? `sha256:${await digestText(text)}`,
    mergeMode: importMergeMode.value,
    items: parsed.items
  };
  changing.value = true;
  try {
    importPreview.value = await previewAdministrativeRegionImport(payload);
    importPayload.value = payload;
    importOpen.value = true;
  } catch (error: unknown) {
    problem.value = resolveProblem(error, 'administrativeRegions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function applyImport(): Promise<void> {
  if (!importPayload.value) {
    return;
  }
  changing.value = true;
  try {
    const result = await applyAdministrativeRegionImport(importPayload.value);
    ElMessage.success(t('administrativeRegions.importSuccess', {
      added: result.addedCount,
      updated: result.updatedCount,
      removed: result.removedCount
    }));
    importOpen.value = false;
    importPreview.value = null;
    importPayload.value = null;
    await load();
  } catch (error: unknown) {
    problem.value = resolveProblem(error, 'administrativeRegions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function digestText(text: string): Promise<string> {
  const data = new TextEncoder().encode(text);
  const hash = await crypto.subtle.digest('SHA-256', data);
  return Array.from(new Uint8Array(hash))
    .map(byte => byte.toString(16).padStart(2, '0'))
    .join('');
}

function isImportFilePayload(value: unknown): value is {
  datasetKey?: string;
  datasetVersion?: string;
  sourceDigest?: string;
  items: ImportAdministrativeRegionsRequest['items'];
} {
  return typeof value === 'object'
    && value !== null
    && 'items' in value
    && Array.isArray((value as { items?: unknown }).items);
}

function resolveProblem(
  error: unknown,
  fallbackKey: 'administrativeRegions.loadFailed' | 'administrativeRegions.operationFailed' = 'administrativeRegions.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.administrative_region_failed', title: t(fallbackKey) };
}

function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}
</script>

<template>
  <section class="administrative-regions-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('administrativeRegions.title') }}</h1>
    <p class="art-page-description">{{ t('administrativeRegions.description') }}</p>

    <ElAlert
      v-if="!inHostContext"
      type="warning"
      :closable="false"
      :title="t('administrativeRegions.hostContextRequired')"
      show-icon
    />

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-card v-if="inHostContext" class="administrative-regions-manifest" shadow="never">
      <p>{{ manifestLabel }}</p>
      <p v-if="manifest" class="administrative-regions-manifest__meta">
        {{ t('administrativeRegions.manifestAppliedAt') }}:
        {{ formatDateTime(manifest.appliedAtUtc) }}
      </p>
    </el-card>

    <el-card v-if="inHostContext" class="art-table-card" shadow="never">
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
            <PermissionGate code="regions.administrative_regions.create">
              <el-button
                type="primary"
                plain
                :icon="Plus"
                data-testid="administrative-regions-action-create"
                @click="openCreate()"
              >
                {{ t('administrativeRegions.addRegion') }}
              </el-button>
            </PermissionGate>
            <PermissionGate code="regions.administrative_regions.import">
              <el-radio-group v-model="importMergeMode" size="small" class="administrative-regions-import-mode">
                <el-radio value="merge">{{ t('administrativeRegions.importMerge') }}</el-radio>
                <el-radio value="replace">{{ t('administrativeRegions.importReplace') }}</el-radio>
              </el-radio-group>
              <el-upload
                :auto-upload="false"
                :show-file-list="false"
                accept="application/json,.json"
                data-testid="administrative-regions-import-upload"
                @change="handleImportFile"
              >
                <el-button type="primary" plain :icon="Upload">
                  {{ t('administrativeRegions.importJson') }}
                </el-button>
              </el-upload>
            </PermissionGate>
          </template>
        </ArtTableHeader>

        <div class="art-table" :class="{ 'is-empty': treeRows.length === 0 }">
          <el-table
            v-loading="loading"
            :data="treeRows"
            row-key="id"
            :tree-props="{ children: 'children' }"
            default-expand-all
            :height="tableHeight"
            :size="tableSize"
            :stripe="tableZebra"
            :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle"
            class="art-crud-data-table"
            data-testid="administrative-regions-tree-table"
          >
            <el-table-column :label="t('administrativeRegions.fieldCode')" prop="code" min-width="120" />
            <el-table-column :label="t('administrativeRegions.fieldName')" prop="name" min-width="160" />
            <el-table-column :label="t('administrativeRegions.fieldLevel')" prop="level" width="80" align="center" />
            <el-table-column :label="t('users.columnSortOrder')" prop="displayOrder" width="88" align="center" />
            <!-- @vue-generic {TreeRow} -->
          <el-table-column :label="t('users.columnActions')" width="180" fixed="right">
              <template #default="{ row }">
                <ArtTableActionGroup>
                  <PermissionGate code="regions.administrative_regions.create">
                    <ArtTableActionButton
                      type="add"
                      test-id="administrative-regions-add-child"
                      @click="openCreate(row.id)"
                    />
                  </PermissionGate>
                  <PermissionGate code="regions.administrative_regions.update">
                    <ArtTableActionButton type="edit"
                      test-id="administrative-regions-edit"
                      @click="openEdit(row)"
                    >
                      {{ t('administrativeRegions.edit') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                  <PermissionGate code="regions.administrative_regions.delete">
                    <ArtTableActionButton
                      type="delete"
                      test-id="administrative-regions-delete"
                      @click="confirmDelete(row)"
                    >
                      {{ t('administrativeRegions.delete') }}
                    </ArtTableActionButton>
                  </PermissionGate>
                </ArtTableActionGroup>
              </template>
            </el-table-column>
          </el-table>
        </div>
      </div>
    </el-card>

    <el-card class="administrative-regions-cascader-demo" shadow="never">
      <template #header>{{ t('administrativeRegions.cascaderDemoTitle') }}</template>
      <AdministrativeRegionCascader
        v-model="cascaderDemo"
        :placeholder="t('administrativeRegions.cascaderPlaceholder')"
      />
    </el-card>

    <ArtFormDialog
      v-model:open="editorOpen"
      :title="editorMode === 'create' ? t('administrativeRegions.createDialogTitle') : t('administrativeRegions.editDialogTitle')"
      :confirm-label="editorMode === 'create' ? t('administrativeRegions.create') : t('administrativeRegions.save')"
      :saving="changing"
      confirm-test-id="administrative-regions-editor-submit"
      @confirm="submitEditor"
    >
      <el-form ref="editorFormRef" label-position="top" data-testid="administrative-regions-editor-form" @submit.prevent>
        <el-form-item v-if="editorMode === 'create'" :label="t('administrativeRegions.fieldCode')" :error="fieldErrors.code">
          <el-input v-model="editorForm.code" data-testid="administrative-regions-editor-code" maxlength="12" />
        </el-form-item>
        <el-form-item :label="t('administrativeRegions.fieldName')" :error="fieldErrors.name">
          <el-input v-model="editorForm.name" data-testid="administrative-regions-editor-name" maxlength="128" />
        </el-form-item>
        <el-form-item :label="t('administrativeRegions.fieldLevel')" :error="fieldErrors.level">
          <el-input-number v-model="editorForm.level" :min="1" :max="5" />
        </el-form-item>
        <el-form-item :label="t('users.columnSortOrder')">
          <el-input-number v-model="editorForm.displayOrder" :min="0" :max="9999" />
        </el-form-item>
        <el-form-item :label="t('administrativeRegions.fieldRegionType')">
          <el-input v-model="editorForm.regionType" maxlength="32" />
        </el-form-item>
        <el-form-item :label="t('administrativeRegions.fieldRemark')">
          <el-input v-model="editorForm.remark" type="textarea" :rows="3" maxlength="128" />
        </el-form-item>
      </el-form>
    </ArtFormDialog>

    <ArtFormDialog
      v-model:open="importOpen"
      :title="t('administrativeRegions.importPreviewTitle')"
      :confirm-label="t('administrativeRegions.importApply')"
      :saving="changing"
      confirm-test-id="administrative-regions-import-apply"
      @confirm="applyImport"
    >
      <div v-if="importPreview" class="administrative-regions-import-preview" data-testid="administrative-regions-import-preview">
        <p>{{ t('administrativeRegions.importAdded', { count: importPreview.added.length }) }}</p>
        <p>{{ t('administrativeRegions.importUpdated', { count: importPreview.updated.length }) }}</p>
        <p>{{ t('administrativeRegions.importRemoved', { count: importPreview.removed.length }) }}</p>
        <p>{{ t('administrativeRegions.importSkipped', { count: importPreview.skippedCount }) }}</p>
        <el-table :data="importPreview.added" size="small" max-height="160">
          <el-table-column prop="code" :label="t('administrativeRegions.fieldCode')" />
          <el-table-column prop="name" :label="t('administrativeRegions.fieldName')" />
          <el-table-column prop="level" :label="t('administrativeRegions.fieldLevel')" width="80" />
        </el-table>
      </div>
    </ArtFormDialog>
  </section>
</template>

<style scoped>
.administrative-regions-manifest {
  margin-bottom: 12px;
}

.administrative-regions-manifest__meta {
  margin: 4px 0 0;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.administrative-regions-import-mode {
  margin-right: 12px;
}

.administrative-regions-cascader-demo {
  margin-top: 16px;
}
</style>
