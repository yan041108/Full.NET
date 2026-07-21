<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
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
  <section class="org-units-view" :aria-busy="loading">
    <header class="org-units-heading">
      <div>
        <p>{{ t('orgUnits.eyebrow') }}</p>
        <h1 data-route-heading tabindex="-1">{{ t('orgUnits.title') }}</h1>
        <span>{{ t('orgUnits.description') }}</span>
      </div>
    </header>

    <div v-if="problem" class="org-units-problem" role="alert">
      <strong translate="no">{{ problem.code }}</strong><span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <section v-if="canWrite" class="create-strip" aria-labelledby="create-title">
      <div><small>01</small><h2 id="create-title">{{ t('orgUnits.createTitle') }}</h2></div>
      <label>
        <span>{{ t('orgUnits.code') }}</span>
        <el-input v-model="code" :placeholder="t('orgUnits.codePlaceholder')" />
      </label>
      <label>
        <span>{{ t('orgUnits.name') }}</span>
        <el-input
          v-model="name"
          :placeholder="t('orgUnits.namePlaceholder')"
          @keyup.enter="create"
        />
      </label>
      <el-button type="primary" :loading="changing" @click="create">
        {{ t('orgUnits.create') }}
      </el-button>
    </section>

    <section class="identity-ledger">
      <header>
        <div><small>02</small><h2>{{ t('orgUnits.directoryTitle') }}</h2></div>
        <b>{{ units.length }}</b>
      </header>
      <p v-if="units.length === 0" class="org-units-empty">{{ t('orgUnits.emptyDirectory') }}</p>
      <article v-for="unit in units" :key="unit.id">
        <span class="identity-mark">{{ unit.code.slice(0, 2).toUpperCase() }}</span>
        <div>
          <strong translate="no">{{ unit.name }}</strong>
          <code translate="no">{{ unit.code }}</code>
        </div>
        <div class="org-units-tags">
          <el-tag :type="unit.isActive ? 'success' : 'info'">
            {{ t(unit.isActive ? 'orgUnits.active' : 'orgUnits.inactive') }}
          </el-tag>
        </div>
        <div class="org-units-actions">
          <el-button v-if="canWrite" plain :disabled="changing" @click="edit(unit)">
            {{ t('orgUnits.edit') }}
          </el-button>
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
    </section>
  </section>
</template>

<style scoped>
.org-units-view { display: grid; gap: 18px; }
.org-units-heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 24px; padding: 8px 2px; }
.org-units-heading p { margin: 0 0 10px; color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 10px; font-weight: 700; letter-spacing: .2em; }
.org-units-heading h1 { margin: 0; font-family: var(--fullnet-font-display); font-size: clamp(30px, 4vw, 48px); font-weight: 500; letter-spacing: -.05em; }
.org-units-heading span { display: block; margin-top: 10px; color: var(--fullnet-color-ink-muted); font-size: 13px; }
.org-units-problem { display: flex; gap: 14px; padding: 13px 16px; border-left: 3px solid var(--fullnet-color-danger); background: rgb(201 74 74 / 8%); }
.org-units-problem code { margin-left: auto; }
.org-units-tags { display: flex; gap: 8px; flex-wrap: wrap; }
.org-units-actions { display: flex; gap: 8px; justify-content: flex-end; flex-wrap: wrap; }
.org-units-empty { padding: 28px; margin: 0; text-align: center; color: var(--fullnet-color-ink-muted); }
@media (min-width: 960px) {
  .identity-ledger article .org-units-actions { grid-column: 2 / -1; }
}
</style>
