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
import { useSessionStore } from '../auth/session';
import { useEnterpriseRequestPage } from './enterprise-requests/enterprise-requests-page.generated';
import type { EnterpriseRequestResponse } from './enterprise-requests/enterprise-requests.generated';

const session = useSessionStore();
const { locale } = useAdminI18n();
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
  reset(); problem.value = undefined; createOpen.value = false; editOpen.value = false; editing.value = undefined;
  query.value = defaultEnterpriseRequestQuery();
  pendingQuery = undefined;
  aggregateOpen.value = false; aggregateId.value = undefined;
  readDetailOpen.value = false; readDetailId.value = undefined;
  deliveryOpen.value = false; deliveryId.value = undefined;
  if (pageActive.value && !session.switching && !queryEnabled.value) void load();
}, { flush: 'sync' });
onBeforeUnmount(reset);

/** 查询切换只取消读取；编辑或业务写入未结束时不重置其结果。 */
function applyQuery(configuration: SavedQueryConfiguration) {
  if (!pageActive.value || session.switching) return;
  // 深链详情打开期间只保留读取到的默认条件；关闭后再应用，避免面板与实际列表条件不一致。
  if (changing.value || aggregateOpen.value || readDetailOpen.value || deliveryOpen.value || createOpen.value || editOpen.value) { pendingQuery = configuration; return; }
  pendingQuery = undefined;
  reset(); query.value = configuration; problem.value = undefined; void load(1);
}
function queryReady() { if (!pendingQuery && pageActive.value && !session.switching && !loading.value && !items.value.length) void load(1); }
watch(() => [changing.value, aggregateOpen.value, readDetailOpen.value, deliveryOpen.value, createOpen.value, editOpen.value], value => {
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
onDeactivated(() => { pageActive.value = false; pendingQuery = undefined; reset(); aggregateOpen.value = false; readDetailOpen.value = false; deliveryOpen.value = false; createOpen.value = false; editOpen.value = false; });
watch(() => aggregateOpen.value || readDetailOpen.value, (value, previous) => {
  if (!value && previous && route.name === 'enterprise-requests' && route.query.requestId !== undefined) {
    const { requestId: _requestId, ...query } = route.query;
    void router.replace({ query });
  }
});

function openCreate(): void {
  createForm.status = 'Draft';
  createOpen.value = true;
}

function openEdit(row: EnterpriseRequestResponse): void {
  editing.value = row;
  Object.assign(editForm, {
    requestNumber: row.requestNumber, title: row.title, status: row.status,
    totalAmount: row.totalAmount, applicantUserId: row.applicantUserId
  });
  editOpen.value = true;
}

async function submitCreate(): Promise<void> {
  const succeeded = await create({ ...createForm });
  if (succeeded) {
    createOpen.value = false;
  }
}

async function submitEdit(): Promise<void> {
  if (!editing.value) {
    return;
  }
  const succeeded = await update(editing.value, { ...editForm });
  if (succeeded) {
    editOpen.value = false;
  }
}

async function removeRow(row: EnterpriseRequestResponse): Promise<void> {
  await remove(row);
}
</script>

<template>
  <section class="generated-crud-view">
    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{problem.code}}</strong>
      <span>{{problem.title}}</span>
    </div>
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
      :disabled="changing || aggregateOpen || readDetailOpen || deliveryOpen || createOpen || editOpen" :defaults="defaultEnterpriseRequestQuery"
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
            @click="submitForApproval(row)"
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
            @click="removeRow(row)"
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
    <el-dialog v-model="createOpen" title="创建">
      <el-form label-width="120px">
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
        <el-button @click="createOpen = false">取消</el-button>
        <el-button type="primary" @click="submitCreate">保存</el-button>
      </template>
    </el-dialog>
    <el-dialog v-model="editOpen" title="编辑">
      <el-form label-width="120px">
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
        <el-button @click="editOpen = false">取消</el-button>
        <el-button type="primary" @click="submitEdit">保存</el-button>
      </template>
    </el-dialog>
    <EnterpriseRequestAggregateEditor v-model="aggregateOpen" :request-id="aggregateId" @saved="load(page)" />
    <EnterpriseRequestReadDetail v-model="readDetailOpen" :request-id="readDetailId" />
  </section>
</template>
