<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElAlert, ElButton, ElCard, ElCheckbox, ElInput, ElMessage, ElSwitch } from 'element-plus';
import type { AiAgentReconciliationResponse, ReconcileAiAgentRunRequest } from '@fullnet/client-contracts';
import { translateAiReconciliation } from '@fullnet/admin-i18n';
import { getAiAgentReconciliation, reconcileAiAgentRun } from '../api/ai-agent-runs';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from './PermissionGate.vue';

const props = defineProps<{ runId: string }>();
const emit = defineEmits<{ reconciled: [] }>();
const { locale } = useAdminI18n();
const text = (key: Parameters<typeof translateAiReconciliation>[1]) => translateAiReconciliation(locale.value, key);
const snapshot = ref<AiAgentReconciliationResponse>();
const usageText = computed(() => text(snapshot.value?.usageStatus === 'known' ? 'usageKnown'
  : snapshot.value?.usageStatus === 'reserved' ? 'usageReserved' : 'usageUnknown'));
const evidence = ref(''); const known = ref(false); const input = ref(''); const output = ref(''); const cached = ref('');
const reviewed = ref(false); const busy = ref(false); const failure = ref('');
let controller = new AbortController(); let epoch = 0; let request: ReconcileAiAgentRunRequest | undefined;
const number = (value: string) => /^\d{1,10}$/.test(value) && Number.isSafeInteger(Number(value)) && Number(value) <= 1_000_000_000 ? Number(value) : undefined;
const ready = computed(() => snapshot.value?.canReconcile && reviewed.value && /^[A-Fa-f0-9]{64}$/.test(evidence.value)
  && (!known.value || number(input.value) !== undefined && number(output.value) !== undefined
    && (!cached.value || number(cached.value) !== undefined && Number(cached.value) <= Number(input.value))));

watch(() => props.runId, () => {
  ++epoch; controller.abort(); controller = new AbortController(); snapshot.value = undefined; request = undefined;
  evidence.value = ''; input.value = ''; output.value = ''; cached.value = ''; known.value = false; reviewed.value = false; busy.value = false; failure.value = '';
}, { flush: 'sync' });
watch([evidence, known, input, output, cached], () => { request = undefined; reviewed.value = false; }, { flush: 'sync' });

async function load() {
  const ticket = ++epoch; controller.abort(); controller = new AbortController(); busy.value = true; failure.value = '';
  try {
    const result = await getAiAgentReconciliation(props.runId, controller.signal);
    if (ticket !== epoch) return;
    if (result.runId.toLowerCase() !== props.runId.toLowerCase()) throw new Error('client.invalid_ai_agent_reconciliation_response');
    snapshot.value = result; request = undefined; reviewed.value = false;
  } catch { if (ticket === epoch) failure.value = text('failed'); }
  finally { if (ticket === epoch) busy.value = false; }
}
async function submit() {
  if (!ready.value || busy.value || !snapshot.value) return;
  const ticket = epoch; busy.value = true; failure.value = '';
  request ??= { clientRequestId: crypto.randomUUID(), expectedVersion: snapshot.value.version, evidenceDigest: evidence.value.toUpperCase(),
    ...(known.value ? { inputTokens: number(input.value)!, outputTokens: number(output.value)!, ...(cached.value ? { cachedInputTokens: number(cached.value)! } : {}) } : {}) };
  try {
    await reconcileAiAgentRun(props.runId, request, controller.signal);
    if (ticket !== epoch) return;
    snapshot.value = undefined; reviewed.value = false; ElMessage.success(text('success')); emit('reconciled');
  } catch { if (ticket === epoch) failure.value = text('failed'); }
  finally { if (ticket === epoch) busy.value = false; }
}
onBeforeUnmount(() => { ++epoch; controller.abort(); });
</script>

<template>
  <ElCard shadow="never" class="art-page-card" data-testid="ai-reconciliation-panel">
    <template #header>{{ text('title') }}</template>
    <ElAlert :title="text('notice')" type="warning" :closable="false" show-icon />
    <ElAlert v-if="failure" :title="failure" type="error" :closable="false" />
    <ElButton :loading="busy" @click="load">{{ text('load') }}</ElButton>
    <template v-if="snapshot">
      <p>{{ text('usage') }}: {{ usageText }} · {{ text('reserved') }}: {{ snapshot.reservedTokens }} · {{ text('month') }}: {{ snapshot.quotaMonthKey }}</p>
      <ElAlert v-if="!snapshot.canReconcile" :title="text('unavailable')" type="info" :closable="false" />
      <PermissionGate code="ai.agent_runs.reconcile">
        <template v-if="snapshot.canReconcile">
          <ElInput v-model="evidence" :aria-label="text('evidence')" :placeholder="text('evidence')" :maxlength="64" :disabled="busy" />
          <ElSwitch v-model="known" :active-text="text('known')" :disabled="busy" />
          <template v-if="known">
            <ElInput v-model="input" :aria-label="text('input')" :placeholder="text('input')" :disabled="busy" />
            <ElInput v-model="output" :aria-label="text('output')" :placeholder="text('output')" :disabled="busy" />
            <ElInput v-model="cached" :aria-label="text('cached')" :placeholder="text('cached')" :disabled="busy" />
          </template>
          <ElCheckbox v-model="reviewed" :disabled="busy">{{ text('reviewed') }}</ElCheckbox>
          <ElButton type="warning" :disabled="!ready || busy" :loading="busy" @click="submit">{{ text('submit') }}</ElButton>
        </template>
      </PermissionGate>
    </template>
  </ElCard>
</template>
