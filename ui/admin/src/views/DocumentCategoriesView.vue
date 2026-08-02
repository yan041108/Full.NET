<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElInput, ElMessage, ElMessageBox } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentCategory } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostDocumentCategory,
  deleteHostDocumentCategory,
  listHostDocumentCategories,
  updateHostDocumentCategory
} from '../api/host-document-categories';

const session = useSessionStore();
const { t } = useAdminI18n();
const categories = ref<HostDocumentCategory[]>([]);
const name = ref('');
const sortOrder = ref('0');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canManage = computed(() => session.can('document.categories.manage'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    categories.value = await listHostDocumentCategories();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value || !name.value.trim()) return;
  changing.value = true;
  problem.value = undefined;
  try {
    await createHostDocumentCategory(
      name.value.trim(),
      null,
      Number.parseInt(sortOrder.value, 10) || 0
    );
    name.value = '';
    sortOrder.value = '0';
    ElMessage.success(t('documentCategories.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'documentCategories.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function edit(category: HostDocumentCategory): Promise<void> {
  if (changing.value) return;
  try {
    const result = await ElMessageBox.prompt(
      t('documentCategories.editTitle'),
      t('documentCategories.edit'),
      { inputValue: category.name, inputPattern: /.+/, showCancelButton: true }
    );
    changing.value = true;
    await updateHostDocumentCategory(
      category.id,
      result.value.trim(),
      category.parentId,
      category.sortOrder,
      category.version
    );
    ElMessage.success(t('documentCategories.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'documentCategories.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function remove(category: HostDocumentCategory): Promise<void> {
  if (changing.value) return;
  try {
    await ElMessageBox.confirm(
      t('documentCategories.confirmDelete', { name: category.name }),
      t('documentCategories.delete'),
      { type: 'warning', confirmButtonText: t('documentCategories.delete'), cancelButtonText: t('status.back') }
    );
    changing.value = true;
    await deleteHostDocumentCategory(category.id, category.version);
    ElMessage.success(t('documentCategories.deleteSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'documentCategories.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'documentCategories.loadFailed' | 'documentCategories.operationFailed' = 'documentCategories.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_document_category_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="document-categories-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentCategories.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canManage" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-3" aria-labelledby="create-document-category-title">
        <div><h2 id="create-document-category-title">{{ t('documentCategories.createTitle') }}</h2></div>
        <label>
          <span>{{ t('documentCategories.name') }}</span>
          <el-input v-model="name" :placeholder="t('documentCategories.namePlaceholder')" />
        </label>
        <label>
          <span>{{ t('documentCategories.sortOrder') }}</span>
          <el-input v-model="sortOrder" type="number" />
        </label>
        <el-button type="primary" :loading="changing" @click="create">{{ t('documentCategories.create') }}</el-button>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('documentCategories.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ categories.length }}</span>
        </div>
      </template>

      <p v-if="categories.length === 0" class="art-empty-state">{{ t('documentCategories.emptyDirectory') }}</p>
      <article v-for="category in categories" :key="category.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ category.name.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ category.name }}</strong>
          <small class="art-data-row__meta" translate="no">
            {{ t('documentCategories.sortOrder') }}: {{ category.sortOrder }}
          </small>
        </div>
        <div v-if="canManage" class="art-data-row__actions">
          <el-button plain :disabled="changing" @click="edit(category)">{{ t('documentCategories.edit') }}</el-button>
          <el-button type="danger" plain :disabled="changing" @click="remove(category)">
            {{ t('documentCategories.delete') }}
          </el-button>
        </div>
      </article>
    </el-card>
  </section>
</template>
