<script setup lang="ts">
import { onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, HostTenantPackage } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import {
  createHostTenantPackage,
  disableHostTenantPackage,
  listHostTenantPackages,
  updateHostTenantPackage
} from '../api/tenant-packages';

const { t } = useAdminI18n();
const packages = ref<HostTenantPackage[]>([]);
const code = ref('');
const name = ref('');
const description = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listHostTenantPackages();
    packages.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenantPackages.loadFailed');
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
    await createHostTenantPackage(
      code.value.trim().toLowerCase(),
      name.value.trim(),
      description.value.trim() || null
    );
    code.value = '';
    name.value = '';
    description.value = '';
    ElMessage.success(t('tenantPackages.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'tenantPackages.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function edit(pkg: HostTenantPackage): Promise<void> {
  if (changing.value) return;
  try {
    const result = await ElMessageBox.prompt(
      t('tenantPackages.editTitle'),
      t('tenantPackages.edit'),
      {
        inputValue: pkg.name,
        inputPattern: /.+/,
        showCancelButton: true
      }
    );
    changing.value = true;
    await updateHostTenantPackage(
      pkg.id,
      result.value.trim(),
      pkg.description,
      pkg.version
    );
    ElMessage.success(t('tenantPackages.updateSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'tenantPackages.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function disable(pkg: HostTenantPackage): Promise<void> {
  if (changing.value || !pkg.isActive) return;
  try {
    await ElMessageBox.confirm(
      t('tenantPackages.confirmDisable', { name: pkg.code }),
      t('tenantPackages.disable'),
      {
        type: 'warning',
        confirmButtonText: t('tenantPackages.disable'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await disableHostTenantPackage(pkg.id);
    ElMessage.success(t('tenantPackages.disableSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') return;
    problem.value = toProblem(error, 'tenantPackages.operationFailed');
  } finally {
    changing.value = false;
  }
}

function toProblem(
  error: unknown,
  fallbackKey: 'tenantPackages.loadFailed' | 'tenantPackages.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.host_tenant_package_failed',
        title: t(fallbackKey)
      };
}
</script>

<template>
  <section class="tenant-packages-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('tenantPackages.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <PermissionGate code="tenancy.tenant_packages.create">
      <el-card class="art-form-card" shadow="never">
      <div class="art-form-grid art-form-grid--cols-3" aria-labelledby="create-package-title">
        <div><h2 id="create-package-title">{{ t('tenantPackages.createTitle') }}</h2></div>
        <label>
          <span>{{ t('tenantPackages.code') }}</span>
          <el-input v-model="code" :placeholder="t('tenantPackages.codePlaceholder')" />
        </label>
        <label>
          <span>{{ t('tenantPackages.name') }}</span>
          <el-input v-model="name" :placeholder="t('tenantPackages.namePlaceholder')" />
        </label>
        <label>
          <span>{{ t('tenantPackages.descriptionLabel') }}</span>
          <el-input v-model="description" :placeholder="t('tenantPackages.descriptionPlaceholder')" @keyup.enter="create" />
        </label>
        <el-button type="primary" :loading="changing" @click="create">{{ t('tenantPackages.create') }}</el-button>
      </div>
    </el-card>
    </PermissionGate>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('tenantPackages.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ packages.length }}</span>
        </div>
      </template>

      <p v-if="packages.length === 0" class="art-empty-state">{{ t('tenantPackages.emptyDirectory') }}</p>
      <article v-for="pkg in packages" :key="pkg.id" class="art-data-row">
        <span class="art-data-row__avatar">{{ pkg.code.slice(0, 2).toUpperCase() }}</span>
        <div class="art-data-row__main">
          <strong translate="no">{{ pkg.name }}</strong>
          <code translate="no">{{ pkg.code }}</code>
          <small class="art-data-row__meta" translate="no">
            {{ t('tenantPackages.assignedTenantCount') }}: {{ pkg.assignedTenantCount }}
          </small>
          <small v-if="pkg.description" class="art-data-row__meta" translate="no">{{ pkg.description }}</small>
        </div>
        <el-tag :type="pkg.isActive ? 'success' : 'info'">
          {{ t(pkg.isActive ? 'tenantPackages.active' : 'tenantPackages.inactive') }}
        </el-tag>
        <div class="art-data-row__actions">
          <PermissionGate code="tenancy.tenant_packages.update">
            <el-button plain :disabled="changing" @click="edit(pkg)">{{ t('tenantPackages.edit') }}</el-button>
          </PermissionGate>
          <PermissionGate v-if="pkg.isActive" code="tenancy.tenant_packages.disable">
            <el-button
              type="danger"
              plain
              :disabled="changing"
              @click="disable(pkg)"
            >
              {{ t('tenantPackages.disable') }}
            </el-button>
          </PermissionGate>
        </div>
      </article>
    </el-card>
  </section>
</template>
