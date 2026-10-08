<script setup lang="ts">
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
import { http } from '../api/http';
import { useSessionStore } from '../auth/session';
import { useProductPage } from './products-page.generated';
import type { ProductResponse } from './products.generated';

const session = useSessionStore();
const problem = ref<FullNetProblemDetails>();
const createOpen = ref(false);
const editOpen = ref(false);
const deleteOpen = ref(false);
const editing = ref<ProductResponse>();
const deleting = ref<ProductResponse>();
let createTicket = 0; let editTicket = 0; let deleteTicket = 0;
const initialCreateForm = () => ({
  displayName: '',
  description: null,
  isActive: false
});
const initialEditForm = () => ({
  displayName: '',
  description: null,
  isActive: false
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
  canWrite,
  load,
  create,
  update,
  disable
} = useProductPage({
  request: http,
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

// 旧上下文的对话框、行与输入必须同步失效，迟到响应由页面模型拒绝接入。
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
  if (!canWrite.value || changing.value) return;
  problem.value = undefined;
  createOpen.value = true;
}

function openEdit(row: unknown): void {
  if (!canWrite.value || changing.value) return;
  // 表格插槽将行标为通用对象；只接受已由生成客户端校验并进入页面模型的同一对象。
  const item = items.value.find(candidate => candidate === row);
  if (!item) return;
  editing.value = item;
  // 只拷贝声明可更新字段；身份、租户和审计字段不得进入可提交表单。
  Object.assign(editForm, {
    displayName: item.displayName,
    description: item.description,
    isActive: item.isActive
  });
  editOpen.value = true;
}

function openDelete(row: unknown): void {
  if (!canWrite.value || changing.value) return;
  // 先展示确认弹窗；表格按钮不得直接执行不可撤销的删除请求。
  const item = items.value.find(candidate => candidate === row);
  if (!item) return;
  deleting.value = item;
  deleteOpen.value = true;
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

async function confirmDelete(): Promise<void> {
  if (!deleteOpen.value || !deleting.value) return;
  const ticket = deleteTicket; const scopeTicket = scopeVersion.value;
  const succeeded = await disable(deleting.value);
  if (succeeded && ticket === deleteTicket && scopeTicket === scopeVersion.value && deleteOpen.value) {
    deleteOpen.value = false;
  }
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
        v-if="canWrite"
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
      <el-table-column prop="displayName" label="Name" />
      <el-table-column prop="description" label="Description" />
      <el-table-column prop="isActive" label="IsActive" />
      <el-table-column label="操作" width="160">
        <template #default="{ row }">
          <el-button
            v-if="canWrite"
            link
            type="primary"
            @click="openEdit(row)"
          >
            编辑
          </el-button>
          <el-button
            v-if="canWrite"
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
      <el-form-item label="Name">
        <el-input v-model="createForm.displayName" />
      </el-form-item>
      <el-form-item label="Description">
        <el-input v-model="createForm.description" />
      </el-form-item>
      <el-form-item label="IsActive">
        <el-switch v-model="createForm.isActive" />
      </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createOpen = false">取消</el-button>
        <el-button type="primary" :loading="changing" @click="submitCreate">保存</el-button>
      </template>
    </el-dialog>
    <el-dialog v-model="editOpen" title="编辑">
      <el-form label-width="120px">
      <el-form-item label="Name">
        <el-input v-model="editForm.displayName" />
      </el-form-item>
      <el-form-item label="Description">
        <el-input v-model="editForm.description" />
      </el-form-item>
      <el-form-item label="IsActive">
        <el-switch v-model="editForm.isActive" />
      </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editOpen = false">取消</el-button>
        <el-button type="primary" :loading="changing" @click="submitEdit">保存</el-button>
      </template>
    </el-dialog>
    <el-dialog v-model="deleteOpen" title="确认删除" style="--el-color-danger: #b42318; --el-color-danger-light-3: #b42318; --el-color-danger-dark-2: #991b1b">
      <p>确定删除该条记录吗？</p>
      <template #footer>
        <el-button @click="deleteOpen = false">取消</el-button>
        <el-button type="danger" :loading="changing" @click="confirmDelete">确认删除</el-button>
      </template>
    </el-dialog>
  </section>
</template>
