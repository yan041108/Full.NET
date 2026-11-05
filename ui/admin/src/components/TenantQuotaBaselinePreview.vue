<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElRadio, ElRadioGroup } from 'element-plus';
import { isFullNetProblemDetails, type ReconcileTenantQuotaUsageBaselineResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { tenantQuotaText } from '../i18n/tenant-quota';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { previewTenantQuotaUsage } from '../api/tenant-quota';
import { validQuotaPreview } from './tenant-quota-preview';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';

const permission = 'tenancy.tenant_quota.reconcile_usage_baseline';
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof tenantQuotaText>[1]) => tenantQuotaText(locale.value, key);
const allowed = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can(permission));
const open = ref(false); const metric = ref<'identity.seats' | 'files.storage_bytes'>('identity.seats');
const loading = ref(false); const failure = ref(''); const batch = ref(0);
const result = ref<ReconcileTenantQuotaUsageBaselineResponse>();
const scope = useAuthorizedViewScope(session, () => { result.value = undefined; batch.value = 0; loading.value = false; failure.value = ''; }, () => {});
let pending: ReturnType<typeof scope.begin>;
watch([open, metric], () => scope.invalidate(), { flush: 'sync' });
watch(allowed, value => { if (!value) open.value = false; }, { flush: 'sync' });
async function load() {
  if (!open.value || !allowed.value || loading.value || result.value?.isComplete) return;
  const operation = scope.begin(permission); if (!operation) return;
  const target = metric.value; const after = result.value?.nextAfterTenantId ?? null;
  pending = operation; loading.value = true; failure.value = '';
  try {
    const value = await previewTenantQuotaUsage(target, after, operation.signal);
    if (!operation.current() || !open.value || !allowed.value || target !== metric.value) return;
    if (!validQuotaPreview(value, after)) { failure.value = text('previewMismatch'); return; }
    result.value = value; batch.value++;
  } catch (error) {
    if (!operation.current() || !open.value) return;
    const status = isFullNetProblemDetails(error) ? error.status : 0;
    if ([401, 403].includes(status)) { result.value = undefined; batch.value = 0; }
    failure.value = text(status === 401 || status === 403 ? 'previewForbidden' : status === 400 ? 'previewUnsupported' : 'previewFailed');
  } finally { operation.finish(); if (pending === operation) { pending = undefined; loading.value = false; } }
}
</script>
<template>
  <ElButton v-if="allowed" data-testid="quota-preview-open" @click="open = true">{{ text('previewTitle') }}</ElButton>
  <ArtFormDialog trap-focus :open="open && allowed" :title="text('previewTitle')" width="min(760px, calc(100vw - 32px))"
    :show-confirm="false" :cancel-label="text('close')" @update:open="open = $event">
    <p>{{ text('previewHint') }}</p>
    <p id="quota-preview-metric-label">{{ text('previewMetric') }}</p>
    <ElRadioGroup v-model="metric" :aria-label="text('previewMetric')" data-testid="quota-preview-metric">
      <ElRadio value="identity.seats">{{ text('seats') }}</ElRadio><ElRadio value="files.storage_bytes">{{ text('storage') }}</ElRadio>
    </ElRadioGroup>
    <p>{{ text('previewSkipped') }}</p>
    <ElAlert v-if="failure" type="warning" role="status" :title="failure" :closable="false" data-testid="quota-preview-problem" />
    <ElButton data-testid="quota-preview-load" :loading="loading" :disabled="!allowed || result?.isComplete" @click="load">
      {{ text(result ? 'previewNext' : 'previewStart') }}
    </ElButton>
    <ElButton v-if="result" data-testid="quota-preview-reset" :disabled="loading" @click="scope.invalidate()">{{ text('previewReset') }}</ElButton>
    <section v-if="result" aria-live="polite" data-testid="quota-preview-result">
      <p>{{ text('previewBatch') }} {{ batch }} · {{ text('previewCandidates') }} {{ result.candidateCount }}</p>
      <p>{{ text('previewApplied') }} {{ result.appliedCount }}</p>
      <p>{{ text(result.isComplete ? 'previewComplete' : 'previewMore') }}</p>
      <p>{{ text('previewSamples') }}</p>
      <ul v-if="result.tenantIds.length"><li v-for="id in result.tenantIds" :key="id"><code>{{ id }}</code></li></ul>
      <p v-else>{{ text('previewEmpty') }}</p>
    </section>
  </ArtFormDialog>
</template>
<style scoped>
section { margin-top: 16px; } li { overflow-wrap: anywhere; }
</style>
