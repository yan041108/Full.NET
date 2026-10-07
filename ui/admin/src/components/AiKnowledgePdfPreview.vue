<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue';
import { ElAlert, ElButton } from 'element-plus';
import type { PDFDocumentProxy, PDFPageProxy, RenderTask } from 'pdfjs-dist';
import { loadKnowledgePdf, type KnowledgePdfTask } from './knowledge-pdf';
import { useAdminI18n } from '../i18n/adminI18n';

const props = defineProps<{ data: Uint8Array; initialPage?: number }>();
const { t } = useAdminI18n();
const canvas = ref<HTMLCanvasElement>();
const currentPage = ref(1); const total = ref(0); const scale = ref(1);
const busy = ref(false); const failed = ref(false); const ready = ref(false);
let epoch = 0; let controller: AbortController | undefined;
let task: KnowledgePdfTask | undefined; let document: PDFDocumentProxy | undefined;
let page: PDFPageProxy | undefined; let renderTask: RenderTask | undefined;
let timer: ReturnType<typeof setTimeout> | undefined;
function stop() {
  ++epoch; clearTimeout(timer); controller?.abort(); renderTask?.cancel(); task?.destroy(); page?.cleanup();
  task = undefined; document = undefined; page = undefined; renderTask = undefined;
  if (canvas.value) { canvas.value.width = 0; canvas.value.height = 0; }
  busy.value = false; ready.value = false;
}
function fail(current: number) { if (current !== epoch) return; stop(); failed.value = true; }
function deadline(current: number) { clearTimeout(timer); timer = setTimeout(() => fail(current), 15_000); }
async function draw() {
  if (!document || busy.value) return;
  const current = epoch; busy.value = true; ready.value = false; deadline(current);
  try {
    const next = await document.getPage(currentPage.value);
    if (current !== epoch) { next.cleanup(); return; }
    page?.cleanup(); page = next;
    const original = next.getViewport({ scale: scale.value });
    if (!Number.isFinite(original.width) || !Number.isFinite(original.height) || original.width <= 0 || original.height <= 0) throw new Error('client.pdf_page_invalid');
    // 缩放保留页面比例；物理画布不会随异常页面尺寸或高 DPI 无限分配。
    const ratio = Math.min(1, 2400 / original.width, 2400 / original.height, Math.sqrt(4_000_000 / original.width / original.height));
    const viewport = next.getViewport({ scale: scale.value * ratio });
    await nextTick(); if (current !== epoch || !canvas.value) return;
    canvas.value.width = Math.max(1, Math.floor(viewport.width)); canvas.value.height = Math.max(1, Math.floor(viewport.height));
    renderTask = next.render({ canvas: canvas.value, viewport, annotationMode: 0 });
    await renderTask.promise;
    if (current === epoch) { clearTimeout(timer); renderTask = undefined; ready.value = true; }
  } catch { fail(current); }
  finally { if (current === epoch) busy.value = false; }
}
async function start() {
  stop(); failed.value = false; total.value = 0; scale.value = 1;
  const current = epoch; controller = new AbortController(); busy.value = true; deadline(current);
  try {
    const initial = props.initialPage ?? 1;
    if (!props.data.byteLength || props.data.byteLength > 8 * 1024 * 1024 || !Number.isSafeInteger(initial) || initial < 1) throw new Error('client.pdf_invalid');
    // 转移复制的数据到 Worker，避免破坏来源持有的缓冲区。
    const loaded = await loadKnowledgePdf(props.data.slice(), controller.signal);
    if (current !== epoch) { loaded.destroy(); return; } task = loaded;
    const result = await loaded.promise;
    if (current !== epoch) return;
    if (!Number.isSafeInteger(result.numPages) || result.numPages < initial || result.numPages > 500) throw new Error('client.pdf_pages_invalid');
    document = result; total.value = result.numPages; currentPage.value = initial;
    clearTimeout(timer); busy.value = false; await draw();
  } catch { fail(current); }
}
function move(delta: number) { if (!busy.value && currentPage.value + delta >= 1 && currentPage.value + delta <= total.value) { currentPage.value += delta; void draw(); } }
function zoom(delta: number) { if (!busy.value && scale.value + delta >= 0.5 && scale.value + delta <= 2) { scale.value += delta; void draw(); } }
function locate() { if (!busy.value) { currentPage.value = props.initialPage ?? 1; void draw(); } }
watch(() => [props.data, props.initialPage], start, { immediate: true });
onBeforeUnmount(stop);
</script>
<template>
  <section class="pdf-preview" :aria-label="t('knowledge.pdfPreview')" :aria-busy="busy">
    <el-alert v-if="failed" data-testid="knowledge-pdf-error" :title="t('knowledge.pdfUnavailable')" type="warning" :closable="false" />
    <template v-else>
      <div class="pdf-toolbar">
        <el-button data-testid="knowledge-pdf-previous" :disabled="busy || currentPage <= 1 || !total" @click="move(-1)">{{ t('knowledge.pdfPrevious') }}</el-button>
        <span data-testid="knowledge-pdf-page" aria-live="polite">{{ currentPage }} / {{ total }}</span>
        <el-button data-testid="knowledge-pdf-next" :disabled="busy || currentPage >= total" @click="move(1)">{{ t('knowledge.pdfNext') }}</el-button>
        <el-button v-if="initialPage" data-testid="knowledge-pdf-locate" :disabled="busy || !total" @click="locate">{{ t('knowledge.pdfLocate') }}</el-button>
        <el-button data-testid="knowledge-pdf-zoom-out" :disabled="busy || scale <= 0.5 || !total" @click="zoom(-0.25)">{{ t('knowledge.pdfZoomOut') }}</el-button>
        <span>{{ Math.round(scale * 100) }}%</span>
        <el-button data-testid="knowledge-pdf-zoom-in" :disabled="busy || scale >= 2 || !total" @click="zoom(0.25)">{{ t('knowledge.pdfZoomIn') }}</el-button>
      </div>
      <p v-if="busy" role="status">{{ t('knowledge.pdfLoading') }}</p>
      <div class="pdf-canvas"><canvas ref="canvas" v-show="ready" data-testid="knowledge-pdf-canvas" :aria-label="t('knowledge.pdfPage', { page: currentPage })">{{ t('knowledge.pdfCanvasHint') }}</canvas></div>
      <p>{{ t('knowledge.pdfCanvasHint') }}</p>
    </template>
  </section>
</template>
<style scoped>
.pdf-toolbar { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; margin-bottom: 12px; }
.pdf-toolbar .el-button + .el-button { margin-left: 0; }
.pdf-canvas { max-height: 60vh; overflow: auto; background: var(--el-fill-color-light); }
canvas { display: block; margin: auto; }
</style>
