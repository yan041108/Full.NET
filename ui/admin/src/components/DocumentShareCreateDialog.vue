<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import { ElForm, ElFormItem, ElInput, ElOption, ElSelect } from 'element-plus';
import type { HostDocumentItemResponse, HostDocumentShareResponse } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import { showError, showWarning } from '../feedback/fullNetMessage';
import { useAdminI18n } from '../i18n/adminI18n';
import { listDocumentItems } from '../api/host-document-items';
import { batchCreateDocumentShares, createDocumentShare } from '../api/document-shares';
import { useSessionStore } from '../auth/session';
import { useAuthorizedViewScope } from '../composables/useAuthorizedViewScope';
import { buildDocumentShareUrl } from '../utils/documentShareUrl';

defineOptions({ name: 'DocumentShareCreateDialog' });

const props = defineProps<{
  open: boolean;
  /** 分享列表与文档库分别使用自己的父页面读取权限。 */
  parentReadPermission?: 'document.host_shares.read' | 'document.host_documents.read';
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
const session = useSessionStore();
const canCreate = computed(() => session.currentUser?.scope === 'host'
  && session.can(props.parentReadPermission ?? 'document.host_shares.read')
  && session.can('document.host_shares.create'));
const accepting = ref(false);
let initialized = false;
const saving = ref(false);
const documentOptions = ref<HostDocumentItemResponse[]>([]);
const optionsLoading = ref(false);
const editorForm = reactive({
  documentId: '',
  validDays: '7',
  password: '',
  maxAccessCount: ''
});

const lockedDocument = computed(() => props.presetDocuments?.[0] ?? props.presetDocument ?? null);
const lockedDocuments = computed(() => props.presetDocuments ?? []);
const isBatchPreset = computed(() => lockedDocuments.value.length > 1);
const documentSelectDisabled = computed(
  () => lockedDocument.value !== null || lockedDocuments.value.length > 0
);

function resetEditor(): void {
  accepting.value = false; saving.value = false; optionsLoading.value = false; documentOptions.value = [];
  Object.assign(editorForm, { documentId: '', validDays: '7', password: '', maxAccessCount: '' });
}
const scope = useAuthorizedViewScope(session, () => {
  resetEditor();
  if (props.open) emit('update:open', false);
}, () => {
  if (!initialized) { initialized = true; if (props.open) initializeEditor(); }
});
const dialogOpen = computed({
  get: () => props.open && accepting.value && canCreate.value,
  set: (value: boolean) => {
    // 父组件尚未回写 open 时也必须立即取消，防止迟到创建结果继续发出。
    if (!value) scope.invalidate();
    else emit('update:open', true);
  }
});
function initializeEditor(): void {
  initialized = true;
  resetEditor();
  if (!canCreate.value) return;
  accepting.value = true;
  editorForm.documentId = lockedDocument.value?.id ?? '';
  if (!documentSelectDisabled.value) void loadDocumentOptions();
}
// 关闭同步取消；开启等同一轮父属性完整写入后再初始化，避免预设文档触发误关闭。
watch(() => props.open, open => { if (!open) scope.invalidate(); }, { flush: 'sync' });
watch(() => props.open, open => { if (open) initializeEditor(); });
watch(() => JSON.stringify([props.parentReadPermission, lockedDocument.value?.id,
  lockedDocuments.value.map(item => item.id)]), () => { if (accepting.value) scope.invalidate(); }, { flush: 'sync' });

async function loadDocumentOptions() {
  if (!props.open || !accepting.value || !canCreate.value) return;
  const request = scope.begin('document.host_documents.read');
  if (!request) return;
  optionsLoading.value = true;
  try {
    const collected: HostDocumentItemResponse[] = [];
    let page = 1; let total = 0;
    do {
      const result = await listDocumentItems(page, 100, {}, request.signal);
      if (!request.current() || !props.open || !canCreate.value) return;
      collected.push(...result.items); total = result.total; page++;
    } while (collected.length < total && page <= 10);
    documentOptions.value = collected;
  } catch {
    if (request.current()) documentOptions.value = [];
  } finally {
    if (request.current()) optionsLoading.value = false;
    request.finish();
  }
}

function documentOptionLabel(item: HostDocumentItemResponse): string {
  return `${item.title} (${item.documentNo})`;
}

async function submitCreate() {
  if (saving.value || !props.open || !accepting.value || !canCreate.value) return;
  const documentIds = isBatchPreset.value
    ? lockedDocuments.value.map(item => item.id)
    : [(lockedDocument.value?.id ?? editorForm.documentId).trim()].filter(Boolean);
  if (documentIds.length === 0) {
    showWarning(t('documentShares.selectDocumentRequired'));
    return;
  }
  const validDays = Number(editorForm.validDays);
  if (!Number.isInteger(validDays) || validDays < 1 || validDays > 365) {
    showWarning(t('documentShares.validDaysInvalid'));
    return;
  }
  const maxAccessCount = editorForm.maxAccessCount.trim()
    ? Number(editorForm.maxAccessCount)
    : null;
  if (maxAccessCount !== null && (!Number.isInteger(maxAccessCount) || maxAccessCount < 1 || maxAccessCount > 2147483647)) {
    showWarning(t('documentShares.maxAccessCountInvalid'));
    return;
  }
  const password = editorForm.password.trim() || null;
  const request = scope.begin('document.host_shares.create');
  if (!request) return;
  saving.value = true;
  try {
    if (documentIds.length > 1) {
      const batch = await batchCreateDocumentShares({
        documentIds,
        validDays,
        password,
        maxAccessCount
      }, request.signal);
      if (!request.current() || !props.open || !canCreate.value) return;
      emit('batchCreated', batch.succeededCount, documentIds.length);
      dialogOpen.value = false;
      return;
    }
    const share = await createDocumentShare({
      documentId: documentIds[0]!,
      validDays,
      password,
      maxAccessCount
    }, request.signal);
    if (!request.current() || !props.open || !canCreate.value) return;
    const shareUrl = buildDocumentShareUrl(share.shareCode);
    emit('created', share, shareUrl);
    dialogOpen.value = false;
  } catch {
    if (request.current()) showError(t('documentShares.operationFailed'));
  } finally {
    if (request.current()) saving.value = false;
    request.finish();
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
    :show-confirm="canCreate"
    @confirm="submitCreate"
  >
    <el-form :disabled="saving" data-testid="document-share-editor-form" label-width="120px">
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
      <el-form-item v-else required :label="t('documentShares.documentLabel')">
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
      <el-form-item required :label="t('documentShares.validDays')">
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
