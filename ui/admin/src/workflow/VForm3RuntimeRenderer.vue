<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue';
import { VForm3DesignerHost, VFORM_RUNTIME_HASH, type VForm3DesignerHostInstance } from '@fullnet/admin-form-designer';
import { readVFormBindings, convertVFormValues } from './vform3-values';
import { useAdminI18n } from '../i18n/adminI18n';
import type { WorkflowSubmission, WorkflowFieldPolicy } from '@fullnet/client-contracts';
const props = defineProps<{ json: Record<string, unknown>; submission: WorkflowSubmission; fieldPolicies: Readonly<Record<string, WorkflowFieldPolicy>> }>();
const emit = defineEmits<{ 'update:patch': [value: WorkflowSubmission]; availability: [ready: boolean] }>();
const { t } = useAdminI18n();
const host = ref<VForm3DesignerHostInstance>(); const error = ref(false); const ready = ref(false);
const loading = ref(true); let pending: Promise<void> = Promise.resolve(); let generation = 0; let disposed = false;
onBeforeUnmount(() => { disposed = true; generation++; emit('availability', false); });
async function load(): Promise<void> {
  if (disposed || !ready.value) return;
  const current = ++generation;
  emit('availability', false); emit('update:patch', {}); error.value = false; loading.value = true;
  try {
    const json = JSON.parse(JSON.stringify(props.json)) as Record<string, unknown>;
    // 切换为只读详情时进一步收紧控件，不能放宽服务端已裁剪的投影。
    function tighten(value: unknown): void {
      if (!value || typeof value !== 'object') return;
      if (Array.isArray(value)) { value.forEach(tighten); return; }
      const object = value as Record<string, unknown>; const options = object.options as Record<string, unknown> | undefined;
      if (options && typeof options.name === 'string' && props.fieldPolicies[options.name] === 'readOnly') options.disabled = true;
      for (const key of ['widgetList', 'cols', 'rows', 'tabs']) tighten(object[key]);
    }
    tighten(json); const data = convertVFormValues(readVFormBindings(json, VFORM_RUNTIME_HASH), props.submission, false);
    // 权限/任务切换按快照顺序装载，旧渲染中的迟到事件不能产生新任务 Patch。
    pending = pending.catch(() => {}).then(async () => {
      // 排队期间投影可能失效或宿主已报错，不能再把旧授权数据送入帧。
      if (disposed || current !== generation || !ready.value) return;
      if (!host.value) throw new Error('client.vform3_not_ready');
      const values = await host.value.render({ json, data });
      // 回执只属于当前装载代次；初始化脚本变更也必须通过值协议与授权白名单。
      if (!disposed && current === generation && ready.value) emitPatch(values);
    });
    await pending;
  } catch { if (!disposed && current === generation) error.value = true; }
  finally { if (!disposed && current === generation) { loading.value = false; emit('availability', ready.value && !error.value); } }
}
watch(() => [props.json, props.submission, props.fieldPolicies], () => { void load(); }, { flush: 'sync' });
function onReady(): void {
  if (disposed) return;
  ready.value = true;
  void load();
}
function onError(): void {
  if (disposed) return;
  // 运输故障使全部载入代次失效，属性刷新不能替代新的宿主就绪通知。
  generation++; ready.value = false; error.value = true; loading.value = false;
  emit('availability', false); emit('update:patch', {});
}
function change(values: Record<string, unknown>): void {
  if (loading.value || error.value || disposed || !ready.value) return;
  try { emitPatch(values); }
  catch { error.value = true; emit('availability', false); emit('update:patch', {}); return; }
}
function emitPatch(values: Record<string, unknown>): void {
  if (!values || typeof values !== 'object' || Array.isArray(values)) throw new Error('client.vform3_value_codec_mismatch');
  const bindings = readVFormBindings(props.json, VFORM_RUNTIME_HASH);
  values = convertVFormValues(bindings, values, true);
  const original = convertVFormValues(bindings, convertVFormValues(bindings, props.submission, false), true);
  const patch: Record<string, unknown> = {};
  for (const [key, policy] of Object.entries(props.fieldPolicies))
    if ((policy === 'editable' || policy === 'required') && Object.hasOwn(values, key) && JSON.stringify(values[key]) !== JSON.stringify(original[key])) patch[key] = values[key];
  emit('update:patch', patch);
}
</script>
<template>
  <p v-if="error" role="alert">{{ t('workflowTodos.rendererUnavailable') }}</p>
  <VForm3DesignerHost ref="host" :disabled="loading || error" :style="{ visibility: loading || error ? 'hidden' : undefined }"
    @ready="onReady" @change="change" @error="onError" />
</template>
