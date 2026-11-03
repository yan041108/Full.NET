<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElButton, ElDrawer } from 'element-plus';
import type { WorkflowDefinitionVersionResponse } from '@fullnet/client-contracts';
import { getWorkflowDefinitionVersion } from '../api/workflow-definitions';
import { getWorkflowStartForm } from '../api/workflow-runtime';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { compareWorkflowVersions, formatWorkflowVersionValue, type WorkflowVersionComparison } from './workflow-version-comparison';

const props = defineProps<{ definitionId: string; versions: readonly WorkflowDefinitionVersionResponse[] }>();
const { t } = useAdminI18n(); const session = useSessionStore();
const open = ref(false), beforeId = ref(''), afterId = ref(''), loading = ref(false), failed = ref(false);
const result = ref<WorkflowVersionComparison>(); let epoch = 0; let controller: AbortController | undefined;
const canCompare = computed(() => !session.switching && session.can('workflow.definitions.read') && session.can('workflow.forms.read'));
const available = computed(() => props.versions.filter(version => version.definitionId === props.definitionId));
const identity = computed(() => JSON.stringify([session.currentUser?.id, session.currentUser?.sessionId,
  session.currentUser?.tenantId, session.currentUser?.scope, session.currentUser?.profileVersion, canCompare.value]));
function clear(): void { epoch++; controller?.abort(); controller = undefined; result.value = undefined; loading.value = false; failed.value = false; }
watch([beforeId, afterId], clear, { flush: 'sync' });
watch(() => JSON.stringify([identity.value, props.definitionId, available.value.map(version => version.id)]), () => {
  clear(); open.value = false;
  const ordered = [...available.value].sort((left, right) => left.versionNumber - right.versionNumber);
  beforeId.value = ordered.at(-2)?.id ?? ''; afterId.value = ordered.at(-1)?.id ?? '';
}, { immediate: true, flush: 'sync' });
watch(open, value => { if (!value) clear(); });
onBeforeUnmount(clear);

async function compare(): Promise<void> {
  if (!canCompare.value || loading.value || beforeId.value === afterId.value ||
      !available.value.some(version => version.id === beforeId.value) || !available.value.some(version => version.id === afterId.value)) return;
  clear(); const current = epoch; const definitionId = props.definitionId;
  const cancellation = new AbortController(); controller = cancellation; loading.value = true;
  try {
    const [before, after] = await Promise.all([getWorkflowDefinitionVersion(beforeId.value, cancellation.signal), getWorkflowDefinitionVersion(afterId.value, cancellation.signal)]);
    if (current !== epoch || cancellation.signal.aborted || !canCompare.value) return;
    if (before.definitionId !== definitionId || after.definitionId !== definitionId || before.id !== beforeId.value || after.id !== afterId.value) throw new TypeError('workflow.version_comparison.binding_mismatch');
    const formIds = [...new Set([before.formVersionId, after.formVersionId])];
    const forms = new Map(await Promise.all(formIds.map(async id => [id, await getWorkflowStartForm(id, cancellation.signal)] as const)));
    if (current !== epoch || cancellation.signal.aborted || !canCompare.value) return;
    result.value = compareWorkflowVersions(before, after, forms.get(before.formVersionId)!, forms.get(after.formVersionId)!);
  } catch {
    if (current === epoch && !cancellation.signal.aborted) { result.value = undefined; failed.value = true; }
  } finally { if (current === epoch) loading.value = false; }
}
</script>

<template>
  <div v-if="available.length >= 2" class="workflow-version-compare">
    <el-button v-if="canCompare" data-testid="workflow-version-compare-open" @click="open = true">{{ t('workflowVersionCompare.open') }}</el-button>
    <p v-else role="status">{{ t('workflowVersionCompare.permission') }}</p>
    <el-drawer v-model="open" :title="t('workflowVersionCompare.title')" size="min(1100px, 100vw)" destroy-on-close append-to-body>
      <div v-if="open && canCompare" class="workflow-version-compare__content">
        <p>{{ t('workflowVersionCompare.help') }}</p>
        <div class="workflow-version-compare__controls">
          <label>{{ t('workflowVersionCompare.before') }}
            <select v-model="beforeId" data-testid="workflow-version-compare-before">
              <option v-for="version in available" :key="version.id" :value="version.id">{{ version.versionNumber }}</option>
            </select>
          </label>
          <label>{{ t('workflowVersionCompare.after') }}
            <select v-model="afterId" data-testid="workflow-version-compare-after">
              <option v-for="version in available" :key="version.id" :value="version.id">{{ version.versionNumber }}</option>
            </select>
          </label>
          <el-button type="primary" data-testid="workflow-version-compare-run" :disabled="loading || !beforeId || !afterId || beforeId === afterId" @click="compare">{{ t('workflowVersionCompare.run') }}</el-button>
        </div>
        <p v-if="loading" role="status">{{ t('workflowVersionCompare.loading') }}</p>
        <p v-if="failed" role="alert">{{ t('workflowVersionCompare.failed') }}</p>
        <section v-if="result" data-testid="workflow-version-compare-result">
          <p>{{ t('workflowVersionCompare.count', { count: result.totalChanges }) }}</p>
          <p>{{ t('workflowVersionCompare.impact') }}</p>
          <p v-if="result.truncated" role="status">{{ t('workflowVersionCompare.truncated') }}</p>
          <p v-if="result.totalChanges === 0">{{ t('workflowVersionCompare.empty') }}</p>
          <div v-else class="workflow-version-compare__table">
            <table>
              <thead><tr><th>{{ t('workflowVersionCompare.area') }}</th><th>{{ t('workflowVersionCompare.path') }}</th><th>{{ t('workflowVersionCompare.before') }}</th><th>{{ t('workflowVersionCompare.after') }}</th></tr></thead>
              <tbody><tr v-for="change in result.changes" :key="change.path">
                <td>{{ t(`workflowVersionCompare.area.${change.area}`) }}<br>{{ t(`workflowVersionCompare.kind.${change.kind}`) }}</td>
                <td><code translate="no">{{ change.path }}</code></td>
                <td><pre translate="no">{{ formatWorkflowVersionValue(change.before) }}</pre></td>
                <td><pre translate="no">{{ formatWorkflowVersionValue(change.after) }}</pre></td>
              </tr></tbody>
            </table>
          </div>
        </section>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.workflow-version-compare { margin-top: 1rem; }
.workflow-version-compare__content { display: grid; gap: .75rem; }
.workflow-version-compare__controls { display: flex; flex-wrap: wrap; gap: 1rem; align-items: end; }
.workflow-version-compare__controls label { display: grid; gap: .35rem; flex: 1; min-width: 7rem; }
.workflow-version-compare__controls select { min-height: 2rem; color: var(--el-text-color-primary); background: var(--el-bg-color); border: 1px solid var(--el-border-color); }
.workflow-version-compare__table { overflow: auto; }
table { width: 100%; border-collapse: collapse; text-align: start; }
th, td { border: 1px solid var(--el-border-color); padding: .6rem; vertical-align: top; }
pre { margin: 0; max-height: 12rem; overflow: auto; white-space: pre-wrap; overflow-wrap: anywhere; min-width: 9rem; max-width: 24rem; }
code { overflow-wrap: anywhere; }
</style>
