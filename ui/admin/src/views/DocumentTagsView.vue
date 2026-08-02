<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElInput, ElMessage, ElMessageBox } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentTag } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostDocumentTag,
  deleteHostDocumentTag,
  listHostDocumentTags,
  updateHostDocumentTag
} from '../api/host-document-tags';
import PermissionGate from '../components/PermissionGate.vue';

const session = useSessionStore();
const { t } = useAdminI18n();
const tags = ref<HostDocumentTag[]>([]);
const name = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canCreate = computed(() => session.can('document.tags.create'));
const canUpdate = computed(() => session.can('document.tags.update'));
const canDelete = computed(() => session.can('document.tags.delete'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    tags.value = await listHostDocumentTags();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value || !canCreate.value || !name.value.trim()) return;
  changing.value = true;
  problem.value = undefined;
  try {
    await createHostDocumentTag(name.value.trim());
    name.value = '';
    ElMessage.success(t('documentTags.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'documentTags.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function edit(tag: HostDocumentTag): Promise<void> {
  if (changing.value || !canUpdate.value) return;
  try {
    const result = await ElMessageBox.prompt(
      t('documentTags.editTitle'),
      t('documentTags.edit'),
      { inputValue: tag.name, inputPattern: /.+/, showCancelButton: true }
    );
    changing.value = true;
    await updateHostDocumentTag(tag.id, result.value.trim(), tag.version);
    ElMessage.success(t('documentTags.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'documentTags.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function remove(tag: HostDocumentTag): Promise<void> {
  if (changing.value || !canDelete.value) return;
  try {
    await ElMessageBox.confirm(
      t('documentTags.confirmDelete', { name: tag.name }),
      t('documentTags.delete'),
      { type: 'warning', confirmButtonText: t('documentTags.delete'), cancelButtonText: t('status.back') }
    );
    changing.value = true;
    await deleteHostDocumentTag(tag.id, tag.version);
    ElMessage.success(t('documentTags.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'documentTags.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'documentTags.loadFailed' | 'documentTags.operationFailed' = 'documentTags.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_document_tag_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="document-tags-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentTags.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canCreate" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-3" aria-labelledby="create-document-tag-title">
        <div><h2 id="create-document-tag-title">{{ t('documentTags.createTitle') }}</h2></div>
        <label>
          <span>{{ t('documentTags.name') }}</span>
          <el-input v-model="name" data-testid="document-tag-name" :placeholder="t('documentTags.namePlaceholder')" />
        </label>
        <PermissionGate code="document.tags.create">
          <el-button type="primary" data-testid="document-tag-create" :loading="changing" @click="create">
            {{ t('documentTags.create') }}
          </el-button>
        </PermissionGate>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('documentTags.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ tags.length }}</span>
        </div>
      </template>

      <p v-if="tags.length === 0" class="art-empty-state">{{ t('documentTags.emptyDirectory') }}</p>
      <article v-for="tag in tags" :key="tag.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ tag.name.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ tag.name }}</strong>
        </div>
        <div class="art-data-row__actions">
          <PermissionGate code="document.tags.update">
            <el-button plain data-testid="document-tag-edit" :disabled="changing" @click="edit(tag)">
              {{ t('documentTags.edit') }}
            </el-button>
          </PermissionGate>
          <PermissionGate code="document.tags.delete">
            <el-button type="danger" plain data-testid="document-tag-delete" :disabled="changing" @click="remove(tag)">
              {{ t('documentTags.delete') }}
            </el-button>
          </PermissionGate>
        </div>
      </article>
    </el-card>
  </section>
</template>
