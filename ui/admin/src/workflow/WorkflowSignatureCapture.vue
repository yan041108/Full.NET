<script setup lang="ts">
import { ref } from 'vue';
import { ElButton } from 'element-plus';
import { useAdminI18n } from '../i18n/adminI18n';

const props = defineProps<{ busy: boolean; captured: boolean }>();
const emit = defineEmits<{ capture: [file: File]; clear: [] }>();
const { t } = useAdminI18n();
const canvas = ref<HTMLCanvasElement>();
const drawn = ref(false);
let activePointer: number | undefined;

function point(event: PointerEvent): [number, number] {
  const element = canvas.value!;
  const box = element.getBoundingClientRect();
  return [(event.clientX - box.left) * element.width / box.width, (event.clientY - box.top) * element.height / box.height];
}
function begin(event: PointerEvent): void {
  if (props.busy || activePointer !== undefined || !canvas.value) return;
  const context = canvas.value.getContext('2d');
  if (!context) return;
  activePointer = event.pointerId;
  canvas.value.setPointerCapture(event.pointerId);
  context.beginPath(); context.moveTo(...point(event));
}
function move(event: PointerEvent): void {
  if (activePointer !== event.pointerId || !canvas.value) return;
  const context = canvas.value.getContext('2d');
  if (!context) return;
  context.lineWidth = 3; context.lineCap = 'round'; context.lineJoin = 'round'; context.strokeStyle = '#172033';
  context.lineTo(...point(event)); context.stroke(); drawn.value = true;
  emit('clear');
}
function end(event: PointerEvent): void {
  if (activePointer !== event.pointerId) return;
  activePointer = undefined;
  if (canvas.value?.hasPointerCapture(event.pointerId)) canvas.value.releasePointerCapture(event.pointerId);
}
function clear(): void {
  if (props.busy) return;
  const element = canvas.value;
  element?.getContext('2d')?.clearRect(0, 0, element.width, element.height);
  drawn.value = false; emit('clear');
}
function save(): void {
  if (props.busy || !drawn.value) return;
  canvas.value?.toBlob(blob => {
    if (blob) emit('capture', new File([blob], 'signature.png', { type: 'image/png' }));
  }, 'image/png');
}
function selectFile(event: Event): void {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (file && !props.busy) emit('capture', file);
  input.value = '';
}
</script>

<template>
  <fieldset class="workflow-signature" :disabled="busy" :aria-busy="busy">
    <legend>{{ t('workflowTodos.signature.title') }}</legend>
    <p>{{ t('workflowTodos.signature.help') }}</p>
    <canvas ref="canvas" width="768" height="320" :aria-label="t('workflowTodos.signature.canvas')"
      @pointerdown.prevent="begin" @pointermove.prevent="move" @pointerup="end" @pointercancel="end" />
    <div class="workflow-signature__actions">
      <el-button :disabled="busy || !drawn" @click="save">{{ t('workflowTodos.signature.save') }}</el-button>
      <el-button :disabled="busy" @click="clear">{{ t('workflowTodos.signature.clear') }}</el-button>
      <label>{{ t('workflowTodos.signature.choose') }}
        <input type="file" accept="image/png,image/jpeg" :disabled="busy" @change="selectFile">
      </label>
    </div>
    <p role="status">{{ t(captured ? 'workflowTodos.signature.ready' : 'workflowTodos.signature.pending') }}</p>
  </fieldset>
</template>

<style scoped>
.workflow-signature { margin: 0; border: 1px solid var(--el-border-color); border-radius: 8px; padding: 16px; }
.workflow-signature p { color: var(--el-text-color-secondary); margin: 8px 0; }
.workflow-signature canvas { display: block; width: 100%; max-width: 480px; height: 180px; background: #fff; border: 1px solid var(--el-border-color); touch-action: none; }
.workflow-signature__actions { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; margin-top: 12px; }
.workflow-signature__actions label { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
</style>
