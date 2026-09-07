<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElMessage,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, StorageProviderCatalogItem } from '@fullnet/client-contracts';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { listStorageProviders, testStorageProviderConnectivity } from '../api/storage-providers';

defineOptions({ name: 'StorageProvidersView' });

const { t } = useAdminI18n();
const items = ref<StorageProviderCatalogItem[]>([]);
const loading = ref(false);
const testingKey = ref<string | null>(null);
const problem = ref<FullNetProblemDetails>();

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

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    items.value = await listStorageProviders();
    updateTableHeight();
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    loading.value = false;
  }
}

async function runConnectivityTest(provider: StorageProviderCatalogItem) {
  testingKey.value = provider.providerKey;
  try {
    const result = await testStorageProviderConnectivity(provider.providerKey);
    if (result.succeeded) {
      ElMessage.success(result.message);
    } else {
      ElMessage.warning(result.message);
    }
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    testingKey.value = null;
  }
}

function kindLabel(kind: string) {
  switch (kind) {
    case 'local':
      return t('storageProviders.kindLocal');
    case 's3':
      return t('storageProviders.kindS3');
    case 'oss':
      return t('storageProviders.kindOss');
    default:
      return kind;
  }
}

onMounted(() => {
  void load();
});
</script>

<template>
  <div class="storage-providers-view">
    <ArtTableHeader :title="t('storageProviders.title')" />
    <ElCard ref="tableMainRef" class="storage-providers-card">
      <ElTable
        v-loading="loading"
        :data="items"
        :height="tableHeight"
        :size="tableSize"
        :stripe="tableZebra"
        :border="tableBorder"
        :header-cell-style="tableHeaderCellStyle"
        :header-cell-class-name="tableHeaderBackground ? 'art-table-header-background' : ''"
      >
        <ElTableColumn prop="providerKey" :label="t('storageProviders.fieldProviderKey')" min-width="120" />
        <ElTableColumn prop="displayName" :label="t('storageProviders.fieldDisplayName')" min-width="160" />
        <ElTableColumn :label="t('storageProviders.fieldKind')" min-width="120">
          <template #default="{ row }">
            {{ kindLabel(row.kind) }}
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('storageProviders.fieldDefault')" width="100">
          <template #default="{ row }">
            <ElTag v-if="row.isDefault" type="success">{{ t('storageProviders.defaultBadge') }}</ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('storageProviders.fieldConfigured')" width="110">
          <template #default="{ row }">
            <ElTag :type="row.isConfigured ? 'success' : 'info'">
              {{ row.isConfigured ? t('storageProviders.configuredYes') : t('storageProviders.configuredNo') }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn
          prop="configurationSummary"
          :label="t('storageProviders.fieldConfigurationSummary')"
          min-width="260"
          show-overflow-tooltip
        />
        <!-- @vue-generic {StorageProviderCatalogItem} -->
          <ElTableColumn :label="t('storageProviders.actions')" width="140" fixed="right">
          <template #default="{ row }">
            <ArtTableActionGroup>
              <PermissionGate code="files.storage_providers.test">
                <ArtTableActionButton type="view"
                  v-if="row.supportsConnectivityTest"
                  :loading="testingKey === row.providerKey"
                  @click="runConnectivityTest(row)"
                >
                  {{ t('storageProviders.testConnection') }}
                </ArtTableActionButton>
              </PermissionGate>
            </ArtTableActionGroup>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>
  </div>
</template>

<style scoped>
.storage-providers-view {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.storage-providers-card {
  flex: 1;
}
</style>
