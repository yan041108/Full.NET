<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { ElButton, ElCard, ElMessageBox } from 'element-plus';
import type { AiPromptBody, AiPromptResponse, AiPromptVersionResponse } from '@fullnet/client-contracts';
import * as api from '../api/ai-prompts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { promptText } from '../i18n/prompts';
const props = defineProps<{ modelId?: string }>();
const emit = defineEmits<{ select: [version: AiPromptVersionResponse | undefined] }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof promptText>[1]) => promptText(locale.value, key);
const readable = computed(() => !session.switching && session.can('ai.agent_runs.read') && session.can('ai.prompts.read'));
const can = (action: string) => readable.value && session.can(`ai.prompts.${action}`);
const heads = ref<AiPromptResponse[]>([]); const versions = ref<AiPromptVersionResponse[]>([]);
const selectedId = ref(''); const head = computed(() => heads.value.find(h => h.id === selectedId.value));
const busy = ref(false); const refreshRequired = ref(false); const problem = ref('');
const name = ref(''); const template = ref('{{input}}'); const languageTag = ref('zh-CN'); const modelId = ref(props.modelId ?? '');
let epoch = 0; let controller: AbortController | undefined; let clientRequestId = '';
let confirmationOpen = false;
const body = computed<AiPromptBody>(() => ({ modelConfigId: modelId.value, languageTag: languageTag.value, template: template.value, inputTokenLimit: 4096, outputTokenLimit: 4096 }));
function reset() {
  if (confirmationOpen) { ElMessageBox.close(); confirmationOpen = false; }
  ++epoch; controller?.abort(); controller = undefined; busy.value = false; heads.value = []; versions.value = [];
  selectedId.value = ''; name.value = ''; template.value = '{{input}}'; modelId.value = ''; languageTag.value = 'zh-CN';
  clientRequestId = ''; problem.value = ''; refreshRequired.value = false; emit('select', undefined);
}
// 主体、租户或精确权限改变立即丢弃正文和绑定，晚到响应不能附着到新上下文。
watch(() => [session.currentUser, session.currentContextName, session.switching, readable.value], reset, { deep: true, flush: 'sync' });
watch(() => props.modelId, value => { if (!modelId.value) modelId.value = value ?? ''; });
watch([name, template, modelId, languageTag], () => { if (!busy.value) clientRequestId = ''; }, { flush: 'sync' });
onUnmounted(reset);
async function perform(action: (signal: AbortSignal) => Promise<void>, write = false) {
  if (!readable.value || busy.value) return;
  const run = ++epoch; controller?.abort(); const abort = new AbortController(); controller = abort;
  busy.value = true; problem.value = '';
  try { await action(abort.signal); }
  catch { if (run === epoch && !abort.signal.aborted) { problem.value = text(write ? 'retry' : 'unavailable'); if (write) refreshRequired.value = true; } }
  finally { if (run === epoch) { busy.value = false; controller = undefined; } }
}
async function reload(signal: AbortSignal) {
  const run = epoch;
  const catalogs = await api.listPrompts({ page: 1, pageSize: 100 }, signal);
  if (run !== epoch || signal.aborted || !readable.value) return;
  heads.value = catalogs.items; versions.value = []; emit('select', undefined);
  if (head.value) {
    const result = await api.getPromptVersions({ promptId: head.value.id, page: 1, pageSize: 100 }, signal);
    if (run !== epoch || signal.aborted || !readable.value) return;
    versions.value = result.items;
  }
  refreshRequired.value = false;
}
function refresh() { return perform(reload); }
function selectCatalog() { versions.value = []; emit('select', undefined); return refresh(); }
function create() {
  if (!can('create')) return;
  clientRequestId ||= crypto.randomUUID();
  const request = { clientRequestId, name: name.value, body: body.value };
  return perform(async signal => {
    const run = epoch; const result = await api.createPrompt({ body: request }, signal);
    if (run !== epoch || signal.aborted || !can('create')) return;
    selectedId.value = result.id; clientRequestId = ''; await reload(signal);
  }, true);
}
function append() {
  if (!can('append') || !head.value || refreshRequired.value) return;
  const request = { promptId: head.value.id, body: { revision: head.value.revision, body: body.value } };
  return perform(async signal => { await api.appendPrompt(request, signal); if (!signal.aborted) await reload(signal); }, true);
}
function change(action: 'publish' | 'rollback' | 'deactivate', version?: AiPromptVersionResponse) {
  const current = head.value;
  if (!can(action) || !current || refreshRequired.value || (action !== 'deactivate' && !version)) return;
  return perform(async signal => {
    const run = epoch;
    confirmationOpen = true;
    try { await ElMessageBox.confirm(`${text('confirm')} ${text(action)} · ${current.name} · r${current.revision}${version ? ` · v${version.version} · ${version.bodyHash} · ${version.body.template}` : ''}`, text('title'), { type: 'warning', dangerouslyUseHTMLString: false }); }
    catch { return; }
    finally { if (run === epoch) confirmationOpen = false; }
    if (run !== epoch || signal.aborted || !can(action)) return;
    if (action === 'deactivate') await api.deactivatePrompt({ promptId: current.id, body: { revision: current.revision } }, signal);
    else await (action === 'publish' ? api.publishPrompt : api.rollbackPrompt)({ promptId: current.id, body: { revision: current.revision, versionId: version!.id } }, signal);
    if (run === epoch && !signal.aborted) await reload(signal);
  }, true);
}
function use(version: AiPromptVersionResponse) {
  if (can('use') && session.can('ai.agent_runs.create') && !refreshRequired.value && head.value?.activeVersionId === version.id) emit('select', version);
}
</script>

<template>
  <ElCard v-if="readable" shadow="never" data-testid="prompt-panel" :aria-busy="busy">
    <template #header>{{ text('title') }}</template>
    <p>{{ text('hint') }}</p>
    <p v-if="problem" role="alert">{{ problem }}</p>
    <div class="prompt-fields">
      <ElButton :disabled="busy" data-testid="prompt-refresh" @click="refresh">{{ text('refresh') }}</ElButton>
      <label>{{ text('choose') }}
        <select v-model="selectedId" :disabled="busy" data-testid="prompt-catalog" @change="selectCatalog">
          <option value="">—</option><option v-for="item in heads" :key="item.id" :value="item.id">{{ item.name }} · r{{ item.revision }}</option>
        </select>
      </label>
      <ElButton v-if="can('deactivate') && head?.activeVersionId" :disabled="busy || refreshRequired" data-testid="prompt-deactivate" @click="change('deactivate')">{{ text('deactivate') }}</ElButton>
    </div>
    <div v-if="can('create') || can('append')" class="prompt-fields">
      <label>{{ text('name') }}<input v-model="name" maxlength="128" :disabled="busy" data-testid="prompt-name"></label>
      <label>{{ text('model') }}<input v-model="modelId" :disabled="busy" data-testid="prompt-model"></label>
      <label>{{ text('language') }}<select v-model="languageTag" :disabled="busy"><option>zh-CN</option><option>en-US</option></select></label>
      <label class="prompt-template">{{ text('template') }}<textarea v-model="template" maxlength="8192" rows="3" :disabled="busy" data-testid="prompt-template" /></label>
      <ElButton v-if="can('create')" :disabled="busy || !name || !modelId" data-testid="prompt-create" @click="create">{{ text('create') }}</ElButton>
      <ElButton v-if="can('append') && head" :disabled="busy || refreshRequired || !modelId" data-testid="prompt-append" @click="append">{{ text('append') }}</ElButton>
    </div>
    <article v-for="version in versions" :key="version.id" class="prompt-version" data-testid="prompt-version">
      <strong>v{{ version.version }} · {{ head?.activeVersionId === version.id ? text('active') : version.wasPublished ? text('published') : text('draft') }}</strong>
      <p>{{ version.providerKey }} / {{ version.modelId }} · {{ version.body.languageTag }} · {{ version.bodyHash }}</p>
      <pre>{{ version.body.template }}</pre>
      <ElButton v-if="can('publish') && !version.wasPublished" :disabled="busy || refreshRequired" data-testid="prompt-publish" @click="change('publish', version)">{{ text('publish') }}</ElButton>
      <ElButton v-if="can('rollback') && version.wasPublished && head?.activeVersionId !== version.id" :disabled="busy || refreshRequired" data-testid="prompt-rollback" @click="change('rollback', version)">{{ text('rollback') }}</ElButton>
      <ElButton v-if="can('use') && session.can('ai.agent_runs.create') && head?.activeVersionId === version.id" :disabled="busy || refreshRequired" data-testid="prompt-use" @click="use(version)">{{ text('use') }}</ElButton>
    </article>
  </ElCard>
</template>

<style scoped>
.prompt-fields { display: flex; flex-wrap: wrap; align-items: end; gap: 12px; margin-block: 12px; }
label { display: grid; gap: 6px; min-width: 180px; }
input, select, textarea { color: var(--el-text-color-primary); background: var(--el-fill-color-blank); border: 1px solid var(--el-border-color); border-radius: 4px; padding: 8px; font: inherit; }
.prompt-template { flex-basis: 100%; }
.prompt-version { padding-block: 14px; border-top: 1px solid var(--el-border-color); overflow-wrap: anywhere; }
pre { white-space: pre-wrap; font: inherit; }
</style>
