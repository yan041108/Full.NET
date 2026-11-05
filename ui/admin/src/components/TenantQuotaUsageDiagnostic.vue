<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem, ElRadio, ElRadioGroup } from 'element-plus';
import { isFullNetProblemDetails, isGuid, type TenantQuotaUsageDiagnosticResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { tenantQuotaText } from '../i18n/tenant-quota';
import { getTenantQuotaUsageDiagnostic } from '../api/tenant-quota';

const props = defineProps<{ tenantId: string }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof tenantQuotaText>[1]) => tenantQuotaText(locale.value, key);
const permission = 'tenancy.tenant_quota.reconcile_usage_baseline';
const allowed = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null && session.currentUser.actorScope === 'host'
  && session.can('tenancy.tenant_quota.read') && session.can(permission));
const metric = ref<'identity.seats' | 'files.storage_bytes'>('identity.seats'); const loading = ref(false); const failure = ref('');
const result = ref<TenantQuotaUsageDiagnosticResponse>();
const scope = useAuthorizedViewScope(session, () => { result.value = undefined; loading.value = false; failure.value = ''; }, () => {});
watch([() => props.tenantId, metric], () => scope.invalidate(), { flush: 'sync' });
const exact = (value: unknown, signed = false): value is string => {
  if (typeof value !== 'string' || value.length > 20 || !/^-?(0|[1-9]\d*)$/.test(value)) return false;
  const number = BigInt(value);
  return number.toString() === value && number <= 9223372036854775807n && number >= (signed ? -9223372036854775808n : 0n);
};
function valid(value: TenantQuotaUsageDiagnosticResponse, target: string, code: string) {
  if (value.tenantId.toLowerCase() !== target.toLowerCase() || value.metricCode !== code || !Number.isFinite(Date.parse(value.readAtUtc))
    || !['Aligned', 'Different', 'InProgress', 'Unconfigured', 'Unavailable'].includes(value.state)) return false;
  const configured = value.metricId != null;
  if (configured ? !isGuid(value.metricId) || !exact(value.bookUsedValue) || !exact(value.reservedValue)
    : value.bookUsedValue != null || value.reservedValue != null) return false;
  if (value.state === 'Unavailable') return value.authoritativeUsedValue == null && value.observedDifference == null;
  if (!exact(value.authoritativeUsedValue)) return false;
  if (value.state === 'InProgress') return value.observedDifference == null;
  if (value.state === 'Unconfigured') return !configured && value.observedDifference == null;
  if (!configured || !exact(value.bookUsedValue) || !exact(value.reservedValue) || BigInt(value.reservedValue) !== 0n || !exact(value.observedDifference, true)) return false;
  const difference = BigInt(value.authoritativeUsedValue) - BigInt(value.bookUsedValue);
  return difference === BigInt(value.observedDifference) && ((value.state === 'Aligned') === (difference === 0n));
}
const stateText = computed(() => result.value?.state === 'Aligned' ? text('diagnosticAligned') : result.value?.state === 'Different' ? text('diagnosticDifferent')
  : result.value?.state === 'InProgress' ? text('diagnosticProgress') : result.value?.state === 'Unconfigured' ? text('diagnosticUnconfigured') : text('diagnosticUnavailable'));
async function read() {
  if (!allowed.value || loading.value) return;
  const operation = scope.begin(permission); if (!operation) return;
  const target = props.tenantId; const code = metric.value; loading.value = true; failure.value = '';
  try {
    const value = await getTenantQuotaUsageDiagnostic(target, code, operation.signal);
    if (!operation.current()) return;
    if (!valid(value, target, code)) { result.value = undefined; failure.value = text('mismatch'); return; }
    result.value = value;
  } catch (error) {
    if (!operation.current()) return;
    const status = isFullNetProblemDetails(error) ? error.status : 0;
    if ([401, 403, 404].includes(status)) result.value = undefined;
    failure.value = text(status === 401 || status === 403 ? 'forbidden' : status === 404 ? 'absent' : 'failed');
  } finally { operation.finish(); if (operation.current()) loading.value = false; }
}
</script>
<template>
  <section v-if="allowed" class="quota-diagnostic">
    <h3>{{ text('diagnosticTitle') }}</h3><p>{{ text('diagnosticHint') }}</p>
    <ElRadioGroup v-model="metric" :aria-label="text('previewMetric')" data-testid="quota-diagnostic-metric">
      <ElRadio value="identity.seats">{{ text('seats') }}</ElRadio><ElRadio value="files.storage_bytes">{{ text('storage') }}</ElRadio>
    </ElRadioGroup>
    <p>{{ text(metric === 'identity.seats' ? 'diagnosticSeatsSource' : 'diagnosticStorageSource') }}</p>
    <ElButton data-testid="quota-diagnostic-read" :loading="loading" @click="read">{{ text('diagnosticRead') }}</ElButton>
    <ElAlert v-if="failure" :title="failure" type="warning" :closable="false" data-testid="quota-diagnostic-failure" />
    <div v-if="result" aria-live="polite" data-testid="quota-diagnostic-result">
      <p data-testid="quota-diagnostic-state">{{ stateText }}</p>
      <ElDescriptions :column="1" border>
        <ElDescriptionsItem :label="text('used')"><span data-testid="diagnostic-book">{{ result.bookUsedValue ?? text('diagnosticUnknown') }}</span></ElDescriptionsItem>
        <ElDescriptionsItem :label="text('reserved')">{{ result.reservedValue ?? text('diagnosticUnknown') }}</ElDescriptionsItem>
        <ElDescriptionsItem :label="text('diagnosticAuthoritative')"><span data-testid="diagnostic-authoritative">{{ result.authoritativeUsedValue ?? text('diagnosticUnknown') }}</span></ElDescriptionsItem>
        <ElDescriptionsItem :label="text('diagnosticDifference')"><span data-testid="diagnostic-difference">{{ result.observedDifference ?? text('diagnosticNoComparison') }}</span></ElDescriptionsItem>
        <ElDescriptionsItem :label="text('readAt')">{{ result.readAtUtc }}</ElDescriptionsItem>
      </ElDescriptions>
    </div>
  </section>
</template>
<style scoped>
.quota-diagnostic { margin-top: 24px; }
.quota-diagnostic :deep(.el-descriptions__content) { overflow-wrap: anywhere; font-variant-numeric: tabular-nums; }
</style>
