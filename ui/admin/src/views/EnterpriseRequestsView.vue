<script setup lang="ts">
defineOptions({ name: 'EnterpriseRequestsView' });
import { computed, onActivated, onBeforeUnmount, onDeactivated, onMounted, reactive, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  ElButton,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElPagination,
  ElPopconfirm,
  ElSwitch,
  ElTable,
  ElTableColumn
} from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type SavedQueryConfiguration
} from '@fullnet/client-contracts';
import { enterpriseRequestsHttp } from '../api/enterprise-requests';
import EnterpriseRequestApprovalProgressDialog from './enterprise-requests/EnterpriseRequestApprovalProgressDialog.vue';
import EnterpriseRequestDelivery from '../components/EnterpriseRequestDelivery.vue';
import EnterpriseRequestAggregateEditor from '../components/EnterpriseRequestAggregateEditor.vue';
import EnterpriseRequestReadDetail from '../components/EnterpriseRequestReadDetail.vue';
import QuerySchemePanel from '../components/QuerySchemePanel.vue';
import { defaultEnterpriseRequestQuery, enterpriseRequestQueryHttp } from './enterprise-requests/enterprise-request-query';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { enterpriseEditorText } from '../i18n/enterprise-request-editor';
import { enterpriseDeliveryText } from '../i18n/enterprise-request-delivery';
import { isPrintingRecordId } from '../printing/record-print-link';
import { useSessionStore } from '../auth/session';
import { useEnterpriseRequestPage } from './enterprise-requests/enterprise-requests-page.generated';
import type { EnterpriseRequestResponse } from './enterprise-requests/enterprise-requests.generated';

const session = useSessionStore();
const { locale, t } = useAdminI18n();
const editorText = (key: Parameters<typeof enterpriseEditorText>[1]) => enterpriseEditorText(locale.value, key);
const pageActive = ref(true), query = ref(defaultEnterpriseRequestQuery());
let pendingQuery: SavedQueryConfiguration | undefined;
const queryEnabled = computed(() => pageActive.value && !session.switching && !!session.currentUser?.tenantId
  && session.can('enterprise_request.enterprise_requests.read') && !!session.currentUser?.permissions.includes('settings.query_schemes.read'));
const queryColumns = computed(() => query.value.columns.filter(column => column.visible).sort((left, right) => left.order - right.order));
const queryFieldLabel = (key: string) => (({ id: 'ID', requestNumber: editorText('number'), title: editorText('subject'),
  status: editorText('status'), totalAmount: editorText('total'), applicantUserId: editorText('applicant') } as Record<string, string>)[key] ?? key);
const aggregateOpen = ref(false), aggregateId = ref<string>();
const readDetailOpen = ref(false), readDetailId = ref<string>();
const deliveryOpen = ref(false), deliveryId = ref<string>();
const approvalProgressId = ref<string>();
const canUseDelivery = () => !session.switching && session.can('enterprise_request.enterprise_requests.read') &&
  ['attachments.read', 'attachments.manage', 'reports.read', 'documents.print'].some(key => session.can(`enterprise_request.${key}`));
const route = useRoute(), router = useRouter();
const printEpoch = ref(0);
const canPrintSummary = computed(() => pageActive.value && !session.switching && !!session.currentUser?.tenantId
  && session.can('enterprise_request.enterprise_requests.read') && session.can('printing.published_templates.read')
  && session.can('printing.published_templates.preview'));
watch(() => [pageActive.value, route.name, session.currentUser?.id, session.currentUser?.tenantId,
  session.currentUser?.sessionId, session.currentUser?.permissions.join('|'), session.switching], () => { printEpoch.value++; }, { flush: 'sync' });
/** 按渲染时的会话和列表身份冻结回调；旧按钮不能在撤权后或另一企业重新生效。 */
function printSummary(row: EnterpriseRequestResponse) {
  const epoch = printEpoch.value, rows = items.value, id = row.id;
  return () => {
    if (epoch !== printEpoch.value || !canPrintSummary.value || route.name !== 'enterprise-requests' || changing.value
      || rows !== items.value || !items.value.includes(row) || row.id !== id || !isPrintingRecordId(id)) return;
    void router.push({ name: 'printing-published-templates', query: { formSchemaKey: 'enterprise_request.request_summary', recordId: id } });
  };
}
const problem = ref<FullNetProblemDetails>();
const createOpen = ref(false);
const editOpen = ref(false);
const editing = ref<EnterpriseRequestResponse>();
const createForm = reactive({
  organizationUnitId: '',
  requestNumber: '',
  title: '',
  status: 'Draft',
  totalAmount: '0',
  applicantUserId: ''
});
const editForm = reactive({
  requestNumber: '',
  title: '',
  status: '',
  totalAmount: '0',
  applicantUserId: ''
});
type EditorMutation = {epoch:number;context:string;requestNumber:string;recordId?:string;kind:'create'|'update'|'submit'|'delete'};
const editorEpoch = ref(0), editorBlocked = reactive(new Map<string,EditorMutation>());
const editorContext = () => JSON.stringify([session.currentUser?.id, session.currentUser?.sessionId, session.currentUser?.tenantId]);
const editorUnconfirmed = computed(() => editorBlocked.has(editorContext()));
const blockedOperation=computed(()=>editorBlocked.get(editorContext()));
const blockedOperationLabel=computed(()=>blockedOperation.value?t(({create:'enterpriseRequests.create',update:'enterpriseRequests.edit',submit:'enterpriseRequests.submit',delete:'enterpriseRequests.delete'} as const)[blockedOperation.value.kind]):'');
let editorMutation: EditorMutation | undefined;
const commandConfirmation=ref<{kind:'submit'|'delete';row:EnterpriseRequestResponse;epoch:number;scope:number;context:string}>();
function clearEditorInputs() {
  Object.assign(createForm, {organizationUnitId:'',requestNumber:'',title:'',status:'Draft',totalAmount:'0',applicantUserId:''});
  Object.assign(editForm, {requestNumber:'',title:'',status:'',totalAmount:'0',applicantUserId:''});
  editing.value = undefined;
}
/** 取消网络等待不证明写入回滚；旧操作未确认时，新写入需人工核对后明确解锁。 */
function stopEditor() {
  editorEpoch.value++;
  if (editorMutation && changing.value) { editorBlocked.set(editorMutation.context,editorMutation); reset(); }
  editorMutation = undefined; clearEditorInputs();
}
const editorRecovery = computed(() => {
  const epoch=editorEpoch.value,context=editorContext();
  return () => {if(epoch===editorEpoch.value&&context===editorContext()&&!changing.value&&!loading.value&&!session.switching&&pageActive.value
    &&session.can('enterprise_request.enterprise_requests.read')&&(canCreate.value||canUpdate.value||canSubmit.value||canDisable.value)){editorBlocked.delete(context);problem.value=undefined;}};
});
const editorControls = computed(() => {
  const epoch=editorEpoch.value,context=editorContext(),owned=()=>epoch===editorEpoch.value&&context===editorContext()&&pageActive.value&&!session.switching;
  return {create:()=>{if(owned())void submitCreate();},edit:()=>{if(owned())void submitEdit();},
    createClose:()=>{if(owned())createOpen.value=false;},editClose:()=>{if(owned())editOpen.value=false;},
    createUpdate:(value:boolean)=>{if(owned())createOpen.value=value;},editUpdate:(value:boolean)=>{if(owned())editOpen.value=value;}};
});

const {
  items,
  page,
  pageSize,
  total,
  loading,
  canCreate,
  canUpdate,
  canDisable,
  canSubmit,
  changing,
  reset,
  load,
  create,
  update,
  remove,
  submitForApproval
} = useEnterpriseRequestPage({
  request: enterpriseRequestQueryHttp(enterpriseRequestsHttp, () => query.value),
  hasPermission: permission => session.can(permission),
  onProblem: (error, fallbackCode) => {
    if (editorMutation && (!isFullNetProblemDetails(error) || error.status >= 500 || error.status === 408)) editorBlocked.set(editorMutation.context,editorMutation);
    problem.value = isFullNetProblemDetails(error)
      ? error
      : { status: 500, code: fallbackCode, title: fallbackCode };
  }
});

onMounted(() => {
  if (!queryEnabled.value) void load();
});
watch(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId,
  session.currentUser?.permissions.join('|'), session.switching], () => {
  stopEditor();
  commandConfirmation.value=undefined;
  reset(); problem.value = undefined; createOpen.value = false; editOpen.value = false; editing.value = undefined;
  query.value = defaultEnterpriseRequestQuery();
  pendingQuery = undefined;
  aggregateOpen.value = false; aggregateId.value = undefined;
  readDetailOpen.value = false; readDetailId.value = undefined;
  deliveryOpen.value = false; deliveryId.value = undefined;
  if (pageActive.value && !session.switching && !queryEnabled.value) void load();
}, { flush: 'sync' });
onBeforeUnmount(() => {stopEditor();reset();});
watch([createOpen,editOpen], ([created,edited], [wasCreated,wasEdited]) => {
  if (created!==wasCreated || edited!==wasEdited) editorEpoch.value++;
  if ((!created&&wasCreated)||(!edited&&wasEdited)) stopEditor();
},{flush:'sync'});
watch(commandConfirmation,(current,previous)=>{if(previous&&!current)stopEditor();},{flush:'sync'});

/** 查询切换只取消读取；编辑或业务写入未结束时不重置其结果。 */
function applyQuery(configuration: SavedQueryConfiguration) {
  if (!pageActive.value || session.switching) return;
  // 深链详情打开期间只保留读取到的默认条件；关闭后再应用，避免面板与实际列表条件不一致。
  if (changing.value || aggregateOpen.value || readDetailOpen.value || deliveryOpen.value || createOpen.value || editOpen.value || commandConfirmation.value) { pendingQuery = configuration; return; }
  pendingQuery = undefined;
  reset(); query.value = configuration; problem.value = undefined; void load(1);
}
function queryReady() { if (!pendingQuery && pageActive.value && !session.switching && !loading.value && !items.value.length) void load(1); }
watch(() => [changing.value, aggregateOpen.value, readDetailOpen.value, deliveryOpen.value, createOpen.value, editOpen.value,!!commandConfirmation.value], value => {
  if (pendingQuery && value.every(busy => !busy)) applyQuery(pendingQuery);
});

/** 业务待办只携带 UUID；实际内容仍由当前租户及精确读取权限约束的业务 API 返回。 */
function openRequestedDocument() {
  if (route.name !== 'enterprise-requests' || session.switching || !session.can('enterprise_request.enterprise_requests.read')) {
    approvalProgressId.value = undefined; aggregateOpen.value = false; readDetailOpen.value = false; return;
  }
  const id = route.query.requestId;
  if (typeof id !== 'string' || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)
    || id === '00000000-0000-0000-0000-000000000000') { aggregateOpen.value = false; readDetailOpen.value = false; return; }
  if (session.can('enterprise_request.enterprise_request_lines.read')) { readDetailOpen.value = false; aggregateId.value = id; aggregateOpen.value = true; }
  else { aggregateOpen.value = false; readDetailId.value = id; readDetailOpen.value = true; }
}
watch(() => [route.name, route.query.requestId], () => {
  if (route.name !== 'enterprise-requests') { aggregateOpen.value = false; readDetailOpen.value = false; }
  else openRequestedDocument();
}, { immediate: true });
onActivated(() => { pageActive.value = true; openRequestedDocument(); if (!queryEnabled.value && !loading.value && !items.value.length) void load(); });
onDeactivated(() => { pageActive.value = false; pendingQuery = undefined; stopEditor(); reset(); commandConfirmation.value=undefined; aggregateOpen.value = false; readDetailOpen.value = false; deliveryOpen.value = false; createOpen.value = false; editOpen.value = false; });
watch(() => aggregateOpen.value || readDetailOpen.value, (value, previous) => {
  if (!value && previous && route.name === 'enterprise-requests' && route.query.requestId !== undefined) {
    const { requestId: _requestId, ...query } = route.query;
    void router.replace({ query });
  }
});

function openCreate(): void {
  if(!pageActive.value||session.switching||!canCreate.value||changing.value)return;
  clearEditorInputs();
  createOpen.value = true;
}

function openEdit(row: EnterpriseRequestResponse): void {
  if(!pageActive.value||session.switching||!canUpdate.value||changing.value||!items.value.includes(row)||row.status!=='Draft')return;
  editing.value = row;
  Object.assign(editForm, {
    requestNumber: row.requestNumber, title: row.title, status: row.status,
    totalAmount: row.totalAmount, applicantUserId: row.applicantUserId
  });
  editOpen.value = true;
}

async function submitCreate(): Promise<void> {
  if(!createOpen.value||editorUnconfirmed.value||changing.value||session.switching||!pageActive.value||!canCreate.value)return;
  const mutation:EditorMutation={epoch:editorEpoch.value,context:editorContext(),requestNumber:createForm.requestNumber.slice(0,128),kind:'create'};editorMutation=mutation;
  try {const succeeded=await create({...createForm,status:'Draft'});if(succeeded&&mutation.epoch===editorEpoch.value&&mutation.context===editorContext())createOpen.value=false;}
  finally {if(editorMutation===mutation)editorMutation=undefined;}
}

async function submitEdit(): Promise<void> {
  if (!editing.value||!editOpen.value||editorUnconfirmed.value||changing.value||session.switching||!pageActive.value||!canUpdate.value) {
    return;
  }
  const mutation:EditorMutation={epoch:editorEpoch.value,context:editorContext(),requestNumber:editForm.requestNumber.slice(0,128),recordId:editing.value.id,kind:'update'};editorMutation=mutation;
  try {const succeeded=await update(editing.value,{...editForm});if(succeeded&&mutation.epoch===editorEpoch.value&&mutation.context===editorContext())editOpen.value=false;}
  finally {if(editorMutation===mutation)editorMutation=undefined;}
}

/** 确认只接受本次列表中仍为原修订的草稿；URL、旧按钮和客户端状态不能代替业务权威判断。 */
function confirmCommand(row:EnterpriseRequestResponse,kind:'submit'|'delete') {
  const scope=printEpoch.value,rows=items.value,id=row.id,version=row.version;
  return ()=>{
    if(scope!==printEpoch.value||route.name!=='enterprise-requests'||!pageActive.value||session.switching||changing.value
      ||createOpen.value||editOpen.value||commandConfirmation.value||rows!==items.value||!items.value.includes(row)||row.id!==id||row.version!==version||row.status!=='Draft'
      ||!session.can('enterprise_request.enterprise_requests.read')||!(kind==='submit'?canSubmit.value:canDisable.value))return;
    editorEpoch.value++;commandConfirmation.value={kind,row:{...row},epoch:editorEpoch.value,scope,context:editorContext()};
  };
}
const commandControls=computed(()=>{
  const target=commandConfirmation.value;
  const owned=()=>!!target&&commandConfirmation.value===target&&target.epoch===editorEpoch.value&&target.scope===printEpoch.value
    &&target.context===editorContext()&&pageActive.value&&!session.switching&&route.name==='enterprise-requests'
    &&session.can('enterprise_request.enterprise_requests.read')&&(target.kind==='submit'?canSubmit.value:canDisable.value);
  const close=()=>{if(owned())commandConfirmation.value=undefined;};
  const original=()=>!!target&&items.value.some(row=>row.id===target.row.id&&row.version===target.row.version&&row.status==='Draft');
  return {close,available:owned()&&original(),update:(value:boolean)=>{if(!value)close();},confirm:async()=>{
    if(!owned()||!target||changing.value||editorUnconfirmed.value||!original())return;
    const mutation:EditorMutation={epoch:target.epoch,context:target.context,requestNumber:target.row.requestNumber.slice(0,128),recordId:target.row.id,kind:target.kind};editorMutation=mutation;
    try{const succeeded=await(target.kind==='submit'?submitForApproval(target.row):remove(target.row));if(succeeded&&owned())commandConfirmation.value=undefined;}
    finally{if(editorMutation===mutation)editorMutation=undefined;}
  }};
});
</script>

<template>
  <section class="generated-crud-view">
    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{problem.code}}</strong>
      <span>{{problem.title}}</span>
    </div>
    <section v-if="editorUnconfirmed" role="status" data-testid="enterprise-editor-unconfirmed">
      <p>{{ editorText('writeUnconfirmed') }}</p>
      <strong>{{blockedOperationLabel}}</strong> <code>{{ blockedOperation?.requestNumber }}</code>
      <code v-if="blockedOperation?.recordId">{{ blockedOperation.recordId }}</code>
      <el-button :disabled="loading||changing" @click="load()">{{ editorText('reloadRecords') }}</el-button>
      <ElPopconfirm :key="editorEpoch" :title="editorText('confirmChecked')" :width="280" v-on="{confirm:editorRecovery}">
        <template #reference><el-button :disabled="loading||changing" data-testid="enterprise-editor-recover">{{ editorText('checkedResults') }}</el-button></template>
      </ElPopconfirm>
    </section>
    <div class="generated-crud-view__toolbar">
      <el-button v-if="canCreate" @click="aggregateId = undefined; aggregateOpen = true">{{ editorText('create') }}</el-button>
      <el-button
        v-if="canCreate"
        type="primary"
        @click="openCreate"
      >
        创建
      </el-button>
    </div>
    <QuerySchemePanel v-if="queryEnabled" grid-key="enterprise_request.enterprise_requests"
      business-permission="enterprise_request.enterprise_requests.read" :active="pageActive"
      :disabled="changing || aggregateOpen || readDetailOpen || deliveryOpen || createOpen || editOpen || !!commandConfirmation" :defaults="defaultEnterpriseRequestQuery"
      :field-label="queryFieldLabel" @apply="applyQuery" @ready="queryReady" />
    <el-table
      :data="[...items]"
      row-key="id"
      empty-text="暂无数据"
      v-loading="loading"
    >
      <el-table-column prop="id" label="Id" />
      <el-table-column prop="organizationUnitId" label="OrganizationUnitId" />
      <el-table-column v-for="column in queryColumns" :key="column.columnKey" :prop="column.columnKey"
        :label="queryFieldLabel(column.columnKey)" :width="column.width ?? undefined" :fixed="column.fixed ?? undefined" />
      <el-table-column prop="createdById" label="CreatedById" />
      <el-table-column prop="updatedById" label="UpdatedById" />
      <el-table-column prop="deletedById" label="DeletedById" />
      <!-- @vue-generic {EnterpriseRequestResponse} -->
      <el-table-column label="操作" width="360">
        <template #default="{ row }">
          <ArtTableActionGroup>
          <el-button v-if="canPrintSummary" link type="primary" :disabled="changing" data-testid="enterprise-request-print"
            v-on="{click:printSummary(row)}">{{ t('enterpriseRequests.printSummary') }}</el-button>
          <el-button v-if="session.can('enterprise_request.enterprise_requests.read')" link data-testid="enterprise-request-approval-progress" @click="approvalProgressId = row.id">审批进度</el-button>
          <el-button v-if="session.can('enterprise_request.enterprise_request_lines.read')" link type="primary"
            @click="aggregateId = row.id; aggregateOpen = true">{{ editorText('open') }}</el-button>
          <el-button v-if="canUseDelivery()" link type="primary" @click="deliveryId = row.id; deliveryOpen = true">
            {{ enterpriseDeliveryText(locale, 'open') }}
          </el-button>
          <el-button
            v-if="canSubmit && row.status === 'Draft'"
            :disabled="changing"
            link
            type="success"
            v-on="{click:confirmCommand(row,'submit')}"
          >
            提交审批
          </el-button>
          <el-button
            v-if="canUpdate && row.status === 'Draft'"
            link
            type="primary"
            @click="openEdit(row)"
          >
            编辑
          </el-button>
          <el-button
            v-if="canDisable && row.status === 'Draft'"
            link
            type="danger"
            v-on="{click:confirmCommand(row,'delete')}"
          >
            删除
          </el-button>
          </ArtTableActionGroup>
        </template>
      </el-table-column>
    </el-table>
    <el-pagination
      :current-page="page"
      :page-size="pageSize"
      :total="total"
      layout="total, prev, pager, next"
      @current-change="(next: number) => load(next)"
    />
    <!-- 文档编辑会刷新列表；弹窗由页面持有，不能随表格行或操作插槽重建而消失。 -->
    <EnterpriseRequestApprovalProgressDialog v-if="approvalProgressId" :request-id="approvalProgressId" @close="approvalProgressId = undefined" />
    <EnterpriseRequestDelivery v-if="deliveryId" v-model="deliveryOpen" :request-id="deliveryId" @updated="load(page)" />
    <el-dialog v-if="commandConfirmation" :model-value="true" v-on="{'update:modelValue':commandControls.update}"
      :title="t(commandConfirmation.kind==='submit'?'enterpriseRequests.submitTitle':'enterpriseRequests.deleteTitle')" width="min(520px, calc(100vw - 32px))" :close-on-click-modal="false">
      <p>{{t(commandConfirmation.kind==='submit'?'enterpriseRequests.submitConfirm':'enterpriseRequests.deleteConfirm')}}</p>
      <p><code>{{commandConfirmation.row.requestNumber}}</code> · {{commandConfirmation.row.title}}</p>
      <code>{{commandConfirmation.row.id}}</code>
      <p v-if="editorUnconfirmed">{{editorText('writeUnconfirmed')}}</p>
      <p v-else-if="!changing&&!commandControls.available" role="status">{{editorText('commandUnavailable')}}</p>
      <template #footer>
        <el-button v-on="{click:commandControls.close}">{{t('common.cancel')}}</el-button>
        <el-button type="primary" :loading="changing" :disabled="changing||editorUnconfirmed||!commandControls.available" data-testid="enterprise-command-confirm" v-on="{click:commandControls.confirm}">{{t(commandConfirmation.kind==='submit'?'enterpriseRequests.confirmSubmit':'enterpriseRequests.confirmDelete')}}</el-button>
      </template>
    </el-dialog>
    <el-dialog :model-value="createOpen" v-on="{'update:modelValue':editorControls.createUpdate}" title="创建" width="min(720px, calc(100vw - 32px))">
      <el-form label-width="120px" :disabled="changing">
      <el-form-item label="OrganizationUnitId">
        <el-input v-model="createForm.organizationUnitId" />
      </el-form-item>
      <el-form-item label="RequestNumber">
        <el-input v-model="createForm.requestNumber" />
      </el-form-item>
      <el-form-item label="Title">
        <el-input v-model="createForm.title" />
      </el-form-item>
      <el-form-item label="Status">
        <el-input v-model="createForm.status" readonly />
      </el-form-item>
      <el-form-item label="TotalAmount">
        <el-input v-model="createForm.totalAmount" />
      </el-form-item>
      <el-form-item label="ApplicantUserId">
        <el-input v-model="createForm.applicantUserId" />
      </el-form-item>
      </el-form>
      <template #footer>
        <el-button v-on="{click:editorControls.createClose}">取消</el-button>
        <el-button type="primary" :disabled="changing||editorUnconfirmed" v-on="{click:editorControls.create}">保存</el-button>
      </template>
    </el-dialog>
    <el-dialog :model-value="editOpen" v-on="{'update:modelValue':editorControls.editUpdate}" title="编辑" width="min(720px, calc(100vw - 32px))">
      <el-form label-width="120px" :disabled="changing">
      <el-form-item label="RequestNumber">
        <el-input v-model="editForm.requestNumber" />
      </el-form-item>
      <el-form-item label="Title">
        <el-input v-model="editForm.title" />
      </el-form-item>
      <el-form-item label="Status">
        <el-input v-model="editForm.status" readonly />
      </el-form-item>
      <el-form-item label="TotalAmount">
        <el-input v-model="editForm.totalAmount" />
      </el-form-item>
      <el-form-item label="ApplicantUserId">
        <el-input v-model="editForm.applicantUserId" />
      </el-form-item>
      </el-form>
      <template #footer>
        <el-button v-on="{click:editorControls.editClose}">取消</el-button>
        <el-button type="primary" :disabled="changing||editorUnconfirmed" v-on="{click:editorControls.edit}">保存</el-button>
      </template>
    </el-dialog>
    <EnterpriseRequestAggregateEditor v-model="aggregateOpen" :request-id="aggregateId" @saved="load(page)" />
    <EnterpriseRequestReadDetail v-model="readDetailOpen" :request-id="readDetailId" />
  </section>
</template>
