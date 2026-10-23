<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue';
import type { AiKnowledgeSourceLocation } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
const props = defineProps<{ source: Blob; location: AiKnowledgeSourceLocation }>();
const { t } = useAdminI18n(); const canvas = ref<HTMLCanvasElement>(); const failed = ref(false); let epoch = 0;
async function render() {
  const current = ++epoch; failed.value = false;
  const box = props.location.image;
  if (!box || box.parent || box.frameNumber !== 1 || box.imageWidth * box.imageHeight > 40000000) { failed.value = true; return; }
  let bitmap: ImageBitmap | undefined;
  try {
    // 只接受已完整索引图片的首帧坐标；HTML/SVG 和不支持的容器使用安全下载。
    bitmap = await createImageBitmap(props.source, { imageOrientation: 'from-image', colorSpaceConversion: 'default' });
    if (current !== epoch) return;
    if (bitmap.width !== box.imageWidth || bitmap.height !== box.imageHeight) throw new Error('client.image_geometry_mismatch');
    await nextTick(); if (current !== epoch || !canvas.value) return;
    const scale = Math.min(1, 1600 / bitmap.width, 1600 / bitmap.height);
    canvas.value.width = Math.ceil(bitmap.width * scale); canvas.value.height = Math.ceil(bitmap.height * scale);
    const context = canvas.value.getContext('2d'); if (!context) throw new Error('client.image_canvas_unavailable');
    context.fillStyle = 'white'; context.fillRect(0, 0, canvas.value.width, canvas.value.height);
    context.drawImage(bitmap, 0, 0, canvas.value.width, canvas.value.height);
    context.strokeStyle = '#c84a18'; context.lineWidth = 2; context.strokeRect(box.x * scale, box.y * scale, box.width * scale, box.height * scale);
  } catch { if (current === epoch) failed.value = true; }
  finally { bitmap?.close(); }
}
watch(() => [props.source, props.location], render, { immediate: true });
onBeforeUnmount(() => { ++epoch; if (canvas.value) { canvas.value.width = 0; canvas.value.height = 0; } });
</script>
<template><p v-if="failed" data-testid="knowledge-image-unavailable">{{ t('knowledge.sourceBinary') }}</p><canvas v-else ref="canvas" role="img" :aria-label="t('knowledge.source')" data-testid="knowledge-image-preview" /></template>
<style scoped>canvas { display: block; max-width: 100%; height: auto; margin-top: 16px; border: 1px solid var(--el-border-color); }</style>
