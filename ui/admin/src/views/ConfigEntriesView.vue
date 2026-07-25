<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElOption,
  ElSelect,
  ElTag
} from 'element-plus';
import type {
  FullNetProblemDetails,
  SettingsConfigEntry,
  SettingsConfigValueKind
} from '@fullnet/client-contracts';
import {
  SETTINGS_CONFIG_VALUE_KINDS,
  isFullNetProblemDetails
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createSettingsConfigEntry,
  disableSettingsConfigEntry,
  listSettingsConfigEntries,
  updateSettingsConfigEntry
} from '../api/config-entries';

const session = useSessionStore();
const { t } = useAdminI18n();
const entries = ref<SettingsConfigEntry[]>([]);
const configKey = ref('');
const displayName = ref('');
const description = ref('');
const valueKind = ref<SettingsConfigValueKind>('string');
const value = ref('');
const displayOrder = ref('0');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canWrite = computed(() => session.can('settings.config.write'));
const valueKinds = SETTINGS_CONFIG_VALUE_KINDS;

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listSettingsConfigEntries();
    entries.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'configEntries.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (
    changing.value
    || !configKey.value.trim()
    || !displayName.value.trim()
  ) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createSettingsConfigEntry(
      configKey.value.trim().toLowerCase(),
      displayName.value.trim(),
      description.value.trim() || null,
      valueKind.value,
      value.value.trim(),
      Number.parseInt(displayOrder.value, 10) || 0
    );
    configKey.value = '';
    displayName.value = '';
    description.value = '';
    valueKind.value = 'string';
    value.value = '';
    displayOrder.value = '0';
    ElMessage.success(t('configEntries.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'configEntries.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function edit(entry: SettingsConfigEntry): Promise<void> {
  if (changing.value) return;
  try {
    const result = await ElMessageBox.prompt(
      t('configEntries.editTitle'),
      t('configEntries.edit'),
      {
        inputValue: entry.value,
        inputPattern: /[\s\S]+/,
        showCancelButton: true
      }
    );
    changing.value = true;
    await updateSettingsConfigEntry(
      entry.id,
      entry.displayName,
      entry.description,
      result.value,
      entry.displayOrder,
      entry.version
    );
    ElMessage.success(t('configEntries.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'configEntries.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(entry: SettingsConfigEntry): Promise<void> {
  if (changing.value || !entry.isActive) return;
  try {
    await ElMessageBox.confirm(
      t('configEntries.confirmDisable', { name: entry.configKey }),
      t('configEntries.disable'),
      {
        type: 'warning',
        confirmButtonText: t('configEntries.disable'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await disableSettingsConfigEntry(entry.id);
    ElMessage.success(t('configEntries.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'configEntries.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'configEntries.loadFailed' | 'configEntries.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.settings_config_entry_failed',
        title: t(fallbackKey)
      };
}
</script>

<template>
  <section class="config-entries-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('configEntries.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canWrite" shadow="never" class="art-form-card" aria-labelledby="create-config-entry-title">
      <div><h2 id="create-config-entry-title">{{ t('configEntries.createTitle') }}</h2></div>
      <label>
        <span>{{ t('configEntries.configKey') }}</span>
        <el-input v-model="configKey" :placeholder="t('configEntries.configKeyPlaceholder')" />
      </label>
      <label>
        <span>{{ t('configEntries.displayName') }}</span>
        <el-input v-model="displayName" :placeholder="t('configEntries.displayNamePlaceholder')" />
      </label>
      <label>
        <span>{{ t('configEntries.descriptionLabel') }}</span>
        <el-input v-model="description" :placeholder="t('configEntries.descriptionPlaceholder')" />
      </label>
      <label>
        <span>{{ t('configEntries.valueKind') }}</span>
        <el-select v-model="valueKind">
          <el-option v-for="kind in valueKinds" :key="kind" :label="kind" :value="kind" />
        </el-select>
      </label>
      <label>
        <span>{{ t('configEntries.value') }}</span>
        <el-input v-model="value" :placeholder="t('configEntries.valuePlaceholder')" />
      </label>
      <label>
        <span>{{ t('configEntries.displayOrder') }}</span>
        <el-input v-model="displayOrder" type="number" />
      </label>
      <el-button type="primary" :loading="changing" @click="create">{{ t('configEntries.create') }}</el-button>
    </el-card>

    <el-card shadow="never" class="art-table-card">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('configEntries.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ entries.length }}</span>
        </div>
      </template>

      <p v-if="entries.length === 0" class="art-empty-state">{{ t('configEntries.emptyDirectory') }}</p>
      <article v-for="entry in entries" :key="entry.id" class="art-data-row">
        <div class="art-data-row__identity">
          <strong>{{ entry.displayName }}</strong>
          <code>{{ entry.configKey }}</code>
          <small>
            {{ entry.valueKind }} · {{ entry.value }}
            · {{ t('configEntries.displayOrder') }}: {{ entry.displayOrder }}
          </small>
          <small v-if="entry.description">{{ entry.description }}</small>
        </div>
        <el-tag :type="entry.isActive ? 'success' : 'info'" effect="plain">
          {{ t(entry.isActive ? 'configEntries.active' : 'configEntries.inactive') }}
        </el-tag>
        <div class="art-data-row__actions">
          <el-button v-if="canWrite" plain :disabled="changing" @click="edit(entry)">
            {{ t('configEntries.edit') }}
          </el-button>
          <el-button
            v-if="canWrite && entry.isActive"
            type="danger"
            plain
            :disabled="changing"
            @click="disable(entry)"
          >
            {{ t('configEntries.disable') }}
          </el-button>
        </div>
      </article>
    </el-card>
  </section>
</template>
