<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useRoute } from 'vue-router';
import { ElAlert, ElButton, ElCard, ElTag } from 'element-plus';
import type { FullNetProblemDetails, GoViewProjectPreview } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { previewGoViewProject } from '../api/goview-projects';

defineOptions({ name: 'GoViewPreviewView' });

interface CanvasComponent {
  id?: string;
  type?: string;
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  props?: Record<string, unknown>;
}

interface CanvasDocument {
  width?: number;
  height?: number;
  backgroundColor?: string;
  components?: CanvasComponent[];
}

const route = useRoute();
const { t } = useAdminI18n();
const preview = ref<GoViewProjectPreview>();
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();

const projectId = computed(() => String(route.params.projectId ?? ''));
const canvas = computed<CanvasDocument | null>(() => {
  if (!preview.value) {
    return null;
  }

  try {
    return JSON.parse(preview.value.canvasJson) as CanvasDocument;
  } catch {
    return null;
  }
});

function toProblem(error: unknown, fallbackKey: Parameters<typeof t>[0]): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { code: 'client.request_failed', title: t(fallbackKey), status: 500, type: 'about:blank' };
}

function componentLabel(component: CanvasComponent): string {
  const text = component.props?.text;
  return typeof text === 'string' && text.length > 0 ? text : component.type ?? 'component';
}

async function load(): Promise<void> {
  if (!projectId.value) {
    return;
  }

  loading.value = true;
  problem.value = undefined;
  try {
    preview.value = await previewGoViewProject(projectId.value, {});
  } catch (error: unknown) {
    problem.value = toProblem(error, 'goviewPreview.loadFailed');
  } finally {
    loading.value = false;
  }
}

onMounted(() => {
  void load();
});
</script>

<template>
  <div class="goview-preview-view">
    <ArtTableHeader :title="t('goviewPreview.title')">
      <ElButton data-testid="goview-preview-reload" :loading="loading" @click="load">
        {{ t('goviewPreview.reload') }}
      </ElButton>
    </ArtTableHeader>

    <ElAlert
      v-if="problem"
      type="error"
      :title="problem.title"
      show-icon
      class="mb-4"
    />

    <ElCard v-if="preview" shadow="never" class="mb-4">
      <div class="flex gap-2 items-center flex-wrap">
        <ElTag type="success">v{{ preview.versionNumber }}</ElTag>
        <span>{{ preview.projectName }}</span>
        <span class="text-gray-500">{{ preview.projectKey }}</span>
      </div>
    </ElCard>

    <div
      v-if="canvas"
      class="goview-preview-stage"
      data-testid="goview-preview-stage"
      :style="{
        width: `${canvas.width ?? 1920}px`,
        height: `${canvas.height ?? 1080}px`,
        backgroundColor: canvas.backgroundColor ?? '#0a1628',
        position: 'relative',
        overflow: 'hidden',
        transform: 'scale(0.5)',
        transformOrigin: 'top left'
      }"
    >
      <div
        v-for="(component, index) in canvas.components ?? []"
        :key="component.id ?? index"
        class="goview-preview-component"
        :style="{
          position: 'absolute',
          left: `${component.x ?? 0}px`,
          top: `${component.y ?? 0}px`,
          width: `${component.width ?? 120}px`,
          height: `${component.height ?? 40}px`,
          color: String(component.props?.color ?? '#ffffff'),
          fontSize: `${String(component.props?.fontSize ?? 16)}px`,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          border: '1px dashed rgba(255,255,255,0.2)'
        }"
      >
        {{ componentLabel(component) }}
      </div>
    </div>
  </div>
</template>

<style scoped>
.goview-preview-view {
  overflow: auto;
}
</style>
