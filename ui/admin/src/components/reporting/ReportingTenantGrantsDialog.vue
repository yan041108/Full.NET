<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, onMounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElForm, ElFormItem, ElInput, ElOption, ElPagination, ElSelect, ElTable, ElTableColumn } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails, type ReportingDefinition, type ReportingDefinitionVersion } from '@fullnet/client-contracts';
import ArtTableActionGroup from '../../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableActionButton from '../../framework/art-design/components/ArtTableActionButton.vue';
import { ART_TABLE_ACTION_COLUMN_WIDTH } from '../../framework/art-design/components/artTableActions';
import ArtFormDialog from '../../framework/art-design/components/ArtFormDialog.vue';
import { useSessionStore } from '../../auth/session';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../../i18n/adminI18n';
import { showProblem, showSuccess, showWarning } from '../../feedback/fullNetMessage';
import { listReportingDefinitionVersions, listReportingTenantVersionGrants, setReportingTenantVersionGrant } from '../../api/reporting-definitions';

const props = defineProps<{ definition: Pick<ReportingDefinition, 'id' | 'name' | 'latestPublishedVersionNumber'> }>();
const emit = defineEmits<{ close: [] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const permission = 'reporting.definitions.grant_tenants';
const definitionId = props.definition.id;
const active = ref(false), closed = ref(false);
onMounted(() => { active.value = true; }); onActivated(() => { active.value = true; });
onDeactivated(() => { active.value = false; }); onBeforeUnmount(() => { active.value = false; });
const versions = ref<ReportingDefinitionVersion[]>([]); const selectedVersion = ref<number>();
const tenantId = ref(''); const tenants = ref<string[]>([]); const page = ref(1); const total = ref(0);
const loading = ref(false); const acting = ref(false); const problem = ref<FullNetProblemDetails>();
const canManage = computed(() => active.value && !closed.value && host() && session.can('reporting.definitions.read') && session.can(permission));
const busy = computed(() => loading.value || acting.value);
let confirmationSequence = 0;
const confirmation = ref<{ token: number; target: string; version: number; resolve: (confirmed: boolean) => void; accept: () => void; update: (open: boolean) => void }>();
function finishConfirmation(confirmed: boolean, token = confirmation.value?.token): void {
  if (!confirmation.value || confirmation.value.token !== token) return;
  const pending = confirmation.value; confirmation.value = undefined; pending?.resolve(confirmed);
}
const receipt = ref<{ target: string; version: number; grant: boolean; outcome: 'processing' | 'succeeded' | 'failed' | 'unconfirmed'; errorCode: string | null }>();
const unconfirmed = ref(new Set<string>());
const grantKey = (version: number, tenant: string) => version + ':' + tenant.toLowerCase();
const validVersion = () => selectedVersion.value !== undefined && versions.value.some(row => row.versionNumber === selectedVersion.value);
let controlSequence = 0;
interface Controls { token: number; selectVersion: (value: number) => void; input: (value: string) => void; grant: () => void; refresh: () => void; reloadVersions: () => void; page: (value: number) => void; close: (open: boolean) => void }
const controls = ref<Controls>(makeControls());
function makeControls(): Controls {
  const token = ++controlSequence, owned = () => controls.value.token === token && canManage.value;
  return { token, selectVersion: value => { if (owned() && !busy.value) { selectedVersion.value = value; void loadGrants(1); } },
    input: value => { if (owned() && !busy.value) tenantId.value = value; }, grant: () => { if (owned()) void changeGrant(tenantId.value, true); },
    refresh: () => { if (owned() && !busy.value) void loadGrants(page.value); }, reloadVersions: () => { if (owned() && !busy.value) void loadVersions(); },
    page: value => { if (owned() && !acting.value && lastGrantKey !== selectedVersion.value + ':' + value) void loadGrants(value); }, close: open => { if (!open && owned()) scope.invalidate(); } };
}
// 行绑定当前版本与查询代次，旧行点击不能迁移为另一版本的撤销。
const grantRows = computed(() => {
  const token = controls.value.token, version = selectedVersion.value;
  return tenants.value.map(id => ({ id, revoke: () => { if (controls.value.token === token && selectedVersion.value === version) void changeGrant(id, false); } }));
});
const host = () => session.currentUser?.scope === 'host' && session.currentUser.tenantId === null;
const scope = useAuthorizedViewScope(session, () => {
  // 确认框属于本页面，失效时同步清除原租户提示并解除等待。
  finishConfirmation(false);
  const wasClosed = closed.value; closed.value = true;
  versions.value = []; selectedVersion.value = undefined; tenantId.value = ''; tenants.value = []; total.value = 0;
  loading.value = false; acting.value = false; problem.value = undefined; receipt.value = undefined; unconfirmed.value = new Set();
  controls.value = makeControls(); if (!wasClosed) emit('close');
}, loadVersions);
// 授权对象变化必须关闭旧弹窗，不能把旧版本和待确认操作迁移到另一个报表定义。
watch(() => props.definition.id.toLowerCase(), () => scope.invalidate(), { flush: 'sync' });
let listRequest: ReturnType<typeof scope.begin>;
let versionsRequest: ReturnType<typeof scope.begin>, actionRequest: ReturnType<typeof scope.begin>;
let lastGrantKey: string | undefined;
let mutation: { version: number; target: string; dispatched: boolean } | undefined;
function markUnconfirmed(version: number, target: string): void {
  listRequest?.cancel(); loading.value = false; unconfirmed.value = new Set([...unconfirmed.value, grantKey(version, target)]);
  if (receipt.value?.version === version && receipt.value.target === target) receipt.value.outcome = 'unconfirmed';
}
watch(selectedVersion, () => {
  if (actionRequest?.current() && mutation?.dispatched) markUnconfirmed(mutation.version, mutation.target);
  finishConfirmation(false); actionRequest?.cancel(); actionRequest = undefined; mutation = undefined; acting.value = false;
  listRequest?.cancel(); listRequest = undefined; loading.value = false; tenants.value = []; total.value = 0; page.value = 1; lastGrantKey = undefined; controls.value = makeControls();
}, { flush: 'sync' });

function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error) ? error : {status:500,code:'client.unexpected_error',title:t('reportingDefinitions.loadFailed')};
}

async function loadVersions(): Promise<void> {
  if (!canManage.value) return;
  versionsRequest?.cancel();
  const request = scope.begin(permission); if (!request) return;
  versionsRequest = request;
  let loaded = false;
  loading.value = true; problem.value = undefined;
  try {
    const rows = await listReportingDefinitionVersions(definitionId, request.signal);
    if (!request.current()) return;
    if (rows.some(row => row.definitionId.toLowerCase() !== definitionId.toLowerCase() || !Number.isSafeInteger(row.versionNumber) || row.versionNumber < 1) || new Set(rows.map(row => row.versionNumber)).size !== rows.length) throw new Error('client.invalid_reporting_definition_version');
    versions.value = rows;
    selectedVersion.value = rows.find(row => row.versionNumber === selectedVersion.value)?.versionNumber
      ?? rows.find(row => row.versionNumber === props.definition.latestPublishedVersionNumber)?.versionNumber ?? rows[0]?.versionNumber;
    loaded = true;
  } catch (error) { if (request.current()) problem.value = toProblem(error); }
  finally { if (request.current()) loading.value = false; request.finish(); }
  if (loaded && request.current() && selectedVersion.value !== undefined) await loadGrants(1);
}

async function loadGrants(nextPage: number, recoverPage = true): Promise<void> {
  if (!canManage.value || !validVersion() || selectedVersion.value === undefined || !Number.isSafeInteger(nextPage) || nextPage < 1) return;
  listRequest?.cancel(); const request = scope.begin(permission); if (!request) return;
  listRequest = request; const version = selectedVersion.value;
  const key = version + ':' + nextPage;
  if (lastGrantKey !== key) { tenants.value = []; total.value = 0; controls.value = makeControls(); lastGrantKey = key; }
  loading.value = true; problem.value = undefined; page.value = nextPage;
  try {
    const result = await listReportingTenantVersionGrants(definitionId, version, nextPage, 20, request.signal);
    if (!request.current() || version !== selectedVersion.value) return;
    if (result.page !== nextPage || result.pageSize !== 20) throw new Error('client.invalid_reporting_tenant_grants');
    const lastPage = Math.max(1, Math.ceil(result.total / 20));
    if (recoverPage && !result.items.length && nextPage > lastPage) { await loadGrants(lastPage, false); return; }
    unconfirmed.value = new Set([...unconfirmed.value].filter(key => !key.startsWith(version + ':')));
    tenants.value = result.items; total.value = result.total;
  } catch (error) { if (request.current()) problem.value = toProblem(error); }
  finally { if (request.current()) loading.value = false; request.finish(); }
}

async function changeGrant(target: string, grant: boolean): Promise<void> {
  if (busy.value || !canManage.value || !validVersion() || selectedVersion.value === undefined) return;
  target = target.trim().toLowerCase();
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/u.test(target)
    || target === '00000000-0000-0000-0000-000000000000') { showWarning(t('reportingGrants.invalidTenant')); return; }
  if (unconfirmed.value.has(grantKey(selectedVersion.value, target)) || !grant && !tenants.value.some(id => id.toLowerCase() === target)) return;
  const request = scope.begin(permission); if (!request) return;
  const version = selectedVersion.value; acting.value = true; actionRequest = request; mutation = { version, target, dispatched: false };
  try {
    if (!grant) {
      const confirmed = await new Promise<boolean>(resolve => {
        const token = ++confirmationSequence;
        confirmation.value = { token, target, version, resolve, accept: () => finishConfirmation(true, token), update: open => { if (!open) finishConfirmation(false, token); } };
      });
      if (!confirmed) return;
    }
    // 确认框或网络返回之后重验当前授权代次，不把旧版本结果接入新页面。
    if (!request.current() || version !== selectedVersion.value) return;
    if (!grant && !tenants.value.some(id => id.toLowerCase() === target)) return;
    listRequest?.cancel(); loading.value = false; mutation!.dispatched = true;
    receipt.value = { target, version, grant, outcome: 'processing', errorCode: null };
    const result = await setReportingTenantVersionGrant(definitionId, version, target, grant, request.signal);
    if (!request.current() || version !== selectedVersion.value) return;
    if (result !== true) throw new Error('client.invalid_reporting_tenant_grant');
    mutation!.dispatched = false; listRequest?.cancel(); loading.value = false;
    receipt.value = { target, version, grant, outcome: 'succeeded', errorCode: null };
    if (!grant) { const before = tenants.value.length; tenants.value = tenants.value.filter(id => id.toLowerCase() !== target); total.value = Math.max(0, total.value - before + tenants.value.length); }
    if (grant && tenantId.value.trim().toLowerCase() === target) tenantId.value = '';
    showSuccess(t(grant ? 'reportingGrants.granted' : 'reportingGrants.revoked'));
    await loadGrants(grant ? 1 : page.value);
  } catch (error) { if (request.current()) {
    const failure = toProblem(error);
    // 只有明确的客户端拒绝可以直接重试；协议异常和超时仍可能已经提交。
    const unknown = !isFullNetProblemDetails(error) || failure.status < 400 || failure.status >= 500 || failure.status === 408;
    receipt.value = { target, version, grant, outcome: unknown ? 'unconfirmed' : 'failed', errorCode: failure.code };
    if (unknown) markUnconfirmed(version, target);
    showProblem(failure, t('reportingDefinitions.loadFailed'));
  } }
  finally { if (actionRequest === request) { if (request.current()) acting.value = false; actionRequest = undefined; mutation = undefined; } request.finish(); }
}
</script>

<template>
  <ArtFormDialog v-if="canManage" :open="true" :title="t('reportingGrants.title', {name:definition.name})" :show-confirm="false"
    :cancel-label="t('reportingGrants.close')" v-on="{ 'update:open': controls.close }">
    <ElAlert :title="t('reportingGrants.versionHint')" type="info" :closable="false" />
    <ElAlert v-if="problem" :title="problem.title" type="error" :closable="false" />
    <div v-if="receipt" role="status" data-testid="reporting-grant-receipt">
      <p>{{ t('reportingGrants.receiptTarget', {tenant:receipt.target,version:String(receipt.version)}) }} · {{ t(receipt.grant ? 'reportingGrants.grant' : 'reportingGrants.revoke') }}</p>
      <p>{{ t(receipt.outcome === 'succeeded' ? 'reportingGrants.resultSucceeded' : receipt.outcome === 'failed' ? 'reportingGrants.resultFailed' : receipt.outcome === 'processing' ? 'reportingGrants.resultProcessing' : 'reportingGrants.resultUnconfirmed') }} <code v-if="receipt.errorCode" translate="no">{{ receipt.errorCode }}</code></p>
      <p>{{ t('reportingGrants.recoveryHint') }}</p>
    </div>
    <ElButton :disabled="busy" data-testid="reporting-grant-versions-refresh" v-on="{ click: controls.reloadVersions }">{{ t('reportingGrants.refreshVersions') }}</ElButton>
    <ElForm label-width="120px">
      <ElFormItem :label="t('reportingGrants.version')" required>
        <ElSelect :model-value="selectedVersion" :disabled="busy" v-on="{ 'update:modelValue': controls.selectVersion }">
          <ElOption v-for="version in versions" :key="version.id" :label="String(version.versionNumber)" :value="version.versionNumber" />
        </ElSelect>
      </ElFormItem>
      <ElFormItem :label="t('reportingGrants.tenant')" required>
        <ElInput :model-value="tenantId" :disabled="busy" :placeholder="t('reportingGrants.tenantHint')" data-testid="reporting-grant-tenant" v-on="{ 'update:modelValue': controls.input }" />
      </ElFormItem>
      <ElButton :disabled="busy || !validVersion() || selectedVersion !== undefined && unconfirmed.has(grantKey(selectedVersion,tenantId.trim()))" data-testid="reporting-grant-save" v-on="{ click: controls.grant }">{{ t('reportingGrants.grant') }}</ElButton>
      <ElButton :disabled="busy || !validVersion()" data-testid="reporting-grant-refresh" v-on="{ click: controls.refresh }">{{ t('reportingGrants.refresh') }}</ElButton>
    </ElForm>
    <ElTable v-loading="loading" :data="grantRows" class="art-crud-data-table" max-height="320">
      <ElTableColumn prop="id" :label="t('reportingGrants.tenant')" min-width="280" />
      <ElTableColumn :label="t('reportingGrants.actions')" :width="ART_TABLE_ACTION_COLUMN_WIDTH">
        <template #default="{row}"><ArtTableActionGroup><ArtTableActionButton type="delete" :disabled="busy || selectedVersion !== undefined && unconfirmed.has(grantKey(selectedVersion,row.id))" test-id="reporting-grant-revoke" :title="t('reportingGrants.revoke')" v-on="{ click: row.revoke }" /></ArtTableActionGroup></template>
      </ElTableColumn>
    </ElTable>
    <ElPagination :current-page="page" :page-size="20" :total="total" :disabled="acting || !validVersion()" layout="prev, pager, next" v-on="{ 'current-change': controls.page }" />
  </ArtFormDialog>
  <ArtFormDialog v-if="confirmation && canManage" :key="confirmation.token" :open="true" :title="t('reportingGrants.revoke')"
    :confirm-label="t('reportingGrants.revoke')" confirm-test-id="reporting-grant-confirm-revoke"
    v-on="{ confirm: confirmation.accept, 'update:open': confirmation.update }">
    <p>{{ t('reportingGrants.confirmRevoke', {tenant:confirmation.target,version:String(confirmation.version)}) }}</p>
  </ArtFormDialog>
</template>
