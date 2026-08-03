<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElInput, ElMessage, ElMessageBox } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentItem } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostDocumentItem,
  deleteHostDocumentItem,
  downloadHostDocumentContent,
  listHostDocumentItems,
  openHostDocumentBlob,
  restoreHostDocumentItem,
  updateHostDocumentItem,
  uploadHostDocumentVersion
} from '../api/host-document-items';
import PermissionGate from '../components/PermissionGate.vue';

interface DeletedDocumentEntry {
  item: HostDocumentItem;
  restoreVersion: number;
}

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
const editingId = ref<string>();
const recentlyDeleted = ref<DeletedDocumentEntry[]>([]);
const canCreate = computed(() => session.can('document.host_documents.create'));
const canUpdate = computed(() => session.can('document.host_documents.update'));
const canAddVersion = computed(() => session.can('document.host_documents.add_version'));
const canDelete = computed(() => session.can('document.host_documents.delete'));
const canRestore = computed(() => session.can('document.host_documents.restore'));
const canDownload = computed(() => session.can('document.host_documents.download'));
const editingItem = computed(() =>
  items.value.find(entry => entry.id === editingId.value)
);

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
  if (changing.value || !canCreate.value || !title.value.trim()) return;
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
  if (
    changing.value
    || !canAddVersion.value
    || !versionFile.value
    || versionTargetId.value !== item.id
  ) return;
  changing.value = true;
  problem.value = undefined;
  try {
    await uploadHostDocumentVersion(item.id, versionFile.value);
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
  if (changing.value || !canDelete.value) return;
  try {
    await ElMessageBox.confirm(
      t('hostDocumentItems.confirmDelete', { name: item.title }),
      t('hostDocumentItems.delete'),
      { type: 'warning', confirmButtonText: t('hostDocumentItems.delete'), cancelButtonText: t('status.back') }
    );
    changing.value = true;
    await deleteHostDocumentItem(item.id, item.version);
    recentlyDeleted.value = [
      { item, restoreVersion: item.version + 1 },
      ...recentlyDeleted.value.filter(entry => entry.item.id !== item.id)
    ];
    ElMessage.success(t('hostDocumentItems.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

function startEdit(item: HostDocumentItem): void {
  editingId.value = item.id;
  title.value = item.title;
  description.value = item.description ?? '';
}

function cancelEdit(): void {
  editingId.value = undefined;
  title.value = '';
  description.value = '';
}

async function saveEdit(item: HostDocumentItem): Promise<void> {
  if (changing.value || !canUpdate.value || editingId.value !== item.id) return;
  changing.value = true;
  problem.value = undefined;
  try {
    await updateHostDocumentItem(
      item.id,
      title.value.trim(),
      description.value.trim() || null,
      item.version
    );
    editingId.value = undefined;
    title.value = '';
    description.value = '';
    ElMessage.success(t('hostDocumentItems.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function restoreDeleted(entry: DeletedDocumentEntry): Promise<void> {
  if (changing.value || !canRestore.value) return;
  changing.value = true;
  problem.value = undefined;
  try {
    await restoreHostDocumentItem(entry.item.id, entry.restoreVersion);
    recentlyDeleted.value = recentlyDeleted.value.filter(
      candidate => candidate.item.id !== entry.item.id
    );
    ElMessage.success(t('hostDocumentItems.restoreSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function downloadFile(itemId: string): Promise<void> {
  if (changing.value || !canDownload.value) return;
  changing.value = true;
  problem.value = undefined;
  try {
    const blob = await downloadHostDocumentContent(itemId);
    openHostDocumentBlob(blob);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostDocumentItems.operationFailed');
  } finally {
    changing.value = false;
  }
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

    <el-card v-if="canCreate && !editingId" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-3" aria-labelledby="create-document-item-title">
        <div><h2 id="create-document-item-title">{{ t('hostDocumentItems.createTitle') }}</h2></div>
        <label>
          <span>{{ t('hostDocumentItems.titleLabel') }}</span>
          <el-input v-model="title" data-testid="host-document-item-title" :placeholder="t('hostDocumentItems.titlePlaceholder')" />
        </label>
        <label>
          <span>{{ t('hostDocumentItems.descriptionLabel') }}</span>
          <el-input v-model="description" data-testid="host-document-item-description" :placeholder="t('hostDocumentItems.descriptionPlaceholder')" />
        </label>
        <PermissionGate code="document.host_documents.create">
          <el-button type="primary" data-testid="host-document-item-create" :loading="changing" @click="create">
            {{ t('hostDocumentItems.create') }}
          </el-button>
        </PermissionGate>
      </div>
    </el-card>

    <el-card v-if="editingId && canUpdate" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-3" aria-labelledby="edit-document-item-title">
        <div><h2 id="edit-document-item-title">{{ t('hostDocumentItems.editTitle') }}</h2></div>
        <label>
          <span>{{ t('hostDocumentItems.titleLabel') }}</span>
          <el-input v-model="title" data-testid="host-document-item-edit-title" />
        </label>
        <label>
          <span>{{ t('hostDocumentItems.descriptionLabel') }}</span>
          <el-input v-model="description" data-testid="host-document-item-edit-description" />
        </label>
        <PermissionGate code="document.host_documents.update">
          <el-button type="primary" data-testid="host-document-item-save" :loading="changing" :disabled="!editingItem" @click="editingItem && saveEdit(editingItem)">
            {{ t('hostDocumentItems.save') }}
          </el-button>
        </PermissionGate>
        <el-button plain :disabled="changing" @click="cancelEdit">{{ t('hostDocumentItems.cancel') }}</el-button>
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
          <PermissionGate v-if="item.currentVersion" code="document.host_documents.download">
            <el-button
              plain
              data-testid="host-document-item-download"
              :disabled="changing"
              @click="downloadFile(item.id)"
            >
              {{ t('hostDocumentItems.download') }}
            </el-button>
          </PermissionGate>
          <PermissionGate code="document.host_documents.update">
            <el-button plain data-testid="host-document-item-edit" @click="startEdit(item)">
              {{ t('hostDocumentItems.edit') }}
            </el-button>
          </PermissionGate>
          <PermissionGate v-if="canAddVersion" code="document.host_documents.add_version">
            <label>
              <span class="art-sr-heading">{{ t('hostDocumentItems.chooseVersionFile') }}</span>
              <input type="file" data-testid="host-document-item-version-file" @change="onVersionFileSelected($event, item.id)" />
            </label>
            <el-button
              plain
              data-testid="host-document-item-upload-version"
              :disabled="changing || !versionFile || versionTargetId !== item.id"
              @click="uploadVersion(item)"
            >
              {{ t('hostDocumentItems.uploadVersion') }}
            </el-button>
          </PermissionGate>
          <PermissionGate code="document.host_documents.delete">
            <el-button type="danger" plain data-testid="host-document-item-delete" :disabled="changing" @click="remove(item)">
              {{ t('hostDocumentItems.delete') }}
            </el-button>
          </PermissionGate>
        </div>
      </article>
    </el-card>

    <el-card v-if="recentlyDeleted.length && canRestore" class="art-table-card" shadow="never">
      <template #header>
        <h2>{{ t('hostDocumentItems.recentlyDeletedTitle') }}</h2>
      </template>
      <article v-for="entry in recentlyDeleted" :key="entry.item.id" class="art-data-row">
        <div class="art-data-row__main">
          <strong translate="no">{{ entry.item.title }}</strong>
        </div>
        <PermissionGate code="document.host_documents.restore">
          <el-button plain data-testid="host-document-item-restore" :disabled="changing" @click="restoreDeleted(entry)">
            {{ t('hostDocumentItems.restore') }}
          </el-button>
        </PermissionGate>
      </article>
    </el-card>
  </section>
</template>
