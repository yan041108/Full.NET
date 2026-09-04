<script setup lang="ts">
import {
  getCurrentInstance,
  nextTick,
  onMounted,
  ref,
  shallowRef,
  type Component
} from 'vue';
import { loadVForm3Designer } from './vform3-loader';

interface VFormDesignerInstance {
  getFormJson?: () => unknown;
  setFormJson?: (value: unknown) => void;
}

withDefaults(defineProps<{
  disabled?: boolean;
  loadingText?: string;
}>(), {
  disabled: false,
  loadingText: 'Loading VForm3…'
});
const emit = defineEmits<{
  ready: [];
  error: [code: string];
}>();
const designer = ref<VFormDesignerInstance>();
const designerComponent = shallowRef<Component>();
const app = getCurrentInstance()?.appContext.app;

onMounted(async () => {
  try {
    if (app === undefined) throw new Error('client.vform3_install_failed');
    designerComponent.value = await loadVForm3Designer(app);
    await nextTick();
    emit('ready');
  } catch (error: unknown) {
    emit('error', toErrorCode(error));
  }
});

function getFormJson(): unknown {
  const value = designer.value?.getFormJson?.();
  if (value === undefined) throw new Error('client.vform3_not_ready');
  return value;
}

function setFormJson(value: unknown): void {
  if (designer.value?.setFormJson === undefined) {
    throw new Error('client.vform3_not_ready');
  }
  designer.value.setFormJson(value);
}

function toErrorCode(error: unknown): string {
  return error instanceof Error ? error.message : 'client.vform3_install_failed';
}

defineExpose({ getFormJson, setFormJson });
</script>

<template>
  <section class="fullnet-form-designer" data-testid="vform3-designer-host">
    <div v-if="disabled" class="fullnet-form-designer__guard" aria-hidden="true" />
    <component
      :is="designerComponent"
      v-if="designerComponent"
      ref="designer"
      class="fullnet-form-designer__canvas"
      :inert="disabled"
      :aria-disabled="disabled"
    />
    <div v-else class="fullnet-form-designer__loading" role="status">{{ loadingText }}</div>
  </section>
</template>

<style scoped>
.fullnet-form-designer {
  position: relative;
  min-height: 620px;
  overflow: hidden;
  border: 1px solid var(--el-border-color);
  background: var(--el-bg-color);
}

.fullnet-form-designer__canvas {
  display: block;
  height: min(72vh, 760px);
}

.fullnet-form-designer__guard {
  position: absolute;
  z-index: 20;
  inset: 0;
  background: rgb(255 255 255 / 24%);
  cursor: not-allowed;
}

.fullnet-form-designer__loading {
  display: grid;
  min-height: 620px;
  place-items: center;
  color: var(--el-text-color-secondary);
}
</style>
