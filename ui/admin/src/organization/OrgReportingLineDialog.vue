<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue';
import { ElAlert, ElButton, ElOption, ElPagination, ElSelect } from 'element-plus';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { getOrganizationReportingLine, listOrganizationReportingCandidates, setOrganizationReportingLine,
  type OrganizationReportingLine, type OrganizationReportingCandidates } from '../api/org-reporting-lines';
const props = defineProps<{ employeeUserId: string; employeeName: string }>();
const emit = defineEmits<{ close: []; saved: [] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const configuration = ref<OrganizationReportingLine>(); const candidates = ref<OrganizationReportingCandidates>();
const selected = ref<string | null>(null); const selectedName = ref(''); const page = ref(1); const loading = ref(false); const saving = ref(false);
const message = ref(''); const needsRefresh = ref(false); const closed = ref(false);
const canRead = computed(() => session.can('organization.reporting_lines.read'));
const canUpdate = computed(() => session.can('organization.reporting_lines.update'));
const identity = computed(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId, session.currentUser?.actorScope, ...(session.currentUser?.permissions ?? [])].join('|'));
let epoch = 0; let controller: AbortController | undefined;
function cancel(): void { epoch++; controller?.abort(); controller = undefined; }
function close(): void { if (closed.value) return; closed.value = true; cancel(); configuration.value = undefined; candidates.value = undefined; selected.value = null; selectedName.value = ''; emit('close'); }
function error(failure: unknown): void { message.value = isFullNetProblemDetails(failure) ? failure.title ?? t('orgReporting.failed') : t('orgReporting.failed'); }
async function load(): Promise<void> {
  if (closed.value || !canRead.value) return;
  cancel(); const ticket = epoch; controller = new AbortController(); const signal = controller.signal;
  configuration.value = undefined; candidates.value = undefined; loading.value = true; needsRefresh.value = false; message.value = '';
  try {
    const current = await getOrganizationReportingLine(props.employeeUserId, signal); if (ticket !== epoch) return;
    configuration.value = current; selected.value = current.managerUserId; selectedName.value = current.managerDisplayName ?? t('orgUnitLeadership.unavailable'); page.value = 1;
    if (canUpdate.value) { const result = await listOrganizationReportingCandidates(props.employeeUserId, 1, 20, signal); if (ticket === epoch) candidates.value = result; }
  } catch (failure) { if (ticket === epoch) { error(failure); needsRefresh.value = true; } }
  finally { if (ticket === epoch) loading.value = false; }
}
async function changePage(value: number): Promise<void> {
  if (closed.value || !canUpdate.value || !configuration.value || loading.value || saving.value) return;
  const ticket = epoch; page.value = value; loading.value = true; message.value = '';
  try { const result = await listOrganizationReportingCandidates(props.employeeUserId, value, 20, controller?.signal); if (ticket === epoch) candidates.value = result; }
  catch (failure) { if (ticket === epoch) { candidates.value = undefined; error(failure); } }
  finally { if (ticket === epoch) loading.value = false; }
}
async function save(): Promise<void> {
  if (closed.value || !canUpdate.value || !configuration.value || loading.value || saving.value || needsRefresh.value || selected.value === props.employeeUserId) return;
  const ticket = epoch; saving.value = true; message.value = '';
  try {
    const result = await setOrganizationReportingLine(props.employeeUserId, selected.value, configuration.value.revision, controller?.signal);
    if (ticket !== epoch) return; configuration.value = result; emit('saved'); close();
  } catch (failure) { if (ticket === epoch) { error(failure); needsRefresh.value = !isFullNetProblemDetails(failure) || ![400, 422, 429].includes(failure.status ?? 0); } }
  finally { if (ticket === epoch) saving.value = false; }
}
function choose(value: unknown): void {
  selected.value = typeof value === 'string' && value ? value : null;
  const person = candidates.value?.items.find(item => item.id === selected.value);
  selectedName.value = person ? person.displayName || person.username : configuration.value?.managerUserId === selected.value ? configuration.value.managerDisplayName ?? t('orgUnitLeadership.unavailable') : selected.value ?? '';
}
watch(() => props.employeeUserId, () => { if (canRead.value) void load(); }, { immediate: true });
watch(identity, close, { flush: 'sync' }); onBeforeUnmount(cancel);
</script>
<template>
  <ArtFormDialog :open="canRead && !closed" :title="t('orgReporting.title', { name: employeeName })" :saving="saving"
    :show-confirm="canUpdate && configuration !== undefined && !needsRefresh && !loading" confirm-test-id="org-reporting-save"
    :confirm-label="t('users.confirm')" :cancel-label="t('users.cancel')" @update:open="value => { if (!value) close(); }" @cancel="close" @confirm="save">
    <div class="reporting" data-testid="org-reporting-dialog">
      <p>{{ t('orgReporting.help') }}</p>
      <ElAlert v-if="message" :title="message" type="error" :closable="false" role="alert" />
      <ElAlert v-if="needsRefresh" :title="t('orgUnitLeadership.refreshHint')" type="warning" :closable="false" />
      <label><span>{{ t('orgReporting.manager') }}</span>
        <ElSelect :model-value="selected ?? undefined" clearable filterable :disabled="!canUpdate || loading || saving || !configuration || needsRefresh"
          data-testid="org-reporting-select" :aria-label="t('orgReporting.manager')" @update:model-value="choose">
          <ElOption v-if="selected && !candidates?.items.some(item => item.id === selected)" :value="selected" :label="selectedName" />
          <ElOption v-for="person in candidates?.items ?? []" :key="person.id" :value="person.id" :label="person.displayName || person.username" :disabled="person.id === employeeUserId" />
        </ElSelect>
      </label>
      <p v-if="configuration?.managerUserId && configuration.managerDisplayName === null">{{ t('orgUnitLeadership.unavailableHint') }}</p>
      <ElPagination v-if="canUpdate && candidates && candidates.total > 20" :current-page="page" :page-size="20" :total="candidates.total" :disabled="loading || saving" layout="prev, pager, next" @current-change="changePage" />
      <ElButton :disabled="loading || saving" data-testid="org-reporting-refresh" @click="load">{{ t('orgUnitLeadership.refresh') }}</ElButton>
    </div>
  </ArtFormDialog>
</template>
<style scoped>
.reporting, .reporting label { display: grid; gap: .75rem; }
.reporting p { margin: 0; line-height: 1.6; }
</style>
