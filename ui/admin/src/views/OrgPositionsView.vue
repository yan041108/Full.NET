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
import {
  type FullNetProblemDetails,
  type OrganizationPosition
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createOrganizationPosition,
  disableOrganizationPosition,
  listOrganizationPositions,
  updateOrganizationPosition
} from '../api/org-positions';

const session = useSessionStore();
const { t } = useAdminI18n();
const positions = ref<OrganizationPosition[]>([]);
const code = ref('');
const name = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canWrite = computed(() => session.can('organization.positions.write'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listOrganizationPositions();
    positions.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgPositions.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value || !code.value.trim() || !name.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createOrganizationPosition(code.value.trim(), name.value.trim());
    code.value = '';
    name.value = '';
    ElMessage.success(t('orgPositions.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function edit(position: OrganizationPosition): Promise<void> {
  if (changing.value) return;
  try {
    const result = await ElMessageBox.prompt(
      t('orgPositions.editTitle'),
      t('orgPositions.edit'),
      {
        inputValue: position.name,
        inputPattern: /.+/,
        showCancelButton: true
      }
    );
    changing.value = true;
    await updateOrganizationPosition(
      position.id,
      result.value.trim(),
      position.displayOrder,
      position.version
    );
    ElMessage.success(t('orgPositions.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'orgPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(position: OrganizationPosition): Promise<void> {
  if (changing.value || !position.isActive) return;
  try {
    await ElMessageBox.confirm(
      t('orgPositions.confirmDisable', { name: position.code }),
      t('orgPositions.disable'),
      {
        type: 'warning',
        confirmButtonText: t('orgPositions.disable'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await disableOrganizationPosition(position.id);
    ElMessage.success(t('orgPositions.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'orgPositions.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'orgPositions.loadFailed' | 'orgPositions.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.organization_position_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="org-positions-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('orgPositions.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canWrite" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-2" aria-labelledby="create-title">
        <div><h2 id="create-title">{{ t('orgPositions.createTitle') }}</h2></div>
        <label>
          <span>{{ t('orgPositions.code') }}</span>
          <el-input v-model="code" :placeholder="t('orgPositions.codePlaceholder')" />
        </label>
        <label>
          <span>{{ t('orgPositions.name') }}</span>
          <el-input v-model="name" :placeholder="t('orgPositions.namePlaceholder')" @keyup.enter="create" />
        </label>
        <el-button type="primary" :loading="changing" @click="create">{{ t('orgPositions.create') }}</el-button>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('orgPositions.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ positions.length }}</span>
        </div>
      </template>

      <p v-if="positions.length === 0" class="art-empty-state">{{ t('orgPositions.emptyDirectory') }}</p>
      <article v-for="position in positions" :key="position.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ position.code.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ position.name }}</strong>
          <code translate="no">{{ position.code }}</code>
        </div>
        <el-tag :type="position.isActive ? 'success' : 'info'">
          {{ t(position.isActive ? 'orgPositions.active' : 'orgPositions.inactive') }}
        </el-tag>
        <div class="art-data-row__actions">
          <el-button v-if="canWrite" plain :disabled="changing" @click="edit(position)">
            {{ t('orgPositions.edit') }}
          </el-button>
          <el-button
            v-if="canWrite && position.isActive"
            type="danger"
            plain
            :disabled="changing"
            @click="disable(position)"
          >
            {{ t('orgPositions.disable') }}
          </el-button>
        </div>
      </article>
    </el-card>
  </section>
</template>
