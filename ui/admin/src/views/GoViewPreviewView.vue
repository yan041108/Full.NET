<script setup lang="ts">
import { computed, ref } from 'vue';
import { ElAlert, ElButton, ElCard, ElTag } from 'element-plus';
import type { FullNetProblemDetails, GoViewProjectPreview } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { useGoViewScope } from '../composables/useGoViewScope';
import { previewGoViewProject } from '../api/goview-projects';
import { validGoViewId, validGoViewPreview, validGoViewVersionNumber } from '../api/goview-guards';
import { displayGoViewCanvas } from '../goview/displayCanvas';
defineOptions({ name: 'GoViewPreviewView' });
const { t } = useAdminI18n(), preview = ref<GoViewProjectPreview>(), loading = ref(false), problem = ref<FullNetProblemDetails>(), owner = ref(0);
const context = useGoViewScope('goview-preview', reset, async () => { await load(); }, 'goview.projects.preview'), { route, canRead } = context;
type Lease = NonNullable<ReturnType<typeof context.begin>>; let request: Lease | undefined, versionFloor = 0;
const known = new Map<number, { key: string; canvas: string }>();
function reset(): void { request = undefined; preview.value = undefined; loading.value = false; problem.value = undefined; versionFloor = 0; known.clear(); owner.value++; }
const canvas = computed(() => preview.value ? displayGoViewCanvas(preview.value.canvasJson) : undefined);
async function load(): Promise<void> {
  if (!canRead.value) return; request?.cancel(); const lease = context.begin('goview.projects.preview'); if (!lease) return; request = lease;
  preview.value = undefined; loading.value = true; problem.value = undefined; owner.value++;
  const id = route?.params.projectId, raw = route?.query.versionNumber;
  try {
    if (!validGoViewId(id) || raw !== undefined && (typeof raw !== 'string' || !/^[1-9]\d*$/u.test(raw) || !validGoViewVersionNumber(Number(raw)))) throw Error('client.invalid_goview_project_preview');
    const version = typeof raw === 'string' ? Number(raw) : undefined, value = await previewGoViewProject(id, version === undefined ? {} : { versionNumber: version }, lease.signal);
    if (!lease.current() || request !== lease) return;
    const previous = known.get(value.versionNumber);
    if (!validGoViewPreview(value, id, version) || version === undefined && value.versionNumber < versionFloor || previous && (value.projectKey !== previous.key || value.canvasJson !== previous.canvas) || !displayGoViewCanvas(value.canvasJson)) throw Error('client.invalid_goview_project_preview');
    versionFloor = Math.max(versionFloor, value.versionNumber); known.clear(); known.set(value.versionNumber, { key: value.projectKey, canvas: value.canvasJson }); preview.value = value;
  } catch (error) { if (lease.current() && request === lease) problem.value = isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.request_failed', title: t('goviewPreview.loadFailed') }; }
  finally { if (request === lease) { request = undefined; loading.value = false; owner.value++; } lease.finish(); }
}
const controls = computed(() => { const ticket = owner.value; return { reload: () => { if (canRead.value && ticket === owner.value) void load(); } }; });
</script>

<template>
  <div v-if="canRead" class="goview-preview-view">
    <ArtTableHeader :title="t('goviewPreview.title')">
      <ElButton data-testid="goview-preview-reload" :loading="loading" v-on="{ click: controls.reload }">
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
        v-for="component in canvas.components"
        :key="component.key"
        class="goview-preview-component"
        :style="{
          position: 'absolute',
          left: `${component.x ?? 0}px`,
          top: `${component.y ?? 0}px`,
          width: `${component.width ?? 120}px`,
          height: `${component.height ?? 40}px`,
          color: component.color,
          fontSize: `${component.fontSize}px`,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          border: '1px dashed rgba(255,255,255,0.2)'
        }"
      >
        {{ component.text }}
      </div>
    </div>
  </div>
</template>

<style scoped>
.goview-preview-view {
  overflow: auto;
}
</style>
