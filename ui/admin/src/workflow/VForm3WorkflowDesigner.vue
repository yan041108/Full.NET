<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue';
import { VForm3DesignerHost, type VForm3DesignerHostInstance } from '@fullnet/admin-form-designer';
const props = defineProps<{ designerJson: Record<string, unknown>; disabled: boolean }>();
const emit = defineEmits<{ 'validation-error': [code: string] }>();
const designer = ref<VForm3DesignerHostInstance>();
const ready = ref(false); const isLoading = ref(true); const failed = ref(false);
let loading: Promise<void> = Promise.resolve(); let generation = 0; let disposed = false;
watch(() => props.designerJson, () => { if (ready.value) void loadDesign(); }, { flush: 'sync' });
onBeforeUnmount(() => { disposed = true; generation++; ready.value = false; });
function requireDesigner(): VForm3DesignerHostInstance {
  if (disposed || !ready.value || !designer.value) throw new Error('client.vform3_not_ready');
  return designer.value;
}
async function loadDesign(): Promise<void> {
  const current = ++generation; isLoading.value = true; failed.value = false;
  let snapshot: Record<string, unknown> | undefined;
  try { snapshot = JSON.parse(JSON.stringify(props.designerJson)); }
  catch { /* 不可序列化配置也必须进入载入失败门禁，不能继续读取旧画布。 */ }
  // 在任何 await 之前登记完整载入任务；保存方必须等该资产及其后续排队资产完成。
  const task = loading.catch(() => {}).then(async () => {
    if (!snapshot) throw new Error('client.invalid_workflow_form_draft');
    await requireDesigner().setFormJson(snapshot);
  });
  loading = task;
  try { await task; }
  catch { if (!disposed && current === generation) { failed.value = true; emit('validation-error', 'client.vform3_operation_failed'); } }
  finally { if (!disposed && current === generation) isLoading.value = false; }
}
async function readDesign(): Promise<Record<string, unknown>> {
  // 先接收本轮父组件属性更新，避免同一轮选中切换后的保存越过载入监听。
  await nextTick();
  for (;;) {
    requireDesigner(); const current = generation; const task = loading;
    try {
      await task;
      if (current !== generation) continue;
      const raw = await requireDesigner().getFormJson();
      // 等待帧回执时资产可能已切换；旧结果不能以新修订保存，重新读取最新画布。
      if (current !== generation) continue;
      requireDesigner();
      if (typeof raw !== 'object' || raw === null || Array.isArray(raw) || !('widgetList' in raw) || !('formConfig' in raw)) throw new Error('client.invalid_workflow_form_draft');
      return raw as Record<string, unknown>;
    } catch (error) {
      if (!disposed && ready.value && current !== generation) continue;
      throw error;
    }
  }
}
function onReady(): void { ready.value = true; void loadDesign(); }
function onError(code: string): void {
  generation++; ready.value = false; failed.value = true; isLoading.value = false;
  emit('validation-error', code);
}
defineExpose({ readDesign, loadDesign });
</script>
<template>
  <VForm3DesignerHost ref="designer" :disabled="disabled || !ready || isLoading || failed" data-testid="vform3-workflow-designer" @ready="onReady" @error="onError" />
</template>
