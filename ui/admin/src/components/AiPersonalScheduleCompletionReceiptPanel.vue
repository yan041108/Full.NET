<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElAlert, ElButton, ElCard } from 'element-plus';
import type { AiPersonalScheduleCompletionStatus } from '@fullnet/client-contracts';
import { getScheduleCompletionStatus, reconcileScheduleCompletion } from '../api/ai-personal-schedule-completion';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { personalScheduleCompletionText } from '../i18n/personal-schedule-completion';
const props = defineProps<{ runId: string }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof personalScheduleCompletionText>[1]) => personalScheduleCompletionText(locale.value, key);
const allowed = computed(() => !session.switching && ['ai.agent_runs.read', 'calendar.personal_schedules.read', 'calendar.personal_schedules.assistant_complete'].every(p => session.can(p)));
const value = ref<AiPersonalScheduleCompletionStatus>(), busy = ref(false), failed = ref(false);
let epoch = 0, controller = new AbortController();
function clear() { ++epoch; controller.abort(); controller = new AbortController(); value.value = undefined; busy.value = false; failed.value = false; }
watch(() => [props.runId, session.currentUser, session.currentContextName, session.switching, allowed.value], clear, { deep: true, flush: 'sync' });
onBeforeUnmount(clear);
async function read(reconcile = false) {
  if (!allowed.value || busy.value) return; const current = epoch; busy.value = true; failed.value = false;
  try { const result = await (reconcile ? reconcileScheduleCompletion : getScheduleCompletionStatus)(props.runId, controller.signal);
    if (current === epoch) value.value = result; }
  catch { if (current === epoch) { value.value = undefined; failed.value = true; } }
  finally { if (current === epoch) busy.value = false; }
}
const label = computed(() => { const key = value.value?.state; return key === 'accepted' || key === 'unknown' || key === 'completed' || key === 'rejected' ? text(key) : ''; });
const reasonLabel = computed(() => { const key = value.value?.reason; return key === 'authorization_unavailable' || key === 'intent_expired' || key === 'target_changed' ? text(key) : ''; });
</script>
<template>
  <ElCard v-if="allowed" shadow="never" class="completion-receipt" data-testid="schedule-completion-receipt-panel">
    <template #header>{{ text('title') }}</template>
    <p>{{ text('receiptHint') }}</p>
    <ElAlert v-if="failed" :title="text('unavailable')" type="error" :closable="false" />
    <ElButton :disabled="busy" @click="read()">{{ text('refresh') }}</ElButton>
    <ElButton v-if="value?.state === 'unknown'" :disabled="busy" @click="read(true)">{{ text('reconcile') }}</ElButton>
    <div v-if="value" role="status"><p>{{ label }}</p><p>{{ text('operation') }}: <code>{{ value.operationId }}</code></p>
      <p>{{ text('target') }}: <code>{{ value.scheduleId }}</code> · {{ text('version') }}: {{ value.expectedVersion }}</p>
      <p v-if="value.state === 'completed'">{{ text('actualVersion') }}: {{ value.actualVersion }} · {{ value.completedAtUtc }}</p>
      <p v-if="reasonLabel">{{ text('reason') }}: {{ reasonLabel }}</p></div>
  </ElCard>
</template>
<style scoped>.completion-receipt { min-width: 0; overflow-wrap: anywhere; }</style>
