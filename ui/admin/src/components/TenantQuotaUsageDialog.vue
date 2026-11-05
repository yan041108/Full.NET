<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDescriptions, ElDescriptionsItem } from 'element-plus';
import { isFullNetProblemDetails, isGuid } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { tenantQuotaText } from '../i18n/tenant-quota';
import { getTenantQuotaUsage, type TenantQuotaUsageSummaryResponse } from '../api/tenant-quota';
import { quotaUsageRows, type QuotaUsageRow } from './tenant-quota-usage';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';

const props = defineProps<{ open: boolean; tenantId: string; tenantName?: string }>();
const emit = defineEmits<{ 'update:open': [value: boolean] }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof tenantQuotaText>[1]) => tenantQuotaText(locale.value, key);
const allowed = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null && session.currentUser.actorScope === 'host'
  && session.can('tenancy.tenant_quota.read'));
const loading = ref(false); const failure = ref('');
const data = ref<{ summary: TenantQuotaUsageSummaryResponse; rows: QuotaUsageRow[] }>();
const numbers = computed(() => new Intl.NumberFormat(locale.value));
const dates = computed(() => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'medium' }));
const format = (value?: string) => value === undefined ? '—' : numbers.value.format(BigInt(value));
const scope = useAuthorizedViewScope(session, () => { data.value = undefined; failure.value = ''; loading.value = false; }, () => { if (props.open) void load(); });
let pending: ReturnType<typeof scope.begin>;
watch([() => props.open, () => props.tenantId], () => { scope.invalidate(); if (props.open) void load(); }, { flush: 'sync' });
async function load() {
  if (!props.open || !allowed.value || loading.value || !isGuid(props.tenantId)) return;
  pending?.cancel(); const operation = scope.begin('tenancy.tenant_quota.read'); if (!operation) return;
  const target = props.tenantId; pending = operation; loading.value = true; failure.value = '';
  try {
    const summary = await getTenantQuotaUsage(target, operation.signal);
    if (!operation.current() || !props.open || !allowed.value || target !== props.tenantId) return;
    const rows = quotaUsageRows(summary.metrics);
    if (summary.tenantId.toLowerCase() !== target.toLowerCase() || !Number.isFinite(Date.parse(summary.readAtUtc)) || !rows) { failure.value = text('mismatch'); return; }
    data.value = { summary, rows };
  } catch (error) {
    if (!operation.current() || !props.open) return;
    const status = isFullNetProblemDetails(error) ? error.status : 0;
    if ([401, 403, 404].includes(status)) data.value = undefined;
    failure.value = text(status === 401 || status === 403 ? 'forbidden' : status === 404 ? 'absent' : 'failed');
  } finally { operation.finish(); if (pending === operation) { pending = undefined; loading.value = false; } }
}
</script>
<template>
  <ArtFormDialog trap-focus :open="open && allowed" :title="text('title')" width="min(840px, calc(100vw - 32px))" :show-confirm="false"
    :cancel-label="text('close')" @update:open="emit('update:open', $event)">
    <p data-testid="quota-usage-target">{{ tenantName || tenantId }}</p>
    <p>{{ text('hint') }}</p>
    <ElAlert v-if="failure" role="status" type="warning" :title="failure" :closable="false" data-testid="quota-usage-problem" />
    <ElButton data-testid="quota-usage-refresh" :loading="loading" :disabled="!allowed" @click="load">{{ text('refresh') }}</ElButton>
    <template v-if="data">
      <p>{{ text(data.summary.tenantIsActive ? 'active' : 'inactive') }} · {{ text('readAt') }} <time data-testid="quota-read-at" :datetime="data.summary.readAtUtc">{{ dates.format(new Date(data.summary.readAtUtc)) }}</time></p>
      <div class="quota-grid">
        <article v-for="row in data.rows" :key="row.metricCode" :data-testid="'quota-metric-' + row.metricCode">
          <h3>{{ text(row.metricCode === 'identity.seats' ? 'seats' : 'storage') }}</h3>
          <p>{{ text(row.metricCode === 'identity.seats' ? 'seatUnit' : 'storageUnit') }}</p>
          <p v-if="!row.configured" role="status">{{ text('missing') }}</p>
          <template v-else>
            <ElAlert v-if="row.overLimit" :title="text('exceeded')" type="warning" :closable="false" />
            <ElDescriptions :column="1" border>
              <ElDescriptionsItem :label="text('limit')"><span class="quota-amount" data-testid="quota-limit">{{ format(row.limit) }}</span></ElDescriptionsItem>
              <ElDescriptionsItem :label="text('used')"><span class="quota-amount" data-testid="quota-used">{{ format(row.used) }}</span></ElDescriptionsItem>
              <ElDescriptionsItem :label="text('reserved')"><span class="quota-amount" data-testid="quota-reserved">{{ format(row.reserved) }}</span></ElDescriptionsItem>
              <ElDescriptionsItem :label="text('remaining')"><span class="quota-amount" data-testid="quota-remaining">{{ format(row.remaining) }}</span></ElDescriptionsItem>
            </ElDescriptions>
          </template>
        </article>
      </div>
    </template>
  </ArtFormDialog>
</template>
<style scoped>
.quota-grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 20px; margin-top: 16px; }
.quota-grid h3 { margin: 0; }
.quota-amount { overflow-wrap: anywhere; font-variant-numeric: tabular-nums; }
@media (max-width: 600px) { .quota-grid { grid-template-columns: minmax(0, 1fr); } }
</style>
