<script setup lang="ts">
defineOptions({ name: 'EnterpriseRequestsView' });
import { onMounted, reactive, ref } from 'vue';
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
const editing = ref<EnterpriseRequestResponse>();
const createForm = reactive({
  organizationUnitId: '',
  requestNumber: '',
  title: '',
  status: '',
  totalAmount: 0,
  applicantUserId: '',
  createdById: '',
  updatedById: null,
  deletedById: null
});
const editForm = reactive({
  organizationUnitId: '',
  requestNumber: '',
  title: '',
  status: '',
  totalAmount: 0,
  applicantUserId: '',
  createdById: '',
  updatedById: null,
  deletedById: null
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
  load,
  create,
  update,
  remove,
  submitForApproval
} = useEnterpriseRequestPage({
  request: enterpriseRequestsHttp,
  hasPermission: permission => session.can(permission),
  onProblem: (error, fallbackCode) => {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : { status: 500, code: fallbackCode, title: fallbackCode };
  }
});

onMounted(() => {
  void load();
});

function openCreate(): void {
  createOpen.value = true;
}

function openEdit(row: EnterpriseRequestResponse): void {
  editing.value = row;
  Object.assign(editForm, row);
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
            v-if="canUpdate && row.status === 'Draft'"
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
            @click="removeRow(row)"
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
      <el-form-item label="CreatedById">
        <el-input v-model="createForm.createdById" />
      </el-form-item>
      <el-form-item label="UpdatedById">
        <el-input v-model="createForm.updatedById" />
      </el-form-item>
      <el-form-item label="DeletedById">
        <el-input v-model="createForm.deletedById" />
      </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createOpen = false">取消</el-button>
        <el-button type="primary" @click="submitCreate">保存</el-button>
      </template>
    </el-dialog>
    <el-dialog v-model="editOpen" title="编辑">
      <el-form label-width="120px">
      <el-form-item label="OrganizationUnitId">
        <el-input v-model="editForm.organizationUnitId" />
      </el-form-item>
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
      <el-form-item label="CreatedById">
        <el-input v-model="editForm.createdById" />
      </el-form-item>
      <el-form-item label="UpdatedById">
        <el-input v-model="editForm.updatedById" />
      </el-form-item>
      <el-form-item label="DeletedById">
        <el-input v-model="editForm.deletedById" />
      </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editOpen = false">取消</el-button>
        <el-button type="primary" @click="submitEdit">保存</el-button>
      </template>
    </el-dialog>
  </section>
</template>
