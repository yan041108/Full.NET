<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElSwitch
} from 'element-plus';
import type { FullNetProblemDetails, GoViewProject } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getGoViewProject,
  publishGoViewProject,
  updateGoViewProject
} from '../api/goview-projects';

defineOptions({ name: 'GoViewEditorView' });

const DEFAULT_CANVAS_JSON = '{"width":1920,"height":1080,"backgroundColor":"#0a1628","components":[]}';

const route = useRoute();
const router = useRouter();
const session = useSessionStore();
const { t } = useAdminI18n();
const project = ref<GoViewProject>();
const loading = ref(false);
const saving = ref(false);
const publishing = ref(false);
const problem = ref<FullNetProblemDetails>();
const editorForm = reactive({
  name: '',
  canvasJson: DEFAULT_CANVAS_JSON,
  isEnabled: true,
  version: 0
});

const projectId = computed(() => String(route.params.projectId ?? ''));
const componentCount = computed(() => {
  try {
    const parsed = JSON.parse(editorForm.canvasJson) as { components?: unknown[] };
    return Array.isArray(parsed.components) ? parsed.components.length : 0;
  } catch {
    return 0;
  }
});

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

async function load(): Promise<void> {
  if (!projectId.value) {
    return;
  }

  loading.value = true;
  problem.value = undefined;
  try {
    project.value = await getGoViewProject(projectId.value);
    editorForm.name = project.value.name;
    editorForm.canvasJson = project.value.canvasJson;
    editorForm.isEnabled = project.value.isEnabled;
    editorForm.version = project.value.version;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'goviewEditor.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function saveDraft(): Promise<void> {
  if (!projectId.value) {
    return;
  }

  saving.value = true;
  problem.value = undefined;
  try {
    project.value = await updateGoViewProject(projectId.value, {
      name: editorForm.name,
      canvasJson: editorForm.canvasJson,
      isEnabled: editorForm.isEnabled,
      version: editorForm.version
    });
    editorForm.version = project.value.version;
    ElMessage.success(t('goviewEditor.saveSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'goviewEditor.saveFailed');
  } finally {
    saving.value = false;
  }
}

async function publish(): Promise<void> {
  if (!projectId.value) {
    return;
  }

  publishing.value = true;
  problem.value = undefined;
  try {
    if (editorForm.version !== project.value?.version) {
      project.value = await updateGoViewProject(projectId.value, {
        name: editorForm.name,
        canvasJson: editorForm.canvasJson,
        isEnabled: editorForm.isEnabled,
        version: editorForm.version
      });
      editorForm.version = project.value.version;
    }

    await publishGoViewProject(projectId.value, { version: editorForm.version });
    await load();
    ElMessage.success(t('goviewEditor.publishSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error, 'goviewEditor.publishFailed');
  } finally {
    publishing.value = false;
  }
}

function openPreview(): void {
  void router.push({ name: 'goview-preview', params: { projectId: projectId.value } });
}

onMounted(() => {
  void load();
});
</script>

<template>
  <div class="goview-editor-view">
    <ArtTableHeader :title="t('goviewEditor.title')">
      <PermissionGate code="goview.projects.preview">
        <ElButton
          data-testid="goview-editor-open-preview"
          :disabled="!project || project.latestPublishedVersionNumber <= 0"
          @click="openPreview"
        >
          {{ t('goviewEditor.openPreview') }}
        </ElButton>
      </PermissionGate>
      <PermissionGate code="goview.projects.update">
        <ElButton
          data-testid="goview-editor-save"
          type="primary"
          :loading="saving"
          @click="saveDraft"
        >
          {{ t('goviewEditor.saveDraft') }}
        </ElButton>
      </PermissionGate>
      <PermissionGate code="goview.projects.publish">
        <ElButton
          v-if="session.can('goview.projects.publish')"
          data-testid="goview-editor-publish"
          type="success"
          :loading="publishing"
          @click="publish"
        >
          {{ t('goviewEditor.publish') }}
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

    <ElCard v-loading="loading" shadow="never">
      <ElForm label-width="120px">
        <ElFormItem :label="t('goviewProjects.fieldName')">
          <ElInput v-model="editorForm.name" data-testid="goview-editor-name" />
        </ElFormItem>
        <ElFormItem :label="t('goviewProjects.fieldEnabled')">
          <ElSwitch v-model="editorForm.isEnabled" />
        </ElFormItem>
        <ElFormItem :label="t('goviewEditor.componentCount')">
          <span data-testid="goview-editor-component-count">{{ componentCount }}</span>
        </ElFormItem>
        <ElFormItem :label="t('goviewEditor.canvasJson')">
          <ElInput
            v-model="editorForm.canvasJson"
            type="textarea"
            :rows="18"
            data-testid="goview-editor-canvas-json"
          />
        </ElFormItem>
      </ElForm>
    </ElCard>
  </div>
</template>
