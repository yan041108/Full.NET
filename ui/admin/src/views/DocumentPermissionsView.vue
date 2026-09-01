<script setup lang="ts">
import { ref } from 'vue';
import { ElAlert, ElButton, ElCard, ElForm, ElFormItem, ElInput, ElMessage, ElTable, ElTableColumn } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentPermissionResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getDocumentPermissionsByDocument,
  setDocumentPermissions
} from '../api/document-permissions';

defineOptions({ name: 'DocumentPermissionsView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const documentId = ref('');
const userId = ref('');
const permissionLevel = ref('read');
const loading = ref(false);
const saving = ref(false);
const problem = ref<FullNetProblemDetails>();
const permissions = ref<HostDocumentPermissionResponse[]>([]);

/** 读取/写入权限码分别控制两个操作入口，避免页面只靠按钮隐藏表达授权边界。 */
const canRead = () => session.can('document.host_permissions.read');
const canSet = () => session.can('document.host_permissions.set');

/** 只有在输入了文档标识后才请求权限快照，失败时清空旧列表避免误看历史结果。 */
async function loadPermissions() {
  if (!documentId.value.trim()) {
    return;
  }
  loading.value = true;
  problem.value = undefined;
  try {
    permissions.value = await getDocumentPermissionsByDocument(documentId.value.trim());
  } catch (error) {
    permissions.value = [];
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

/** 当前页采用整量保存语义；提交成功后直接以服务端返回结果替换本地列表，避免前端自行拼补权限集。 */
async function savePermission() {
  if (!documentId.value.trim() || !userId.value.trim()) {
    return;
  }
  saving.value = true;
  problem.value = undefined;
  try {
    permissions.value = await setDocumentPermissions({
      documentId: documentId.value.trim(),
      permissions: [
        {
          userId: userId.value.trim(),
          permissionLevel: permissionLevel.value.trim()
        }
      ]
    });
    ElMessage.success(t('documentPermissions.saveSuccess'));
  } catch (error) {
    problem.value = toProblem(error, 'documentPermissions.operationFailed');
  } finally {
    saving.value = false;
  }
}

/** 将未知异常统一折叠为当前页的稳定错误结构，保证加载失败和保存失败共享同一展示通道。 */
function toProblem(
  error: unknown,
  fallbackKey: 'documentPermissions.loadFailed' | 'documentPermissions.operationFailed' = 'documentPermissions.loadFailed'
): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t(fallbackKey), status: 500, code: fallbackKey };
}
</script>

<template>
  <section class="art-page">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentPermissions.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

    <el-card shadow="never">
      <el-form label-width="120px" data-testid="document-permissions-form">
        <el-form-item :label="t('documentPermissions.documentId')">
          <el-input v-model="documentId" autocomplete="off" />
        </el-form-item>
        <el-form-item>
          <PermissionGate code="document.host_permissions.read">
            <el-button
              type="primary"
              plain
              data-testid="document-permissions-load"
              :loading="loading"
              @click="loadPermissions"
            >
              {{ t('documentPermissions.load') }}
            </el-button>
          </PermissionGate>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card v-loading="loading" shadow="never" class="art-table-card">
      <el-table :data="permissions" class="art-crud-data-table">
        <el-table-column :label="t('documentPermissions.userId')" prop="userId" min-width="280" />
        <el-table-column :label="t('documentPermissions.permissionLevel')" prop="permissionLevel" min-width="160" />
        <template #empty>{{ t('documentPermissions.emptyDirectory') }}</template>
      </el-table>
    </el-card>

    <el-card shadow="never">
      <el-form label-width="120px" data-testid="document-permissions-set-form">
        <el-form-item :label="t('documentPermissions.userId')">
          <el-input v-model="userId" autocomplete="off" />
        </el-form-item>
        <el-form-item :label="t('documentPermissions.permissionLevel')">
          <el-input v-model="permissionLevel" autocomplete="off" />
        </el-form-item>
        <el-form-item>
          <PermissionGate code="document.host_permissions.set">
            <el-button
              type="primary"
              data-testid="document-permissions-save"
              :loading="saving"
              @click="savePermission"
            >
              {{ t('documentPermissions.save') }}
            </el-button>
          </PermissionGate>
        </el-form-item>
      </el-form>
    </el-card>
  </section>
</template>
