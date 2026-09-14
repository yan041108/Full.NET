<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import PermissionGate from '../components/PermissionGate.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  approveAiMcpRemoteTool,
  createAiMcpRemoteConnection,
  discoverAiMcpRemoteTools,
  listAiMcpRemoteConnections,
  type AiMcpRemoteConnectionListItem,
  type AiMcpRemoteDiscoveredToolItem
} from '../api/ai-mcp-remote-connections';

defineOptions({ name: 'AiMcpRemoteConnectionsView' });

const { t } = useAdminI18n();
const loading = ref(false);
const items = ref<AiMcpRemoteConnectionListItem[]>([]);
const editorOpen = ref(false);
const discoverOpen = ref(false);
const discovered = ref<AiMcpRemoteDiscoveredToolItem[]>([]);
const activeConnection = ref<AiMcpRemoteConnectionListItem | null>(null);
const editorForm = reactive({
  connectionKey: '',
  displayName: '',
  endpointUrl: '',
  serviceToken: ''
});

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

async function loadConnections() {
  loading.value = true;
  try {
    items.value = await listAiMcpRemoteConnections();
  } catch (error) {
    ElMessage.error(toProblem(error, 'aiMcpRemoteConnections.loadFailed').title);
  } finally {
    loading.value = false;
  }
}

function openCreate() {
  editorForm.connectionKey = '';
  editorForm.displayName = '';
  editorForm.endpointUrl = '';
  editorForm.serviceToken = '';
  editorOpen.value = true;
}

async function submitCreate() {
  loading.value = true;
  try {
    await createAiMcpRemoteConnection({
      connectionKey: editorForm.connectionKey.trim(),
      displayName: editorForm.displayName.trim(),
      endpointUrl: editorForm.endpointUrl.trim(),
      serviceToken: editorForm.serviceToken.trim()
    });
    ElMessage.success(t('aiMcpRemoteConnections.createSuccess'));
    editorOpen.value = false;
    await loadConnections();
  } catch (error) {
    ElMessage.error(toProblem(error, 'aiMcpRemoteConnections.saveFailed').title);
  } finally {
    loading.value = false;
  }
}

async function openDiscover(row: AiMcpRemoteConnectionListItem) {
  activeConnection.value = row;
  loading.value = true;
  try {
    discovered.value = await discoverAiMcpRemoteTools(row.id);
    discoverOpen.value = true;
  } catch (error) {
    ElMessage.error(toProblem(error, 'aiMcpRemoteConnections.discoverFailed').title);
  } finally {
    loading.value = false;
  }
}

async function approveTool(tool: AiMcpRemoteDiscoveredToolItem) {
  if (!activeConnection.value) {
    return;
  }
  loading.value = true;
  try {
    await approveAiMcpRemoteTool(activeConnection.value.id, {
      remoteToolName: tool.remoteToolName,
      sideEffectKey: 'read',
      permissionCode: 'ai.mcp_remote.invoke'
    });
    ElMessage.success(t('aiMcpRemoteConnections.approveSuccess'));
    discovered.value = await discoverAiMcpRemoteTools(activeConnection.value.id);
  } catch (error) {
    ElMessage.error(toProblem(error, 'aiMcpRemoteConnections.approveFailed').title);
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  void loadConnections();
});
</script>

<template>
  <section class="ai-mcp-remote-connections-view art-page-stack" :aria-busy="loading">
    <ElCard shadow="never">
      <ArtTableHeader>
        <template #left>
          <PermissionGate code="ai.mcp_remote.manage">
            <ElButton type="primary" :icon="Plus" data-testid="ai-mcp-remote-create" @click="openCreate">
              {{ t('aiMcpRemoteConnections.addConnection') }}
            </ElButton>
          </PermissionGate>
        </template>
      </ArtTableHeader>

      <ElTable :data="items" v-loading="loading">
        <ElTableColumn prop="connectionKey" :label="t('aiMcpRemoteConnections.fieldKey')" min-width="140" />
        <ElTableColumn prop="displayName" :label="t('aiMcpRemoteConnections.fieldName')" min-width="160" />
        <ElTableColumn prop="maskedEndpointUrl" :label="t('aiMcpRemoteConnections.fieldEndpoint')" min-width="220" />
        <ElTableColumn :label="t('aiMcpRemoteConnections.fieldEnabled')" width="100">
          <template #default="{ row }">
            <ElTag :type="row.isEnabled ? 'success' : 'info'">{{ row.isEnabled ? 'on' : 'off' }}</ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('aiMcpRemoteConnections.actions')" width="180" fixed="right">
          <template #default="{ row }">
            <PermissionGate code="ai.mcp_remote.manage">
              <ElButton link type="primary" @click="openDiscover(row as AiMcpRemoteConnectionListItem)">
                {{ t('aiMcpRemoteConnections.discoverTools') }}
              </ElButton>
            </PermissionGate>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <ElDialog v-model="editorOpen" :title="t('aiMcpRemoteConnections.createTitle')" width="520px">
      <ElForm label-width="120px">
        <ElFormItem :label="t('aiMcpRemoteConnections.fieldKey')" required>
          <ElInput v-model="editorForm.connectionKey" />
        </ElFormItem>
        <ElFormItem :label="t('aiMcpRemoteConnections.fieldName')" required>
          <ElInput v-model="editorForm.displayName" />
        </ElFormItem>
        <ElFormItem :label="t('aiMcpRemoteConnections.fieldEndpoint')" required>
          <ElInput v-model="editorForm.endpointUrl" />
        </ElFormItem>
        <ElFormItem :label="t('aiMcpRemoteConnections.fieldServiceToken')" required>
          <ElInput v-model="editorForm.serviceToken" type="password" show-password autocomplete="new-password" />
        </ElFormItem>
      </ElForm>
      <template #footer>
        <ElButton @click="editorOpen = false">{{ t('common.cancel') }}</ElButton>
        <ElButton type="primary" @click="submitCreate">{{ t('aiMcpRemoteConnections.submitCreate') }}</ElButton>
      </template>
    </ElDialog>

    <ElDialog v-model="discoverOpen" :title="t('aiMcpRemoteConnections.discoverTitle')" width="720px">
      <ElTable :data="discovered" v-loading="loading">
        <ElTableColumn prop="remoteToolName" :label="t('aiMcpRemoteConnections.fieldTool')" min-width="180" />
        <ElTableColumn :label="t('aiMcpRemoteConnections.fieldApproved')" width="120">
          <template #default="{ row }">
            <ElTag :type="row.isApproved ? 'success' : 'info'">
              {{ row.isApproved ? 'yes' : 'no' }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('aiMcpRemoteConnections.actions')" width="120">
          <template #default="{ row }">
            <ElButton v-if="!row.isApproved" link type="primary" @click="approveTool(row as AiMcpRemoteDiscoveredToolItem)">
              {{ t('aiMcpRemoteConnections.approve') }}
            </ElButton>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElDialog>
  </section>
</template>
