<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElButton, ElCard, ElTag } from 'element-plus';
import type {
  FullNetProblemDetails,
  IdentityModuleCatalogEntry
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import { getIdentityModule, listIdentityModules } from '../api/module-catalog';

const { t } = useAdminI18n();
const modules = ref<IdentityModuleCatalogEntry[]>([]);
const selected = ref<IdentityModuleCatalogEntry>();
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    modules.value = await listIdentityModules();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

async function openModule(entry: IdentityModuleCatalogEntry): Promise<void> {
  problem.value = undefined;
  try {
    selected.value = await getIdentityModule(entry.moduleKey);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  }
}

function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.identity_module_catalog_failed',
        title: t('moduleCatalog.loadFailed')
      };
}
</script>

<template>
  <section class="module-catalog-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('moduleCatalog.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-card shadow="never" class="art-table-card">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('moduleCatalog.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ modules.length }}</span>
        </div>
      </template>

      <p v-if="modules.length === 0" class="art-empty-state">{{ t('moduleCatalog.emptyDirectory') }}</p>
      <article v-for="entry in modules" :key="entry.moduleKey" class="art-data-row">
        <div class="art-data-row__identity">
          <strong>{{ entry.displayName }}</strong>
          <code>{{ entry.moduleKey }}</code>
          <small>{{ t('moduleCatalog.version') }}: {{ entry.version }}</small>
          <el-tag size="small" effect="plain">{{ entry.sourceClassification }}</el-tag>
        </div>
        <el-button plain @click="openModule(entry)">{{ t('moduleCatalog.select') }}</el-button>
      </article>
    </el-card>

    <el-card v-if="selected" shadow="never" class="art-table-card">
      <template #header>
        <h2>{{ selected.displayName }}</h2>
      </template>
      <p><strong>{{ t('moduleCatalog.hostProfiles') }}:</strong> {{ selected.hostProfiles.join(', ') }}</p>
      <p><strong>{{ t('moduleCatalog.dependencies') }}:</strong>
        {{ selected.dependencies.length === 0 ? t('moduleCatalog.noDependencies') : selected.dependencies.join(', ') }}
      </p>
      <p><strong>{{ t('moduleCatalog.healthCapability') }}:</strong> {{ selected.healthCapability }}</p>
    </el-card>
  </section>
</template>
