<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { ElButton, ElInput } from 'element-plus';
import { isFullNetProblemDetails, type TenantMember, type TenantOwnershipState, type TenantOwnerTransferReceipt } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { getTenantMember } from '../api/tenant-members';
import { getTenantOwnership, getTenantOwnerTransferReceipt, transferTenantOwner } from '../api/tenant-ownership';
import { forgetPendingOwner, ownerPendingKey, readPendingOwner, rememberPendingOwner, retainOwnerSession,
  type PendingOwnerTransfer } from './tenant-owner-pending';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { tenantOwnershipText } from '../i18n/tenant-ownership';

const props = defineProps<{ open: boolean; target?: TenantMember }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; changed: [confirmedWrite: boolean] }>();
const session = useSessionStore(), route = useRoute(), { locale } = useAdminI18n();
const text = (key: Parameters<typeof tenantOwnershipText>[1]) => tenantOwnershipText(locale.value, key);
const active = ref(true), loading = ref(false), saving = ref(false), completed = ref(false), failed = ref(false);
const member = ref<TenantMember>(), ownership = ref<TenantOwnershipState>(), errorText = ref('');
const password = ref(''), totp = ref(''), pending = ref<PendingOwnerTransfer>(), epoch = ref(0);
const same = (a: string | null | undefined, b: string | null | undefined) => !!a && a.toLowerCase() === b?.toLowerCase();
const equalNullable = (a: string | null | undefined, b: string | null | undefined) => a == null && b == null || same(a, b);
const permitted = computed(() => active.value && session.state === 'authenticated' && !!session.currentUser?.sessionId
  && !!session.currentUser.tenantId && session.currentUser.scope !== 'host' && session.can('identity.tenant_members.read')
  && session.can('identity.tenant_members.transfer_ownership'));
const scope = computed(() => JSON.stringify([session.state, session.currentUser?.id, session.currentUser?.sessionId,
  session.currentUser?.actorScope, session.currentUser?.scope, session.currentUser?.tenantId, permitted.value, route?.fullPath]));
const targetKey = computed(() => JSON.stringify([props.target?.id.toLowerCase(), props.target?.tenantId.toLowerCase(),
  props.target?.userId.toLowerCase(), props.target?.version, props.target?.status, props.target?.memberRole]));
const operationKey = () => ownerPendingKey(session.currentUser!.id, session.currentUser!.sessionId!,
  session.currentUser!.tenantId!, props.target!.id);
const canAct = computed(() => !!member.value && member.value.status === 'Active' && ['Admin', 'Member'].includes(member.value.memberRole)
  && !!ownership.value && (ownership.value.owner ? ownership.value.canTransfer : ownership.value.canInitialize));
const canSubmit = computed(() => canAct.value && password.value.length > 0 && password.value.length <= 1024
  && (!ownership.value?.requiresTotp || /^\d{6}$/u.test(totp.value)) && !loading.value && !saving.value && !pending.value && !completed.value);
let controller: AbortController | undefined;
function stop() {
  epoch.value++; controller?.abort(); controller = undefined; loading.value = false; saving.value = false;
  member.value = undefined; ownership.value = undefined; password.value = ''; totp.value = '';
}
function current(ticket: number, key: string, identity: string) {
  return ticket === epoch.value && key === scope.value && identity === targetKey.value && permitted.value && props.open;
}
async function read() {
  const target = props.target;
  if (!permitted.value || !props.open || !target || saving.value || pending.value || !same(target.tenantId, session.currentUser?.tenantId)) return;
  stop(); const ticket = epoch.value, key = scope.value, identity = targetKey.value, request = new AbortController(); controller = request;
  loading.value = true; failed.value = false; completed.value = false; errorText.value = '';
  try {
    const [row, state] = await Promise.all([getTenantMember(target.id, request.signal), getTenantOwnership(request.signal)]);
    if (!current(ticket, key, identity)) return;
    if (!same(row.id, target.id) || !same(row.userId, target.userId) || !same(row.tenantId, target.tenantId)
      || !Number.isSafeInteger(row.version) || row.version < target.version || row.version < 1
      || !same(state.tenantId, target.tenantId) || state.owner && (!same(state.owner.tenantId, target.tenantId)
        || state.owner.memberRole !== 'Owner' || !Number.isSafeInteger(state.owner.version) || state.owner.version < 1)
      || state.owner && state.canInitialize || !state.owner && state.canTransfer
      || state.owner && same(row.userId, state.owner.userId) && row.memberRole !== 'Owner'
      || state.canTransfer && (!same(state.owner?.userId, session.currentUser?.id) || state.owner?.status !== 'Active'))
      throw Error('client.invalid_tenant_ownership_scope');
    member.value = row; ownership.value = state;
  } catch (error) { if (current(ticket, key, identity) && !request.signal.aborted) {
    failed.value = true; errorText.value = isFullNetProblemDetails(error) ? error.title ?? '' : '';
  } } finally { if (current(ticket, key, identity)) { loading.value = false; controller = undefined; } }
}
function matches(receipt: TenantOwnerTransferReceipt, operation: PendingOwnerTransfer) {
  return same(receipt.tenantId, operation.tenantId) && same(receipt.operationId, operation.operationId)
    && same(receipt.actorUserId, operation.actorUserId) && same(receipt.targetMemberId, operation.targetMemberId)
    && same(receipt.targetUserId, operation.targetUserId) && receipt.targetPreviousVersion === operation.targetVersion
    && receipt.targetVersion === operation.targetVersion + 1 && equalNullable(receipt.sourceMemberId, operation.sourceMemberId)
    && equalNullable(receipt.sourceUserId, operation.sourceUserId)
    && (receipt.sourcePreviousVersion ?? null) === (operation.sourceVersion ?? null)
    && (receipt.sourceVersion ?? null) === (operation.sourceVersion == null ? null : operation.sourceVersion + 1);
}
async function submit(ticket: number, key: string, identity: string) {
  if (!current(ticket, key, identity) || !canSubmit.value || !member.value || !ownership.value) return;
  const state = ownership.value, row = member.value, storageKey = operationKey();
  const operation: PendingOwnerTransfer = { operationId: crypto.randomUUID(), tenantId: row.tenantId,
    actorUserId: session.currentUser!.id, sourceMemberId: state.owner?.id, sourceVersion: state.owner?.version,
    sourceUserId: state.owner?.userId, targetMemberId: row.id, targetUserId: row.userId, targetVersion: row.version };
  if (!rememberPendingOwner(storageKey, operation)) { failed.value = true; errorText.value = text('limit'); return; }
  const credentials = { currentPassword: password.value, totpCode: totp.value || undefined };
  password.value = ''; totp.value = ''; pending.value = operation;
  const request = new AbortController(); controller = request; saving.value = true; failed.value = false; errorText.value = '';
  try {
    const receipt = await transferTenantOwner({ operationId: operation.operationId, sourceMemberId: operation.sourceMemberId,
      sourceVersion: operation.sourceVersion, targetMemberId: operation.targetMemberId, targetVersion: operation.targetVersion,
      ...credentials }, request.signal);
    if (!current(ticket, key, identity)) return;
    if (!matches(receipt, operation)) throw Error('client.invalid_tenant_owner_transfer_receipt');
    forgetPendingOwner(storageKey); pending.value = undefined; completed.value = true;
    member.value = undefined; ownership.value = undefined; emit('changed', true);
  } catch (error) { if (current(ticket, key, identity) && !request.signal.aborted) {
    if (isFullNetProblemDetails(error) && error.status >= 400 && error.status < 500) {
      forgetPendingOwner(storageKey); pending.value = undefined;
    }
    member.value = undefined; ownership.value = undefined; failed.value = true;
    errorText.value = isFullNetProblemDetails(error) ? error.title ?? '' : '';
  } } finally { if (current(ticket, key, identity)) { saving.value = false; controller = undefined; } }
}
async function check() {
  if (!pending.value || saving.value || loading.value || !permitted.value || !props.open) return;
  const operation = { ...pending.value }, storageKey = operationKey();
  const ticket = epoch.value, key = scope.value, identity = targetKey.value, request = new AbortController(); controller = request;
  loading.value = true; failed.value = false; errorText.value = '';
  try {
    const receipt = await getTenantOwnerTransferReceipt(operation.operationId, request.signal);
    if (!current(ticket, key, identity)) return;
    if (!matches(receipt, operation)) throw Error('client.invalid_tenant_owner_transfer_receipt');
    forgetPendingOwner(storageKey); pending.value = undefined; completed.value = true; emit('changed', false);
  } catch (error) { if (current(ticket, key, identity) && !request.signal.aborted) {
    failed.value = true; errorText.value = isFullNetProblemDetails(error) && error.status === 404 ? text('missing')
      : isFullNetProblemDetails(error) ? error.title ?? '' : '';
  } } finally { if (current(ticket, key, identity)) { loading.value = false; controller = undefined; } }
}
// 事件闭包固定当前窗口、主体和目标；旧确认和关闭事件不能控制后开的窗口。
const events = computed(() => { const ticket = epoch.value, key = scope.value, identity = targetKey.value; return {
  confirm: () => submit(ticket, key, identity), 'update:open': (value: boolean) => {
    if (!value && current(ticket, key, identity)) { stop(); emit('update:open', false); }
  }
}; });
watch(() => [props.open, scope.value, targetKey.value], (_, before) => {
  stop(); failed.value = false; completed.value = false; errorText.value = ''; pending.value = undefined;
  if (session.currentUser?.id && session.currentUser.sessionId) retainOwnerSession(session.currentUser.id, session.currentUser.sessionId);
  if (permitted.value && props.target && same(props.target.tenantId, session.currentUser?.tenantId)) pending.value = readPendingOwner(operationKey());
  if (before && before[1] !== scope.value) { emit('update:open', false); return; }
  if (props.open && permitted.value && !pending.value) void read();
}, { immediate: true, flush: 'sync' });
onDeactivated(() => { active.value = false; stop(); }); onActivated(() => { active.value = true; }); onBeforeUnmount(stop);
</script>

<template>
  <ArtFormDialog :open="open && permitted" :title="text('title')" width="min(560px, calc(100vw - 32px))" :saving="saving"
    :show-confirm="canSubmit" :confirm-label="text('confirm')" :cancel-label="text('close')"
    confirm-test-id="tenant-owner-transfer-confirm" v-on="events">
    <section data-testid="tenant-owner-transfer-panel" :aria-busy="loading || saving">
      <h3>{{ text('target') }}：{{ target?.displayName }} · {{ target?.username }}</h3><p>{{ text('hint') }}</p>
      <p v-if="loading" role="status">{{ text('loading') }}</p><p v-if="saving" role="status">{{ text('saving') }}</p>
      <p v-if="pending && !saving" role="alert">{{ text('unknown') }}</p>
      <p v-if="pending">{{ text('operation') }}：{{ pending.operationId }}</p>
      <p v-if="failed" role="alert">{{ errorText || text('failed') }}</p>
      <p v-if="ownership">{{ text('source') }}：{{ ownership.owner?.displayName || text('none') }}
        <template v-if="ownership.owner"> · {{ text('version') }} {{ ownership.owner.version }}</template></p>
      <p v-if="ownership && !ownership.owner">{{ text('initialize') }}</p>
      <p v-if="member">{{ member.displayName }} · {{ text('version') }} {{ member.version }}</p>
      <p v-if="ownership && member && !canAct" role="alert">{{ text('denied') }}</p>
      <template v-if="canAct && !pending && !completed">
        <label for="tenant-owner-current-password">{{ text('password') }}</label>
        <ElInput id="tenant-owner-current-password" v-model="password" type="password" autocomplete="current-password" :maxlength="1024" :disabled="saving" />
        <template v-if="ownership?.requiresTotp"><label for="tenant-owner-current-totp">{{ text('totp') }}</label>
          <ElInput id="tenant-owner-current-totp" v-model="totp" inputmode="numeric" autocomplete="one-time-code" :maxlength="6" :disabled="saving" /></template>
      </template>
      <p v-if="completed" role="status">{{ text('success') }}</p>
      <ElButton v-if="pending" :disabled="saving || loading" data-testid="tenant-owner-transfer-check" @click="check">{{ text('check') }}</ElButton>
      <ElButton v-else-if="!completed" :disabled="saving || loading" data-testid="tenant-owner-transfer-refresh" @click="read">{{ text('refresh') }}</ElButton>
    </section>
  </ArtFormDialog>
</template>

<style scoped>
h3, p { overflow-wrap: anywhere; } label { display: block; margin: 12px 0 6px; }
</style>
