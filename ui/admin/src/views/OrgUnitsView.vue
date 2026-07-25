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
  type OrganizationUnit
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createOrganizationUnit,
  disableOrganizationUnit,
  listOrganizationUnits,
  updateOrganizationUnit
} from '../api/org-units';

const session = useSessionStore();
const { t } = useAdminI18n();
const units = ref<OrganizationUnit[]>([]);
const code = ref('');
const name = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canWrite = computed(() => session.can('organization.units.write'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listOrganizationUnits();
    units.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUnits.loadFailed');
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
    await createOrganizationUnit(code.value.trim(), name.value.trim());
    code.value = '';
    name.value = '';
    ElMessage.success(t('orgUnits.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'orgUnits.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function edit(unit: OrganizationUnit): Promise<void> {
  if (changing.value) return;
  try {
    const result = await ElMessageBox.prompt(
      t('orgUnits.editTitle'),
      t('orgUnits.edit'),
      {
        inputValue: unit.name,
        inputPattern: /.+/,
        showCancelButton: true
      }
    );
    changing.value = true;
    await updateOrganizationUnit(
      unit.id,
      result.value.trim(),
      unit.displayOrder,
      unit.version
    );
    ElMessage.success(t('orgUnits.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'orgUnits.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(unit: OrganizationUnit): Promise<void> {
  if (changing.value || !unit.isActive) return;
  try {
    await ElMessageBox.confirm(
      t('orgUnits.confirmDisable', { name: unit.code }),
      t('orgUnits.disable'),
      {
        type: 'warning',
        confirmButtonText: t('orgUnits.disable'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await disableOrganizationUnit(unit.id);
    ElMessage.success(t('orgUnits.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'orgUnits.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'orgUnits.loadFailed' | 'orgUnits.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.organization_unit_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="org-units-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('orgUnits.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canWrite" class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-2" aria-labelledby="create-title">
        <div><h2 id="create-title">{{ t('orgUnits.createTitle') }}</h2></div>
        <label>
          <span>{{ t('orgUnits.code') }}</span>
          <el-input v-model="code" :placeholder="t('orgUnits.codePlaceholder')" />
        </label>
        <label>
          <span>{{ t('orgUnits.name') }}</span>
          <el-input v-model="name" :placeholder="t('orgUnits.namePlaceholder')" @keyup.enter="create" />
        </label>
        <el-button type="primary" :loading="changing" @click="create">{{ t('orgUnits.create') }}</el-button>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('orgUnits.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ units.length }}</span>
        </div>
      </template>

      <p v-if="units.length === 0" class="art-empty-state">{{ t('orgUnits.emptyDirectory') }}</p>
      <article v-for="unit in units" :key="unit.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ unit.code.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ unit.name }}</strong>
          <code translate="no">{{ unit.code }}</code>
        </div>
        <el-tag :type="unit.isActive ? 'success' : 'info'">
          {{ t(unit.isActive ? 'orgUnits.active' : 'orgUnits.inactive') }}
        </el-tag>
        <div class="art-data-row__actions">
          <el-button v-if="canWrite" plain :disabled="changing" @click="edit(unit)">{{ t('orgUnits.edit') }}</el-button>
          <el-button
            v-if="canWrite && unit.isActive"
            type="danger"
            plain
            :disabled="changing"
            @click="disable(unit)"
          >
            {{ t('orgUnits.disable') }}
          </el-button>
        </div>
      </article>
    </el-card>
  </section>
</template>
