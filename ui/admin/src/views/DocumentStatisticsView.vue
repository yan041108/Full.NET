<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElAlert, ElCard, ElCol, ElRow, ElStatistic } from 'element-plus';
import type { FullNetProblemDetails, HostDocumentStatisticsResponse } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useAdminI18n } from '../i18n/adminI18n';
import { getDocumentStatistics } from '../api/document-statistics';

defineOptions({ name: 'DocumentStatisticsView' });

const { t } = useAdminI18n();
const loading = ref(false);
const problem = ref<FullNetProblemDetails>();
const statistics = ref<HostDocumentStatisticsResponse | null>(null);

function formatTotalSize(): string {
  return statistics.value?.summary.totalSizeInfo ?? '';
}

async function load() {
  loading.value = true;
  problem.value = undefined;
  try {
    statistics.value = await getDocumentStatistics();
  } catch (error) {
    statistics.value = null;
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function toProblem(error: unknown): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) {
    return error;
  }
  return { title: t('documentStatistics.loadFailed'), status: 500, code: 'documentStatistics.loadFailed' };
}

onMounted(load);
</script>

<template>
  <section class="art-page">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('documentStatistics.title') }}</h1>

    <el-alert
      v-if="problem"
      type="error"
      :title="problem.title"
      :description="problem.detail ?? problem.code"
      show-icon
      class="art-page-alert"
    />

    <el-card v-loading="loading" shadow="never" data-testid="document-statistics-panel">
      <template v-if="statistics">
        <el-row :gutter="16">
          <el-col :xs="24" :sm="12" :md="8">
            <el-statistic :title="t('documentStatistics.totalItems')" :value="statistics.summary.totalItems" />
          </el-col>
          <el-col :xs="24" :sm="12" :md="8">
            <el-statistic :title="t('documentStatistics.totalVersions')" :value="statistics.summary.totalVersions" />
          </el-col>
          <el-col :xs="24" :sm="12" :md="8">
            <el-statistic
              :title="t('documentStatistics.totalSize')"
              :value="statistics.summary.totalSizeKb"
              :formatter="formatTotalSize"
            />
          </el-col>
          <el-col :xs="24" :sm="12" :md="8">
            <el-statistic :title="t('documentStatistics.shareCount')" :value="statistics.shareCount" />
          </el-col>
          <el-col :xs="24" :sm="12" :md="8">
            <el-statistic :title="t('documentStatistics.recycleBinCount')" :value="statistics.recycleBinCount" />
          </el-col>
          <el-col :xs="24" :sm="12" :md="8">
            <el-statistic :title="t('documentStatistics.todayCreated')" :value="statistics.todayCreatedCount" />
          </el-col>
        </el-row>
      </template>
      <p v-else-if="!loading">{{ t('documentStatistics.empty') }}</p>
    </el-card>
  </section>
</template>
