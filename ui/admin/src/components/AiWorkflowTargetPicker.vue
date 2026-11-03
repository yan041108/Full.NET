<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue';
import { ElAlert, ElButton, ElOption, ElSelect } from 'element-plus';
import type { AiChatSessionListItem } from '@fullnet/client-contracts';
import { listAiChatSessions } from '../api/ai-chat';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { aiWorkflowText } from '../i18n/ai-workflow';
const model = defineModel<string>({ required: true });
const props = defineProps<{ disabled?: boolean }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof aiWorkflowText>[1]) => aiWorkflowText(locale.value, key);
const rows = ref<AiChatSessionListItem[]>([]), page = ref(1), total = ref(0), busy = ref(false), failed = ref(false);
let epoch = 0, controller = new AbortController();
function reset() { epoch++; controller.abort(); controller = new AbortController(); rows.value = []; model.value = ''; page.value = 1; total.value = 0; busy.value = false; failed.value = false; }
watch(() => [session.currentUser, session.currentContextName, session.switching], () => { reset(); if (!session.switching) void load(); }, { deep: true, flush: 'sync' });
onBeforeUnmount(() => { epoch++; controller.abort(); });
async function load() {
  if (session.switching || !session.can('ai.chat.sessions.read') || busy.value) return;
  const current = ++epoch; busy.value = true; failed.value = false;
  try {
    const result = await listAiChatSessions({ page: page.value, pageSize: 100 }, controller.signal);
    if (current === epoch && !controller.signal.aborted) { rows.value = result.items; total.value = result.total; }
  } catch { if (current === epoch && !controller.signal.aborted) failed.value = true; }
  finally { if (current === epoch) busy.value = false; }
}
function move(delta: number) { page.value += delta; model.value = ''; void load(); }
void load();
</script>
<template>
  <div class="ai-workflow-target">
    <ElAlert v-if="failed" :title="text('unavailable')" type="error" :closable="false" />
    <ElSelect v-model="model" :aria-label="text('target')" :placeholder="text('target')" filterable :loading="busy" :disabled="disabled || busy" data-testid="ai-workflow-target">
      <ElOption v-for="row in rows" :key="row.id" :value="row.id" :label="row.title" />
    </ElSelect>
    <div class="ai-workflow-target__pages">
      <ElButton :disabled="busy || page <= 1 || disabled" @click="move(-1)">{{ text('previous') }}</ElButton>
      <ElButton :disabled="busy || page * 100 >= total || disabled" @click="move(1)">{{ text('next') }}</ElButton>
      <ElButton :disabled="busy || disabled" @click="load">{{ text('refresh') }}</ElButton>
    </div>
    <p>{{ text('targetHint') }}</p>
  </div>
</template>
<style scoped>.ai-workflow-target__pages { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 8px; }.ai-workflow-target { min-width: 0; overflow-wrap: anywhere; }</style>
