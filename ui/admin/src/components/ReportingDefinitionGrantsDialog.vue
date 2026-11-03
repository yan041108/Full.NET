<script setup lang="ts">
import { computed, onBeforeUnmount, onDeactivated, ref, watch } from 'vue';
import { ElAlert, ElButton, ElDialog, ElForm, ElFormItem, ElOption, ElPagination, ElSelect, ElTable, ElTableColumn, ElTag } from 'element-plus';
import type { ReportingDefinitionTenantGrant, HostTenant } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listHostTenants } from '../api/tenants';
import { listReportingDefinitionTenantGrants, setReportingDefinitionTenantGrant } from '../api/reporting-publications';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';

const open = defineModel<boolean>({ required: true });
const props = defineProps<{ definitionId: string }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const items = ref<ReportingDefinitionTenantGrant[]>([]), tenants = ref<HostTenant[]>([]);
const page = ref(1), total = ref(0), selected = ref(''), loading = ref(false), saving = ref(false);
const scope = () => [session.currentUser?.sessionId, session.currentUser?.tenantId, session.currentUser?.id].join('|');
const readable = computed(() => !session.switching && session.currentUser?.tenantId === null && session.can('reporting.definition_grants.read'));
const manageable = computed(() => readable.value && session.can('reporting.definition_grants.manage'));
let generation = 0, searchVersion = 0; let cancellation = new AbortController(), search: AbortController | undefined;
function reset() {
  generation++; cancellation.abort(); cancellation = new AbortController(); search?.abort(); searchVersion++;
  items.value = []; tenants.value = []; selected.value = ''; page.value = 1; total.value = 0; loading.value = false; saving.value = false;
}
async function load() {
  if (!open.value || !readable.value || !props.definitionId) return;
  const current = generation; loading.value = true;
  try {
    const result = await listReportingDefinitionTenantGrants(props.definitionId, page.value, cancellation.signal);
    if (current === generation && open.value && readable.value) { items.value = result.items; total.value = result.total; }
  } catch (error) { if (current === generation) showProblem(error, t('reportingGrants.loadFailed')); }
  finally { if (current === generation) loading.value = false; }
}
async function searchTenants(keyword: string) {
  if (!open.value || !manageable.value || !session.can('tenancy.tenants.read')) return;
  search?.abort(); const controller = new AbortController(); search = controller; const current = generation, version = ++searchVersion;
  try {
    const result = await listHostTenants(1, 20, controller.signal, { name: keyword, status: 'active' });
    if (current === generation && version === searchVersion && !controller.signal.aborted) tenants.value = result.items;
  } catch (error) { if (current === generation && !controller.signal.aborted) showProblem(error, t('reportingGrants.loadFailed')); }
}
async function save(tenantId: string, enabled: boolean, version: number) {
  if (!open.value || !manageable.value || saving.value || !tenantId) return;
  const current = generation; saving.value = true;
  try {
    await setReportingDefinitionTenantGrant(props.definitionId, tenantId, enabled, version, cancellation.signal);
    if (current !== generation || !manageable.value) return;
    showSuccess(t('reportingGrants.saved')); selected.value = ''; await load();
  } catch (error) {
    if (current === generation) { showProblem(error, t('reportingGrants.saveFailed')); await load(); }
  } finally { if (current === generation) saving.value = false; }
}
function tenantName(id: string) { return tenants.value.find(item => item.id === id)?.name ?? id; }
watch(() => [scope(), session.currentUser?.permissions.join('|'), session.switching, props.definitionId], () => { reset(); open.value = false; }, { flush: 'sync' });
watch(open, value => { reset(); if (value && readable.value) { void load(); void searchTenants(''); } else if (value) open.value = false; });
onBeforeUnmount(reset);
onDeactivated(() => { reset(); open.value = false; });
</script>

<template>
  <ElDialog v-model="open" class="reporting-publication-grants" :title="t('reportingGrants.title')" width="min(720px, 96vw)" destroy-on-close>
    <ElAlert :title="t('reportingGrants.description')" type="info" :closable="false" />
    <ElForm v-if="manageable && session.can('tenancy.tenants.read')">
    <ElFormItem :label="t('reportingGrants.tenant')" class="mt-4">
      <ElSelect v-model="selected" class="grant-tenant-select" filterable remote :remote-method="searchTenants" :aria-label="t('reportingGrants.tenant')" data-testid="reporting-grant-tenant" style="width: 70%">
        <ElOption v-for="tenant in tenants" :key="tenant.id" :label="tenant.name" :value="tenant.id" />
      </ElSelect>
      <ElButton :disabled="!selected || saving" :loading="saving" data-testid="reporting-grant-enable"
        @click="save(selected, true, items.find(item => item.tenantId === selected)?.version ?? 0)">{{ t('reportingGrants.enable') }}</ElButton>
    </ElFormItem>
    </ElForm>
    <ElTable v-loading="loading" :data="items" row-key="id" class="mt-4">
      <ElTableColumn :label="t('reportingGrants.tenant')" min-width="200"><template #default="{ row }">{{ tenantName(row.tenantId) }}</template></ElTableColumn>
      <ElTableColumn :label="t('reportingExportTasks.fieldStatus')" width="100"><template #default="{ row }"><ElTag :type="row.isEnabled ? 'success' : 'info'">{{ t(row.isEnabled ? 'reportingGrants.enabled' : 'reportingGrants.disabled') }}</ElTag></template></ElTableColumn>
      <ElTableColumn v-if="manageable" :label="t('reportingExportTasks.actions')" width="110"><template #default="{ row }">
        <ElButton link :type="row.isEnabled ? 'danger' : 'primary'" :disabled="saving" data-testid="reporting-grant-toggle"
          @click="save(row.tenantId, !row.isEnabled, row.version)">{{ t(row.isEnabled ? 'reportingGrants.disable' : 'reportingGrants.enable') }}</ElButton>
      </template></ElTableColumn>
    </ElTable>
    <ElPagination v-model:current-page="page" :total="total" :page-size="20" layout="prev,pager,next,total" @current-change="load" />
  </ElDialog>
</template>

<style scoped>
/* 选择器的占位是可见文本，使用正文对比度，不继承低对比度的装饰占位色。 */
.grant-tenant-select :deep(.el-select__placeholder.is-transparent) {
  color: var(--el-text-color-regular);
  /* 默认负层级会落入弹窗背景层，文本保留在选择器内且不拦截输入。 */
  z-index: 0;
  pointer-events: none;
}
</style>
