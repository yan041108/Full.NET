<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from 'vue';
import {
  ElButton,
  ElDialog,
  ElForm,
  ElFormItem,
  ElInput,
  ElInputNumber,
  ElDatePicker,
  ElSelect,
  ElRadio,
  ElRadioGroup,
  ElOption,
  type FormInstance,
  type FormItemRule,
  ElPagination,
  ElSwitch,
  ElTable,
  vLoading,
  ElTableColumn
} from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import { http } from '../api/http';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { useProductPage } from './products-page.generated';
import type { ProductResponse, CreateProductRequest, ProductListOptions } from './products.generated';


const session = useSessionStore();
const { t, locale } = useAdminI18n();
const createFormRef = ref<FormInstance>();
const editFormRef = ref<FormInstance>();

const problem = ref<FullNetProblemDetails>();
const createOpen = ref(false);
const editOpen = ref(false);
const deleteOpen = ref(false);
const editing = ref<ProductResponse>();
const deleting = ref<ProductResponse>();
let createTicket = 0; let editTicket = 0; let deleteTicket = 0;
type FormModel = { -readonly [Key in keyof CreateProductRequest]: CreateProductRequest[Key] };
function defaults(): FormModel { return { displayName: '',
  description: null,
  isActive: false }; }
const createForm = reactive<FormModel>(defaults());
const editForm = reactive<FormModel>(defaults());
const initialCreateForm = defaults; const initialEditForm = defaults;

const {
  items,
  page,
  pageSize,
  total,
  loading,
  changing,
  query,
  scopeVersion,
  cancelChange,
  canWrite,
  load,
  create,
  update,
  disable: remove
} = useProductPage({
  request: http,
  contextKey: () => JSON.stringify([session.state, session.currentUser?.id,
    session.currentUser?.sessionId, session.currentUser?.tenantId, session.currentUser?.scope,
    session.currentUser?.actorScope, session.currentUser?.permissions]),
  hasPermission: permission => session.can(permission),
  onProblem: (error, fallbackCode) => {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : { status: 500, code: fallbackCode, title: t('generatedCrud.operationFailed') };
  }
});


interface GeneratedField { name: string; kind: string; required: boolean; nullable: boolean; length: number; precision: number; scale: number }
const createRules = computed(() => buildRules([  { name: 'displayName', kind: 'String', required: true, nullable: false, length: 200, precision: 0, scale: 0 },
  { name: 'description', kind: 'String', required: false, nullable: true, length: 500, precision: 0, scale: 0 },
  { name: 'isActive', kind: 'Boolean', required: true, nullable: false, length: 0, precision: 0, scale: 0 }], locale.value));
const editRules = computed(() => buildRules([  { name: 'displayName', kind: 'String', required: true, nullable: false, length: 200, precision: 0, scale: 0 },
  { name: 'description', kind: 'String', required: false, nullable: true, length: 500, precision: 0, scale: 0 },
  { name: 'isActive', kind: 'Boolean', required: true, nullable: false, length: 0, precision: 0, scale: 0 }], locale.value));
function buildRules(fields: readonly GeneratedField[], _locale: string): Record<string, FormItemRule[]> {
  return Object.fromEntries(fields.map(field => [field.name, [{ trigger: ['blur', 'change'],
    validator: (_rule: unknown, value: unknown, callback: (error?: Error) => void) => {
      const message = validateField(value, field);
      callback(message ? new Error(message) : undefined);
    } }]]));
}
function validateField(value: unknown, field: GeneratedField): string | undefined {
  if (value === null || value === undefined) return field.nullable && !field.required ? undefined : t('generatedCrud.valueRequired');
  if (field.kind === 'String') {
    if (typeof value !== 'string' || field.required && value.trim() === '') return t('generatedCrud.valueRequired');
    return value.length > field.length ? t('generatedCrud.valueTooLong', { count: field.length }) : undefined;
  }
  if (field.kind === 'Boolean') return typeof value === 'boolean' ? undefined : t('generatedCrud.valueInvalid');
  if (field.kind === 'Int32') return typeof value === 'number' && Number.isInteger(value) && value >= -2147483648 && value <= 2147483647 ? undefined : t('generatedCrud.integerRange');
  if (typeof value !== 'string') return t('generatedCrud.valueInvalid');
  if (field.kind === 'Int64') {
    if (value.length > 128 || !/^-?(?:0|[1-9]\d*)$/.test(value)) return t('generatedCrud.integerRange');
    const integer = BigInt(value);
    return integer >= -9223372036854775808n && integer <= 9223372036854775807n ? undefined : t('generatedCrud.integerRange');
  }
  if (field.kind === 'Decimal') {
    if (value.length > 128 || !/^-?(?:0|[1-9]\d*)(?:\.\d+)?$/.test(value)) return t('generatedCrud.decimalPrecision');
    const [integer = '', rawFraction = ''] = value.replace(/^-/, '').split('.');
    const fraction = rawFraction.replace(/0+$/, '');
    const digits = integer.replace(/^0+/, '');
    if (digits.length > field.precision - field.scale || fraction.length > field.scale || fraction.length > 28
      || BigInt((integer + fraction) || '0') > 79228162514264337593543950335n) return t('generatedCrud.decimalPrecision');
    return undefined;
  }
  if (field.kind === 'Uuid') return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
    && (!field.required || value !== '00000000-0000-0000-0000-000000000000') ? undefined : t('generatedCrud.uuidInvalid');
  if (field.kind === 'DateTimeUtc') {
    const date = new Date(value);
    const fraction = /\.(\d+)/.exec(value)?.[1]?.replace(/0+$/, '') ?? '';
    return !Number.isNaN(date.valueOf()) && date.getUTCFullYear() >= 1000 && date.getUTCFullYear() <= 9999 && fraction.length <= 6 ? undefined : t('generatedCrud.dateInvalid');
  }
  return t('generatedCrud.valueInvalid');
}



const sortKeys = new Set<string>(['id', 'displayName', 'description', 'isActive']);
function sortChanged(event: { prop?: string | null; order?: string | null }): void {
  if (event.prop && event.order) {
    if (!sortKeys.has(event.prop)) return;
    query.sortBy = event.prop as ProductListOptions['sortBy'];
    query.descending = event.order === 'descending';
  } else { query.sortBy = undefined; query.descending = undefined; }
  void load(1);
}
function clearFilters(): void {
  for (const key of Object.keys(query)) Reflect.deleteProperty(query, key);
  void load(1);
}

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
  Object.assign(createForm, defaults());

  createOpen.value = true;
  void nextTick(() => createFormRef.value?.clearValidate());
}

async function openEdit(row: unknown): Promise<void> {
  if (!canWrite.value || changing.value) return;
  // 树行附带 children；按白名单中的标识回到已由生成客户端校验的当前记录。
  let item = resolveRow(row);
  if (!item) return;

  editing.value = item;
  Object.assign(editForm, { displayName: item.displayName, description: item.description, isActive: item.isActive });
  editOpen.value = true;

  void nextTick(() => editFormRef.value?.clearValidate());
}

function openDelete(row: unknown): void {
  if (!canWrite.value || changing.value) return;
  // 先展示确认弹窗；表格按钮不得直接执行不可撤销的删除请求。
  const item = resolveRow(row);
  if (!item) return;
  deleting.value = item;
  deleteOpen.value = true;
}

async function submitCreate(): Promise<void> {
  if (!await createFormRef.value?.validate().catch(() => false)) return;


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
  if (!await editFormRef.value?.validate().catch(() => false)) return;

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
function resolveRow(row: unknown): ProductResponse | undefined {
  if (typeof row !== 'object' || row === null || !('id' in row)) return undefined;
  return items.value.find(candidate => candidate.id === row.id);
}
</script>

<template>
  <section class="generated-crud-view">
    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{problem.code}}</strong>
      <span>{{problem.title}}</span>
    </div>
    <el-form class="generated-crud-view__filters" inline @submit.prevent="load(1)">
<el-form-item :label="t('generatedCrud.field', { name: 'Name' })" prop="filterName"><el-input :model-value="query.filterName ?? ''" @update:model-value="value => query.filterName = value === '' ? undefined : String(value)" :maxlength="200" :aria-label="t('generatedCrud.field', { name: 'Name' })" /></el-form-item>
<el-form-item :label="t('generatedCrud.field', { name: 'Description' })" prop="filterDescription"><el-input :model-value="query.filterDescription ?? ''" @update:model-value="value => query.filterDescription = value === '' ? undefined : String(value)" :maxlength="500" :aria-label="t('generatedCrud.field', { name: 'Description' })" /></el-form-item>
<el-form-item :label="t('generatedCrud.field', { name: 'IsActive' })" prop="filterIsActive"><el-radio-group :model-value="query.filterIsActive === true ? 'true' : query.filterIsActive === false ? 'false' : 'unset'" @update:model-value="value => query.filterIsActive = value === 'true' ? true : value === 'false' ? false : undefined" :aria-label="t('generatedCrud.field', { name: 'IsActive' })"><el-radio value="unset">{{ t('generatedCrud.unset') }}</el-radio><el-radio value="true">{{ t('generatedCrud.yes') }}</el-radio><el-radio value="false">{{ t('generatedCrud.no') }}</el-radio></el-radio-group></el-form-item>
      <el-button native-type="submit" :loading="loading">{{t('generatedCrud.search')}}</el-button>
      <el-button :disabled="loading" @click="clearFilters">{{t('generatedCrud.reset')}}</el-button>
    </el-form>
    <div class="generated-crud-view__toolbar">
      <el-button
        v-if="canWrite"
        type="primary"
        @click="openCreate"
      >{{t('generatedCrud.create')}}</el-button>
    </div>
    <el-table
      :data="[...items]"

      :empty-text="t('generatedCrud.empty')"
      @sort-change="sortChanged"
      v-loading="loading"
    >
      <el-table-column prop="id" :label="t('generatedCrud.field', { name: 'Id' })" sortable="custom" />
      <el-table-column prop="displayName" :label="t('generatedCrud.field', { name: 'Name' })" sortable="custom" />
      <el-table-column prop="description" :label="t('generatedCrud.field', { name: 'Description' })" sortable="custom" />
      <el-table-column prop="isActive" :label="t('generatedCrud.field', { name: 'IsActive' })" sortable="custom" />
      <el-table-column :label="t('generatedCrud.actions')" width="160">
        <template #default="{ row }">
          <el-button
            v-if="canWrite"
            link
            type="primary"
            @click="openEdit(row)"
          >{{t('generatedCrud.update')}}</el-button>
          <el-button
            v-if="canWrite"
            link
            type="danger"
            @click="openDelete(row)"
          >{{t('generatedCrud.remove')}}</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-pagination v-if="true"
      :current-page="page"
      :page-size="pageSize"
      :total="total"
      layout="total, prev, pager, next"
      @current-change="(next: number) => load(next)"
    />
    <el-dialog append-to-body width="min(680px, calc(100vw - 32px))" v-model="createOpen" :title="t('generatedCrud.create')" style="--el-color-danger: #b42318; --el-text-color-placeholder: #59636f">
      <el-form ref="createFormRef" :model="createForm" :rules="createRules" label-width="120px">
<el-form-item :label="t('generatedCrud.field', { name: 'Name' })" prop="displayName"><el-input :model-value="createForm.displayName ?? ''" @update:model-value="value => createForm.displayName = value === '' ? '' : String(value)" :maxlength="200" :aria-label="t('generatedCrud.field', { name: 'Name' })" /></el-form-item>
<el-form-item :label="t('generatedCrud.field', { name: 'Description' })" prop="description"><el-input :model-value="createForm.description ?? ''" @update:model-value="value => createForm.description = value === '' ? null : String(value)" :maxlength="500" :aria-label="t('generatedCrud.field', { name: 'Description' })" /></el-form-item>
<el-form-item :label="t('generatedCrud.field', { name: 'IsActive' })" prop="isActive"><el-switch v-model="createForm.isActive" :aria-label="t('generatedCrud.field', { name: 'IsActive' })" /></el-form-item>

      </el-form>

      <template #footer>
        <el-button @click="createOpen = false">{{t('generatedCrud.cancel')}}</el-button>
        <el-button type="primary" @click="submitCreate" :loading="changing">{{t('generatedCrud.save')}}</el-button>
      </template>
    </el-dialog>
    <el-dialog append-to-body width="min(680px, calc(100vw - 32px))" v-model="editOpen" :title="t('generatedCrud.update')" style="--el-color-danger: #b42318; --el-text-color-placeholder: #59636f">
      <el-form ref="editFormRef" :model="editForm" :rules="editRules" label-width="120px">
<el-form-item :label="t('generatedCrud.field', { name: 'Name' })" prop="displayName"><el-input :model-value="editForm.displayName ?? ''" @update:model-value="value => editForm.displayName = value === '' ? '' : String(value)" :maxlength="200" :aria-label="t('generatedCrud.field', { name: 'Name' })" /></el-form-item>
<el-form-item :label="t('generatedCrud.field', { name: 'Description' })" prop="description"><el-input :model-value="editForm.description ?? ''" @update:model-value="value => editForm.description = value === '' ? null : String(value)" :maxlength="500" :aria-label="t('generatedCrud.field', { name: 'Description' })" /></el-form-item>
<el-form-item :label="t('generatedCrud.field', { name: 'IsActive' })" prop="isActive"><el-switch v-model="editForm.isActive" :aria-label="t('generatedCrud.field', { name: 'IsActive' })" /></el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="editOpen = false">{{t('generatedCrud.cancel')}}</el-button>
        <el-button type="primary" @click="submitEdit" :loading="changing">{{t('generatedCrud.save')}}</el-button>
      </template>
    </el-dialog>
    <el-dialog append-to-body width="min(680px, calc(100vw - 32px))" v-model="deleteOpen" :title="t('generatedCrud.confirmRemove')" style="--el-color-danger: #b42318; --el-color-danger-light-3: #b42318; --el-color-danger-dark-2: #991b1b" @close="deleting = undefined">
      <p>{{t('generatedCrud.removePrompt')}}</p>
      <template #footer>
        <el-button @click="deleteOpen = false">{{t('generatedCrud.cancel')}}</el-button>
        <el-button type="danger" @click="confirmDelete" :loading="changing">{{t('generatedCrud.confirmRemove')}}</el-button>
      </template>
    </el-dialog>
  </section>
</template>
<style scoped>
.generated-crud-view { display: grid; gap: 1rem; min-width: 0; }
.generated-crud-view__filters { display: flex; flex-wrap: wrap; align-items: center; gap: .5rem; }
.generated-crud-view__details { display: grid; gap: .75rem; margin-block: 1rem; }
.generated-crud-view__detail-row { border: 1px solid var(--el-border-color); border-radius: 4px; padding: .75rem; min-width: 0; }
.generated-crud-view :deep(.el-input-number), .generated-crud-view :deep(.el-date-editor), .generated-crud-view :deep(.el-select) { max-width: 100%; }
/* 空选择框的占位元素使用绝对定位，显式保留内容高度，避免移动端背景采样越过弹窗。 */
:global(.generated-crud-select .el-select__selection) { min-height: 24px; background-color: var(--el-fill-color-blank, #fff); }
</style>
