<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, HostTenant } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostTenant,
  disableHostTenant,
  listHostTenants,
  updateHostTenant
} from '../api/tenants';

const session = useSessionStore();
const { t } = useAdminI18n();
const tenants = ref<HostTenant[]>([]);
const identifier = ref('');
const name = ref('');
const domain = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canWrite = computed(() => session.can('tenancy.tenants.write'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listHostTenants();
    tenants.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (
    changing.value
    || !identifier.value.trim()
    || !name.value.trim()
    || !domain.value.trim()
  ) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createHostTenant(
      identifier.value.trim().toLowerCase(),
      name.value.trim(),
      domain.value.trim().toLowerCase()
    );
    identifier.value = '';
    name.value = '';
    domain.value = '';
    ElMessage.success(t('tenants.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function edit(tenant: HostTenant): Promise<void> {
  if (changing.value) return;
  try {
    const result = await ElMessageBox.prompt(
      t('tenants.editTitle'),
      t('tenants.edit'),
      {
        inputValue: tenant.name,
        inputPattern: /.+/,
        showCancelButton: true
      }
    );
    changing.value = true;
    await updateHostTenant(tenant.id, result.value.trim(), tenant.version);
    ElMessage.success(t('tenants.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(tenant: HostTenant): Promise<void> {
  if (changing.value || !tenant.isActive) return;
  try {
    await ElMessageBox.confirm(
      t('tenants.confirmDisable', { name: tenant.identifier }),
      t('tenants.disable'),
      {
        type: 'warning',
        confirmButtonText: t('tenants.disable'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await disableHostTenant(tenant.id);
    ElMessage.success(t('tenants.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'tenants.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'tenants.loadFailed' | 'tenants.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_tenant_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="tenants-view" :aria-busy="loading">
    <header class="tenants-heading">
      <div>
        <p>{{ t('tenants.eyebrow') }}</p>
        <h1 data-route-heading tabindex="-1">{{ t('tenants.title') }}</h1>
        <span>{{ t('tenants.description') }}</span>
      </div>
    </header>

    <div v-if="problem" class="tenants-problem" role="alert">
      <strong translate="no">{{ problem.code }}</strong><span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <section v-if="canWrite" class="create-strip" aria-labelledby="create-title">
      <div><small>01</small><h2 id="create-title">{{ t('tenants.createTitle') }}</h2></div>
      <label>
        <span>{{ t('tenants.identifier') }}</span>
        <el-input
          v-model="identifier"
          :placeholder="t('tenants.identifierPlaceholder')"
        />
      </label>
      <label>
        <span>{{ t('tenants.name') }}</span>
        <el-input v-model="name" :placeholder="t('tenants.namePlaceholder')" />
      </label>
      <label>
        <span>{{ t('tenants.domain') }}</span>
        <el-input
          v-model="domain"
          :placeholder="t('tenants.domainPlaceholder')"
          @keyup.enter="create"
        />
      </label>
      <el-button type="primary" :loading="changing" @click="create">
        {{ t('tenants.create') }}
      </el-button>
    </section>

    <section class="identity-ledger">
      <header>
        <div><small>02</small><h2>{{ t('tenants.directoryTitle') }}</h2></div>
        <b>{{ tenants.length }}</b>
      </header>
      <p v-if="tenants.length === 0" class="tenants-empty">{{ t('tenants.emptyDirectory') }}</p>
      <article v-for="tenant in tenants" :key="tenant.id">
        <span class="identity-mark">{{ tenant.identifier.slice(0, 2).toUpperCase() }}</span>
        <div>
          <strong translate="no">{{ tenant.name }}</strong>
          <code translate="no">{{ tenant.identifier }} · {{ tenant.domain }}</code>
        </div>
        <el-tag :type="tenant.isActive ? 'success' : 'info'">
          {{ t(tenant.isActive ? 'tenants.active' : 'tenants.inactive') }}
        </el-tag>
        <div class="tenants-actions">
          <el-button
            v-if="canWrite"
            plain
            :disabled="changing"
            @click="edit(tenant)"
          >
            {{ t('tenants.edit') }}
          </el-button>
          <el-button
            v-if="canWrite && tenant.isActive"
            type="danger"
            plain
            :disabled="changing"
            @click="disable(tenant)"
          >
            {{ t('tenants.disable') }}
          </el-button>
        </div>
      </article>
    </section>
  </section>
</template>

<style scoped>
.tenants-view { display: grid; gap: 18px; }
.tenants-heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 24px; padding: 8px 2px; }
.tenants-heading p { margin: 0 0 10px; color: var(--fullnet-color-accent); font-family: var(--fullnet-font-display); font-size: 10px; font-weight: 700; letter-spacing: .2em; }
.tenants-heading h1 { margin: 0; font-family: var(--fullnet-font-display); font-size: clamp(30px, 4vw, 48px); font-weight: 500; letter-spacing: -.05em; }
.tenants-heading span { display: block; margin-top: 10px; color: var(--fullnet-color-ink-muted); font-size: 13px; }
.tenants-problem { display: flex; gap: 14px; padding: 13px 16px; border-left: 3px solid var(--fullnet-color-danger); background: rgb(201 74 74 / 8%); }
.tenants-problem code { margin-left: auto; }
.create-strip { display: grid; grid-template-columns: minmax(160px, .7fr) repeat(3, minmax(180px, 1fr)) auto; align-items: end; gap: 16px; padding: 20px; border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-sidebar); color: #fff; }
.create-strip > div { align-self: center; }
.create-strip small, .identity-ledger small { color: var(--fullnet-color-accent-bright); font-family: var(--fullnet-font-display); }
.create-strip h2, .identity-ledger h2 { margin: 4px 0 0; font-size: 17px; }
.create-strip label span { display: block; margin-bottom: 7px; color: #aeb8b9; font-size: 11px; }
.identity-ledger { overflow: hidden; border: 1px solid var(--fullnet-color-line); border-radius: var(--fullnet-radius-md); background: var(--fullnet-color-panel); box-shadow: var(--fullnet-shadow-panel); }
.identity-ledger > header { display: flex; min-height: 66px; align-items: center; justify-content: space-between; padding: 0 22px; border-bottom: 1px solid var(--fullnet-color-line); }
.identity-ledger article { display: grid; grid-template-columns: 44px minmax(180px, 1fr) auto auto; align-items: center; gap: 16px; padding: 15px 22px; border-bottom: 1px solid var(--fullnet-color-line); }
.tenants-actions { display: flex; gap: 8px; justify-content: flex-end; }
.identity-mark { display: grid; width: 40px; height: 40px; place-items: center; border-radius: 12px; background: var(--fullnet-color-ink); color: #fff; font-weight: 700; }
.identity-ledger article div { display: grid; gap: 4px; }
.identity-ledger code { color: var(--fullnet-color-ink-muted); font-size: 11px; }
.tenants-empty { padding: 28px; margin: 0; text-align: center; color: var(--fullnet-color-ink-muted); }
@media (max-width: 1080px) {
  .create-strip { grid-template-columns: 1fr; }
  .identity-ledger article { grid-template-columns: 44px 1fr auto; }
  .identity-ledger article .el-button { grid-column: 2 / -1; }
}
</style>
