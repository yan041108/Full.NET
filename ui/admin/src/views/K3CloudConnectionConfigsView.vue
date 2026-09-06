<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElInputNumber,
  ElMessage,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FullNetProblemDetails, K3CloudConnectionConfig } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createK3CloudConnectionConfig,
  listK3CloudConnectionConfigs,
  testK3CloudConnectionConfig,
  updateK3CloudConnectionConfig
} from '../api/k3cloud';

defineOptions({ name: 'K3CloudConnectionConfigsView' });

type EditorMode = 'create' | 'edit';

const { t } = useAdminI18n();
const items = ref<K3CloudConnectionConfig[]>([]);
const loading = ref(false);
const saving = ref(false);
const testingId = ref('');
const problem = ref<FullNetProblemDetails>();
const editorOpen = ref(false);
const editorMode = ref<EditorMode>('create');
const editing = ref<K3CloudConnectionConfig | null>(null);
const editorForm = reactive({
  name: '',
  baseUrl: '',
  acctId: '',
  username: '',
  password: '',
  lcid: 2052,
  isDefault: false,
  isEnabled: true,
  version: 0
});

function toProblem(error: unknown, fallbackKey: string): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { title: t(fallbackKey), status: 500, type: 'about:blank' };
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    items.value = await listK3CloudConnectionConfigs();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'k3cloudConnections.loadFailed');
  } finally {
    loading.value = false;
  }
}

function openCreate(): void {
  editorMode.value = 'create';
  editing.value = null;
  editorForm.name = '';
  editorForm.baseUrl = '';
  editorForm.acctId = '';
  editorForm.username = '';
  editorForm.password = '';
  editorForm.lcid = 2052;
  editorForm.isDefault = false;
  editorForm.isEnabled = true;
  editorForm.version = 0;
  editorOpen.value = true;
}

function openEdit(item: K3CloudConnectionConfig): void {
  editorMode.value = 'edit';
  editing.value = item;
  editorForm.name = item.name;
  editorForm.baseUrl = item.baseUrl;
  editorForm.acctId = item.acctId;
  editorForm.username = item.username;
  editorForm.password = '';
  editorForm.lcid = item.lcid;
  editorForm.isDefault = item.isDefault;
  editorForm.isEnabled = item.isEnabled;
  editorForm.version = item.version;
  editorOpen.value = true;
}

async function submitEditor(): Promise<void> {
  saving.value = true;
  problem.value = undefined;
  try {
    if (editorMode.value === 'create') {
      await createK3CloudConnectionConfig({
        name: editorForm.name,
        baseUrl: editorForm.baseUrl,
        acctId: editorForm.acctId,
        username: editorForm.username,
        password: editorForm.password,
        lcid: editorForm.lcid,
        isDefault: editorForm.isDefault,
        isEnabled: editorForm.isEnabled
      });
      ElMessage.success(t('k3cloudConnections.createSuccess'));
    } else if (editing.value) {
      await updateK3CloudConnectionConfig(editing.value.id, {
        name: editorForm.name,
        baseUrl: editorForm.baseUrl,
        acctId: editorForm.acctId,
        username: editorForm.username,
        password: editorForm.password || null,
        lcid: editorForm.lcid,
        isDefault: editorForm.isDefault,
        isEnabled: editorForm.isEnabled,
        version: editorForm.version
      });
      ElMessage.success(t('k3cloudConnections.updateSuccess'));
    }
    editorOpen.value = false;
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'k3cloudConnections.saveFailed');
  } finally {
    saving.value = false;
  }
}

async function runTest(item: K3CloudConnectionConfig): Promise<void> {
  testingId.value = item.id;
  problem.value = undefined;
  try {
    const result = await testK3CloudConnectionConfig(item.id);
    ElMessage[result.succeeded ? 'success' : 'error'](result.message);
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'k3cloudConnections.testFailed');
  } finally {
    testingId.value = '';
  }
}

onMounted(() => {
  void load();
});
</script>

<template>
  <div class="k3cloud-connection-configs-view">
    <ArtTableHeader :title="t('k3cloudConnections.title')">
      <PermissionGate permission="k3cloud.connections.create">
        <ElButton data-testid="k3cloud-connection-create" type="primary" :icon="Plus" @click="openCreate">
          {{ t('k3cloudConnections.addConnection') }}
        </ElButton>
      </PermissionGate>
    </ArtTableHeader>

    <ElAlert v-if="problem" type="error" :title="problem.title" show-icon class="mb-4" />

    <ElCard shadow="never">
      <ElTable v-loading="loading" :data="items" row-key="id">
        <ElTableColumn prop="name" :label="t('k3cloudConnections.fieldName')" min-width="140" />
        <ElTableColumn prop="baseUrl" :label="t('k3cloudConnections.fieldBaseUrl')" min-width="200" />
        <ElTableColumn prop="acctId" :label="t('k3cloudConnections.fieldAcctId')" min-width="120" />
        <ElTableColumn :label="t('k3cloudConnections.fieldLastTest')" min-width="120">
          <template #default="{ row }">
            <ElTag v-if="row.lastTestStatusKey" :type="row.lastTestStatusKey === 'succeeded' ? 'success' : 'danger'">
              {{ row.lastTestStatusKey }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('k3cloudConnections.actions')" width="220" fixed="right">
          <template #default="{ row }">
            <ArtTableActionGroup>
              <PermissionGate permission="k3cloud.connections.update">
                <ArtTableActionButton @click="openEdit(row)">{{ t('k3cloudConnections.edit') }}</ArtTableActionButton>
              </PermissionGate>
              <PermissionGate permission="k3cloud.connections.test">
                <ArtTableActionButton
                  data-testid="k3cloud-connection-test"
                  :loading="testingId === row.id"
                  @click="runTest(row)"
                >
                  {{ t('k3cloudConnections.test') }}
                </ArtTableActionButton>
              </PermissionGate>
            </ArtTableActionGroup>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <ArtFormDialog
      v-model="editorOpen"
      :title="editorMode === 'create' ? t('k3cloudConnections.createTitle') : t('k3cloudConnections.editTitle')"
      :confirm-loading="saving"
      @confirm="submitEditor"
    >
      <ElForm label-width="120px">
        <ElFormItem :label="t('k3cloudConnections.fieldName')" required>
          <ElInput v-model="editorForm.name" />
        </ElFormItem>
        <ElFormItem :label="t('k3cloudConnections.fieldBaseUrl')" required>
          <ElInput v-model="editorForm.baseUrl" />
        </ElFormItem>
        <ElFormItem :label="t('k3cloudConnections.fieldAcctId')" required>
          <ElInput v-model="editorForm.acctId" />
        </ElFormItem>
        <ElFormItem :label="t('k3cloudConnections.fieldUsername')" required>
          <ElInput v-model="editorForm.username" />
        </ElFormItem>
        <ElFormItem :label="t('k3cloudConnections.fieldPassword')" :required="editorMode === 'create'">
          <ElInput v-model="editorForm.password" type="password" show-password />
        </ElFormItem>
        <ElFormItem :label="t('k3cloudConnections.fieldLcid')">
          <ElInputNumber v-model="editorForm.lcid" :min="1" />
        </ElFormItem>
        <ElFormItem :label="t('k3cloudConnections.fieldDefault')">
          <ElSwitch v-model="editorForm.isDefault" />
        </ElFormItem>
        <ElFormItem :label="t('k3cloudConnections.fieldEnabled')">
          <ElSwitch v-model="editorForm.isEnabled" />
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>
  </div>
</template>
