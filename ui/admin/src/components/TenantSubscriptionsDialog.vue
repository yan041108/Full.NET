<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElAlert, ElButton, ElOption, ElPagination, ElSelect, ElTable, ElTableColumn } from 'element-plus';
import type { CreateTenantSubscriptionBody, HostTenant, HostTenantPackage, TenantSubscription } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { subscriptionBillingText } from '../i18n/subscription-billing';
import { cancelTenantSubscription, createTenantSubscription, listTenantSubscriptions } from '../api/tenant-subscriptions';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import PaidSubscriptionPurchase from './PaidSubscriptionPurchase.vue';
import SubscriptionCheckoutAction from './SubscriptionCheckoutAction.vue';

const props = defineProps<{ open: boolean; tenant: HostTenant | null; packages: HostTenantPackage[] }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; changed: [] }>();
const session = useSessionStore(); const { locale, t } = useAdminI18n();
const text = (key: Parameters<typeof subscriptionBillingText>[1]) => subscriptionBillingText(locale.value, key);
const canRead = computed(() => session.state === 'authenticated' && session.currentUser?.tenantId === null
  && session.currentUser.actorScope === 'host' && session.can('tenancy.tenant_subscriptions.read'));
const canManage = computed(() => canRead.value && session.can('tenancy.tenant_subscriptions.manage'));
const rows = ref<TenantSubscription[]>([]); const page = ref(1); const total = ref(0); const loading = ref(false);
const hasRead = ref(false); const uncertain = ref(false); const failure = ref(''); const packageId = ref('');
const activePackages = computed(() => props.packages.filter(item => item.isActive));
const hasActive = computed(() => rows.value.some(row => ['Trial', 'Active', 'PastDue'].includes(row.status)));
const receipt = ref<{ action: 'trial' | 'cancel'; target: string; state: 'confirmed' | 'unknown' | 'rejected' }>();
type Confirmation = { tenantId: string; tenantName: string } & ({ action: 'trial'; packageName: string; body: Readonly<CreateTenantSubscriptionBody> }
  | { action: 'cancel'; id: string; version: number });
const confirmation = ref<Confirmation>();
const paidOpen = ref(false); const checkoutOpen = ref(false);
const background = computed(() => !!confirmation.value || paidOpen.value || checkoutOpen.value);
const sameId = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();
const formatDate = (value: string) => Number.isFinite(Date.parse(value))
  ? new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : '—';
const statusLabel = (status: string) => ['Trial', 'Active', 'PastDue', 'Cancelled', 'Expired'].includes(status)
  ? text(('status' + status) as 'statusTrial' | 'statusActive' | 'statusPastDue' | 'statusCancelled' | 'statusExpired') : status;
const scope = useAuthorizedViewScope(session, () => {
  rows.value = []; total.value = 0; page.value = 1; loading.value = false; hasRead.value = false;
  uncertain.value = false; failure.value = ''; packageId.value = ''; confirmation.value = undefined; receipt.value = undefined;
  paidOpen.value = false; checkoutOpen.value = false;
}, () => { if (props.open) void load(); });
let pending: ReturnType<typeof scope.begin>;
watch(() => [props.open, props.tenant?.id], () => {
  scope.invalidate(); if (props.open) void load();
}, { flush: 'sync' });
async function load() {
  if (!props.open || !props.tenant || !canRead.value) return;
  pending?.cancel(); confirmation.value = undefined;
  const operation = scope.begin('tenancy.tenant_subscriptions.read'); if (!operation) return;
  const target = props.tenant.id; const requestedPage = page.value;
  pending = operation; loading.value = true; hasRead.value = false; failure.value = '';
  try {
    const value = await listTenantSubscriptions(target, requestedPage, 20, operation.signal);
    if (!operation.current() || !props.open || !props.tenant || !sameId(props.tenant.id, target)) return;
    if (value.page !== requestedPage || value.pageSize !== 20 || value.items.some(row => !sameId(row.tenantId, target))) {
      failure.value = text('responseMismatch'); rows.value = []; total.value = 0; return;
    }
    rows.value = value.items; total.value = value.total; hasRead.value = true; uncertain.value = false;
  } catch { if (operation.current()) failure.value = text('historyFailed'); }
  finally { operation.finish(); if (pending === operation) { pending = undefined; loading.value = false; } }
}
function beginTrial() {
  if (!canManage.value || !props.tenant?.isActive || !hasRead.value || loading.value || uncertain.value || hasActive.value) return;
  const pkg = activePackages.value.find(item => sameId(item.id, packageId.value)); if (!pkg) return;
  const start = new Date(); const end = new Date(start.getTime() + 30 * 86400000);
  confirmation.value = { action: 'trial', tenantId: props.tenant.id, tenantName: props.tenant.name, packageName: pkg.name,
    body: Object.freeze({ packageId: pkg.id, status: 'Trial', trialEndsAtUtc: end.toISOString(), currentPeriodStartUtc: start.toISOString(), currentPeriodEndUtc: end.toISOString() }) };
}
function beginCancel(row: TenantSubscription) {
  if (!canManage.value || !props.tenant || !hasRead.value || loading.value || uncertain.value
    || !sameId(row.tenantId, props.tenant.id) || !['Trial', 'Active', 'PastDue'].includes(row.status)
    || !rows.value.some(current => sameId(current.id, row.id) && current.version === row.version)) return;
  confirmation.value = { action: 'cancel', tenantId: props.tenant.id, tenantName: props.tenant.name, id: row.id, version: row.version };
}
async function confirm() {
  const command = confirmation.value;
  if (!command || !props.open || !props.tenant || !sameId(command.tenantId, props.tenant.id)
    || !canManage.value || !hasRead.value || loading.value || uncertain.value) return;
  if (command.action === 'trial' && (!props.tenant.isActive || !activePackages.value.some(item => sameId(item.id, command.body.packageId!)))) return;
  const operation = scope.begin('tenancy.tenant_subscriptions.manage'); if (!operation) return;
  pending = operation; loading.value = true; failure.value = ''; uncertain.value = true; confirmation.value = undefined;
  const target = command.action === 'cancel' ? command.id : command.body.packageId!;
  receipt.value = { action: command.action, target, state: 'unknown' };
  try {
    const value = await (command.action === 'trial' ? createTenantSubscription(command.tenantId, command.body, operation.signal)
      : cancelTenantSubscription(command.tenantId, command.id, command.version, operation.signal));
    if (!operation.current() || !props.open || !props.tenant || !sameId(props.tenant.id, command.tenantId)) return;
    if (!sameId(value.tenantId, command.tenantId) || (command.action === 'cancel'
      ? !sameId(value.id, command.id) || value.status !== 'Cancelled'
      : value.status !== 'Trial' || !value.packageId || !sameId(value.packageId, command.body.packageId!))) {
      failure.value = text('responseMismatch'); return;
    }
    receipt.value = { action: command.action, target: value.id, state: 'confirmed' }; uncertain.value = false; emit('changed');
  } catch (error) {
    if (!operation.current()) return;
    const rejected = isFullNetProblemDetails(error) && error.status >= 400 && error.status < 500 && ![408, 429].includes(error.status);
    receipt.value = { action: command.action, target, state: rejected ? 'rejected' : 'unknown' };
    failure.value = text(rejected ? 'operationRejected' : 'operationUnknown');
  } finally { operation.finish(); if (pending === operation) { pending = undefined; loading.value = false; } }
  if (receipt.value?.state === 'confirmed' && props.open && canRead.value) await load();
}
function changePage(value: number) { if (loading.value || !Number.isSafeInteger(value) || value < 1) return; page.value = value; void load(); }
function paidCreated() { emit('changed'); void load(); }
</script>

<template>
  <ArtFormDialog trap-focus :inert="background ? true : undefined" :aria-hidden="background ? true : undefined"
    :open="open && canRead" :title="t('tenants.subscriptions')" width="min(760px, calc(100vw - 32px))"
    :show-confirm="false" :cancel-label="text('close')" @update:open="emit('update:open', $event)">
    <p data-testid="subscription-target">{{ tenant?.name }}</p>
    <ElAlert v-if="failure" :title="failure" type="warning" :closable="false" />
    <p v-if="receipt" role="status" data-testid="subscription-operation-receipt">{{ text(receipt.state === 'confirmed' ? 'operationConfirmed' : receipt.state === 'rejected' ? 'operationRejected' : 'operationUnknown') }} <code>{{ receipt.target }}</code></p>
    <ElButton data-testid="tenant-subscriptions-refresh" :loading="loading" @click="load">{{ text('historyRefresh') }}</ElButton>
    <ElTable v-loading="loading" class="subscription-history" :data="rows" row-key="id">
      <ElTableColumn :label="t('tenancy.tenantSubscriptions.status')" min-width="140"><template #default="{ row }">{{ statusLabel(row.status) }}</template></ElTableColumn>
      <ElTableColumn :label="t('tenancy.tenantSubscriptions.periodEnd')" min-width="200"><template #default="{ row }">{{ formatDate(row.currentPeriodEndUtc) }}</template></ElTableColumn>
      <ElTableColumn v-if="canManage" :label="t('users.columnActions')" width="240"><template #default="{ row }">
        <PaidSubscriptionPurchase v-if="row.status === 'Expired' && row.packageId && tenant?.isActive && session.can('tenancy.tenant_packages.read')"
          :key="'repurchase-' + row.id" :tenant-id="tenant.id" :packages="packages" :source="{ subscriptionId: row.id, packageId: row.packageId }"
          :disabled="loading || !hasRead || uncertain || hasActive" @created="paidCreated" @dialog-open="paidOpen = $event" />
        <span v-if="row.status === 'Expired' && row.packageId && tenant?.isActive && session.can('tenancy.tenant_packages.read') && !activePackages.some(pkg => sameId(pkg.id, row.packageId))"
          data-testid="subscription-repurchase-unavailable">{{ text('repurchaseUnavailable') }}</span>
        <SubscriptionCheckoutAction v-if="hasRead && !uncertain && tenant?.isActive && row.status === 'PastDue'" :key="row.id" :tenant-id="tenant.id" :subscription-id="row.id" @dialog-open="checkoutOpen = $event" />
        <ElButton v-if="['Trial', 'Active', 'PastDue'].includes(row.status)" link type="danger" data-testid="tenant-subscription-cancel"
          :disabled="loading || !hasRead || uncertain" @click="beginCancel(row as TenantSubscription)">{{ t('tenants.subscriptionCancel') }}</ElButton>
      </template></ElTableColumn>
    </ElTable>
    <ElPagination :current-page="page" :page-size="20" :total="total" layout="prev, pager, next, total" :disabled="loading" @current-change="changePage" />
    <div v-if="canManage && tenant?.isActive" class="subscription-operations">
      <PaidSubscriptionPurchase :key="tenant.id" :tenant-id="tenant.id" :packages="packages" :disabled="loading || !hasRead || uncertain" @created="paidCreated" @dialog-open="paidOpen = $event" />
      <label for="trial-subscription-package">{{ text('trialPackage') }}</label>
      <ElSelect id="trial-subscription-package" v-model="packageId" data-testid="trial-subscription-package" :disabled="loading || !hasRead || uncertain || hasActive" :aria-label="text('trialPackage')">
        <ElOption v-for="pkg in activePackages" :key="pkg.id" :label="pkg.name" :value="pkg.id" />
      </ElSelect>
      <ElButton type="primary" data-testid="tenant-subscription-trial" :disabled="loading || !hasRead || uncertain || hasActive || !packageId" @click="beginTrial">{{ t('tenants.subscriptionStartTrial') }}</ElButton>
      <p>{{ text(hasActive ? 'activeSubscriptionHint' : 'trialDuration') }}</p>
    </div>
  </ArtFormDialog>
  <ArtFormDialog trap-focus :open="!!confirmation && open && canManage" :title="text(confirmation?.action === 'trial' ? 'trialConfirm' : 'cancelConfirm')"
    width="min(520px, calc(100vw - 32px))" :confirm-disabled="loading || !canManage" confirm-test-id="tenant-subscription-confirm"
    :confirm-label="text('confirmOperation')" :cancel-label="text('close')" @update:open="value => { if (!value) confirmation = undefined; }" @confirm="confirm">
    <template v-if="confirmation">
      <p>{{ confirmation.tenantName }}</p>
      <template v-if="confirmation.action === 'trial'"><p>{{ confirmation.packageName }}</p><p>{{ formatDate(confirmation.body.currentPeriodStartUtc) }} – {{ formatDate(confirmation.body.currentPeriodEndUtc) }}</p></template>
      <template v-else><code>{{ confirmation.id }}</code><p>{{ text('cancelPolicy') }}</p></template>
    </template>
  </ArtFormDialog>
</template>

<style scoped>
.subscription-operations { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; margin-top: 16px; }
.subscription-operations :deep(.el-select) { width: min(240px, 100%); }
/* 表头与空状态都是需要读取的信息，避免组件默认灰字在白色背景下对比不足。 */
.subscription-history :deep(th.el-table__cell), .subscription-history :deep(.el-table__empty-text) { color: var(--el-text-color-primary); }
</style>
