<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { ElAlert, ElCard, ElTable, ElTableColumn, ElTag } from 'element-plus';
import { isFullNetProblemDetails, type LogTimelineResponse } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import { useAdminI18n } from '../i18n/adminI18n';
import { usePermission } from '../auth/permission';
import { getLogTimeline } from '../api/log-timeline';

defineOptions({ name: 'LogTimelineView' });
const { t } = useAdminI18n();
const { can } = usePermission();
const route = useRoute();
const allowed = computed(() => can('auditing.timeline.read'));
const initial = () => ({ fromUtc: typeof route.query.fromUtc === 'string' ? route.query.fromUtc : new Date(Date.now() - 3600000).toISOString(),
  toUtc: typeof route.query.toUtc === 'string' ? route.query.toUtc : new Date().toISOString(),
  traceId: typeof route.query.traceId === 'string' ? route.query.traceId : '' });
const form = ref<Record<string, string | undefined>>(initial());
const loading = ref(false);
const result = ref<LogTimelineResponse>();
const error = ref('');
let controller: AbortController | undefined;
const { tableMainRef, tableHeight, updateTableHeight } = useArtCrudTableLayout({ bottomOffset: 8 });
const fields = computed<ArtSearchBarItem[]>(() => [
  { key: 'traceId', label: 'TraceId' },
  { key: 'fromUtc', label: t('accessLogs.fromUtc') }, { key: 'toUtc', label: t('accessLogs.toUtc') },
  { key: 'sessionId', label: t('logTimeline.session') }, { key: 'userId', label: t('logTimeline.user') },
  { key: 'clientIpFingerprint', label: t('logTimeline.ip') }
]);
async function search() {
  controller?.abort();
  result.value = undefined; error.value = '';
  if (!allowed.value) return;
  const current = new AbortController(); controller = current; loading.value = true;
  try {
    const response = await getLogTimeline({ fromUtc: form.value.fromUtc ?? '', toUtc: form.value.toUtc ?? '',
      traceId: form.value.traceId?.trim() || undefined, sessionId: form.value.sessionId?.trim() || undefined,
      userId: form.value.userId?.trim() || undefined, clientIpFingerprint: form.value.clientIpFingerprint?.trim() || undefined,
      limit: 200 }, current.signal);
    if (!current.signal.aborted) result.value = response;
  } catch (cause: unknown) {
    if (!current.signal.aborted) error.value = isFullNetProblemDetails(cause) ? cause.detail ?? cause.title ?? t('logTimeline.failed') : t('logTimeline.failed');
  } finally {
    if (!current.signal.aborted) { loading.value = false; await nextTick(updateTableHeight); }
  }
}
function reset() { controller?.abort(); loading.value = false; result.value = undefined; error.value = ''; form.value = initial(); }
watch(allowed, value => { if (!value) reset(); });
watch(() => route.fullPath, () => { reset(); if (form.value.traceId) void search(); });
onMounted(() => { if (form.value.traceId) void search(); });
onBeforeUnmount(() => controller?.abort());
</script>

<template>
  <div class="art-page-stack art-full-height">
    <el-alert :title="t('logTimeline.hint')" type="info" :closable="false" />
    <ArtSearchBar v-if="allowed" v-model="form" :items="fields" :loading="loading" @search="search" @reset="reset" />
    <el-alert v-else :title="t('logTimeline.forbidden')" type="warning" :closable="false" />
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-card class="art-table-card timeline-card" shadow="never">
      <div v-if="result" class="timeline-coverage">
        <el-tag v-for="source in result.sources" :key="source.source" :type="source.status === 'available' ? 'info' : 'warning'">
          {{ source.source }} · {{ source.status }}
        </el-tag>
        <p v-if="result.partial">{{ t('logTimeline.partial') }}</p>
      </div>
      <div ref="tableMainRef" class="art-crud-table-main">
        <el-table v-loading="loading" :data="result?.entries ?? []" :height="tableHeight" stripe border>
          <el-table-column prop="occurredAtUtc" :label="t('logTimeline.time')" min-width="240" />
          <el-table-column prop="source" :label="t('logTimeline.source')" min-width="200" />
          <el-table-column prop="summary" :label="t('logTimeline.action')" min-width="170" />
          <el-table-column prop="correlation.traceId" label="TraceId" min-width="285" />
          <el-table-column prop="correlation.spanId" label="SpanId" min-width="165" />
          <el-table-column prop="correlation.parentSpanId" label="ParentSpanId" min-width="165" />
          <el-table-column prop="correlation.requestId" label="RequestId" min-width="185" />
          <el-table-column prop="correlation.sessionId" :label="t('logTimeline.session')" min-width="300" />
          <el-table-column prop="correlation.centerSessionId" label="CenterSessionId" min-width="300" />
          <el-table-column prop="correlation.applicationSessionId" label="ApplicationSessionId" min-width="300" />
          <el-table-column prop="correlation.userId" :label="t('logTimeline.user')" min-width="300" />
          <el-table-column prop="correlation.tenantId" label="TenantId" min-width="300" />
          <el-table-column prop="correlation.clientIpFingerprint" :label="t('logTimeline.ip')" min-width="450" />
        </el-table>
      </div>
    </el-card>
  </div>
</template>

<style scoped>
.timeline-card { flex: 1; min-height: 0; }
.timeline-card :deep(.el-card__body) { display: flex; flex-direction: column; height: 100%; min-height: 0; }
.timeline-coverage { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 12px; }
.timeline-coverage p { width: 100%; margin: 4px 0; }
</style>
