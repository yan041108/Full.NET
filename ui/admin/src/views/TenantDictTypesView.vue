<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElTag
} from 'element-plus';
import type {
  FullNetProblemDetails,
  SettingsDictItem,
  SettingsDictType
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createSettingsTenantDictItem,
  createSettingsTenantDictType,
  disableSettingsTenantDictItem,
  disableSettingsTenantDictType,
  listSettingsTenantDictItems,
  listSettingsTenantDictTypes,
  updateSettingsTenantDictItem,
  updateSettingsTenantDictType
} from '../api/tenant-dict-types';

const session = useSessionStore();
const { t } = useAdminI18n();
const dictTypes = ref<SettingsDictType[]>([]);
const code = ref('');
const name = ref('');
const description = ref('');
const displayOrder = ref('0');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canWrite = computed(() => session.can('settings.tenant_dict_types.write'));

const selectedType = ref<SettingsDictType>();
const dictItems = ref<SettingsDictItem[]>([]);
const itemsLoading = ref(false);
const itemLabel = ref('');
const itemValue = ref('');
const itemColor = ref('');
const itemDisplayOrder = ref('0');

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listSettingsTenantDictTypes();
    dictTypes.value = page.items;
    if (selectedType.value) {
      const refreshed = page.items.find(item => item.id === selectedType.value!.id);
      selectedType.value = refreshed;
      if (refreshed) {
        await loadItems(refreshed.id);
      } else {
        dictItems.value = [];
      }
    }
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dictTypes.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function loadItems(dictTypeId: string): Promise<void> {
  itemsLoading.value = true;
  try {
    const page = await listSettingsTenantDictItems(dictTypeId);
    dictItems.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dictItems.loadFailed');
  } finally {
    itemsLoading.value = false;
  }
}

async function openItems(dictType: SettingsDictType): Promise<void> {
  selectedType.value = dictType;
  problem.value = undefined;
  await loadItems(dictType.id);
}

function closeItems(): void {
  selectedType.value = undefined;
  dictItems.value = [];
  itemLabel.value = '';
  itemValue.value = '';
  itemColor.value = '';
  itemDisplayOrder.value = '0';
}

async function create(): Promise<void> {
  if (changing.value || !code.value.trim() || !name.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createSettingsTenantDictType(
      code.value.trim().toLowerCase(),
      name.value.trim(),
      description.value.trim() || null,
      Number.parseInt(displayOrder.value, 10) || 0
    );
    code.value = '';
    name.value = '';
    description.value = '';
    displayOrder.value = '0';
    ElMessage.success(t('dictTypes.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dictTypes.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function edit(dictType: SettingsDictType): Promise<void> {
  if (changing.value) return;
  try {
    const result = await ElMessageBox.prompt(
      t('dictTypes.editTitle'),
      t('dictTypes.edit'),
      {
        inputValue: dictType.name,
        inputPattern: /.+/,
        showCancelButton: true
      }
    );
    changing.value = true;
    await updateSettingsTenantDictType(
      dictType.id,
      result.value.trim(),
      dictType.description,
      dictType.displayOrder,
      dictType.version
    );
    ElMessage.success(t('dictTypes.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'dictTypes.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(dictType: SettingsDictType): Promise<void> {
  if (changing.value || !dictType.isActive) return;
  try {
    await ElMessageBox.confirm(
      t('dictTypes.confirmDisable', { name: dictType.code }),
      t('dictTypes.disable'),
      {
        type: 'warning',
        confirmButtonText: t('dictTypes.disable'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await disableSettingsTenantDictType(dictType.id);
    ElMessage.success(t('dictTypes.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'dictTypes.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function createItem(): Promise<void> {
  const dictType = selectedType.value;
  if (changing.value || !dictType || !itemLabel.value.trim() || !itemValue.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createSettingsTenantDictItem(
      dictType.id,
      itemLabel.value.trim(),
      itemValue.value.trim().toLowerCase(),
      itemColor.value.trim() || null,
      Number.parseInt(itemDisplayOrder.value, 10) || 0
    );
    itemLabel.value = '';
    itemValue.value = '';
    itemColor.value = '';
    itemDisplayOrder.value = '0';
    ElMessage.success(t('dictItems.createSuccess'));
    await loadItems(dictType.id);
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dictItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function editItem(item: SettingsDictItem): Promise<void> {
  if (changing.value || !selectedType.value) return;
  try {
    const result = await ElMessageBox.prompt(
      t('dictItems.editTitle'),
      t('dictItems.edit'),
      {
        inputValue: item.label,
        inputPattern: /.+/,
        showCancelButton: true
      }
    );
    changing.value = true;
    await updateSettingsTenantDictItem(
      item.id,
      result.value.trim(),
      item.color,
      item.displayOrder,
      item.version
    );
    ElMessage.success(t('dictItems.updateSuccess'));
    await loadItems(selectedType.value.id);
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'dictItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disableItem(item: SettingsDictItem): Promise<void> {
  if (changing.value || !item.isActive || !selectedType.value) return;
  try {
    await ElMessageBox.confirm(
      t('dictItems.confirmDisable', { name: item.value }),
      t('dictItems.disable'),
      {
        type: 'warning',
        confirmButtonText: t('dictItems.disable'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await disableSettingsTenantDictItem(item.id);
    ElMessage.success(t('dictItems.disableSuccess'));
    await loadItems(selectedType.value.id);
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'dictItems.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey:
    | 'dictTypes.loadFailed'
    | 'dictTypes.operationFailed'
    | 'dictItems.loadFailed'
    | 'dictItems.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.settings_dict_type_failed',
        title: t(fallbackKey)
      };
}
</script>

<template>
  <section class="dict-types-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('dictTypes.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canWrite" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-3" aria-labelledby="create-dict-type-title">
        <div><h2 id="create-dict-type-title">{{ t('dictTypes.createTitle') }}</h2></div>
        <label>
          <span>{{ t('dictTypes.code') }}</span>
          <el-input v-model="code" :placeholder="t('dictTypes.codePlaceholder')" />
        </label>
        <label>
          <span>{{ t('dictTypes.name') }}</span>
          <el-input v-model="name" :placeholder="t('dictTypes.namePlaceholder')" />
        </label>
        <label>
          <span>{{ t('dictTypes.descriptionLabel') }}</span>
          <el-input v-model="description" :placeholder="t('dictTypes.descriptionPlaceholder')" />
        </label>
        <label>
          <span>{{ t('dictTypes.displayOrder') }}</span>
          <el-input v-model="displayOrder" type="number" @keyup.enter="create" />
        </label>
        <el-button type="primary" :loading="changing" @click="create">{{ t('dictTypes.create') }}</el-button>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('dictTypes.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ dictTypes.length }}</span>
        </div>
      </template>

      <p v-if="dictTypes.length === 0" class="art-empty-state">{{ t('dictTypes.emptyDirectory') }}</p>
      <article v-for="dictType in dictTypes" :key="dictType.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ dictType.code.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ dictType.name }}</strong>
          <code translate="no">{{ dictType.code }}</code>
          <small class="art-data-row__meta" translate="no">
            {{ t('dictTypes.displayOrder') }}: {{ dictType.displayOrder }}
          </small>
          <small v-if="dictType.description" class="art-data-row__meta" translate="no">{{ dictType.description }}</small>
        </div>
        <el-tag :type="dictType.isActive ? 'success' : 'info'">
          {{ t(dictType.isActive ? 'dictTypes.active' : 'dictTypes.inactive') }}
        </el-tag>
        <div class="art-data-row__actions">
          <el-button plain :disabled="changing || itemsLoading" @click="openItems(dictType)">
            {{ t('dictItems.manage') }}
          </el-button>
          <el-button v-if="canWrite" plain :disabled="changing" @click="edit(dictType)">{{ t('dictTypes.edit') }}</el-button>
          <el-button
            v-if="canWrite && dictType.isActive"
            type="danger"
            plain
            :disabled="changing"
            @click="disable(dictType)"
          >
            {{ t('dictTypes.disable') }}
          </el-button>
        </div>
      </article>
    </el-card>

    <el-card
      v-if="selectedType"
      class="art-table-card"
      shadow="never"
      data-dict-items-panel
      :aria-busy="itemsLoading"
    >
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('dictItems.panelTitle', { name: selectedType.code }) }}</h2>
          <el-button plain @click="closeItems">{{ t('dictItems.close') }}</el-button>
        </div>
      </template>

      <div
        v-if="canWrite"
        class="art-form-grid art-form-grid--cols-3"
        aria-labelledby="create-dict-item-title"
        data-dict-items-create-form
      >
        <div><h3 id="create-dict-item-title">{{ t('dictItems.createTitle') }}</h3></div>
        <label>
          <span>{{ t('dictItems.label') }}</span>
          <el-input v-model="itemLabel" :placeholder="t('dictItems.labelPlaceholder')" />
        </label>
        <label>
          <span>{{ t('dictItems.value') }}</span>
          <el-input v-model="itemValue" :placeholder="t('dictItems.valuePlaceholder')" />
        </label>
        <label>
          <span>{{ t('dictItems.color') }}</span>
          <el-input v-model="itemColor" :placeholder="t('dictItems.colorPlaceholder')" />
        </label>
        <label>
          <span>{{ t('dictItems.displayOrder') }}</span>
          <el-input v-model="itemDisplayOrder" type="number" @keyup.enter="createItem" />
        </label>
        <el-button type="primary" :loading="changing" @click="createItem">{{ t('dictItems.create') }}</el-button>
      </div>

      <h3>{{ t('dictItems.directoryTitle') }}</h3>
      <p v-if="dictItems.length === 0" class="art-empty-state" data-dict-items-empty>
        {{ t('dictItems.emptyDirectory') }}
      </p>
      <div data-dict-items-directory>
        <article v-for="item in dictItems" :key="item.id" class="art-data-row">
          <span class="art-data-row__avatar">{{ item.value.slice(0, 2).toUpperCase() }}</span>
          <div class="art-data-row__main">
            <strong translate="no">{{ item.label }}</strong>
            <code translate="no">{{ item.value }}</code>
            <small v-if="item.color" class="art-data-row__meta" translate="no">{{ item.color }}</small>
            <small class="art-data-row__meta" translate="no">
              {{ t('dictItems.displayOrder') }}: {{ item.displayOrder }}
            </small>
          </div>
          <el-tag :type="item.isActive ? 'success' : 'info'">
            {{ t(item.isActive ? 'dictItems.active' : 'dictItems.inactive') }}
          </el-tag>
          <div class="art-data-row__actions">
            <el-button v-if="canWrite" plain :disabled="changing" @click="editItem(item)">
              {{ t('dictItems.edit') }}
            </el-button>
            <el-button
              v-if="canWrite && item.isActive"
              type="danger"
              plain
              :disabled="changing"
              @click="disableItem(item)"
            >
              {{ t('dictItems.disable') }}
            </el-button>
          </div>
        </article>
      </div>
    </el-card>
  </section>
</template>
