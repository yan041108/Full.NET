<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { ElButton } from 'element-plus';
import { isFullNetProblemDetails, type TenantMember } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { getTenantMember, removeTenantMember } from '../api/tenant-members';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { tenantMemberRemovalText } from '../i18n/tenant-member-removal';

const props = defineProps<{ open: boolean; target?: TenantMember }>();
const emit = defineEmits<{ 'update:open': [value: boolean]; removed: [confirmedWrite: boolean] }>();
const session = useSessionStore(), route = useRoute(), { locale } = useAdminI18n();
const text = (key: Parameters<typeof tenantMemberRemovalText>[1]) => tenantMemberRemovalText(locale.value, key);
const active = ref(true), loading = ref(false), saving = ref(false), failed = ref(false), completed = ref(false);
const member = ref<TenantMember>(), errorText = ref(''), epoch = ref(0);
const unknownMembers = ref(new Set<string>());
const sameId = (a: string | undefined | null, b: string | undefined | null) => !!a && a.toLowerCase() === b?.toLowerCase();
const permitted = computed(() => active.value && session.state === 'authenticated' && !!session.currentUser?.tenantId
  && session.currentUser.scope !== 'host' && session.can('identity.tenant_members.read') && session.can('identity.tenant_members.remove'));
const scope = computed(() => JSON.stringify([session.state, session.currentUser?.id, session.currentUser?.sessionId,
  session.currentUser?.actorScope, session.currentUser?.scope, session.currentUser?.tenantId, permitted.value, route?.fullPath]));
const targetKey = computed(() => JSON.stringify([props.target?.id.toLowerCase(), props.target?.tenantId.toLowerCase(),
  props.target?.userId.toLowerCase(), props.target?.version]));
const targetId = computed(() => props.target?.id.toLowerCase() ?? '');
const operationKey = computed(() => JSON.stringify([session.currentUser?.id.toLowerCase(), session.currentUser?.sessionId?.toLowerCase(),
  session.currentUser?.tenantId?.toLowerCase(), targetId.value]));
const unknown = computed(() => unknownMembers.value.has(operationKey.value));
let controller: AbortController | undefined;
let writingKey: string | undefined;
function stop() {
  // 未确认写入按成员保留，换目标再回来不能把取消等待当作服务端回滚。
  if (saving.value && writingKey) unknownMembers.value.add(writingKey);
  writingKey = undefined;
  epoch.value++; controller?.abort(); controller = undefined; loading.value = false; saving.value = false; member.value = undefined;
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
    const result = await getTenantMember(target.id, request.signal);
    if (!current(ticket, key, identity)) return;
    if (!sameId(result.id, target.id) || !sameId(result.tenantId, target.tenantId) || !sameId(result.userId, target.userId)
      || !Number.isSafeInteger(result.version) || result.version <= 0 || result.version < target.version
      || !['Owner', 'Admin', 'Member'].includes(result.memberRole)
      || !['Active', 'Pending', 'Suspended', 'Removed'].includes(result.status)) throw Error('client.invalid_tenant_member_scope');
    unknownMembers.value.delete(operationKey.value); member.value = result;
    if (result.status === 'Removed') { completed.value = true; emit('removed', false); }
  } catch (error) { if (current(ticket, key, identity) && !request.signal.aborted) {
    failed.value = true; errorText.value = isFullNetProblemDetails(error) ? error.title ?? '' : '';
  } } finally { if (current(ticket, key, identity)) { loading.value = false; controller = undefined; } }
}
async function remove(ticket: number, key: string, identity: string) {
  const target = member.value;
  if (!current(ticket, key, identity) || !target || target.memberRole === 'Owner' || target.status === 'Removed'
    || loading.value || saving.value || unknown.value || completed.value) return;
  const request = new AbortController(); controller = request; saving.value = true; writingKey = operationKey.value;
  failed.value = false; errorText.value = '';
  try {
    const result = await removeTenantMember(target.id, target.version, request.signal);
    if (!current(ticket, key, identity)) return;
    if (!sameId(result.id, target.id) || !sameId(result.tenantId, target.tenantId) || !sameId(result.userId, target.userId)
      || result.status !== 'Removed' || result.version !== target.version + 1) throw Error('client.invalid_tenant_member_removal_receipt');
    saving.value = false; writingKey = undefined; controller = undefined; completed.value = true; member.value = undefined; emit('removed', true);
  } catch (error) { if (current(ticket, key, identity) && !request.signal.aborted) {
    if (!isFullNetProblemDetails(error) || error.status >= 500) unknownMembers.value.add(operationKey.value);
    member.value = undefined; failed.value = true; errorText.value = isFullNetProblemDetails(error) ? error.title ?? '' : '';
  } } finally { if (current(ticket, key, identity)) { saving.value = false; writingKey = undefined; controller = undefined; } }
}
// 固定窗口代次和目标，旧事件不能控制后来窗口或另外一名成员。
const events = computed(() => { const ticket = epoch.value, key = scope.value, identity = targetKey.value; return {
  confirm: () => remove(ticket, key, identity), 'update:open': (value: boolean) => {
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
    :show-confirm="!!member && member.memberRole !== 'Owner' && member.status !== 'Removed' && !loading && !saving && !unknown && !completed"
    :confirm-label="text('confirm')" :cancel-label="text('close')" confirm-test-id="tenant-member-removal-confirm" v-on="events">
    <section data-testid="tenant-member-removal-panel" :aria-busy="loading || saving">
      <h3>{{ target?.displayName }} · {{ target?.username }}</h3><p>{{ text('hint') }}</p>
      <p v-if="loading" role="status">{{ text('loading') }}</p><p v-if="saving" role="status">{{ text('saving') }}</p>
      <p v-if="unknown" role="alert">{{ text('unknown') }}</p><p v-if="failed" role="alert">{{ errorText || text('failed') }}</p>
      <p v-if="member?.memberRole === 'Owner'" role="alert">{{ text('owner') }}</p>
      <p v-if="member">{{ member.displayName }} · {{ text('version') }} {{ member.version }}</p>
      <p v-if="completed" role="status">{{ member?.status === 'Removed' ? text('removed') : text('success') }}</p>
      <ElButton v-if="!completed" :disabled="saving || loading" data-testid="tenant-member-removal-refresh" @click="read">{{ text('refresh') }}</ElButton>
    </section>
  </ArtFormDialog>
</template>

<style scoped>
h3, p { overflow-wrap: anywhere; }
</style>
