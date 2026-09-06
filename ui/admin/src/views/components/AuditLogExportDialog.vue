<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElButton, ElDialog, ElForm, ElFormItem, ElInput } from 'element-plus';
import type { FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useAdminI18n } from '../../i18n/adminI18n';
import {
  createDefaultExportRequest,
  exportAuditLogs,
  type AuditLogExportKind
} from '../../api/auditing-export';

const props = defineProps<{
  kind: AuditLogExportKind;
}>();

const open = defineModel<boolean>('open', { default: false });

const { t } = useAdminI18n();
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const fromUtcInput = ref('');
const toUtcInput = ref('');

const dialogTitle = computed(() => t(`auditExport.title.${props.kind}`));

function resetForm(): void {
  const defaults = createDefaultExportRequest();
  fromUtcInput.value = toDateTimeLocal(defaults.fromUtc);
  toUtcInput.value = toDateTimeLocal(defaults.toUtc);
  problem.value = undefined;
}

watch(open, value => {
  if (value) {
    resetForm();
  }
});

function toDateTimeLocal(value: string): string {
  const parsed = new Date(value);
  const local = new Date(parsed.getTime() - parsed.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}

function toUtcIso(value: string): string | undefined {
  if (!value) {
    return undefined;
  }
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.toISOString();
}

function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.click();
  URL.revokeObjectURL(url);
}

async function submitExport(): Promise<void> {
  const fromUtc = toUtcIso(fromUtcInput.value);
  const toUtc = toUtcIso(toUtcInput.value);
  if (!fromUtc || !toUtc) {
    problem.value = {
      status: 400,
      code: 'client.audit_export_invalid_range',
      title: t('auditExport.invalidRange')
    };
    return;
  }

  loading.value = true;
  problem.value = undefined;
  try {
    const blob = await exportAuditLogs(props.kind, { fromUtc, toUtc });
    downloadBlob(blob, `${props.kind}-logs-export.xlsx`);
    open.value = false;
  } catch (error: unknown) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : {
          status: 500,
          code: 'client.audit_export_failed',
          title: t('auditExport.failed')
        };
  } finally {
    loading.value = false;
  }
}

</script>

<template>
  <el-dialog
    v-model="open"
    :title="dialogTitle"
    width="480px"
    append-to-body
  >
    <p class="audit-log-export-dialog__hint">{{ t('auditExport.hint') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-form label-position="top">
      <el-form-item :label="t('accessLogs.fromUtc')">
        <el-input v-model="fromUtcInput" type="datetime-local" />
      </el-form-item>
      <el-form-item :label="t('accessLogs.toUtc')">
        <el-input v-model="toUtcInput" type="datetime-local" />
      </el-form-item>
    </el-form>

    <template #footer>
      <el-button @click="open = false">{{ t('auditExport.cancel') }}</el-button>
      <el-button type="primary" :loading="loading" @click="submitExport">
        {{ t('auditExport.download') }}
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.audit-log-export-dialog__hint {
  margin: 0 0 12px;
  color: var(--art-text-secondary, #667085);
  font-size: 13px;
}
</style>
