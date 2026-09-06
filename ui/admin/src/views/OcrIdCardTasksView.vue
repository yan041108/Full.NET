<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag,
  ElUpload
} from 'element-plus';
import type { FullNetProblemDetails, OcrIdCardTask } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableActionGroup from '../framework/art-design/components/ArtTableActionGroup.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { uploadHostFile } from '../api/host-files';
import {
  confirmOcrIdCardTask,
  createOcrIdCardTask,
  listOcrIdCardTasks,
  rejectOcrIdCardTask
} from '../api/ocr';

defineOptions({ name: 'OcrIdCardTasksView' });

const { t } = useAdminI18n();
const items = ref<OcrIdCardTask[]>([]);
const total = ref(0);
const page = ref(1);
const pageSize = ref(20);
const loading = ref(false);
const uploading = ref(false);
const confirming = ref(false);
const rejectingId = ref('');
const problem = ref<FullNetProblemDetails>();
const confirmDialogVisible = ref(false);
const selectedTask = ref<OcrIdCardTask | null>(null);
const confirmForm = reactive({
  name: '',
  idNumber: '',
  gender: '',
  nation: '',
  address: '',
  birthDate: '',
  version: 0
});

function toProblem(error: unknown, fallbackKey: string): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { title: t(fallbackKey), status: 500, type: 'about:blank' };
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listOcrIdCardTasks(page.value, pageSize.value);
    items.value = result.items;
    total.value = result.total;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'ocrIdCardTasks.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function handleUpload(file: File): Promise<void> {
  uploading.value = true;
  problem.value = undefined;
  try {
    const uploaded = await uploadHostFile(file);
    await createOcrIdCardTask({ sourceFileId: uploaded.id });
    ElMessage.success(t('ocrIdCardTasks.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'ocrIdCardTasks.createFailed');
  } finally {
    uploading.value = false;
  }
}

function openConfirm(task: OcrIdCardTask): void {
  selectedTask.value = task;
  confirmForm.name = task.recognizedName ?? '';
  confirmForm.idNumber = task.recognizedIdNumber ?? '';
  confirmForm.gender = task.recognizedGender ?? '';
  confirmForm.nation = task.recognizedNation ?? '';
  confirmForm.address = task.recognizedAddress ?? '';
  confirmForm.birthDate = task.recognizedBirthDate ?? '';
  confirmForm.version = task.version;
  confirmDialogVisible.value = true;
}

async function submitConfirm(): Promise<void> {
  if (!selectedTask.value) {
    return;
  }
  confirming.value = true;
  problem.value = undefined;
  try {
    await confirmOcrIdCardTask(selectedTask.value.id, {
      name: confirmForm.name,
      idNumber: confirmForm.idNumber,
      gender: confirmForm.gender || null,
      nation: confirmForm.nation || null,
      address: confirmForm.address || null,
      birthDate: confirmForm.birthDate || null,
      version: confirmForm.version
    });
    ElMessage.success(t('ocrIdCardTasks.confirmSuccess'));
    confirmDialogVisible.value = false;
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'ocrIdCardTasks.confirmFailed');
  } finally {
    confirming.value = false;
  }
}

async function runReject(task: OcrIdCardTask): Promise<void> {
  rejectingId.value = task.id;
  problem.value = undefined;
  try {
    await rejectOcrIdCardTask(task.id, task.version);
    ElMessage.success(t('ocrIdCardTasks.rejectSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'ocrIdCardTasks.rejectFailed');
  } finally {
    rejectingId.value = '';
  }
}

onMounted(() => {
  void load();
});
</script>

<template>
  <div class="page-stack">
    <ElAlert
      v-if="problem"
      :title="problem.title"
      type="error"
      show-icon
      :closable="false"
      class="page-alert"
    />
    <ElCard v-loading="loading">
      <ArtTableHeader :title="t('ocrIdCardTasks.title')">
        <PermissionGate permission="ocr.id_card_tasks.create">
          <ElUpload
            data-testid="ocr-id-card-upload"
            :show-file-list="false"
            accept="image/jpeg,image/png,image/webp"
            :auto-upload="false"
            :on-change="(uploadFile) => uploadFile.raw && handleUpload(uploadFile.raw)"
          >
            <ElButton type="primary" :loading="uploading">
              {{ t('ocrIdCardTasks.upload') }}
            </ElButton>
          </ElUpload>
        </PermissionGate>
      </ArtTableHeader>
      <ElTable :data="items" row-key="id">
        <ElTableColumn prop="statusKey" :label="t('ocrIdCardTasks.fieldStatus')" width="120">
          <template #default="{ row }">
            <ElTag>{{ row.statusKey }}</ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn prop="recognizedName" :label="t('ocrIdCardTasks.fieldName')" />
        <ElTableColumn prop="recognizedIdNumber" :label="t('ocrIdCardTasks.fieldIdNumber')" />
        <ElTableColumn prop="failureMessage" :label="t('ocrIdCardTasks.fieldFailure')" />
        <ElTableColumn :label="t('ocrIdCardTasks.actions')" width="220">
          <template #default="{ row }">
            <ArtTableActionGroup>
              <PermissionGate permission="ocr.id_card_tasks.confirm">
                <ArtTableActionButton
                  v-if="row.statusKey === 'recognized'"
                  data-testid="ocr-id-card-confirm"
                  @click="openConfirm(row)"
                >
                  {{ t('ocrIdCardTasks.confirm') }}
                </ArtTableActionButton>
              </PermissionGate>
              <PermissionGate permission="ocr.id_card_tasks.reject">
                <ArtTableActionButton
                  v-if="row.statusKey === 'recognized'"
                  :loading="rejectingId === row.id"
                  @click="runReject(row)"
                >
                  {{ t('ocrIdCardTasks.reject') }}
                </ArtTableActionButton>
              </PermissionGate>
            </ArtTableActionGroup>
          </template>
        </ElTableColumn>
      </ElTable>
      <ElPagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        :total="total"
        layout="total, prev, pager, next"
        class="table-pagination"
        @current-change="load"
        @size-change="load"
      />
    </ElCard>
    <ArtFormDialog
      v-model="confirmDialogVisible"
      :title="t('ocrIdCardTasks.confirmTitle')"
      :confirm-loading="confirming"
      @confirm="submitConfirm"
    >
      <ElForm label-width="100px">
        <ElFormItem :label="t('ocrIdCardTasks.fieldName')">
          <ElInput v-model="confirmForm.name" />
        </ElFormItem>
        <ElFormItem :label="t('ocrIdCardTasks.fieldIdNumber')">
          <ElInput v-model="confirmForm.idNumber" />
        </ElFormItem>
        <ElFormItem :label="t('ocrIdCardTasks.fieldGender')">
          <ElInput v-model="confirmForm.gender" />
        </ElFormItem>
        <ElFormItem :label="t('ocrIdCardTasks.fieldNation')">
          <ElInput v-model="confirmForm.nation" />
        </ElFormItem>
        <ElFormItem :label="t('ocrIdCardTasks.fieldAddress')">
          <ElInput v-model="confirmForm.address" type="textarea" />
        </ElFormItem>
        <ElFormItem :label="t('ocrIdCardTasks.fieldBirthDate')">
          <ElInput v-model="confirmForm.birthDate" />
        </ElFormItem>
      </ElForm>
      <ElAlert
        :title="t('ocrIdCardTasks.confirmHint')"
        type="info"
        show-icon
        :closable="false"
      />
    </ArtFormDialog>
  </div>
</template>
