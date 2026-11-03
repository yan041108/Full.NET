<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { ElButton, ElCard, ElTag } from 'element-plus';
import type { TenantRegistrationProgress } from '@fullnet/client-contracts';
import { listMyTenantRegistrations, continueMyTenantRegistration } from '../api/my-tenant-registrations';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { registrationProgressText } from '../i18n/registration-progress';
import { showProblem, showSuccess, showWarning } from '../feedback/fullNetMessage';
import { useBoundedPolling } from '../composables/useBoundedPolling';

const session = useSessionStore();
const { locale } = useAdminI18n();
const text = (key: Parameters<typeof registrationProgressText>[1]) => registrationProgressText(locale.value, key);
const items = ref<TenantRegistrationProgress[]>([]), loading = ref(false), pendingId = ref<string>();
const active = ref(true), failed = ref(false);
const canRead = computed(() => session.can('identity.tenant_registrations.read_self'));
const contextKey = computed(() => `${session.currentUser?.id ?? ''}|${session.currentUser?.tenantId ?? ''}`);
let generation = 0;
async function accept(rows: TenantRegistrationProgress[]) {
  const activated = rows.some(row => row.phase === 'Activated' && items.value.some(old => old.id === row.id && old.phase !== 'Activated'));
  items.value = rows; failed.value = false;
  if (activated) await session.reloadContext();
}
const polling = useBoundedPolling({
  enabled: () => active.value && canRead.value && !loading.value && !pendingId.value,
  pending: () => items.value.some(row => !['Activated', 'Cancelled'].includes(row.phase)),
  key: () => contextKey.value,
  read: listMyTenantRegistrations,
  accept: rows => { void accept(rows).catch(() => { failed.value = true; }); },
  failed: () => { failed.value = true; }, intervalMs: 5000
});
async function refresh() {
  if (!canRead.value || loading.value) return;
  const current = ++generation, key = contextKey.value;
  loading.value = true;
  try {
    const rows = await listMyTenantRegistrations();
    if (current === generation && key === contextKey.value && active.value) { await accept(rows); polling.restart(); }
  } catch { if (current === generation) failed.value = true; }
  finally { if (current === generation) loading.value = false; }
}
async function proceed(row: TenantRegistrationProgress) {
  if (pendingId.value || !session.can('identity.tenant_registrations.continue_self')) return;
  const key = contextKey.value; pendingId.value = row.id;
  try {
    const result = await continueMyTenantRegistration(row.id, row.version);
    if (key !== contextKey.value || !active.value) return;
    if (result.id !== row.id || result.tenantId !== row.tenantId) throw new Error('client.invalid_registration_progress_scope');
    items.value = items.value.map(old => old.id === row.id ? result : old);
    if (result.phase === 'Activated') { await session.reloadContext(); showSuccess(text('complete')); }
    else showWarning(text('waiting'));
    await refresh();
  } catch (error) { showProblem(error, text('failed')); }
  finally { pendingId.value = undefined; }
}
watch(contextKey, () => { generation++; loading.value = false; items.value = []; void refresh(); });
onMounted(() => { void refresh(); });
onActivated(() => { active.value = true; void refresh(); });
onDeactivated(() => { active.value = false; generation++; loading.value = false; polling.stop(); });
onBeforeUnmount(() => { active.value = false; generation++; });
</script>

<template>
  <ElCard v-if="canRead" class="registration-progress" shadow="never" data-testid="registration-progress" :aria-busy="loading">
    <template #header><h2>{{ text('title') }}</h2><p>{{ text('description') }}</p></template>
    <ElButton :loading="loading" :disabled="Boolean(pendingId)" @click="refresh">{{ text('refresh') }}</ElButton>
    <p v-if="failed" role="alert">{{ text('failed') }}</p>
    <p v-else-if="!loading && !items.length">{{ text('empty') }}</p>
    <p v-if="polling.paused.value">{{ text('stopped') }}</p>
    <ul v-if="items.length" class="registration-progress__items">
      <li v-for="row in items" :key="row.id">
        <strong>{{ row.registrationWayName || text('fallback') }}</strong>
        <ElTag :type="row.phase === 'Activated' ? 'success' : row.phase === 'Cancelled' ? 'warning' : 'info'">{{ text(row.phase) }}</ElTag>
        <p v-if="row.errorCode">{{ text('error') }}</p>
        <ElButton v-if="row.canContinue && session.can('identity.tenant_registrations.continue_self')" :loading="pendingId === row.id" :disabled="Boolean(pendingId) && pendingId !== row.id" @click="proceed(row)">{{ text('continue') }}</ElButton>
      </li>
    </ul>
  </ElCard>
</template>

<style scoped>
.registration-progress { flex: 0 0 auto; min-width: 0; }
.registration-progress h2 { margin: 0; font-size: 1rem; }
.registration-progress p, .registration-progress :deep(.el-tag) { color: var(--el-text-color-primary); }
.registration-progress__items { list-style: none; margin: 1rem 0 0; padding: 0; }
.registration-progress__items li { display: flex; align-items: center; flex-wrap: wrap; gap: 0.75rem; padding: 0.75rem 0; border-top: 1px solid var(--el-border-color-lighter); }
.registration-progress__items li p { flex-basis: 100%; margin: 0; }
</style>
