<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import { useRouter } from 'vue-router';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElSwitch,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import { Plus } from '@element-plus/icons-vue';
import type { FormInstance } from 'element-plus';
import type { FullNetProblemDetails, GoViewProject } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { createGoViewProject, listGoViewProjects } from '../api/goview-projects';

defineOptions({ name: 'GoViewProjectsView' });

const router = useRouter();
const { t } = useAdminI18n();
const items = ref<GoViewProject[]>([]);
const loading = ref(false);
const creating = ref(false);
const problem = ref<FullNetProblemDetails>();
const createDialogVisible = ref(false);
const createFormRef = ref<FormInstance>();
const createForm = reactive({
  projectKey: '',
  name: '',
  isEnabled: true
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
    items.value = await listGoViewProjects();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'goviewProjects.loadFailed');
  } finally {
    loading.value = false;
  }
}

function openCreate(): void {
  createForm.projectKey = '';
  createForm.name = '';
  createForm.isEnabled = true;
  createDialogVisible.value = true;
}

async function submitCreate(): Promise<void> {
  creating.value = true;
  problem.value = undefined;
  try {
    const created = await createGoViewProject({
      projectKey: createForm.projectKey,
      name: createForm.name,
      isEnabled: createForm.isEnabled
    });
    ElMessage.success(t('goviewProjects.createSuccess'));
    createDialogVisible.value = false;
    await load();
    await router.push({ name: 'goview-editor', params: { projectId: created.id } });
  } catch (error: unknown) {
    problem.value = toProblem(error, 'goviewProjects.createFailed');
  } finally {
    creating.value = false;
  }
}

function openEditor(projectId: string): void {
  void router.push({ name: 'goview-editor', params: { projectId } });
}

function openPreview(projectId: string): void {
  void router.push({ name: 'goview-preview', params: { projectId } });
}

onMounted(() => {
  void load();
});
</script>

<template>
  <div class="goview-projects-view">
    <ArtTableHeader :title="t('goviewProjects.title')">
      <PermissionGate permission="goview.projects.create">
        <ElButton
          data-testid="goview-project-create"
          type="primary"
          :icon="Plus"
          @click="openCreate"
        >
          {{ t('goviewProjects.addProject') }}
        </ElButton>
      </PermissionGate>
    </ArtTableHeader>

    <ElAlert
      v-if="problem"
      type="error"
      :title="problem.title"
      show-icon
      class="mb-4"
    />

    <ElCard shadow="never">
      <ElTable v-loading="loading" :data="items" row-key="id">
        <ElTableColumn prop="name" :label="t('goviewProjects.fieldName')" min-width="160" />
        <ElTableColumn prop="projectKey" :label="t('goviewProjects.fieldProjectKey')" min-width="160" />
        <ElTableColumn :label="t('goviewProjects.fieldPublishedVersion')" width="140">
          <template #default="{ row }">
            <ElTag :type="row.latestPublishedVersionNumber > 0 ? 'success' : 'info'">
              v{{ row.latestPublishedVersionNumber }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('goviewProjects.fieldEnabled')" width="100">
          <template #default="{ row }">
            <ElTag :type="row.isEnabled ? 'success' : 'info'">
              {{ row.isEnabled ? t('goviewProjects.enabledYes') : t('goviewProjects.enabledNo') }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('goviewProjects.actions')" width="220" fixed="right">
          <template #default="{ row }">
            <ArtTableActionGroup>
              <PermissionGate permission="goview.projects.update">
                <ArtTableActionButton
                  data-testid="goview-project-edit"
                  @click="openEditor(row.id)"
                >
                  {{ t('goviewProjects.editCanvas') }}
                </ArtTableActionButton>
              </PermissionGate>
              <PermissionGate permission="goview.projects.preview">
                <ArtTableActionButton
                  data-testid="goview-project-preview"
                  :disabled="row.latestPublishedVersionNumber <= 0"
                  @click="openPreview(row.id)"
                >
                  {{ t('goviewProjects.preview') }}
                </ArtTableActionButton>
              </PermissionGate>
            </ArtTableActionGroup>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <ArtFormDialog
      v-model="createDialogVisible"
      :title="t('goviewProjects.createTitle')"
      :confirm-loading="creating"
      @confirm="submitCreate"
    >
      <ElForm ref="createFormRef" label-width="120px">
        <ElFormItem :label="t('goviewProjects.fieldProjectKey')" required>
          <ElInput v-model="createForm.projectKey" data-testid="goview-project-key-input" />
        </ElFormItem>
        <ElFormItem :label="t('goviewProjects.fieldName')" required>
          <ElInput v-model="createForm.name" data-testid="goview-project-name-input" />
        </ElFormItem>
        <ElFormItem :label="t('goviewProjects.fieldEnabled')">
          <ElSwitch v-model="createForm.isEnabled" />
        </ElFormItem>
      </ElForm>
    </ArtFormDialog>
  </div>
</template>
