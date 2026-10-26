<script setup lang="ts">
import { ref, onBeforeUnmount } from 'vue';
import { VFormFrameClient } from './frame-client';
withDefaults(defineProps<{ disabled?: boolean; loadingText?: string }>(), { disabled: false, loadingText: 'Loading VForm3…' });
const emit = defineEmits<{ ready: []; error: [code: string]; change: [values: Record<string, unknown>] }>();
const frame = ref<HTMLIFrameElement>(); const ready = ref(false);
const client = new VFormFrameClient(() => frame.value?.contentWindow ?? null, () => { ready.value = true; emit('ready'); }, window, values => emit('change', values), code => { ready.value = false; emit('error', code); });
let frameLoads = 0;
function onFrameLoad(): void {
  // 首次 load 与 Ready 的先后由浏览器决定；后续 load 必须立即使旧设计会话失效。
  if (++frameLoads > 1) client.fail('client.vform3_operation_failed');
}
// 父文档同时限制帧导航，防止设计脚本通过自导航绕过帧内出站策略。
if (!document.querySelector('[data-fullnet-frame-policy]')) {
  const policy = document.createElement('meta'); policy.httpEquiv = 'Content-Security-Policy';
  policy.content = "frame-src 'self'"; policy.dataset.fullnetFramePolicy = 'true'; document.head.append(policy);
}
const source = `/vform3/index.html#${new URLSearchParams({ parent: location.origin, channel: client.channel })}`;
onBeforeUnmount(() => client.dispose());
async function getFormJson(): Promise<unknown> { return client.request('getFormJson'); }
async function setFormJson(value: unknown): Promise<void> { await client.request('setFormJson', value); }
async function render(value: { json: unknown; data: unknown }): Promise<Record<string, unknown>> {
  const values = await client.request('render', value);
  if (!values || typeof values !== 'object' || Array.isArray(values)) throw new Error('client.vform3_operation_failed');
  return values as Record<string, unknown>;
}
async function getFormData(): Promise<unknown> { return client.request('getFormData'); }
defineExpose({ getFormJson, setFormJson, render, getFormData });
</script>
<template>
  <section class="fullnet-form-designer" data-testid="vform3-designer-host" :aria-busy="!ready">
    <div v-if="disabled" class="fullnet-form-designer__guard" aria-hidden="true" />
    <div v-if="!ready" role="status">{{ loadingText }}</div>
    <iframe ref="frame" :src="source" title="VForm 3" sandbox="allow-scripts allow-downloads" referrerpolicy="no-referrer"
      class="fullnet-form-designer__frame" :inert="disabled" @load="onFrameLoad" @error="client.fail('client.vform3_install_failed')" />
  </section>
</template>
<style scoped>
.fullnet-form-designer { position: relative; min-height: var(--fullnet-form-min-height, 720px); border: 1px solid var(--el-border-color); background: var(--el-bg-color); }
.fullnet-form-designer__frame { display: block; width: 100%; height: var(--fullnet-form-height, max(720px, 80vh)); border: 0; }
.fullnet-form-designer__guard { position: absolute; z-index: 20; inset: 0; background: rgb(255 255 255 / 24%); cursor: not-allowed; }
</style>
