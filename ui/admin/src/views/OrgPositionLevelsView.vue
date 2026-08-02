<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElButton, ElCard, ElInput, ElMessage, ElMessageBox, ElTag } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type OrganizationPositionLevel
} from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createOrganizationPositionLevel,
  disableOrganizationPositionLevel,
  listOrganizationPositionLevels,
  updateOrganizationPositionLevel
} from '../api/org-position-levels';
import PermissionGate from '../components/PermissionGate.vue';

const { t } = useAdminI18n();
const levels = ref<OrganizationPositionLevel[]>([]);
const code = ref('');
const name = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    levels.value = (await listOrganizationPositionLevels()).items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgPositionLevels.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value || !code.value.trim() || !name.value.trim()) return;
  changing.value = true;
  try {
    await createOrganizationPositionLevel(code.value.trim(), name.value.trim());
    code.value = '';
    name.value = '';
    ElMessage.success(t('orgPositionLevels.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgPositionLevels.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function edit(level: OrganizationPositionLevel): Promise<void> {
  try {
    const result = await ElMessageBox.prompt(
      t('orgPositionLevels.editTitle'),
      t('orgPositionLevels.edit'),
      { inputValue: level.name, inputPattern: /.+/ }
    );
    changing.value = true;
    await updateOrganizationPositionLevel(
      level.id,
      result.value.trim(),
      level.displayOrder,
      level.version
    );
    ElMessage.success(t('orgPositionLevels.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'orgPositionLevels.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(level: OrganizationPositionLevel): Promise<void> {
  try {
    await ElMessageBox.confirm(
      t('orgPositionLevels.confirmDisable', { name: level.code }),
      t('orgPositionLevels.disable'),
      { type: 'warning' }
    );
    changing.value = true;
    await disableOrganizationPositionLevel(level.id);
    ElMessage.success(t('orgPositionLevels.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'orgPositionLevels.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'orgPositionLevels.loadFailed' | 'orgPositionLevels.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.organization_position_level_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="org-position-levels-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">
      {{ t('orgPositionLevels.title') }}
    </h1>
    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>
    <PermissionGate code="organization.position_levels.create">
      <el-card class="art-form-card" shadow="never">
        <div class="art-form-grid art-form-grid--cols-2">
          <div><h2>{{ t('orgPositionLevels.createTitle') }}</h2></div>
          <label>
            <span>{{ t('orgPositionLevels.code') }}</span>
            <el-input v-model="code" :placeholder="t('orgPositionLevels.codePlaceholder')" />
          </label>
          <label>
            <span>{{ t('orgPositionLevels.name') }}</span>
            <el-input v-model="name" :placeholder="t('orgPositionLevels.namePlaceholder')" @keyup.enter="create" />
          </label>
          <el-button type="primary" :loading="changing" @click="create">
            {{ t('orgPositionLevels.create') }}
          </el-button>
        </div>
      </el-card>
    </PermissionGate>
    <el-card class="art-table-card" shadow="never">
      <template #header><h2>{{ t('orgPositionLevels.directoryTitle') }}</h2></template>
      <p v-if="levels.length === 0" class="art-empty-state">
        {{ t('orgPositionLevels.emptyDirectory') }}
      </p>
      <article v-for="level in levels" :key="level.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ level.code.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ level.name }}</strong>
          <code translate="no">{{ level.code }}</code>
        </div>
        <el-tag :type="level.isActive ? 'success' : 'info'">
          {{ t(level.isActive ? 'orgPositionLevels.active' : 'orgPositionLevels.inactive') }}
        </el-tag>
        <div class="art-data-row__actions">
          <PermissionGate code="organization.position_levels.update">
            <el-button plain :disabled="changing" @click="edit(level)">
              {{ t('orgPositionLevels.edit') }}
            </el-button>
          </PermissionGate>
          <PermissionGate v-if="level.isActive" code="organization.position_levels.disable">
            <el-button
              type="danger"
              plain
              :disabled="changing"
              @click="disable(level)"
            >
              {{ t('orgPositionLevels.disable') }}
            </el-button>
          </PermissionGate>
        </div>
      </article>
    </el-card>
  </section>
</template>
