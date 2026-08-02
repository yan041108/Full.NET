<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElInput, ElMessage, ElMessageBox } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentItem } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  addHostDocumentVersion,
  createHostDocumentItem,
  deleteHostDocumentItem,
  listHostDocumentItems
} from '../api/host-document-items';
import { hostFileContentUrl, uploadHostFile } from '../api/host-files';

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostDocumentItem[]>([]);
const title = ref('');
const description = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const versionFile = ref<File | null>(null);
const versionTargetId = ref<string>();
const canWrite = computed(() => session.can('document.host_documents.write'));
const canDelete = computed(() => session.can('document.host_documents.delete'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listHostDocumentItems();
    items.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value || !title.value.trim()) return;
  changing.value = true;
  problem.value = undefined;
  try {
    await createHostDocumentItem(title.value.trim(), description.value.trim() || null);
    title.value = '';
    description.value = '';
    ElMessage.success(t('hostDocumentItems.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

function onVersionFileSelected(event: Event, itemId: string): void {
  const input = event.target as HTMLInputElement;
  versionFile.value = input.files?.[0] ?? null;
  versionTargetId.value = itemId;
}

async function uploadVersion(item: HostDocumentItem): Promise<void> {
  if (changing.value || !versionFile.value || versionTargetId.value !== item.id) return;
  changing.value = true;
  problem.value = undefined;
  try {
    const file = await uploadHostFile(versionFile.value);
    await addHostDocumentVersion(item.id, file.id);
    versionFile.value = null;
    versionTargetId.value = undefined;
    ElMessage.success(t('hostDocumentItems.versionSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function remove(item: HostDocumentItem): Promise<void> {
  if (changing.value) return;
  try {
    await ElMessageBox.confirm(
      t('hostDocumentItems.confirmDelete', { name: item.title }),
      t('hostDocumentItems.delete'),
      { type: 'warning', confirmButtonText: t('hostDocumentItems.delete'), cancelButtonText: t('status.back') }
    );
    changing.value = true;
    await deleteHostDocumentItem(item.id, item.version);
    ElMessage.success(t('hostDocumentItems.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

function downloadFile(fileId: string): void {
  window.open(hostFileContentUrl(fileId), '_blank', 'noopener,noreferrer');
}

function toProblem(
  error: unknown,
  fallbackKey: 'hostDocumentItems.loadFailed' | 'hostDocumentItems.operationFailed' = 'hostDocumentItems.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_document_item_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="host-document-items-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('hostDocumentItems.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canWrite" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-3" aria-labelledby="create-document-item-title">
        <div><h2 id="create-document-item-title">{{ t('hostDocumentItems.createTitle') }}</h2></div>
        <label>
          <span>{{ t('hostDocumentItems.titleLabel') }}</span>
          <el-input v-model="title" :placeholder="t('hostDocumentItems.titlePlaceholder')" />
        </label>
        <label>
          <span>{{ t('hostDocumentItems.descriptionLabel') }}</span>
          <el-input v-model="description" :placeholder="t('hostDocumentItems.descriptionPlaceholder')" />
        </label>
        <el-button type="primary" :loading="changing" @click="create">{{ t('hostDocumentItems.create') }}</el-button>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('hostDocumentItems.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ items.length }}</span>
        </div>
      </template>

      <p v-if="items.length === 0" class="art-empty-state">{{ t('hostDocumentItems.emptyDirectory') }}</p>
      <article v-for="item in items" :key="item.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ item.title.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ item.title }}</strong>
          <small v-if="item.description" class="art-data-row__meta" translate="no">{{ item.description }}</small>
          <small v-if="item.currentVersion" class="art-data-row__meta" translate="no">
            {{ t('hostDocumentItems.versionLabel') }}: {{ item.currentVersion.versionNumber }}
          </small>
        </div>
        <div class="art-data-row__actions">
          <el-button
            v-if="item.currentVersion"
            plain
            @click="downloadFile(item.currentVersion.fileId)"
          >
            {{ t('hostDocumentItems.download') }}
          </el-button>
          <template v-if="canWrite">
            <label>
              <span class="art-sr-heading">{{ t('hostDocumentItems.uploadVersion') }}</span>
              <input type="file" @change="onVersionFileSelected($event, item.id)" />
            </label>
            <el-button
              plain
              :disabled="changing || !versionFile || versionTargetId !== item.id"
              @click="uploadVersion(item)"
            >
              {{ t('hostDocumentItems.uploadVersion') }}
            </el-button>
          </template>
          <el-button v-if="canDelete" type="danger" plain :disabled="changing" @click="remove(item)">
            {{ t('hostDocumentItems.delete') }}
          </el-button>
        </div>
      </article>
    </el-card>
  </section>
</template>
