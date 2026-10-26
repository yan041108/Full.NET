<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElAlert, ElButton, ElOption, ElPagination, ElSelect } from 'element-plus';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { getOrganizationUnitLeadership, listOrganizationUnitLeaderCandidates, setOrganizationUnitLeadership,
  type OrganizationUnitLeadership, type OrganizationUnitLeaderCandidates } from '../api/org-unit-leadership';

const props = defineProps<{ unitId: string; unitName: string }>();
const emit = defineEmits<{ close: []; saved: [] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const configuration = ref<OrganizationUnitLeadership>(); const candidates = ref<OrganizationUnitLeaderCandidates>();
const selected = ref<string | null>(null); const page = ref(1); const loading = ref(false); const saving = ref(false);
const selectedName = ref(''); const closed = ref(false);
const message = ref(''); const needsRefresh = ref(false);
const canRead = computed(() => session.can('organization.unit_leadership.read'));
const canUpdate = computed(() => session.can('organization.unit_leadership.update'));
const identity = computed(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId,
  session.currentUser?.actorScope, ...(session.currentUser?.permissions ?? [])].join('|'));
let epoch = 0; let controller: AbortController | undefined;
function cancel(): void { epoch++; controller?.abort(); controller = undefined; }
function close(): void { if (closed.value) return; closed.value = true; cancel(); configuration.value = undefined; candidates.value = undefined; selected.value = null; selectedName.value = ''; emit('close'); }
function error(error: unknown): void { message.value = isFullNetProblemDetails(error) ? error.title ?? t('orgUnitLeadership.failed') : t('orgUnitLeadership.failed'); }
async function load(): Promise<void> {
  if (closed.value || !canRead.value) return;
  cancel(); const ticket = epoch; controller = new AbortController(); const signal = controller.signal;
  configuration.value = undefined; candidates.value = undefined; loading.value = true; message.value = ''; needsRefresh.value = false;
  try {
    const current = await getOrganizationUnitLeadership(props.unitId, signal);
    if (ticket !== epoch || !canRead.value) return;
    configuration.value = current; selected.value = current.leaderUserId; selectedName.value = current.leaderDisplayName ?? t('orgUnitLeadership.unavailable'); page.value = 1;
    if (canUpdate.value && current.isUnitActive) {
      const result = await listOrganizationUnitLeaderCandidates(props.unitId, 1, 20, signal);
      if (ticket === epoch) candidates.value = result;
    }
  } catch (failure) { if (ticket === epoch) { error(failure); needsRefresh.value = true; } }
  finally { if (ticket === epoch) loading.value = false; }
}
async function changePage(value: number): Promise<void> {
  if (closed.value || !canUpdate.value || !configuration.value?.isUnitActive || loading.value || saving.value) return;
  const ticket = epoch; page.value = value; loading.value = true; message.value = '';
  try { const result = await listOrganizationUnitLeaderCandidates(props.unitId, value, 20, controller?.signal); if (ticket === epoch) candidates.value = result; }
  catch (failure) { if (ticket === epoch) { candidates.value = undefined; error(failure); } }
  finally { if (ticket === epoch) loading.value = false; }
}
async function save(): Promise<void> {
  if (closed.value || !configuration.value || loading.value || saving.value || needsRefresh.value || !canUpdate.value || !configuration.value.isUnitActive) return;
  const ticket = epoch; saving.value = true; message.value = '';
  try {
    const result = await setOrganizationUnitLeadership(props.unitId, { leaderUserId: selected.value,
      expectedUnitVersion: configuration.value.unitVersion, expectedRevision: configuration.value.revision }, controller?.signal);
    if (ticket !== epoch) return;
    configuration.value = result; selected.value = result.leaderUserId; emit('saved'); close();
  } catch (failure) {
    if (ticket !== epoch) return;
    error(failure);
    // 未知结果或并发冲突先刷新权威状态；不能把旧修订再次当作一次新授权写入。
    needsRefresh.value = !isFullNetProblemDetails(failure) || ![400, 422, 429].includes(failure.status ?? 0);
  } finally { if (ticket === epoch) saving.value = false; }
}
function choose(value: unknown): void {
  selected.value = typeof value === 'string' && value.length > 0 ? value : null;
  const candidate = candidates.value?.items.find(item => item.id === selected.value);
  selectedName.value = candidate ? candidate.displayName || candidate.username : configuration.value?.leaderUserId === selected.value
    ? configuration.value.leaderDisplayName ?? t('orgUnitLeadership.unavailable') : selected.value ?? '';
}
watch(() => props.unitId, () => { if (canRead.value) void load(); }, { immediate: true });
watch(identity, close, { flush: 'sync' });
watch(canRead, value => { if (!value) close(); }, { flush: 'sync' });
onBeforeUnmount(cancel);
</script>

<template>
  <ArtFormDialog :open="canRead && !closed" :title="t('orgUnitLeadership.title', { name: unitName })" :saving="saving"
    :show-confirm="canUpdate && configuration?.isUnitActive === true && !needsRefresh && !loading"
    :confirm-label="t('users.confirm')" :cancel-label="t('users.cancel')" confirm-test-id="org-unit-leadership-save"
    @update:open="value => { if (!value) close(); }" @cancel="close" @confirm="save">
    <div class="leadership" data-testid="org-unit-leadership-dialog">
      <p>{{ t('orgUnitLeadership.help') }}</p>
      <ElAlert v-if="message" :title="message" type="error" :closable="false" role="alert" />
      <ElAlert v-if="needsRefresh" :title="t('orgUnitLeadership.refreshHint')" type="warning" :closable="false" />
      <label><span>{{ t('orgUnitLeadership.leader') }}</span>
        <ElSelect :model-value="selected ?? undefined" filterable clearable :disabled="!canUpdate || loading || saving || !configuration?.isUnitActive || needsRefresh"
          :loading="loading" data-testid="org-unit-leadership-select" :aria-label="t('orgUnitLeadership.leader')"
          @update:model-value="choose">
          <ElOption v-if="selected && !candidates?.items.some(item => item.id === selected)"
            :value="selected" :label="selectedName" :disabled="configuration?.leaderUserId === selected && configuration?.leaderDisplayName === null" />
          <ElOption v-for="candidate in candidates?.items ?? []" :key="candidate.id" :value="candidate.id" :label="candidate.displayName || candidate.username" />
        </ElSelect>
      </label>
      <p v-if="configuration?.leaderUserId && configuration.leaderDisplayName === null">{{ t('orgUnitLeadership.unavailableHint') }}</p>
      <p v-if="configuration && !configuration.isUnitActive">{{ t('orgUnitLeadership.inactiveUnit') }}</p>
      <ElPagination v-if="canUpdate && candidates && candidates.total > 20" :current-page="page" :page-size="20" :total="candidates.total"
        :disabled="loading || saving" layout="prev, pager, next" @current-change="changePage" />
      <ElButton :disabled="loading || saving" data-testid="org-unit-leadership-refresh" @click="load">{{ t('orgUnitLeadership.refresh') }}</ElButton>
    </div>
  </ArtFormDialog>
</template>

<style scoped>
.leadership, .leadership label { display: grid; gap: .75rem; }
.leadership p { margin: 0; line-height: 1.6; }
</style>
