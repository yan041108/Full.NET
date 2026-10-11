<script setup lang="ts">
defineOptions({ name: 'EnterpriseRequestsView' });
import { onActivated, onDeactivated, onMounted, reactive, ref, watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import {
  ElButton,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElPagination,
  ElTable,
  ElTableColumn
} from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import { enterpriseRequestsHttp } from '../api/enterprise-requests';
import { useSessionStore } from '../auth/session';
import { useEnterpriseRequestPage } from './enterprise-requests/enterprise-requests-page.generated';
import type { EnterpriseRequestResponse } from './enterprise-requests/enterprise-requests.generated';
import EnterpriseRequestApprovalProgressDialog from './enterprise-requests/EnterpriseRequestApprovalProgressDialog.vue';
import EnterpriseRequestDetailDialog from './enterprise-requests/EnterpriseRequestDetailDialog.vue';
import EnterpriseRequestLinesDialog from './enterprise-requests/EnterpriseRequestLinesDialog.vue';
import EnterpriseRequestAttachmentsDialog from './enterprise-requests/EnterpriseRequestAttachmentsDialog.vue';
import { requestStatusLabel } from './enterprise-requests/enterprise-request-presentation';
import { useAdminI18n } from '../i18n/adminI18n';

const session = useSessionStore();
const route = useRoute();
const router = useRouter();
let viewActive = true;
let linkBlocked = false;
const { t } = useAdminI18n();
const progressId = ref<string>();
const detailId = ref<string>();
const linesId = ref<string>();
const attachmentsId = ref<string>();
const submitOpen = ref(false);
const submitting = ref<EnterpriseRequestResponse>();
let submitTicket = 0;
const problem = ref<FullNetProblemDetails>();
const createOpen = ref(false);
const editOpen = ref(false);
const deleteOpen = ref(false);
const editing = ref<EnterpriseRequestResponse>();
const deleting = ref<EnterpriseRequestResponse>();
let createTicket = 0; let editTicket = 0; let deleteTicket = 0;
const initialCreateForm = () => ({
  organizationUnitId: '',
  requestNumber: '',
  title: '',
  status: 'Draft',
  totalAmount: 0,
  applicantUserId: ''
});
const initialEditForm = () => ({
  requestNumber: '',
  title: '',
  status: '',
  totalAmount: 0,
  applicantUserId: ''
});
const createForm = reactive(initialCreateForm());
const editForm = reactive(initialEditForm());

const {
  items,
  page,
  pageSize,
  total,
  loading,
  changing,
  scopeVersion,
  cancelChange,
  canRead,
  canCreate,
  canUpdate,
  canSubmit,
  canDisable,
  load,
  create,
  update,
  remove,
  submitForApproval
} = useEnterpriseRequestPage({
  request: enterpriseRequestsHttp,
  contextKey: () => JSON.stringify([session.state, session.currentUser?.id,
    session.currentUser?.sessionId, session.currentUser?.tenantId, session.currentUser?.scope,
    session.currentUser?.actorScope, session.currentUser?.permissions]),
  hasPermission: permission => session.can(permission),
  onProblem: (error, fallbackCode) => {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : { status: 500, code: fallbackCode, title: fallbackCode };
  }
});

// 上下文或激活代次改变时，关闭所有旧资料入口并同步清除输入和错误。
watch(scopeVersion, () => {
  // 租户、会话或权限改变后，旧 URL 不能自动在新上下文中重新读取。
  linkBlocked = true;
  createOpen.value = false; editOpen.value = false; deleteOpen.value = false;
  editing.value = undefined; deleting.value = undefined; problem.value = undefined;
  progressId.value = undefined;
  detailId.value = undefined; submitOpen.value = false; submitting.value = undefined;
  linesId.value = undefined;
  attachmentsId.value = undefined;
  Object.assign(createForm, initialCreateForm()); Object.assign(editForm, initialEditForm());
}, { flush: 'sync' });
watch(createOpen, open => {
  createTicket++;
  if (!open) { cancelChange(); Object.assign(createForm, initialCreateForm()); }
}, { flush: 'sync' });
watch(editOpen, open => {
  editTicket++;
  if (!open) { cancelChange(); editing.value = undefined; Object.assign(editForm, initialEditForm()); }
}, { flush: 'sync' });
watch(deleteOpen, open => {
  deleteTicket++;
  if (!open) { cancelChange(); deleting.value = undefined; }
}, { flush: 'sync' });
watch(submitOpen, open => {
  submitTicket++;
  if (!open) { cancelChange(); submitting.value = undefined; }
}, { flush: 'sync' });

onMounted(() => {
  void load();
  openLinkedDetail();
});
watch([() => route.name, () => route.query.requestId], () => {
  linkBlocked = false;
  openLinkedDetail();
}, { flush: 'sync' });
onDeactivated(() => { viewActive = false; detailId.value = undefined; });
onActivated(() => {
  if (!viewActive) { viewActive = true; openLinkedDetail(); }
});

function openLinkedDetail(): void {
  detailId.value = undefined;
  const id = route.query.requestId;
  if (!viewActive || linkBlocked || !canRead.value || route.name !== 'enterprise-requests'
    || typeof id !== 'string' || id === '00000000-0000-0000-0000-000000000000'
    || !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)) return;
  // URL 只提供单据标识，详情仍由 Endpoint 按可信租户和数据范围授权读取。
  detailId.value = id;
}
function closeDetail(): void {
  const id = detailId.value;
  detailId.value = undefined; linkBlocked = true;
  if (route.name === 'enterprise-requests' && route.query.requestId === id) {
    const query = { ...route.query }; delete query.requestId;
    void router.replace({ query });
  }
}

function openCreate(): void {
  if (!canCreate.value || changing.value) return;
  problem.value = undefined;
  createOpen.value = true;
}

function openEdit(row: EnterpriseRequestResponse): void {
  if (!canUpdate.value || changing.value || row.status !== 'Draft' || !items.value.includes(row)) return;
  problem.value = undefined;
  editing.value = row;
  Object.assign(editForm, {
    requestNumber: row.requestNumber, title: row.title, status: row.status,
    totalAmount: row.totalAmount, applicantUserId: row.applicantUserId
  });
  editOpen.value = true;
}

async function submitCreate(): Promise<void> {
  if (!createOpen.value) return;
  const ticket = createTicket; const scopeTicket = scopeVersion.value;
  const succeeded = await create({ ...createForm });
  if (succeeded && ticket === createTicket && scopeTicket === scopeVersion.value && createOpen.value) {
    createOpen.value = false;
  }
}

async function submitEdit(): Promise<void> {
  if (!editOpen.value || !editing.value) {
    return;
  }
  const ticket = editTicket; const scopeTicket = scopeVersion.value;
  const succeeded = await update(editing.value, { ...editForm });
  if (succeeded && ticket === editTicket && scopeTicket === scopeVersion.value && editOpen.value) {
    editOpen.value = false;
  }
}

function openDelete(row: EnterpriseRequestResponse): void {
  if (!canDisable.value || changing.value || row.status !== 'Draft' || !items.value.includes(row)) return;
  problem.value = undefined; deleting.value = row; deleteOpen.value = true;
}

function openProgress(row: EnterpriseRequestResponse): void {
  if (!canRead.value || !items.value.includes(row)) return;
  progressId.value = row.id;
}
function openDetail(row: EnterpriseRequestResponse): void {
  if (!canRead.value || !items.value.includes(row)) return;
  detailId.value = row.id;
}
function openLines(row: EnterpriseRequestResponse): void {
  if (!canRead.value || !items.value.includes(row)) return;
  linesId.value = row.id;
}
function openAttachments(row: EnterpriseRequestResponse): void {
  if (!canRead.value || !items.value.includes(row)) return;
  attachmentsId.value = row.id;
}
function openSubmit(row: EnterpriseRequestResponse): void {
  if (!canSubmit.value || changing.value || row.status !== 'Draft' || !items.value.includes(row)) return;
  problem.value = undefined; submitting.value = row; submitOpen.value = true;
}
async function confirmSubmit(): Promise<void> {
  if (!submitOpen.value || !submitting.value) return;
  const ticket = submitTicket; const scopeTicket = scopeVersion.value;
  const succeeded = await submitForApproval(submitting.value);
  if (succeeded && ticket === submitTicket && scopeTicket === scopeVersion.value && submitOpen.value)
    submitOpen.value = false;
}
function closeProgress(): void {
  progressId.value = undefined;
  void load();
}

async function confirmDelete(): Promise<void> {
  if (!deleteOpen.value || !deleting.value) return;
  const ticket = deleteTicket; const scopeTicket = scopeVersion.value;
  const succeeded = await remove(deleting.value);
  if (succeeded && ticket === deleteTicket && scopeTicket === scopeVersion.value && deleteOpen.value)
    deleteOpen.value = false;
}
</script>

<template>
  <section class="generated-crud-view">
    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{problem.code}}</strong>
      <span>{{problem.title}}</span>
    </div>
    <div class="generated-crud-view__toolbar">
      <el-button v-if="canRead" :loading="loading" :disabled="changing" @click="load()">{{ t('common.refresh') }}</el-button>
      <el-button
        v-if="canCreate"
        type="primary"
        @click="openCreate"
      >
        {{ t('enterpriseRequests.create') }}
      </el-button>
    </div>
    <el-table
      :data="[...items]"
      :empty-text="t('enterpriseRequests.empty')"
      v-loading="loading"
    >
      <el-table-column prop="id" :label="t('enterpriseRequests.id')" />
      <el-table-column prop="organizationUnitId" :label="t('enterpriseRequests.organizationUnitId')" />
      <el-table-column prop="requestNumber" :label="t('enterpriseRequests.requestNumber')" />
      <el-table-column prop="title" :label="t('enterpriseRequests.title')" />
      <el-table-column prop="status" :label="t('enterpriseRequests.requestStatus')">
        <template #default="{ row }">{{ requestStatusLabel(row.status, t) }}</template>
      </el-table-column>
      <el-table-column prop="totalAmount" :label="t('enterpriseRequests.totalAmount')" />
      <el-table-column prop="applicantUserId" :label="t('enterpriseRequests.applicantUserId')" />
      <el-table-column prop="createdById" :label="t('enterpriseRequests.createdById')" />
      <el-table-column prop="updatedById" :label="t('enterpriseRequests.updatedById')" />
      <el-table-column prop="deletedById" :label="t('enterpriseRequests.deletedById')" />
      <!-- @vue-generic {EnterpriseRequestResponse} -->
      <el-table-column :label="t('enterpriseRequests.actions')" min-width="390">
        <template #default="{ row }">
          <el-button v-if="canRead" link type="primary" @click="openDetail(row)">{{ t('enterpriseRequests.detail') }}</el-button>
          <el-button v-if="canRead" link type="primary" @click="openLines(row)">{{ t('enterpriseRequests.lines') }}</el-button>
          <el-button v-if="canRead" link type="primary" @click="openAttachments(row)">{{ t('enterpriseRequests.attachments') }}</el-button>
          <el-button v-if="canRead" link type="primary" @click="openProgress(row)">
            {{ t('enterpriseRequests.approvalProgress') }}
          </el-button>
          <el-button
            v-if="canSubmit && row.status === 'Draft'"
            link
            type="success"
            :disabled="changing"
            @click="openSubmit(row)"
          >
            {{ t('enterpriseRequests.submit') }}
          </el-button>
          <el-button
            v-if="canUpdate && row.status === 'Draft'"
            link
            type="primary"
            @click="openEdit(row)"
            :disabled="changing"
          >
            {{ t('enterpriseRequests.edit') }}
          </el-button>
          <el-button
            v-if="canDisable && row.status === 'Draft'"
            link
            type="danger"
            @click="openDelete(row)"
            :disabled="changing"
          >
            {{ t('enterpriseRequests.delete') }}
          </el-button>
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
    <EnterpriseRequestApprovalProgressDialog v-if="progressId" :key="progressId" :request-id="progressId" @close="closeProgress" />
    <EnterpriseRequestDetailDialog v-if="detailId" :key="detailId" :request-id="detailId" @close="closeDetail" />
    <EnterpriseRequestLinesDialog v-if="linesId" :key="linesId" :request-id="linesId" @close="linesId = undefined" @changed="load()" />
    <EnterpriseRequestAttachmentsDialog v-if="attachmentsId" :key="attachmentsId" :request-id="attachmentsId" @close="attachmentsId = undefined" @changed="load()" />
    <el-dialog v-model="submitOpen" :title="t('enterpriseRequests.submitTitle')" width="min(520px, 94vw)">
      <p>{{ t('enterpriseRequests.submitConfirm') }}</p>
      <p v-if="submitting" translate="no">{{ submitting.requestNumber }} · {{ submitting.title }}</p>
      <template #footer>
        <el-button @click="submitOpen = false">{{ t('common.cancel') }}</el-button>
        <el-button v-if="canSubmit" type="primary" :loading="changing" @click="confirmSubmit">{{ t('enterpriseRequests.confirmSubmit') }}</el-button>
      </template>
    </el-dialog>
    <el-dialog v-model="createOpen" :title="t('enterpriseRequests.create')">
      <el-form label-width="120px">
      <el-form-item :label="t('enterpriseRequests.organizationUnitId')">
        <el-input v-model="createForm.organizationUnitId" />
      </el-form-item>
      <el-form-item :label="t('enterpriseRequests.requestNumber')">
        <el-input v-model="createForm.requestNumber" />
      </el-form-item>
      <el-form-item :label="t('enterpriseRequests.title')">
        <el-input v-model="createForm.title" />
      </el-form-item>
      <el-form-item :label="t('enterpriseRequests.requestStatus')">
        <el-input :model-value="requestStatusLabel(createForm.status, t)" disabled />
      </el-form-item>
      <el-form-item :label="t('enterpriseRequests.totalAmount')">
        <el-input v-model="createForm.totalAmount" />
      </el-form-item>
      <el-form-item :label="t('enterpriseRequests.applicantUserId')">
        <el-input v-model="createForm.applicantUserId" />
      </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createOpen = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" :loading="changing" @click="submitCreate">{{ t('enterpriseRequests.save') }}</el-button>
      </template>
    </el-dialog>
    <el-dialog v-model="editOpen" :title="t('enterpriseRequests.edit')">
      <el-form label-width="120px">
      <el-form-item :label="t('enterpriseRequests.requestNumber')">
        <el-input v-model="editForm.requestNumber" />
      </el-form-item>
      <el-form-item :label="t('enterpriseRequests.title')">
        <el-input v-model="editForm.title" />
      </el-form-item>
      <el-form-item :label="t('enterpriseRequests.requestStatus')">
        <el-input :model-value="requestStatusLabel(editForm.status, t)" disabled />
      </el-form-item>
      <el-form-item :label="t('enterpriseRequests.totalAmount')">
        <el-input v-model="editForm.totalAmount" />
      </el-form-item>
      <el-form-item :label="t('enterpriseRequests.applicantUserId')">
        <el-input v-model="editForm.applicantUserId" />
      </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editOpen = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" :loading="changing" @click="submitEdit">{{ t('enterpriseRequests.save') }}</el-button>
      </template>
    </el-dialog>
    <el-dialog v-model="deleteOpen" :title="t('enterpriseRequests.deleteTitle')">
      <p>{{ t('enterpriseRequests.deleteConfirm') }}</p>
      <template #footer>
        <el-button @click="deleteOpen = false">{{ t('common.cancel') }}</el-button>
        <el-button type="danger" :loading="changing" @click="confirmDelete">{{ t('enterpriseRequests.confirmDelete') }}</el-button>
      </template>
    </el-dialog>
  </section>
</template>
