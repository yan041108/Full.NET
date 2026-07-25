<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElMessage, ElMessageBox } from 'element-plus';
import type { FullNetProblemDetails, HostFile } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  deleteHostFile,
  hostFileContentUrl,
  listHostFiles,
  uploadHostFile
} from '../api/host-files';

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostFile[]>([]);
const selectedFile = ref<File | null>(null);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canWrite = computed(() => session.can('files.files.write'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listHostFiles();
    items.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function onFileSelected(event: Event): void {
  const input = event.target as HTMLInputElement;
  selectedFile.value = input.files?.[0] ?? null;
}

async function upload(): Promise<void> {
  if (changing.value || !selectedFile.value) return;
  changing.value = true;
  problem.value = undefined;
  try {
    await uploadHostFile(selectedFile.value);
    selectedFile.value = null;
    ElMessage.success(t('hostFiles.uploadSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostFiles.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function remove(item: HostFile): Promise<void> {
  if (changing.value) return;
  try {
    await ElMessageBox.confirm(
      t('hostFiles.confirmDelete', { name: item.originalFileName }),
      t('hostFiles.delete'),
      {
        type: 'warning',
        confirmButtonText: t('hostFiles.delete'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await deleteHostFile(item.id);
    ElMessage.success(t('hostFiles.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'hostFiles.operationFailed');
  } finally {
    changing.value = false;
  }
}

function download(item: HostFile): void {
  window.open(hostFileContentUrl(item.id), '_blank', 'noopener,noreferrer');
}

function toProblem(
  error: unknown,
  fallbackKey: 'hostFiles.loadFailed' | 'hostFiles.operationFailed' = 'hostFiles.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_file_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="host-files-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('hostFiles.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canWrite" shadow="never" class="art-form-card" aria-labelledby="upload-host-file-title">
      <div><h2 id="upload-host-file-title">{{ t('hostFiles.uploadTitle') }}</h2></div>
      <label>
        <span>{{ t('hostFiles.chooseFile') }}</span>
        <input type="file" @change="onFileSelected" />
      </label>
      <el-button type="primary" :loading="changing" :disabled="!selectedFile" @click="upload">
        {{ t('hostFiles.upload') }}
      </el-button>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('hostFiles.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ items.length }}</span>
        </div>
      </template>

      <p v-if="items.length === 0" class="art-empty-state">{{ t('hostFiles.emptyDirectory') }}</p>
      <article v-for="item in items" :key="item.id" class="art-data-row">
        <div class="art-data-row__main">
          <strong translate="no">{{ item.originalFileName }}</strong>
          <code translate="no">{{ item.contentType }}</code>
          <small>{{ t('hostFiles.sizeBytes') }}: {{ item.sizeBytes }}</small>
          <small>{{ t('hostFiles.createdAt') }}: {{ item.createdAtUtc }}</small>
        </div>
        <div class="art-data-row__actions">
          <el-button plain @click="download(item)">{{ t('hostFiles.download') }}</el-button>
          <el-button
            v-if="canWrite"
            type="danger"
            plain
            :disabled="changing"
            @click="remove(item)"
          >
            {{ t('hostFiles.delete') }}
          </el-button>
        </div>
      </article>
    </el-card>
  </section>
</template>
