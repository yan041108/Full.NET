<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
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
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FullNetProblemDetails, K3CloudConnectionConfig, K3CloudDocumentSync } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createK3CloudDocumentSync,
  listK3CloudConnectionConfigs,
  listK3CloudDocumentSyncs,
  retryK3CloudDocumentSync
} from '../api/k3cloud';

defineOptions({ name: 'K3CloudDocumentSyncsView' });

const DEFAULT_PAYLOAD = '{"Model":{"FBillNo":"SO-DEMO-001","FSaleOrgId":{"FNumber":"100"},"FCustId":{"FNumber":"CUST001"}}}';

const { t } = useAdminI18n();
const items = ref<K3CloudDocumentSync[]>([]);
const connections = ref<K3CloudConnectionConfig[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const creating = ref(false);
const retryingId = ref('');
const problem = ref<FullNetProblemDetails>();
const createDialogVisible = ref(false);
const createForm = reactive({
  connectionConfigId: '',
  businessKey: '',
  payloadJson: DEFAULT_PAYLOAD
});

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const [syncPage, connectionItems] = await Promise.all([
      listK3CloudDocumentSyncs(page.value, pageSize.value),
      listK3CloudConnectionConfigs()
    ]);
    items.value = syncPage.items;
    total.value = syncPage.total;
    connections.value = connectionItems;
    createForm.connectionConfigId = connectionItems[0]?.id ?? '';
  } catch (error: unknown) {
    problem.value = toProblem(error, 'k3cloudDocumentSyncs.loadFailed');
  } finally {
    loading.value = false;
  }
}

function openCreate(): void {
  createForm.businessKey = '';
  createForm.payloadJson = DEFAULT_PAYLOAD;
  createDialogVisible.value = true;
}

async function submitCreate(): Promise<void> {
  creating.value = true;
  problem.value = undefined;
  try {
    await createK3CloudDocumentSync({
      connectionConfigId: createForm.connectionConfigId,
      documentTypeKey: 'k3cloud.sal_sale_order',
      businessKey: createForm.businessKey,
      payloadJson: createForm.payloadJson
    });
    ElMessage.success(t('k3cloudDocumentSyncs.createSuccess'));
    createDialogVisible.value = false;
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'k3cloudDocumentSyncs.createFailed');
  } finally {
    creating.value = false;
  }
}

async function runRetry(item: K3CloudDocumentSync): Promise<void> {
  retryingId.value = item.id;
  problem.value = undefined;
  try {
    await retryK3CloudDocumentSync(item.id);
    ElMessage.success(t('k3cloudDocumentSyncs.retrySuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'k3cloudDocumentSyncs.retryFailed');
  } finally {
    retryingId.value = '';
  }
}

onMounted(() => {
  void load();
});
</script>

<template>
  <div class="k3cloud-document-syncs-view">
    <ArtTableHeader :title="t('k3cloudDocumentSyncs.title')">
      <PermissionGate code="k3cloud.document_syncs.create">
        <ElButton data-testid="k3cloud-document-sync-create" type="primary" :icon="Plus" @click="openCreate">
          {{ t('k3cloudDocumentSyncs.createSync') }}
        </ElButton>
      </PermissionGate>
    </ArtTableHeader>

    <ElAlert v-if="problem" type="error" :title="problem.title" show-icon class="mb-4" />

    <ElCard shadow="never">
      <ElTable v-loading="loading" :data="items" row-key="id">
        <ElTableColumn prop="businessKey" :label="t('k3cloudDocumentSyncs.fieldBusinessKey')" min-width="140" />
        <ElTableColumn prop="documentTypeKey" :label="t('k3cloudDocumentSyncs.fieldDocumentType')" min-width="160" />
        <ElTableColumn :label="t('k3cloudDocumentSyncs.fieldStatus')" width="140">
          <template #default="{ row }">
            <ElTag :type="row.statusKey === 'submitted' ? 'success' : row.statusKey.endsWith('failed') ? 'danger' : 'info'">
              {{ row.statusKey }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="externalBillNo" :label="t('k3cloudDocumentSyncs.fieldExternalBillNo')" min-width="120" />
        <ElTableColumn prop="lastErrorMessage" :label="t('k3cloudDocumentSyncs.fieldLastError')" min-width="180" />
        <!-- @vue-generic {K3CloudDocumentSync} -->
          <ElTableColumn :label="t('k3cloudDocumentSyncs.actions')" width="120" fixed="right">
          <template #default="{ row }">
            <ArtTableActionGroup>
              <PermissionGate code="k3cloud.document_syncs.retry">
                <ArtTableActionButton type="delete"
                  test-id="k3cloud-document-sync-retry"
                  :disabled="row.statusKey === 'submitted'"
                  :loading="retryingId === row.id"
                  @click="runRetry(row)"
                >
                  {{ t('k3cloudDocumentSyncs.retry') }}
                </ArtTableActionButton>
              </PermissionGate>
            </ArtTableActionGroup>
          </template>
        </ElTableColumn>
      </ElTable>
      <ElPagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        class="mt-4"
        layout="total, prev, pager, next"
        :total="total"
        @current-change="load"
        @size-change="load"
      />
    </ElCard>

    <ArtFormDialog
      v-model:open="createDialogVisible"
      :title="t('k3cloudDocumentSyncs.createTitle')"
      :confirm-loading="creating"
      @confirm="submitCreate"
    >
      <ElForm label-width="120px">
        <ElFormItem :label="t('k3cloudDocumentSyncs.fieldConnection')" required>
          <ElSelect v-model="createForm.connectionConfigId" class="w-full">
            <ElOption
              v-for="item in connections"
              :key="item.id"
              :label="item.name"
              :value="item.id"
            />
          </ElSelect>
        </ElFormItem>
        <ElFormItem :label="t('k3cloudDocumentSyncs.fieldBusinessKey')" required>
          <ElInput v-model="createForm.businessKey" />
        </ElFormItem>
        <ElFormItem :label="t('k3cloudDocumentSyncs.fieldPayloadJson')" required>
          <ElInput v-model="createForm.payloadJson" type="textarea" :rows="10" />
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>
  </div>
</template>
