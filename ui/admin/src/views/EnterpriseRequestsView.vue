<script setup lang="ts">
defineOptions({ name: 'EnterpriseRequestsView' });
import { onMounted, reactive, ref, watch } from 'vue';
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
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import { enterpriseRequestsHttp } from '../api/enterprise-requests';
import { useSessionStore } from '../auth/session';
import { useEnterpriseRequestPage } from './enterprise-requests/enterprise-requests-page.generated';
import type { EnterpriseRequestResponse } from './enterprise-requests/enterprise-requests.generated';

const session = useSessionStore();
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
  createOpen.value = false; editOpen.value = false; deleteOpen.value = false;
  editing.value = undefined; deleting.value = undefined; problem.value = undefined;
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

onMounted(() => {
  void load();
});

function openCreate(): void {
  if (!canCreate.value || changing.value) return;
  problem.value = undefined;
  createOpen.value = true;
}

function openEdit(row: EnterpriseRequestResponse): void {
  if (!canUpdate.value || changing.value || !items.value.includes(row)) return;
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
  if (!canDisable.value || changing.value || !items.value.includes(row)) return;
  problem.value = undefined; deleting.value = row; deleteOpen.value = true;
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
      <el-button
        v-if="canCreate"
        type="primary"
        @click="openCreate"
      >
        创建
      </el-button>
    </div>
    <el-table
      :data="[...items]"
      empty-text="暂无数据"
      v-loading="loading"
    >
      <el-table-column prop="id" label="Id" />
      <el-table-column prop="organizationUnitId" label="OrganizationUnitId" />
      <el-table-column prop="requestNumber" label="RequestNumber" />
      <el-table-column prop="title" label="Title" />
      <el-table-column prop="status" label="Status" />
      <el-table-column prop="totalAmount" label="TotalAmount" />
      <el-table-column prop="applicantUserId" label="ApplicantUserId" />
      <el-table-column prop="createdById" label="CreatedById" />
      <el-table-column prop="updatedById" label="UpdatedById" />
      <el-table-column prop="deletedById" label="DeletedById" />
      <!-- @vue-generic {EnterpriseRequestResponse} -->
      <el-table-column label="操作" width="240">
        <template #default="{ row }">
          <el-button
            v-if="canSubmit && row.status === 'Draft'"
            link
            type="success"
            @click="submitForApproval(row)"
          >
            提交审批
          </el-button>
          <el-button
            v-if="canUpdate"
            link
            type="primary"
            @click="openEdit(row)"
          >
            编辑
          </el-button>
          <el-button
            v-if="canDisable"
            link
            type="danger"
            @click="openDelete(row)"
          >
            删除
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
        <el-input v-model="createForm.status" />
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
        <el-button type="primary" :loading="changing" @click="submitCreate">保存</el-button>
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
        <el-input v-model="editForm.status" />
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
        <el-button type="primary" :loading="changing" @click="submitEdit">保存</el-button>
      </template>
    </el-dialog>
    <el-dialog v-model="deleteOpen" title="确认删除">
      <p>确定删除该条记录吗？</p>
      <template #footer>
        <el-button @click="deleteOpen = false">取消</el-button>
        <el-button type="danger" :loading="changing" @click="confirmDelete">确认删除</el-button>
      </template>
    </el-dialog>
  </section>
</template>
