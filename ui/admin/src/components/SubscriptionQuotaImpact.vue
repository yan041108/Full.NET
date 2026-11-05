<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton } from 'element-plus';
import { isFullNetProblemDetails, isGuid, type TenantPackageOfferResponse } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { subscriptionBillingText } from '../i18n/subscription-billing';
import { getTenantQuotaUsage } from '../api/tenant-quota';
import { quotaOfferImpactRows, type QuotaOfferImpactRow } from './tenant-quota-usage';
import { isNonnegativeInt64String } from '../utils/exact-integer';

const props = defineProps<{ tenantId: string; offer: TenantPackageOfferResponse; disabled?: boolean }>();
const session = useSessionStore(); const { locale } = useAdminI18n();
const text = (key: Parameters<typeof subscriptionBillingText>[1]) => subscriptionBillingText(locale.value, key);
const permission = 'tenancy.tenant_quota.read';
const allowed = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can(permission));
const validOffer = computed(() => isGuid(props.tenantId) && isGuid(props.offer.packageId)
  && Number.isSafeInteger(props.offer.version) && props.offer.version > 0
  && [props.offer.seatLimit, props.offer.storageLimit].every(isNonnegativeInt64String));
const loading = ref(false); const failure = ref('');
const result = ref<{ rows: QuotaOfferImpactRow[]; readAtUtc: string; tenantIsActive: boolean }>();
const scope = useAuthorizedViewScope(session, () => { result.value = undefined; loading.value = false; failure.value = ''; }, () => {});
watch(() => [props.tenantId, props.offer.packageId, props.offer.version, props.offer.seatLimit, props.offer.storageLimit, props.disabled],
  () => scope.invalidate(), { flush: 'sync' });
async function read() {
  if (!allowed.value || !validOffer.value || props.disabled || loading.value) return;
  const operation = scope.begin(permission); if (!operation) return;
  const tenantId = props.tenantId; const { seatLimit, storageLimit } = props.offer;
  loading.value = true; failure.value = '';
  try {
    const value = await getTenantQuotaUsage(tenantId, operation.signal);
    if (!operation.current() || !allowed.value || props.disabled) return;
    const rows = quotaOfferImpactRows(value.metrics, seatLimit, storageLimit);
    if (value.tenantId.toLowerCase() !== tenantId.toLowerCase() || !Number.isFinite(Date.parse(value.readAtUtc)) || !rows) {
      result.value = undefined; failure.value = text('impactMismatch'); return;
    }
    result.value = { rows, readAtUtc: value.readAtUtc, tenantIsActive: value.tenantIsActive };
  } catch (error) {
    if (!operation.current()) return;
    const status = isFullNetProblemDetails(error) ? error.status : 0;
    if ([401, 403, 404].includes(status)) result.value = undefined;
    failure.value = text([401, 403].includes(status) ? 'impactForbidden' : status === 404 ? 'impactAbsent' : 'impactFailed');
  } finally { operation.finish(); if (operation.current()) loading.value = false; }
}
</script>
<template>
  <section v-if="allowed" class="subscription-quota-impact" data-testid="subscription-quota-impact" :aria-label="text('impactTitle')">
    <h3>{{ text('impactTitle') }}</h3>
    <p>{{ text('impactHint') }}</p>
    <ElButton data-testid="subscription-quota-impact-read" :loading="loading" :disabled="!validOffer || disabled" @click="read">{{ text('impactRead') }}</ElButton>
    <ElAlert v-if="!validOffer || failure" type="warning" :closable="false" :title="!validOffer ? text('impactMismatch') : failure" data-testid="subscription-quota-impact-problem" />
    <template v-if="result">
      <p>{{ text('impactReadAt') }} <time data-testid="subscription-quota-impact-read-at" :datetime="result.readAtUtc">{{ result.readAtUtc }}</time></p>
      <p v-if="!result.tenantIsActive" role="status">{{ text('impactInactive') }}</p>
      <article v-for="row in result.rows" :key="row.metricCode" :data-testid="`subscription-quota-impact-${row.metricCode}`">
        <h4>{{ text(row.metricCode === 'identity.seats' ? 'seats' : 'storage') }}</h4>
        <dl>
          <div><dt>{{ text('impactLimit') }}</dt><dd data-testid="impact-limit">{{ row.proposedLimit }}</dd></div>
          <template v-if="row.configured">
            <div><dt>{{ text('impactUsed') }}</dt><dd data-testid="impact-used">{{ row.used }}</dd></div>
            <div><dt>{{ text('impactReserved') }}</dt><dd data-testid="impact-reserved">{{ row.reserved }}</dd></div>
            <div><dt>{{ text('impactRemaining') }}</dt><dd data-testid="impact-remaining">{{ row.proposedRemaining }}</dd></div>
          </template>
        </dl>
        <p v-if="!row.configured" role="status" data-testid="impact-unknown">{{ text('impactUnknown') }}</p>
        <p v-else-if="row.overLimit" role="status" class="impact-warning" data-testid="impact-over-limit">{{ text('impactOver') }}</p>
      </article>
    </template>
  </section>
</template>
<style scoped>
.subscription-quota-impact { margin-top: 16px; overflow-wrap: anywhere; }
h3, h4 { margin: 12px 0 8px; }
article { margin-top: 12px; border-top: 1px solid var(--el-border-color); }
dl { margin: 0; }
dl > div { display: flex; justify-content: space-between; gap: 16px; margin: 6px 0; }
dd { margin: 0; text-align: right; }
.impact-warning { font-weight: 600; }
.el-alert { margin-top: 12px; }
</style>
