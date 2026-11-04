<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { ElButton } from 'element-plus';
import { isFullNetProblemDetails, type TenantMember } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { getCurrentTenantMember, leaveCurrentTenantMember } from '../api/tenant-members';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { tenantLeaveText } from '../i18n/tenant-leave';

const props = defineProps<{ open: boolean }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; left: [] }>();
const session = useSessionStore(), route = useRoute(), { t, locale } = useAdminI18n();
const text = (key: Parameters<typeof tenantLeaveText>[1]) => tenantLeaveText(locale.value, key);
const active = ref(true), loading = ref(false), saving = ref(false), failed = ref(false), unknown = ref(false), completed = ref(false);
const member = ref<TenantMember>(), title = ref(''), errorText = ref('');
const permitted = computed(() => active.value && session.state === 'authenticated' && !!session.currentUser?.tenantId
  && session.currentUser.scope !== 'host' && session.can('identity.tenant_members.leave_self'));
const scope = computed(() => JSON.stringify([session.state, session.currentUser?.id, session.currentUser?.sessionId,
  session.currentUser?.actorScope, session.currentUser?.scope, session.currentUser?.tenantId, permitted.value, route?.fullPath]));
const epoch = ref(0);
let controller: AbortController | undefined;
const sameId = (a: string, b: string | undefined | null) => a.toLowerCase() === b?.toLowerCase();
function stop() {
  // 取消只终止客户端等待；已发出的退出可能仍在服务端提交。
  if (saving.value) unknown.value = true;
  epoch.value++; controller?.abort(); controller = undefined; loading.value = false; saving.value = false; member.value = undefined;
}
function current(ticket: number, key: string) { return ticket === epoch.value && key === scope.value && permitted.value && props.open; }
async function read() {
  if (!props.open || !permitted.value || saving.value || completed.value) return;
  stop(); const ticket = epoch.value, key = scope.value, request = new AbortController(); controller = request;
  loading.value = true; failed.value = false; errorText.value = '';
  try {
    const result = await getCurrentTenantMember(request.signal);
    if (!current(ticket, key)) return;
    if (!sameId(result.tenantId, session.currentUser?.tenantId) || !sameId(result.userId, session.currentUser?.id)
      || !Number.isSafeInteger(result.version) || result.version <= 0) throw Error('client.invalid_tenant_member_scope');
    // 人工核对已退出的事实即可返回 Host，无需把未知写入再发一次。
    if (result.status === 'Removed') {
      unknown.value = false; completed.value = true; loading.value = false; controller = undefined; emit('left'); return;
    }
    if (result.status !== 'Active') throw Error('client.invalid_tenant_member_scope');
    member.value = result; unknown.value = false;
  } catch (error) { if (current(ticket, key) && !request.signal.aborted) {
    failed.value = true; errorText.value = isFullNetProblemDetails(error) ? error.title ?? '' : '';
  } } finally { if (current(ticket, key)) { loading.value = false; controller = undefined; } }
}
async function leave(ticket: number, key: string) {
  const target = member.value;
  if (!current(ticket, key) || !target || loading.value || saving.value || unknown.value || completed.value || target.memberRole === 'Owner') return;
  const request = new AbortController(); controller = request; saving.value = true; failed.value = false;
  try {
    const result = await leaveCurrentTenantMember(target.version, request.signal);
    if (!current(ticket, key)) return;
    if (!sameId(result.id, target.id) || !sameId(result.tenantId, target.tenantId) || !sameId(result.userId, target.userId)
      || result.status !== 'Removed' || result.version !== target.version + 1) throw Error('client.invalid_tenant_leave_receipt');
    saving.value = false; controller = undefined; completed.value = true; member.value = undefined; emit('left');
  } catch (error) { if (current(ticket, key) && !request.signal.aborted) {
    member.value = undefined; failed.value = true;
    unknown.value = !isFullNetProblemDetails(error) || error.status >= 500;
    errorText.value = isFullNetProblemDetails(error) ? error.title ?? '' : '';
  } } finally { if (current(ticket, key)) { saving.value = false; controller = undefined; } }
}
// 窗口事件冻结代次，旧确认或关闭不能控制重开的窗口。
const events = computed(() => { const ticket = epoch.value, key = scope.value; return {
  confirm: () => leave(ticket, key), 'update:open': (value: boolean) => {
    if (!value && current(ticket, key)) { stop(); emit('update:open', false); }
  }
}; });
watch(() => [props.open, scope.value], (_, before) => {
  const changedScope = before && before[1] !== scope.value;
  stop(); failed.value = false; errorText.value = '';
  if (changedScope) { unknown.value = false; completed.value = false; emit('update:open', false); return; }
  if (props.open && permitted.value) { title.value = session.currentContextName; if (!unknown.value && !completed.value) void read(); }
}, { immediate: true, flush: 'sync' });
onDeactivated(() => { active.value = false; stop(); });
onActivated(() => { active.value = true; });
onBeforeUnmount(stop);
</script>

<template>
  <ArtFormDialog :open="open && permitted" :title="t('tenant.leaveTitle')" width="min(520px, calc(100vw - 32px))"
    :saving="saving" :show-confirm="!!member && member.memberRole !== 'Owner' && !loading && !saving && !unknown && !completed"
    :confirm-label="t('tenant.leaveAction')" :cancel-label="text('close')" confirm-test-id="tenant-leave-confirm" v-on="events">
    <section data-testid="tenant-leave-panel" :aria-busy="loading || saving">
      <h3>{{ title }}</h3>
      <p>{{ t('tenant.leaveConfirm') }}</p><p>{{ text('accountHint') }}</p>
      <p v-if="loading" role="status">{{ text('loading') }}</p>
      <p v-if="saving" role="status">{{ text('saving') }}</p>
      <p v-if="unknown" role="alert">{{ text('unknown') }}</p>
      <p v-if="failed" role="alert">{{ errorText || t('tenant.leaveFailed') }}</p>
      <p v-if="member?.memberRole === 'Owner'" role="alert">{{ t('tenant.leaveOwnerBlocked') }}</p>
      <p v-if="member">{{ member.displayName }} · {{ text('version') }} {{ member.version }}</p>
      <p v-if="completed" role="status">{{ t('tenant.leaveSuccess') }}</p>
      <ElButton v-if="!completed" :disabled="saving || loading" data-testid="tenant-leave-refresh" @click="read">{{ text('refresh') }}</ElButton>
    </section>
  </ArtFormDialog>
</template>

<style scoped>
h3, p { overflow-wrap: anywhere; }
</style>
