<script setup lang="ts">
import { computed, onScopeDispose, ref, watch } from 'vue';
import { ElButton, ElDialog, ElInput } from 'element-plus';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { getTenantLifecycle, changeTenantLifecycle, type TenantLifecycleAction } from '../api/tenant-lifecycle';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { tenantLifecycleText } from '../i18n/tenant-lifecycle';
import { showSuccess } from '../feedback/fullNetMessage';

const props = defineProps<{ open: boolean; tenantId?: string }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; changed: [] }>();
const session = useSessionStore(), { locale } = useAdminI18n();
const text = (key: Parameters<typeof tenantLifecycleText>[1]) => tenantLifecycleText(locale.value, key);
const tenant = ref<Awaited<ReturnType<typeof getTenantLifecycle>>>(), loading = ref(false), saving = ref(false), failed = ref(false), conflict = ref(false);
const action = ref<TenantLifecycleAction>(), confirmation = ref('');
const problemText = ref('');
const canRead = computed(() => session.can('tenancy.tenant_lifecycle.read'));
const allowed = (value: TenantLifecycleAction) => session.can(`tenancy.tenant_lifecycle.${value}`);
let generation = 0, cancellation: AbortController | undefined;
const actions = computed<TenantLifecycleAction[]>(() => {
  if (tenant.value?.provisioningStatus !== 'Completed') return [];
  const status = tenant.value?.lifecycleStatus;
  if (status === 'Active') return ['suspend', 'close'].filter(value => allowed(value as TenantLifecycleAction)) as TenantLifecycleAction[];
  if (status === 'Suspended') return ['reactivate', 'close'].filter(value => allowed(value as TenantLifecycleAction)) as TenantLifecycleAction[];
  return [];
});
const canSubmit = computed(() => Boolean(action.value && tenant.value && allowed(action.value)
  && (action.value !== 'close' || confirmation.value === tenant.value.identifier) && !loading.value && !saving.value));
function stop() { generation++; cancellation?.abort(); cancellation = undefined; }
async function refresh() {
  if (!props.open || !props.tenantId || !canRead.value) return;
  stop(); const current = generation, id = props.tenantId, controller = new AbortController(); cancellation = controller;
  loading.value = true; failed.value = false; action.value = undefined; confirmation.value = '';
  try {
    const result = await getTenantLifecycle(id, controller.signal);
    if (current !== generation || !props.open) return;
    if (result.id.toLowerCase() !== id.toLowerCase()) throw new Error('client.invalid_tenant_lifecycle_scope');
    tenant.value = result;
  } catch (error) { if (current === generation && !controller.signal.aborted) {
    tenant.value = undefined; failed.value = true; problemText.value = isFullNetProblemDetails(error) ? error.title ?? '' : '';
  } }
  finally { if (current === generation) loading.value = false; }
}
async function submit() {
  if (!canSubmit.value || !tenant.value || !action.value) return;
  const id = tenant.value.id, version = tenant.value.version, selected = action.value, current = generation;
  saving.value = true; failed.value = false; conflict.value = false;
  try {
    const result = await changeTenantLifecycle(id, version, selected);
    if (current !== generation || !props.open) return;
    if (result.id !== id || result.lifecycleStatus !== (selected === 'suspend' ? 'Suspended' : selected === 'reactivate' ? 'Active' : 'Closed'))
      throw new Error('client.invalid_tenant_lifecycle_result');
    tenant.value = result; action.value = undefined; confirmation.value = '';
    showSuccess(text('success')); emit('changed');
  } catch (error) {
    if (current !== generation) return;
    if (isFullNetProblemDetails(error) && error.status === 409) { conflict.value = true; await refresh(); }
    else { failed.value = true; problemText.value = isFullNetProblemDetails(error) ? error.title ?? '' : ''; }
  } finally { saving.value = false; }
}
watch(() => [props.open, props.tenantId, canRead.value, session.currentUser?.scope, session.currentUser?.id, session.currentUser?.sessionId], () => {
  stop(); tenant.value = undefined; action.value = undefined; confirmation.value = ''; conflict.value = false; failed.value = false;
  if (props.open) void refresh();
}, { immediate: true });
onScopeDispose(stop);
</script>

<template>
  <ElDialog :model-value="open && canRead" :title="text('title')" width="min(540px, calc(100vw - 32px))" class="tenant-lifecycle-dialog"
    :close-on-click-modal="!saving" :close-on-press-escape="!saving" :show-close="!saving" @update:model-value="emit('update:open', $event)">
    <section :aria-busy="loading || saving" data-testid="tenant-lifecycle-panel">
      <ElButton :loading="loading" :disabled="saving" @click="refresh">{{ text('refresh') }}</ElButton>
      <p v-if="conflict" role="alert">{{ text('conflict') }}</p>
      <p v-if="failed" role="alert">{{ problemText || text('failed') }}</p>
      <template v-if="tenant">
        <h3>{{ tenant.name }}</h3><code translate="no">{{ tenant.identifier }}</code>
        <p>{{ text('status') }}：<strong>{{ text(tenant.lifecycleStatus as 'Active' | 'Suspended' | 'Closing' | 'Closed') }}</strong></p>
        <p v-if="tenant.lifecycleStatus === 'Active' && !tenant.isActive">{{ text('inactive') }}</p>
        <p>{{ text('version') }}：{{ tenant.version }}</p>
        <div class="tenant-lifecycle-dialog__actions">
          <ElButton v-for="item in actions" :key="item" :type="item === 'close' ? 'danger' : 'primary'" :disabled="saving || loading"
            @click="action = item; confirmation = ''; conflict = false">{{ text(item) }}</ElButton>
        </div>
        <p v-if="!actions.length && !['Closing', 'Closed'].includes(tenant.lifecycleStatus)">{{ text('readOnly') }}</p>
        <section v-if="action" class="tenant-lifecycle-dialog__confirmation">
          <p>{{ text(action === 'close' ? 'closeHint' : action === 'suspend' ? 'suspendHint' : 'reactivateHint') }}</p>
          <label v-if="action === 'close'" for="tenant-lifecycle-identifier">{{ text('identifier') }}</label>
          <ElInput v-if="action === 'close'" id="tenant-lifecycle-identifier" v-model="confirmation" :disabled="saving" autocomplete="off" />
          <div class="tenant-lifecycle-dialog__actions">
            <ElButton :disabled="saving" @click="action = undefined">{{ text('cancel') }}</ElButton>
            <ElButton :type="action === 'close' ? 'danger' : 'primary'" :disabled="!canSubmit" :loading="saving" data-testid="tenant-lifecycle-confirm" @click="submit">{{ text('confirm') }}</ElButton>
          </div>
        </section>
      </template>
    </section>
  </ElDialog>
</template>

<style scoped>
.tenant-lifecycle-dialog__actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 16px; }
.tenant-lifecycle-dialog__actions :deep(.el-button + .el-button) { margin-left: 0; }
.tenant-lifecycle-dialog__confirmation { margin-top: 20px; padding-top: 16px; border-top: 1px solid var(--el-border-color); }
code { overflow-wrap: anywhere; }
</style>
