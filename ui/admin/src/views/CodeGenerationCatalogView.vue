<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect,
  ElTabPane,
  ElTabs,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type {
  CodeGenerationCatalogMetadataResponse,
  CodeGenerationCatalogMigrationDraftResponse,
  CodeGenerationCatalogObjectResponse,
  FullNetProblemDetails
} from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  generateCodeGenerationCatalogMigrationDraft,
  getCodeGenerationCatalogMetadata,
  listCodeGenerationCatalogObjects
} from '../api/code-generation-catalog';

defineOptions({ name: 'CodeGenerationCatalogView' });

const { t } = useAdminI18n();
const objects = ref<CodeGenerationCatalogObjectResponse[]>([]);
const selectedName = ref<string | null>(null);
const metadata = ref<CodeGenerationCatalogMetadataResponse | null>(null);
const draft = ref<CodeGenerationCatalogMigrationDraftResponse | null>(null);
const kindFilter = ref<'all' | 'table' | 'view'>('all');
const search = ref('');
const loading = ref(false);
const metadataLoading = ref(false);
const draftLoading = ref(false);
const problem = ref<FullNetProblemDetails>();
const draftTab = ref('sqlServer');

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

const filteredObjects = computed(() => {
  const keyword = search.value.trim().toLowerCase();
  return objects.value.filter((item) => {
    if (kindFilter.value !== 'all' && item.objectKind !== kindFilter.value) {
      return false;
    }
    if (!keyword) {
      return true;
    }
    return item.objectName.toLowerCase().includes(keyword);
  });
});

const selectedObject = computed(() =>
  objects.value.find(item => item.objectName === selectedName.value) ?? null);

const canGenerateDraft = computed(() =>
  selectedObject.value?.objectKind === 'table');

async function loadObjects() {
  loading.value = true;
  problem.value = undefined;
  try {
    objects.value = await listCodeGenerationCatalogObjects();
    updateTableHeight();
    if (selectedName.value
      && !objects.value.some(item => item.objectName === selectedName.value)) {
      selectedName.value = null;
      metadata.value = null;
      draft.value = null;
    }
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    loading.value = false;
  }
}

async function selectObject(objectName: string) {
  selectedName.value = objectName;
  metadata.value = null;
  draft.value = null;
  metadataLoading.value = true;
  problem.value = undefined;
  try {
    metadata.value = await getCodeGenerationCatalogMetadata(objectName);
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    metadataLoading.value = false;
  }
}

async function generateDraft() {
  if (!selectedName.value || !canGenerateDraft.value) {
    return;
  }

  draftLoading.value = true;
  problem.value = undefined;
  try {
    draft.value = await generateCodeGenerationCatalogMigrationDraft(
      selectedName.value
    );
    if (draft.value.warnings.length > 0) {
      ElMessage.warning(t('codeGenerationCatalog.draftWarnings'));
    } else {
      ElMessage.success(t('codeGenerationCatalog.draftReady'));
    }
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    draftLoading.value = false;
  }
}

function kindLabel(kind: string) {
  return kind === 'view'
    ? t('codeGenerationCatalog.kindView')
    : t('codeGenerationCatalog.kindTable');
}

onMounted(() => {
  void loadObjects();
});
</script>

<template>
  <div class="code-generation-catalog-view art-page-stack">
    <ArtTableHeader :title="t('codeGenerationCatalog.title')" />
    <p class="art-muted">{{ t('codeGenerationCatalog.description') }}</p>

    <div class="code-generation-catalog-layout">
      <ElCard ref="tableMainRef" class="code-generation-catalog-objects">
        <div class="code-generation-catalog-filters">
          <ElInput
            v-model="search"
            clearable
            :placeholder="t('codeGenerationCatalog.searchPlaceholder')"
          />
          <ElSelect v-model="kindFilter" :placeholder="t('codeGenerationCatalog.kindFilter')">
            <ElOption :label="t('codeGenerationCatalog.kindAll')" value="all" />
            <ElOption :label="t('codeGenerationCatalog.kindTable')" value="table" />
            <ElOption :label="t('codeGenerationCatalog.kindView')" value="view" />
          </ElSelect>
          <ElButton :loading="loading" @click="loadObjects">
            {{ t('codeGenerationCatalog.refresh') }}
          </ElButton>
        </div>
        <ElTable
          v-loading="loading"
          :data="filteredObjects"
          :height="tableHeight"
          :size="tableSize"
          :stripe="tableZebra"
          :border="tableBorder"
          :header-cell-style="tableHeaderCellStyle"
          highlight-current-row
          @row-click="(row: CodeGenerationCatalogObjectResponse) => selectObject(row.objectName)"
        >
          <ElTableColumn prop="objectName" :label="t('codeGenerationCatalog.objectName')" min-width="220" />
          <ElTableColumn :label="t('codeGenerationCatalog.objectKind')" width="120">
            <template #default="{ row }">
              <ElTag :type="row.objectKind === 'view' ? 'info' : 'success'">
                {{ kindLabel(row.objectKind) }}
              </ElTag>
            </template>
          </ElTableColumn>
        </ElTable>
      </ElCard>

      <ElCard class="code-generation-catalog-detail" v-loading="metadataLoading">
        <template v-if="metadata">
          <div class="code-generation-catalog-detail-header">
            <h2>{{ metadata.objectName }}</h2>
            <ElTag :type="metadata.objectKind === 'view' ? 'info' : 'success'">
              {{ kindLabel(metadata.objectKind) }}
            </ElTag>
            <PermissionGate code="codegen.catalog.read">
              <ElButton
                v-if="canGenerateDraft"
                type="primary"
                :loading="draftLoading"
                @click="generateDraft"
              >
                {{ t('codeGenerationCatalog.generateDraft') }}
              </ElButton>
            </PermissionGate>
          </div>
          <ElTable :data="metadata.columns" size="small" border>
            <ElTableColumn prop="ordinalPosition" :label="t('codeGenerationCatalog.ordinal')" width="70" />
            <ElTableColumn prop="columnName" :label="t('codeGenerationCatalog.columnName')" min-width="140" />
            <ElTableColumn prop="dataType" :label="t('codeGenerationCatalog.dataType')" min-width="100" />
            <ElTableColumn prop="columnType" :label="t('codeGenerationCatalog.columnType')" min-width="160" />
            <ElTableColumn :label="t('codeGenerationCatalog.nullable')" width="90">
              <template #default="{ row }">
                {{ row.isNullable ? t('codeGenerationCatalog.yes') : t('codeGenerationCatalog.no') }}
              </template>
            </ElTableColumn>
            <ElTableColumn prop="maxLength" :label="t('codeGenerationCatalog.maxLength')" width="100" />
            <ElTableColumn prop="numericPrecision" :label="t('codeGenerationCatalog.precision')" width="90" />
            <ElTableColumn prop="numericScale" :label="t('codeGenerationCatalog.scale')" width="90" />
          </ElTable>
        </template>
        <p v-else class="art-empty-state">{{ t('codeGenerationCatalog.selectObject') }}</p>

        <section v-if="draft" class="code-generation-catalog-draft">
          <h3>{{ t('codeGenerationCatalog.draftTitle') }}</h3>
          <ul v-if="draft.warnings.length" class="code-generation-catalog-warnings">
            <li v-for="warning in draft.warnings" :key="warning">{{ warning }}</li>
          </ul>
          <ElTabs v-model="draftTab">
            <ElTabPane :label="t('codeGenerationCatalog.sqlServerDraft')" name="sqlServer">
              <pre class="code-generation-catalog-sql">{{ draft.sqlServerDraft }}</pre>
            </ElTabPane>
            <ElTabPane :label="t('codeGenerationCatalog.mySqlDraft')" name="mySql">
              <pre class="code-generation-catalog-sql">{{ draft.mySqlDraft }}</pre>
            </ElTabPane>
          </ElTabs>
        </section>
      </ElCard>
    </div>
  </div>
</template>

<style scoped>
.code-generation-catalog-layout {
  display: grid;
  grid-template-columns: minmax(280px, 1fr) minmax(360px, 1.4fr);
  gap: 16px;
}

.code-generation-catalog-filters {
  display: grid;
  grid-template-columns: 1fr auto auto;
  gap: 12px;
  margin-bottom: 12px;
}

.code-generation-catalog-detail-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
}

.code-generation-catalog-detail-header h2 {
  margin: 0;
}

.code-generation-catalog-draft {
  margin-top: 20px;
}

.code-generation-catalog-warnings {
  color: var(--el-color-warning);
  margin: 0 0 12px;
  padding-left: 20px;
}

.code-generation-catalog-sql {
  margin: 0;
  padding: 12px;
  overflow: auto;
  background: var(--el-fill-color-light);
  border-radius: 6px;
  white-space: pre-wrap;
  word-break: break-word;
  font-size: 12px;
  line-height: 1.5;
}

@media (max-width: 1100px) {
  .code-generation-catalog-layout {
    grid-template-columns: 1fr;
  }
}
</style>
