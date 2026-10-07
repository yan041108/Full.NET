<script setup lang="ts">
import { computed, ref } from 'vue';
import { ElAlert, ElButton, ElForm, ElFormItem, ElInput, ElOption, ElPagination, ElSelect, ElTable, ElTableColumn } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails, type PrintingTemplate, type PrintingTemplateVersionResponse } from '@fullnet/client-contracts';
import ArtTableActionGroup from '../../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableActionButton from '../../framework/art-design/components/ArtTableActionButton.vue';
import { ART_TABLE_ACTION_COLUMN_WIDTH } from '../../framework/art-design/components/artTableActions';
import ArtFormDialog from '../../framework/art-design/components/ArtFormDialog.vue';
import { useSessionStore } from '../../auth/session';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../../i18n/adminI18n';
import { showProblem, showSuccess, showWarning } from '../../feedback/fullNetMessage';
import { listPrintingTemplateVersions, listPrintingTenantVersionGrants, setPrintingTenantVersionGrant } from '../../api/printing-templates';

const props = defineProps<{ template: Pick<PrintingTemplate, 'id' | 'name' | 'latestPublishedVersionNumber'> }>();
const emit = defineEmits<{ close: [] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const permission = 'printing.templates.grant_tenants';
const versions = ref<PrintingTemplateVersionResponse[]>([]); const selectedVersion = ref<number>();
const tenantId = ref(''); const tenants = ref<string[]>([]); const page = ref(1); const total = ref(0);
const loading = ref(false); const acting = ref(false); const problem = ref<FullNetProblemDetails>();
const canManage = computed(() => host() && session.can('printing.templates.read') && session.can(permission));
const busy = computed(() => loading.value || acting.value);
const confirmation = ref<{ target: string; version: number; resolve: (confirmed: boolean) => void }>();
function finishConfirmation(confirmed: boolean): void {
  const pending = confirmation.value; confirmation.value = undefined; pending?.resolve(confirmed);
}
// 表格行引用只随授权数据变化，不因无关表单或加载状态替换行对象。
const grantRows = computed(() => tenants.value.map(id => ({id})));
const host = () => session.currentUser?.scope === 'host' && session.currentUser.tenantId === null;
const scope = useAuthorizedViewScope(session, () => {
  // 确认框属于本页面，失效时同步清除原租户提示并解除等待。
  finishConfirmation(false);
  versions.value = []; selectedVersion.value = undefined; tenantId.value = ''; tenants.value = []; total.value = 0;
  loading.value = false; acting.value = false; problem.value = undefined; emit('close');
}, loadVersions);
let listRequest: ReturnType<typeof scope.begin>;

function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error) ? error : {status:500,code:'client.unexpected_error',title:t('printingPreview.loadFailed')};
}

async function loadVersions(): Promise<void> {
  if (!host() || !session.can('printing.templates.read')) return;
  const request = scope.begin(permission); if (!request) return;
  loading.value = true; problem.value = undefined;
  try {
    const rows = await listPrintingTemplateVersions(props.template.id, request.signal);
    if (!request.current()) return;
    if (rows.some(row => row.templateId !== props.template.id)) throw new Error('client.invalid_printing_template_version');
    versions.value = rows;
    selectedVersion.value = rows.find(row => row.versionNumber === props.template.latestPublishedVersionNumber)?.versionNumber;
    if (selectedVersion.value === undefined) selectedVersion.value = rows[0]?.versionNumber;
  } catch (error) { if (request.current()) problem.value = toProblem(error); }
  finally { if (request.current()) loading.value = false; request.finish(); }
  if (request.current() && selectedVersion.value !== undefined) await loadGrants(1);
}

async function loadGrants(nextPage: number): Promise<void> {
  if (!host() || selectedVersion.value === undefined) return;
  listRequest?.cancel(); const request = scope.begin(permission); if (!request) return;
  listRequest = request; const version = selectedVersion.value;
  loading.value = true; problem.value = undefined; tenants.value = []; total.value = 0; page.value = nextPage;
  try {
    const result = await listPrintingTenantVersionGrants(props.template.id, version, nextPage, 20, request.signal);
    if (!request.current() || version !== selectedVersion.value) return;
    tenants.value = result.items; total.value = result.total;
  } catch (error) { if (request.current()) problem.value = toProblem(error); }
  finally { if (request.current()) loading.value = false; request.finish(); }
}

async function changeGrant(target: string, grant: boolean): Promise<void> {
  if (busy.value || !host() || selectedVersion.value === undefined) return;
  target = target.trim().toLowerCase();
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/u.test(target)
    || target === '00000000-0000-0000-0000-000000000000') { showWarning(t('printingGrants.invalidTenant')); return; }
  const request = scope.begin(permission); if (!request) return;
  const version = selectedVersion.value; acting.value = true;
  try {
    if (!grant) {
      const confirmed = await new Promise<boolean>(resolve => { confirmation.value = { target, version, resolve }; });
      if (!confirmed) return;
    }
    // 确认框或网络返回之后重验当前授权代次，不把旧版本结果接入新页面。
    if (!request.current() || version !== selectedVersion.value) return;
    await setPrintingTenantVersionGrant(props.template.id, version, target, grant, request.signal);
    if (!request.current() || version !== selectedVersion.value) return;
    if (grant) tenantId.value = '';
    showSuccess(t(grant ? 'printingGrants.granted' : 'printingGrants.revoked'));
    await loadGrants(1);
  } catch (error) { if (request.current()) showProblem(toProblem(error), t('printingPreview.loadFailed')); }
  finally { if (request.current()) acting.value = false; request.finish(); }
}
</script>

<template>
  <ArtFormDialog v-if="canManage" :open="true" :title="t('printingGrants.title', {name:template.name})" :show-confirm="false"
    :cancel-label="t('printingGrants.close')" @update:open="value => { if (!value) scope.invalidate(); }">
    <ElAlert :title="t('printingGrants.versionHint')" type="info" :closable="false" />
    <ElAlert v-if="problem" :title="problem.title" type="error" :closable="false" />
    <ElForm label-width="120px">
      <ElFormItem :label="t('printingGrants.version')" required>
        <ElSelect v-model="selectedVersion" :disabled="busy" @change="() => loadGrants(1)">
          <ElOption v-for="version in versions" :key="version.id" :label="String(version.versionNumber)" :value="version.versionNumber" />
        </ElSelect>
      </ElFormItem>
      <ElFormItem :label="t('printingGrants.tenant')" required>
        <ElInput v-model="tenantId" :disabled="busy" :placeholder="t('printingGrants.tenantHint')" data-testid="printing-grant-tenant" />
      </ElFormItem>
      <ElButton :disabled="busy || selectedVersion === undefined" data-testid="printing-grant-save" @click="changeGrant(tenantId,true)">{{ t('printingGrants.grant') }}</ElButton>
      <ElButton :disabled="busy" data-testid="printing-grant-refresh" @click="loadGrants(page)">{{ t('printingGrants.refresh') }}</ElButton>
    </ElForm>
    <ElTable v-loading="loading" :data="grantRows" max-height="320">
      <ElTableColumn prop="id" :label="t('printingGrants.tenant')" min-width="280" />
      <ElTableColumn :label="t('printingGrants.actions')" :width="ART_TABLE_ACTION_COLUMN_WIDTH">
        <template #default="{row}"><ArtTableActionGroup><ArtTableActionButton type="delete" :disabled="busy" test-id="printing-grant-revoke" :title="t('printingGrants.revoke')" @click="changeGrant(row.id,false)" /></ArtTableActionGroup></template>
      </ElTableColumn>
    </ElTable>
    <ElPagination :current-page="page" :page-size="20" :total="total" :disabled="acting" layout="prev, pager, next" @current-change="loadGrants" />
  </ArtFormDialog>
  <ArtFormDialog v-if="confirmation && canManage" :open="true" :title="t('printingGrants.revoke')"
    :confirm-label="t('printingGrants.revoke')" confirm-test-id="printing-grant-confirm-revoke"
    @confirm="finishConfirmation(true)" @update:open="value => { if (!value) finishConfirmation(false); }">
    <p>{{ t('printingGrants.confirmRevoke', {tenant:confirmation.target,version:String(confirmation.version)}) }}</p>
  </ArtFormDialog>
</template>
