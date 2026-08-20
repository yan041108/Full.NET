<script setup lang="ts">
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
import { http } from '../api/http';
import { useSessionStore } from '../auth/session';
import { useProductPage } from './products-page.generated';
import type { ProductResponse } from './products.generated';

const session = useSessionStore();
const problem = ref<FullNetProblemDetails>();
const createOpen = ref(false);
const editOpen = ref(false);
const editing = ref<ProductResponse>();
const createForm = reactive({
  displayName: '',
  description: null,
  isActive: false
});
const editForm = reactive({
  displayName: '',
  description: null,
  isActive: false
});

const {
  items,
  page,
  pageSize,
  total,
  loading,
  canWrite,
  load,
  create,
  update,
  remove
} = useProductPage({
  request: http,
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

function openEdit(row: ProductResponse): void {
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

async function removeRow(row: ProductResponse): Promise<void> {
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
        v-if="canWrite"
        type="primary"
        @click="openCreate"
      >
        创建
      </el-button>
    </div>
    <el-table
      :data="items"
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
        <el-button type="primary" @click="submitCreate">保存</el-button>
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
        <el-button type="primary" @click="submitEdit">保存</el-button>
      </template>
    </el-dialog>
  </section>
</template>
