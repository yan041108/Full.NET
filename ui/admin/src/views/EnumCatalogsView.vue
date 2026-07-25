<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElButton, ElCard, ElTag } from 'element-plus';
import type {
  FullNetProblemDetails,
  SettingsEnumCatalogDetail,
  SettingsEnumCatalogSummary
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import { getSettingsEnumCatalog, listSettingsEnumCatalogs } from '../api/enum-catalogs';

const { t } = useAdminI18n();
const catalogs = ref<SettingsEnumCatalogSummary[]>([]);
const selected = ref<SettingsEnumCatalogDetail>();
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    catalogs.value = await listSettingsEnumCatalogs();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

async function openCatalog(summary: SettingsEnumCatalogSummary): Promise<void> {
  problem.value = undefined;
  try {
    selected.value = await getSettingsEnumCatalog(summary.key);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  }
}

function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.settings_enum_catalog_failed',
        title: t('enumCatalogs.loadFailed')
      };
}
</script>

<template>
  <section class="enum-catalogs-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('enumCatalogs.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-card shadow="never" class="art-table-card">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('enumCatalogs.directoryTitle') }}</h2>
          <span class="art-table-card__count">{{ catalogs.length }}</span>
        </div>
      </template>

      <p v-if="catalogs.length === 0" class="art-empty-state">{{ t('enumCatalogs.emptyDirectory') }}</p>
      <article v-for="catalog in catalogs" :key="catalog.key" class="art-data-row">
        <div class="art-data-row__identity">
          <strong>{{ catalog.displayName }}</strong>
          <code>{{ catalog.key }}</code>
          <small>{{ t('enumCatalogs.memberCount') }}: {{ catalog.memberCount }}</small>
          <small v-if="catalog.description">{{ catalog.description }}</small>
        </div>
        <el-button plain @click="openCatalog(catalog)">{{ t('enumCatalogs.select') }}</el-button>
      </article>
    </el-card>

    <el-card v-if="selected" shadow="never" class="art-table-card">
      <template #header>
        <h2>{{ t('enumCatalogs.membersTitle', { name: selected.displayName }) }}</h2>
      </template>
      <article v-for="member in selected.members" :key="member.code" class="art-data-row">
        <div class="art-data-row__identity">
          <strong>{{ member.label }}</strong>
          <code>{{ member.code }}</code>
          <small>{{ t('enumCatalogs.displayOrder') }}: {{ member.displayOrder }}</small>
        </div>
        <el-tag effect="plain">{{ t('enumCatalogs.code') }}</el-tag>
      </article>
    </el-card>
    <p v-else class="art-empty-state">{{ t('enumCatalogs.emptyMembers') }}</p>
  </section>
</template>
