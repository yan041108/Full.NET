<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElMessage,
  ElTable,
  ElTableColumn
} from 'element-plus';
import type {
  FullNetProblemDetails,
  LogFileSummary,
  LogFileTail
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  downloadObservabilityLogFile,
  listObservabilityLogFiles,
  tailObservabilityLogFile
} from '../api/observability-log-files';

defineOptions({ name: 'ObservabilityLogFilesView' });

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const files = ref<LogFileSummary[]>([]);
const selected = ref<LogFileSummary>();
const tail = ref<LogFileTail>();
const loading = ref(false);
const downloading = ref(false);
const problem = ref<FullNetProblemDetails>();
const canRead = computed(() => session.can('observability.log_files.read'));
const canDownload = computed(() => session.can('observability.log_files.download'));

/** 将未知异常统一折叠为可渲染的 ProblemDetails，避免页面分支散落多套兜底文案。 */
function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        type: 'about:blank',
        title: t('observabilityLogFiles.loadFailed'),
        status: 500,
        code: 'client.unexpected_error'
      };
}

/** 始终按当前活动语言格式化 UTC 时间，避免切换语言后仍残留旧 locale 的展示结果。 */
function formatUtc(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

/** 仅用于日志文件列表展示的轻量字节格式化；预览正文仍保持服务端原始内容。 */
function formatBytes(value: number): string {
  if (value < 1024) {
    return `${value} B`;
  }
  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KiB`;
  }
  return `${(value / 1024 / 1024).toFixed(1)} MiB`;
}

/** 切换选中文件时先清空旧尾部内容，避免新请求失败后继续误看上一份日志。 */
async function loadTail(file: LogFileSummary): Promise<void> {
  selected.value = file;
  tail.value = undefined;
  problem.value = undefined;
  try {
    tail.value = await tailObservabilityLogFile(file.id, 200, 262144);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  }
}

/** 首次加载与手动刷新共用同一入口，并在无读取权限时失败关闭。 */
async function load(): Promise<void> {
  if (!canRead.value || loading.value) {
    return;
  }
  loading.value = true;
  problem.value = undefined;
  try {
    files.value = await listObservabilityLogFiles();
    const current = selected.value
      ? files.value.find(file => file.id === selected.value?.id)
      : undefined;
    const target = current ?? files.value[0];
    if (target) {
      await loadTail(target);
    } else {
      selected.value = undefined;
      tail.value = undefined;
    }
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

/** 下载只允许作用于当前选中的文件，并复用浏览器原生保存流程而不把 Blob 常驻内存。 */
async function download(): Promise<void> {
  if (!selected.value || !canDownload.value || downloading.value) {
    return;
  }
  downloading.value = true;
  problem.value = undefined;
  try {
    const blob = await downloadObservabilityLogFile(selected.value.id);
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = selected.value.fileName;
    link.click();
    URL.revokeObjectURL(url);
  } catch (error: unknown) {
    problem.value = toProblem(error);
    ElMessage.error(problem.value.title);
  } finally {
    downloading.value = false;
  }
}

onMounted(() => {
  void load();
});
</script>

<template>
  <section class="observability-log-files art-page-stack" :aria-busy="loading">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('observabilityLogFiles.eyebrow') }}</p>
      <h1>{{ t('observabilityLogFiles.title') }}</h1>
      <p>{{ t('observabilityLogFiles.description') }}</p>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <ElCard class="art-card observability-log-files__card">
      <div class="observability-log-files__toolbar">
        <ElButton :loading="loading" @click="load">
          {{ t('observabilityLogFiles.refresh') }}
        </ElButton>
        <ElButton
          v-if="canDownload && selected"
          data-testid="observability-log-download"
          :loading="downloading"
          @click="download"
        >
          {{ t('observabilityLogFiles.download') }}
        </ElButton>
      </div>

      <div class="observability-log-files__layout">
        <ElTable
          v-loading="loading"
          :data="files"
          highlight-current-row
          @row-click="loadTail"
        >
          <ElTableColumn prop="fileName" :label="t('observabilityLogFiles.fileName')" min-width="180" />
          <ElTableColumn :label="t('observabilityLogFiles.size')" width="110">
            <template #default="{ row }">{{ formatBytes(row.sizeBytes) }}</template>
          </ElTableColumn>
          <ElTableColumn :label="t('observabilityLogFiles.modifiedAt')" min-width="160">
            <template #default="{ row }">{{ formatUtc(row.lastModifiedUtc) }}</template>
          </ElTableColumn>
        </ElTable>

        <div class="observability-log-files__tail">
          <h2>{{ selected?.fileName ?? t('observabilityLogFiles.noSelection') }}</h2>
          <p v-if="tail?.isTruncated" class="observability-log-files__notice">
            {{ t('observabilityLogFiles.truncated') }}
          </p>
          <pre translate="no">{{ tail?.content ?? '' }}</pre>
        </div>
      </div>
    </ElCard>
  </section>
</template>

<style scoped>
.observability-log-files__toolbar {
  display: flex;
  gap: 8px;
  margin-bottom: 12px;
}

.observability-log-files__layout {
  display: grid;
  grid-template-columns: minmax(420px, 1fr) minmax(420px, 1.2fr);
  gap: 16px;
}

.observability-log-files__tail {
  min-width: 0;
}

.observability-log-files__tail h2 {
  margin: 0 0 8px;
  font-size: 15px;
}

.observability-log-files__tail pre {
  min-height: 360px;
  max-height: 65vh;
  margin: 0;
  padding: 14px;
  overflow: auto;
  border-radius: 8px;
  color: #d7e0ea;
  background: #111827;
  font: 12px/1.55 ui-monospace, SFMono-Regular, Consolas, monospace;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}

.observability-log-files__notice {
  color: var(--el-color-warning);
}

@media (max-width: 960px) {
  .observability-log-files__layout {
    grid-template-columns: 1fr;
  }
}
</style>
