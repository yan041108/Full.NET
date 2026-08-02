<script setup lang="ts">
import { onMounted, ref } from 'vue';
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
  HostTenant,
  HostTenantPackage
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import { listHostTenantPackages } from '../api/tenant-packages';
import {
  assignHostTenantPackage,
  createHostTenant,
  disableHostTenant,
  listHostTenants,
  updateHostTenant
} from '../api/tenants';

const { t } = useAdminI18n();
const tenants = ref<HostTenant[]>([]);
const packages = ref<HostTenantPackage[]>([]);
const identifier = ref('');
const name = ref('');
const domain = ref('');
const createPackageId = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();

onMounted(async () => {
  await Promise.all([load(), loadPackages()]);
});

async function loadPackages(): Promise<void> {
  try {
    const page = await listHostTenantPackages(1, 100);
    packages.value = page.items.filter(pkg => pkg.isActive);
  } catch {
    packages.value = [];
  }
}

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
      domain.value.trim().toLowerCase(),
      createPackageId.value || null
    );
    identifier.value = '';
    name.value = '';
    domain.value = '';
    createPackageId.value = '';
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

async function assignPackage(
  tenant: HostTenant,
  packageId: string | null
): Promise<void> {
  if (changing.value) return;
  changing.value = true;
  problem.value = undefined;
  try {
    await assignHostTenantPackage(tenant.id, packageId, tenant.version);
    ElMessage.success(t('tenants.packageAssignSuccess'));
    await load();
  } catch (error: unknown) {
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
  <section class="tenants-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('tenants.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <PermissionGate code="tenancy.tenants.create">
      <el-card class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-5" aria-labelledby="create-title">
        <div><h2 id="create-title">{{ t('tenants.createTitle') }}</h2></div>
        <label>
          <span>{{ t('tenants.identifier') }}</span>
          <el-input v-model="identifier" :placeholder="t('tenants.identifierPlaceholder')" />
        </label>
        <label>
          <span>{{ t('tenants.name') }}</span>
          <el-input v-model="name" :placeholder="t('tenants.namePlaceholder')" />
        </label>
        <label>
          <span>{{ t('tenants.domain') }}</span>
          <el-input v-model="domain" :placeholder="t('tenants.domainPlaceholder')" @keyup.enter="create" />
        </label>
        <label>
          <span>{{ t('tenants.packageLabel') }}</span>
          <el-select v-model="createPackageId" :placeholder="t('tenants.packageUnassigned')" clearable>
            <el-option v-for="pkg in packages" :key="pkg.id" :label="pkg.name" :value="pkg.id" />
          </el-select>
        </label>
        <el-button type="primary" :loading="changing" @click="create">{{ t('tenants.create') }}</el-button>
      </div>
    </el-card>
    </PermissionGate>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('tenants.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ tenants.length }}</span>
        </div>
      </template>

      <p v-if="tenants.length === 0" class="art-empty-state">{{ t('tenants.emptyDirectory') }}</p>
      <article v-for="tenant in tenants" :key="tenant.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ tenant.identifier.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ tenant.name }}</strong>
          <code translate="no">{{ tenant.identifier }} · {{ tenant.domain }}</code>
          <small v-if="tenant.tenantPackageName" class="art-data-row__meta" translate="no">
            {{ t('tenants.packageLabel') }}: {{ tenant.tenantPackageName }}
          </small>
        </div>
        <el-tag :type="tenant.isActive ? 'success' : 'info'">
          {{ t(tenant.isActive ? 'tenants.active' : 'tenants.inactive') }}
        </el-tag>
        <div class="art-data-row__actions">
          <PermissionGate code="tenancy.tenants.assign_package">
            <el-select
              :model-value="tenant.tenantPackageId ?? ''"
              :placeholder="t('tenants.packageUnassigned')"
              :disabled="changing"
              @change="value => assignPackage(tenant, value ? String(value) : null)"
            >
              <el-option :label="t('tenants.packageUnassigned')" value="" />
              <el-option v-for="pkg in packages" :key="pkg.id" :label="pkg.name" :value="pkg.id" />
            </el-select>
          </PermissionGate>
          <PermissionGate code="tenancy.tenants.update">
            <el-button plain :disabled="changing" @click="edit(tenant)">{{ t('tenants.edit') }}</el-button>
          </PermissionGate>
          <PermissionGate v-if="tenant.isActive" code="tenancy.tenants.disable">
            <el-button
              type="danger"
              plain
              :disabled="changing"
              @click="disable(tenant)"
            >
              {{ t('tenants.disable') }}
            </el-button>
          </PermissionGate>
        </div>
      </article>
    </el-card>
  </section>
</template>
