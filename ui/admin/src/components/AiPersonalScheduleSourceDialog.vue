<script setup lang="ts">
import { computed, defineAsyncComponent, onMounted, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDialog } from 'element-plus';
import { RouterLink } from 'vue-router';
import type { AiPersonalScheduleSourcePreview } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { personalScheduleAssistantText } from '../i18n/personal-schedule-assistant';
import { previewPersonalScheduleSources } from '../api/ai-personal-schedule-assistant';

const AiPersonalScheduleAskPanel = defineAsyncComponent(() => import('./AiPersonalScheduleAskPanel.vue'));
const props = withDefaults(defineProps<{ scheduleId: string; expectedVersion: number; expectedHash?: string; allowAsk?: boolean }>(), { allowAsk: true });
const emit = defineEmits<{ close: []; invalidated: [] }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const tr = (key: Parameters<typeof personalScheduleAssistantText>[1]) => personalScheduleAssistantText(locale.value, key);
const allowed = computed(() => !session.switching && ['calendar.personal_schedules.read', 'calendar.personal_schedules.assistant_read'].every(code => session.can(code)));
const source = ref<AiPersonalScheduleSourcePreview>(); const busy = ref(false); const failed = ref(false);
let epoch = 0; let abort: AbortController | undefined;
function clear() { ++epoch; abort?.abort(); abort = undefined; source.value = undefined; busy.value = false; }
function close() { clear(); emit('close'); }
async function load() {
  if (!allowed.value || busy.value) return;
  clear(); const current = ++epoch; abort = new AbortController(); busy.value = true; failed.value = false;
  try {
    const value = await previewPersonalScheduleSources([props.scheduleId], abort.signal);
    if (current !== epoch || !allowed.value) return;
    if (value.sources[0]?.version !== props.expectedVersion || props.expectedHash && value.sources[0]?.projectionHash !== props.expectedHash)
      throw new Error('client.personal_schedule_version_changed');
    source.value = value;
  } catch { if (current === epoch) { source.value = undefined; failed.value = true; emit('invalidated'); } }
  finally { if (current === epoch) busy.value = false; }
}
watch(() => [props.scheduleId, props.expectedVersion, props.expectedHash, session.currentUser, session.currentContextName, session.switching, allowed.value], close,
  { deep: true, flush: 'sync' });
onMounted(load); onUnmounted(clear);
</script>

<template>
  <section class="personal-schedule-source-layer">
  <ElDialog :model-value="true" :title="tr('title')" width="min(680px, calc(100vw - 32px))" data-testid="personal-schedule-source-dialog" @close="close">
    <div class="personal-schedule-source">
      <p>{{ tr('hint') }}</p><p>{{ tr('restricted') }}</p>
      <ElAlert v-if="failed" type="error" :closable="false" :title="tr('unavailable')" />
      <RouterLink v-if="failed && session.can('ai.chat.sessions.read')" to="/ai/chat">{{ tr('history') }}</RouterLink>
      <article v-for="row in source?.sources ?? []" :key="row.id" data-testid="personal-schedule-source">
        <p class="personal-schedule-source__content">{{ row.content }}</p>
        <dl><dt>{{ tr('version') }}</dt><dd>{{ row.version }}</dd><dt>{{ tr('time') }}</dt><dd>{{ row.startAtUtc }} – {{ row.endAtUtc }}</dd>
          <dt>{{ tr('hash') }}</dt><dd><code>{{ row.projectionHash }}</code></dd></dl>
        <p>{{ tr(row.status === 'completed' ? 'completed' : 'pending') }}</p>
      </article>
      <AiPersonalScheduleAskPanel v-if="source && allowAsk !== false" :sources="source.sources" @unavailable="clear(); failed = true; emit('invalidated')" />
    </div>
    <template #footer><ElButton v-if="allowed" :loading="busy" :disabled="busy" @click="load">{{ tr('reload') }}</ElButton><ElButton @click="close">{{ tr('close') }}</ElButton></template>
  </ElDialog>
  </section>
</template>

<style scoped>
.personal-schedule-source { min-width: 0; overflow-wrap: anywhere; }
.personal-schedule-source__content { white-space: pre-wrap; }
dl { display: grid; grid-template-columns: minmax(5rem, auto) minmax(0, 1fr); gap: .5rem 1rem; }
dd { margin: 0; min-width: 0; }
@media (max-width: 480px) { dl { grid-template-columns: minmax(0, 1fr); } }
</style>
