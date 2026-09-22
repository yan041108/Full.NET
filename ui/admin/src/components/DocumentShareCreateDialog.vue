<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import { ElForm, ElFormItem, ElInput, ElMessage, ElOption, ElSelect } from 'element-plus';
import type { HostDocumentItemResponse, HostDocumentShareResponse } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { listDocumentItems } from '../api/host-document-items';
import { batchCreateDocumentShares, createDocumentShare } from '../api/document-shares';
import { buildDocumentShareUrl } from '../utils/documentShareUrl';

defineOptions({ name: 'DocumentShareCreateDialog' });

const props = defineProps<{
  open: boolean;
  /** 从 Host 文档库带入时锁定文档，无需手输 ID。 */
  presetDocument?: Pick<HostDocumentItemResponse, 'id' | 'title' | 'documentNo'> | null;
  /** 批量分享时传入多个文档；优先于 presetDocument。 */
  presetDocuments?: Pick<HostDocumentItemResponse, 'id' | 'title' | 'documentNo'>[] | null;
}>();

const emit = defineEmits<{
  'update:open': [value: boolean];
  created: [share: HostDocumentShareResponse, shareUrl: string];
  batchCreated: [succeededCount: number, total: number];
}>();

const { t } = useAdminI18n();
const saving = ref(false);
const documentOptions = ref<HostDocumentItemResponse[]>([]);
const optionsLoading = ref(false);
const editorForm = reactive({
  documentId: '',
  validDays: '7',
  password: '',
  maxAccessCount: ''
});

const lockedDocument = computed(() => props.presetDocument ?? null);
const lockedDocuments = computed(() => props.presetDocuments ?? []);
const isBatchPreset = computed(() => lockedDocuments.value.length > 1);
const documentSelectDisabled = computed(
  () => lockedDocument.value !== null || lockedDocuments.value.length > 0
);

const dialogOpen = computed({
  get: () => props.open,
  set: (value: boolean) => emit('update:open', value)
});

watch(
  () => props.open,
  open => {
    if (!open) {
      return;
    }
    editorForm.documentId = lockedDocument.value?.id ?? lockedDocuments.value[0]?.id ?? '';
    editorForm.validDays = '7';
    editorForm.password = '';
    editorForm.maxAccessCount = '';
    if (!lockedDocument.value && lockedDocuments.value.length === 0) {
      void loadDocumentOptions();
    }
  }
);

async function loadDocumentOptions() {
  optionsLoading.value = true;
  try {
    const collected: HostDocumentItemResponse[] = [];
    let page = 1;
    let total = 0;
    do {
      const result = await listDocumentItems(page, 100);
      collected.push(...result.items);
      total = result.total;
      page += 1;
    } while (collected.length < total && page <= 10);
    documentOptions.value = collected;
  } catch {
    documentOptions.value = [];
  } finally {
    optionsLoading.value = false;
  }
}

function documentOptionLabel(item: HostDocumentItemResponse): string {
  return `${item.title} (${item.documentNo})`;
}

async function submitCreate() {
  const documentIds = isBatchPreset.value
    ? lockedDocuments.value.map(item => item.id)
    : [(lockedDocument.value?.id ?? editorForm.documentId).trim()].filter(Boolean);
  if (documentIds.length === 0) {
    ElMessage.warning(t('documentShares.selectDocumentRequired'));
    return;
  }
  const validDays = Number(editorForm.validDays);
  if (!Number.isFinite(validDays) || validDays < 1) {
    ElMessage.warning(t('documentShares.validDaysInvalid'));
    return;
  }
  const maxAccessCount = editorForm.maxAccessCount.trim()
    ? Number(editorForm.maxAccessCount)
    : null;
  const password = editorForm.password.trim() || null;
  saving.value = true;
  try {
    if (documentIds.length > 1) {
      const batch = await batchCreateDocumentShares({
        documentIds,
        validDays,
        password,
        maxAccessCount: Number.isFinite(maxAccessCount) ? maxAccessCount : null
      });
      emit('batchCreated', batch.succeededCount, documentIds.length);
      dialogOpen.value = false;
      return;
    }
    const share = await createDocumentShare({
      documentId: documentIds[0]!,
      validDays,
      password,
      maxAccessCount: Number.isFinite(maxAccessCount) ? maxAccessCount : null
    });
    const shareUrl = buildDocumentShareUrl(share.shareCode);
    emit('created', share, shareUrl);
    dialogOpen.value = false;
  } catch {
    ElMessage.error(t('documentShares.operationFailed'));
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <ArtFormDialog
    v-model:open="dialogOpen"
    :title="t('documentShares.createDialogTitle')"
    :saving="saving"
    :confirm-label="t('users.confirm')"
    :cancel-label="t('users.cancel')"
    confirm-test-id="document-share-editor-submit"
    :show-confirm="true"
    @confirm="submitCreate"
  >
    <el-form data-testid="document-share-editor-form" label-width="120px">
      <el-form-item v-if="isBatchPreset" :label="t('documentShares.documentLabel')">
        <ul class="document-share-create-dialog__batch-list">
          <li v-for="item in lockedDocuments" :key="item.id" translate="no">
            {{ item.title }} · {{ item.documentNo }}
          </li>
        </ul>
      </el-form-item>
      <el-form-item v-else-if="lockedDocument" :label="t('documentShares.documentLabel')">
        <span translate="no">{{ lockedDocument.title }} · {{ lockedDocument.documentNo }}</span>
      </el-form-item>
      <el-form-item v-else :label="t('documentShares.documentLabel')">
        <el-select
          v-model="editorForm.documentId"
          filterable
          clearable
          :disabled="documentSelectDisabled"
          :loading="optionsLoading"
          :placeholder="t('documentShares.selectDocument')"
          data-testid="document-share-document-select"
          style="width: 100%"
        >
          <el-option
            v-for="item in documentOptions"
            :key="item.id"
            :label="documentOptionLabel(item)"
            :value="item.id"
          />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('documentShares.validDays')">
        <el-input v-model="editorForm.validDays" autocomplete="off" />
      </el-form-item>
      <el-form-item :label="t('documentShares.passwordOptional')">
        <el-input v-model="editorForm.password" type="password" show-password autocomplete="new-password" />
      </el-form-item>
      <el-form-item :label="t('documentShares.maxAccessCount')">
        <el-input v-model="editorForm.maxAccessCount" autocomplete="off" />
      </el-form-item>
    </el-form>
  </ArtFormDialog>
</template>

<style scoped>
.document-share-create-dialog__batch-list {
  margin: 0;
  padding-left: 18px;
  max-height: 160px;
  overflow: auto;
}
</style>
