<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { ElButton } from 'element-plus';
import { isFullNetProblemDetails, type TenantInvitation } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { getTenantInvitation, revokeTenantInvitation } from '../api/tenant-members';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { tenantInvitationRevocationText } from '../i18n/tenant-invitation-revocation';

const props = defineProps<{ open: boolean; target?: TenantInvitation }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; revoked: [confirmedWrite: boolean] }>();
const session = useSessionStore(), route = useRoute(), { locale } = useAdminI18n();
const text = (key: Parameters<typeof tenantInvitationRevocationText>[1]) => tenantInvitationRevocationText(locale.value, key);
const date = (value: string) => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value));
const active = ref(true), loading = ref(false), saving = ref(false), failed = ref(false), completed = ref(false);
const invitation = ref<TenantInvitation>(), errorText = ref(''), epoch = ref(0), unknownInvitations = ref(new Set<string>());
const sameId = (a: string | undefined | null, b: string | undefined | null) => !!a && a.toLowerCase() === b?.toLowerCase();
const permitted = computed(() => active.value && session.state === 'authenticated' && !!session.currentUser?.tenantId
  && session.currentUser.scope !== 'host' && session.can('identity.tenant_members.read') && session.can('identity.tenant_members.revoke_invitation'));
const scope = computed(() => JSON.stringify([session.state, session.currentUser?.id, session.currentUser?.sessionId,
  session.currentUser?.actorScope, session.currentUser?.scope, session.currentUser?.tenantId, permitted.value, route?.fullPath]));
const targetKey = computed(() => JSON.stringify([props.target?.id.toLowerCase(), props.target?.tenantId.toLowerCase(), props.target?.version]));
const operationKey = computed(() => JSON.stringify([session.currentUser?.id.toLowerCase(), session.currentUser?.sessionId?.toLowerCase(),
  session.currentUser?.tenantId?.toLowerCase(), props.target?.id.toLowerCase()]));
const unknown = computed(() => unknownInvitations.value.has(operationKey.value));
const canConfirm = computed(() => !!invitation.value && invitation.value.status === 'Pending' && !loading.value && !saving.value && !unknown.value && !completed.value);
let controller: AbortController | undefined, writingKey: string | undefined;
function stop() {
  // 取消等待不表示事务已回滚；未知结果始终留在原邀请和原会话下。
  if (saving.value && writingKey) unknownInvitations.value.add(writingKey);
  writingKey = undefined; epoch.value++; controller?.abort(); controller = undefined;
  loading.value = false; saving.value = false; invitation.value = undefined;
}
function current(ticket: number, key: string, target: string) {
  return ticket === epoch.value && key === scope.value && target === targetKey.value && permitted.value && props.open;
}
async function read() {
  const target = props.target;
  if (!props.open || !permitted.value || !target || saving.value || !sameId(target.tenantId, session.currentUser?.tenantId)) return;
  stop(); const ticket = epoch.value, key = scope.value, identity = targetKey.value, request = new AbortController(); controller = request;
  loading.value = true; failed.value = false; completed.value = false; errorText.value = '';
  try {
    const result = await getTenantInvitation(target.id, request.signal);
    if (!current(ticket, key, identity)) return;
    if (!sameId(result.id, target.id) || !sameId(result.tenantId, target.tenantId) || !sameId(result.invitedByUserId, target.invitedByUserId)
      || result.targetEmail.toLowerCase() !== target.targetEmail.toLowerCase() || !Number.isSafeInteger(result.version)
      || result.version <= 0 || result.version < target.version || !['Pending', 'Accepted', 'Revoked', 'Expired'].includes(result.status))
      throw Error('client.invalid_tenant_invitation_scope');
    invitation.value = result; unknownInvitations.value.delete(operationKey.value);
    if (result.status === 'Revoked') { completed.value = true; emit('revoked', false); }
  } catch (error) { if (current(ticket, key, identity) && !request.signal.aborted) {
    failed.value = true; errorText.value = isFullNetProblemDetails(error) ? error.title ?? '' : '';
  } } finally { if (current(ticket, key, identity)) { loading.value = false; controller = undefined; } }
}
async function revoke(ticket: number, key: string, identity: string) {
  const target = invitation.value;
  if (!current(ticket, key, identity) || !canConfirm.value || !target) return;
  const request = new AbortController(); controller = request; saving.value = true; writingKey = operationKey.value; failed.value = false; errorText.value = '';
  try {
    const result = await revokeTenantInvitation(target.id, request.signal);
    if (!current(ticket, key, identity)) return;
    if (!sameId(result.id, target.id) || !sameId(result.tenantId, target.tenantId) || !sameId(result.invitedByUserId, target.invitedByUserId)
      || result.targetEmail.toLowerCase() !== target.targetEmail.toLowerCase() || result.status !== 'Revoked' || result.version !== target.version + 1)
      throw Error('client.invalid_tenant_invitation_revocation_receipt');
    saving.value = false; writingKey = undefined; controller = undefined; completed.value = true; invitation.value = undefined; emit('revoked', true);
  } catch (error) { if (current(ticket, key, identity) && !request.signal.aborted) {
    if (!isFullNetProblemDetails(error) || error.status >= 500) unknownInvitations.value.add(operationKey.value);
    invitation.value = undefined; failed.value = true; errorText.value = isFullNetProblemDetails(error) ? error.title ?? '' : '';
  } } finally { if (current(ticket, key, identity)) { saving.value = false; writingKey = undefined; controller = undefined; } }
}
// 每次确认和关闭都固定窗口代次，旧事件不能接管新目标。
const events = computed(() => { const ticket = epoch.value, key = scope.value, identity = targetKey.value; return {
  confirm: () => revoke(ticket, key, identity), 'update:open': (value: boolean) => {
    if (!value && current(ticket, key, identity)) { stop(); emit('update:open', false); }
  }
}; });
watch(() => [props.open, scope.value, targetKey.value], (_, before) => {
  stop(); completed.value = false; failed.value = false; errorText.value = '';
  if (before && before[1] !== scope.value) { emit('update:open', false); return; }
  if (props.open && permitted.value && !unknown.value) void read();
}, { immediate: true, flush: 'sync' });
onDeactivated(() => { active.value = false; stop(); }); onActivated(() => { active.value = true; }); onBeforeUnmount(stop);
</script>

<template>
  <ArtFormDialog :open="open && permitted" :title="text('title')" width="min(520px, calc(100vw - 32px))" :saving="saving"
    :show-confirm="canConfirm" :confirm-label="text('confirm')" :cancel-label="text('close')" confirm-test-id="tenant-invitation-revocation-confirm" v-on="events">
    <section data-testid="tenant-invitation-revocation-panel" :aria-busy="loading || saving">
      <h3>{{ target?.targetEmail }}</h3><p>{{ text('hint') }}</p>
      <p v-if="loading" role="status">{{ text('loading') }}</p><p v-if="saving" role="status">{{ text('saving') }}</p>
      <p v-if="unknown" role="alert">{{ text('unknown') }}</p><p v-if="failed" role="alert">{{ errorText || text('failed') }}</p>
      <p v-if="invitation?.status === 'Accepted'" role="alert">{{ text('accepted') }}</p>
      <p v-if="invitation?.status === 'Expired'" role="status">{{ text('expired') }}</p>
      <p v-if="invitation">{{ text('version') }} {{ invitation.version }}</p>
      <dl v-if="invitation">
        <dt>{{ text('role') }}</dt><dd>{{ invitation.memberRole === 'Admin' ? text('admin') : invitation.memberRole === 'Member' ? text('member') : invitation.memberRole }}</dd>
        <dt>{{ text('created') }}</dt><dd>{{ date(invitation.createdAtUtc) }}</dd>
        <dt>{{ text('expires') }}</dt><dd>{{ date(invitation.expiresAtUtc) }}</dd>
      </dl>
      <p v-if="completed" role="status">{{ invitation?.status === 'Revoked' ? text('observed') : text('success') }}</p>
      <ElButton v-if="!completed" :disabled="loading || saving" data-testid="tenant-invitation-revocation-refresh" @click="read">{{ text('refresh') }}</ElButton>
    </section>
  </ArtFormDialog>
</template>

<style scoped>
h3, p, dd { overflow-wrap: anywhere; }
dl { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 6px 12px; }
dd { margin: 0; }
</style>
