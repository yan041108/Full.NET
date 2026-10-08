<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElButton, ElDialog, ElInput } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails } from '@fullnet/client-contracts';
import { createEnterpriseRequestsApi, enterpriseRequestsHttp, enterpriseRequestPermissions,
  type EnterpriseRequestLinesResponse } from '../../api/enterprise-requests';
import { useSessionStore } from '../../auth/session';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../../i18n/adminI18n';

const props = defineProps<{ requestId: string }>();
const emit = defineEmits<{ close: []; changed: [] }>();
const session = useSessionStore(); const { t } = useAdminI18n();
const api = createEnterpriseRequestsApi(enterpriseRequestsHttp);
const snapshot = ref<EnterpriseRequestLinesResponse>(); const problem = ref<FullNetProblemDetails>();
const busy = ref(false); const editing = ref(false);
const rows = ref<Array<{ itemDescription: string; quantity: string; unitPrice: string }>>([]);
const canRead = computed(() => session.can(enterpriseRequestPermissions.read));
const canEdit = computed(() => canRead.value && session.can(enterpriseRequestPermissions.update) && snapshot.value?.requestStatus === 'Draft');
const scope = useAuthorizedViewScope(session, reset, load);
function reset(): void { snapshot.value = undefined; problem.value = undefined; busy.value = false; editing.value = false; rows.value = []; }
watch(() => props.requestId, () => { scope.invalidate(); void load(); }, { flush: 'sync' });
async function load(): Promise<void> {
  if (!canRead.value || busy.value || editing.value) return;
  const request = scope.begin(enterpriseRequestPermissions.read); if (!request) return;
  const id = props.requestId; busy.value = true; snapshot.value = undefined; problem.value = undefined;
  try {
    const value = await api.lines(id, request.signal);
    if (request.current() && id === props.requestId) snapshot.value = value;
  } catch (error: unknown) { if (request.current()) showProblem(error); }
  finally { if (request.current()) busy.value = false; request.finish(); }
}
function edit(): void {
  if (!canEdit.value || busy.value || !snapshot.value) return;
  rows.value = snapshot.value.items.map(line => ({ itemDescription: line.itemDescription, quantity: String(line.quantity), unitPrice: String(line.unitPrice) }));
  editing.value = true; problem.value = undefined;
}
function cancelEdit(): void { if (busy.value) return; editing.value = false; rows.value = []; problem.value = undefined; }
function add(): void { if (canEdit.value && editing.value && !busy.value && rows.value.length < 200) rows.value.push({ itemDescription: '', quantity: '1', unitPrice: '0' }); }
function remove(index: number): void { if (canEdit.value && editing.value && !busy.value) rows.value.splice(index, 1); }
async function save(): Promise<void> {
  if (!canEdit.value || !editing.value || busy.value || !snapshot.value) return;
  const request = scope.begin(enterpriseRequestPermissions.update); if (!request) return;
  const id = props.requestId; const version = snapshot.value.requestVersion;
  // 保存时复制输入；请求期间锁定编辑，服务端按主表版本拒绝陈旧快照。
  const items = rows.value.map(line => ({ ...line })); busy.value = true; problem.value = undefined;
  try {
    const value = await api.replaceLines(id, { version, items }, request.signal);
    if (request.current() && id === props.requestId) { snapshot.value = value; editing.value = false; rows.value = []; emit('changed'); }
  } catch (error: unknown) { if (request.current()) showProblem(error); }
  finally { if (request.current()) busy.value = false; request.finish(); }
}
function showProblem(error: unknown): void {
  problem.value = isFullNetProblemDetails(error) ? error : { status: 500, code: 'client.enterprise_request_lines_failed', title: t('enterpriseRequests.linesFailed') };
}
function close(): void { scope.invalidate(); emit('close'); }
</script>

<template>
  <el-dialog :model-value="true" :title="t('enterpriseRequests.linesTitle')" width="min(860px, 96vw)"
    @update:model-value="open => { if (!open) close(); }">
    <div :aria-busy="busy" aria-live="polite">
      <p v-if="!canRead">{{ t('enterpriseRequests.progressAccessDenied') }}</p>
      <div v-if="problem" class="art-inline-alert" role="alert"><strong translate="no">{{ problem.code }}</strong><span>{{ problem.title }}</span></div>
      <p v-if="busy">{{ t('common.loading') }}</p>
      <template v-if="snapshot && canRead">
        <p>{{ t('enterpriseRequests.requestVersion') }}: {{ snapshot.requestVersion }} · {{ t('enterpriseRequests.totalAmount') }}: {{ snapshot.totalAmount }}</p>
        <template v-if="editing">
          <p>{{ t('enterpriseRequests.linesHint') }}</p>
          <fieldset v-for="(line, index) in rows" :key="index" :disabled="busy" class="request-line-edit">
            <legend>{{ t('enterpriseRequests.lineNumber') }} {{ index + 1 }}</legend>
            <label>{{ t('enterpriseRequests.itemDescription') }}<el-input v-model="line.itemDescription" maxlength="200" /></label>
            <label>{{ t('enterpriseRequests.quantity') }}<el-input v-model="line.quantity" inputmode="decimal" /></label>
            <label>{{ t('enterpriseRequests.unitPrice') }}<el-input v-model="line.unitPrice" inputmode="decimal" /></label>
            <el-button :disabled="busy" @click="remove(index)">{{ t('enterpriseRequests.delete') }}</el-button>
          </fieldset>
          <el-button :disabled="busy || rows.length >= 200" @click="add">{{ t('enterpriseRequests.addLine') }}</el-button>
        </template>
        <div v-else class="request-lines-scroll">
          <p v-if="!snapshot.items.length">{{ t('enterpriseRequests.linesEmpty') }}</p>
          <table v-else class="request-lines-table">
            <caption>{{ t('enterpriseRequests.linesTitle') }}</caption>
            <thead><tr><th scope="col">{{ t('enterpriseRequests.lineNumber') }}</th><th scope="col">{{ t('enterpriseRequests.itemDescription') }}</th>
              <th scope="col">{{ t('enterpriseRequests.quantity') }}</th><th scope="col">{{ t('enterpriseRequests.unitPrice') }}</th><th scope="col">{{ t('enterpriseRequests.lineAmount') }}</th></tr></thead>
            <tbody><tr v-for="line in snapshot.items" :key="line.id"><td>{{ line.lineNumber }}</td><td>{{ line.itemDescription }}</td>
              <td>{{ line.quantity }}</td><td>{{ line.unitPrice }}</td><td>{{ line.lineAmount }}</td></tr></tbody>
          </table>
        </div>
      </template>
    </div>
    <template #footer>
      <el-button v-if="editing" :disabled="busy" @click="cancelEdit">{{ t('enterpriseRequests.cancelLineEdit') }}</el-button>
      <el-button v-if="canEdit && editing" type="primary" :loading="busy" @click="save">{{ t('enterpriseRequests.saveLines') }}</el-button>
      <el-button v-else-if="canEdit" :disabled="busy" @click="edit">{{ t('enterpriseRequests.editLines') }}</el-button>
      <el-button v-if="canRead" :disabled="busy || editing" @click="load">{{ t('common.refresh') }}</el-button>
      <el-button @click="close">{{ t('common.cancel') }}</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.request-lines-scroll { overflow-x: auto; }
.request-lines-table { width: 100%; border-collapse: collapse; }
.request-lines-table th, .request-lines-table td { padding: .65rem; text-align: start; border-bottom: 1px solid var(--el-border-color); overflow-wrap: anywhere; }
.request-lines-table caption { text-align: start; font-weight: 600; padding: .5rem 0; }
.request-line-edit { display: grid; grid-template-columns: minmax(0, 2fr) repeat(2, minmax(0, 1fr)) auto; gap: .75rem; margin: 1rem 0; border: 1px solid var(--el-border-color); }
.request-line-edit label { display: grid; gap: .4rem; }
@media (max-width: 600px) { .request-line-edit { grid-template-columns: minmax(0, 1fr); } }
</style>
