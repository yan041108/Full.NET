<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ElButton, ElCard, ElDescriptions, ElDescriptionsItem, ElDialog, ElTable, ElTableColumn, ElTag } from 'element-plus';
import type { FullNetProblemDetails } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtSearchBar, { type ArtSearchBarItem } from '../framework/art-design/components/ArtSearchBar.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtPagedTableInCard } from '../framework/art-design/composables/useArtPagedTableInCard';
import { exportAuthenticationEvents, getAuthenticationEvent, listAuthenticationEvents, type AuthenticationEvent } from '../api/authentication-events';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';

defineOptions({ name: 'AuthenticationEventsView' });
const { t } = useAdminI18n();

const rows = ref<AuthenticationEvent[]>([]);
const page = ref(1);
const pageSize = ref(20);
const cursors = ref<Array<string | undefined>>([undefined]);
const nextCursor = ref<string>();
const loading = ref(false);
const exporting = ref(false);
const problem = ref<FullNetProblemDetails>();
const selected = ref<AuthenticationEvent>();
const detailOpen = ref(false);
const searchForm = ref<Record<string, string | undefined>>({});
const filters = ref<Record<string, string | undefined>>({});
let latestLoadId = 0;
const searchItems: ArtSearchBarItem[] = [
  { key: 'userId', label: t('authenticationEvents.userId'), placeholder: t('authenticationEvents.userId') },
  { key: 'eventType', label: t('authenticationEvents.eventType'), placeholder: 'login / logout / refresh' },
  { key: 'succeeded', label: t('authenticationEvents.result'), type: 'select', options: [
    { label: t('authenticationEvents.all'), value: '' },
    { label: t('authenticationEvents.success'), value: 'true' },
    { label: t('authenticationEvents.failure'), value: 'false' }
  ] },
  { key: 'fromUtc', label: t('authenticationEvents.fromUtc'), placeholder: '2026-09-25T00:00:00Z' },
  { key: 'toUtc', label: t('authenticationEvents.toUtc'), placeholder: '2026-09-26T00:00:00Z' }
];
const {
  tableMainRef, tableHeight, tableSize, tableZebra, tableBorder,
  tableHeaderBackground, tableHeaderCellStyle, syncTableLayout
} = useArtPagedTableInCard(loading);

async function load(): Promise<void> {
  const loadId = ++latestLoadId;
  loading.value = true;
  problem.value = undefined;
  try {
    const result = await listAuthenticationEvents({
      page: 1,
      pageSize: pageSize.value,
      cursor: cursors.value[page.value - 1],
      userId: filters.value.userId?.trim() || undefined,
      eventType: filters.value.eventType?.trim() || undefined,
      succeeded: filters.value.succeeded === 'true' ? true
        : filters.value.succeeded === 'false' ? false : undefined,
      fromUtc: filters.value.fromUtc?.trim() || undefined,
      toUtc: filters.value.toUtc?.trim() || undefined
    });
    if (loadId !== latestLoadId) return;
    rows.value = result.items;
    nextCursor.value = result.nextCursor ?? undefined;
  } catch (error) {
    if (loadId !== latestLoadId) return;
    rows.value = [];
    nextCursor.value = undefined;
    problem.value = isFullNetProblemDetails(error)
      ? error : { status: 500, code: 'client.authentication_events_failed', title: t('authenticationEvents.loadFailed') };
  } finally {
    if (loadId === latestLoadId) {
      loading.value = false;
      await syncTableLayout();
    }
  }
}

async function openDetail(row: AuthenticationEvent): Promise<void> {
  problem.value = undefined;
  try {
    selected.value = await getAuthenticationEvent(row.id);
    detailOpen.value = true;
  } catch (error) {
    problem.value = isFullNetProblemDetails(error)
      ? error : { status: 500, code: 'client.authentication_event_detail_failed', title: t('authenticationEvents.loadFailed') };
  }
}

async function downloadExport(): Promise<void> {
  const now = new Date();
  const fromUtc = filters.value.fromUtc?.trim()
    || new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString();
  const toUtc = filters.value.toUtc?.trim() || now.toISOString();
  exporting.value = true;
  problem.value = undefined;
  try {
    const blob = await exportAuthenticationEvents({
      fromUtc, toUtc,
      userId: filters.value.userId?.trim() || undefined,
      eventType: filters.value.eventType?.trim() || undefined,
      succeeded: filters.value.succeeded === 'true' ? true
        : filters.value.succeeded === 'false' ? false : undefined
    });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = `authentication-events-${now.toISOString().slice(0, 10)}.csv`;
    anchor.click();
    URL.revokeObjectURL(url);
  } catch (error) {
    problem.value = isFullNetProblemDetails(error)
      ? error : { status: 500, code: 'client.authentication_events_export_failed', title: t('authenticationEvents.exportFailed') };
  } finally {
    exporting.value = false;
  }
}

function applySearch(form: Record<string, string | undefined>): void {
  filters.value = { ...form };
  page.value = 1;
  cursors.value = [undefined];
  void load();
}

function resetSearch(): void {
  searchForm.value = {};
  filters.value = {};
  page.value = 1;
  cursors.value = [undefined];
  void load();
}

function nextPage(): void {
  if (!nextCursor.value) return;
  cursors.value[page.value] = nextCursor.value;
  page.value += 1;
  void load();
}

function previousPage(): void {
  if (page.value <= 1) return;
  page.value -= 1;
  void load();
}

function changePageSize(value: number): void {
  pageSize.value = value;
  page.value = 1;
  cursors.value = [undefined];
  void load();
}

onMounted(() => { void load(); });
</script>

<template>
  <section class="authentication-events-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('authenticationEvents.title') }}</h1>
    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>
    <ArtSearchBar
      v-model="searchForm" :items="searchItems" :default-visible-count="2"
      :search-label="t('authenticationEvents.query')" :reset-label="t('authenticationEvents.reset')"
      @search="applySearch" @reset="resetSearch"
    />
    <el-card class="art-table-card" shadow="never">
      <div ref="tableMainRef" class="art-crud-table-main">
        <div class="authentication-events-actions">
          <PermissionGate code="identity.authentication_events.export">
            <el-button :loading="exporting" @click="downloadExport">{{ t('authenticationEvents.export') }}</el-button>
          </PermissionGate>
        </div>
        <ArtTableHeader
          v-model:table-size="tableSize" v-model:zebra="tableZebra"
          v-model:border="tableBorder" v-model:header-background="tableHeaderBackground"
          :loading="loading" full-class="art-crud-table-main"
          layout="refresh,size,fullscreen,settings" @refresh="load"
        />
        <div class="art-table" :class="{ 'is-empty': rows.length === 0 }">
          <el-table
            v-loading="loading" :data="rows" :height="tableHeight" :size="tableSize"
            :stripe="tableZebra" :border="tableBorder"
            :header-cell-style="tableHeaderCellStyle" class="art-crud-data-table"
            :class="{ 'art-table--header-bg': tableHeaderBackground }"
          >
            <el-table-column :label="t('authenticationEvents.occurredAt')" prop="occurredAtUtc" min-width="210" />
            <el-table-column :label="t('authenticationEvents.event')" prop="eventType" min-width="165" />
            <el-table-column :label="t('authenticationEvents.result')" min-width="90">
              <template #default="{ row }">
                <el-tag :type="row.succeeded ? 'success' : 'danger'">
                  {{ row.succeeded ? t('authenticationEvents.success') : t('authenticationEvents.failure') }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column :label="t('authenticationEvents.resultCode')" prop="resultCode" min-width="230" />
            <el-table-column :label="t('authenticationEvents.userId')" prop="userId" min-width="275" />
            <el-table-column :label="t('authenticationEvents.sessionId')" prop="sessionId" min-width="275" />
            <el-table-column :label="t('authenticationEvents.action')" width="110" fixed="right">
              <template #default="{ row }">
                <el-button link type="primary" @click="openDetail(row as AuthenticationEvent)">{{ t('authenticationEvents.detail') }}</el-button>
              </template>
            </el-table-column>
          </el-table>
        </div>
        <div class="authentication-events-pagination">
          <label>
            {{ t('authenticationEvents.pageSize') }}
            <select :value="pageSize" @change="changePageSize(Number(($event.target as HTMLSelectElement).value))">
              <option :value="20">20</option><option :value="50">50</option><option :value="100">100</option>
            </select>
          </label>
          <span>{{ t('authenticationEvents.page') }} {{ page }}</span>
          <el-button :disabled="page <= 1 || loading" @click="previousPage">{{ t('authenticationEvents.previous') }}</el-button>
          <el-button :disabled="!nextCursor || loading" @click="nextPage">{{ t('authenticationEvents.next') }}</el-button>
        </div>
      </div>
    </el-card>
    <el-dialog v-model="detailOpen" :title="t('authenticationEvents.detail')" width="700px">
      <el-descriptions v-if="selected" :column="1" border>
        <el-descriptions-item :label="t('authenticationEvents.occurredAt')">{{ selected.occurredAtUtc }}</el-descriptions-item>
        <el-descriptions-item :label="t('authenticationEvents.event')">{{ selected.eventType }}</el-descriptions-item>
        <el-descriptions-item :label="t('authenticationEvents.resultCode')">{{ selected.resultCode }}</el-descriptions-item>
        <el-descriptions-item :label="t('authenticationEvents.userId')">{{ selected.userId || '—' }}</el-descriptions-item>
        <el-descriptions-item :label="t('authenticationEvents.actorUserId')">{{ selected.actorUserId || '—' }}</el-descriptions-item>
        <el-descriptions-item :label="t('authenticationEvents.authenticationMethod')">{{ selected.authenticationMethod || '—' }}</el-descriptions-item>
        <el-descriptions-item :label="t('authenticationEvents.clientId')">{{ selected.clientId || '—' }}</el-descriptions-item>
        <el-descriptions-item :label="t('authenticationEvents.traceId')">{{ selected.traceId || '—' }}</el-descriptions-item>
        <el-descriptions-item :label="t('authenticationEvents.sessionId')">{{ selected.sessionId || '—' }}</el-descriptions-item>
        <el-descriptions-item :label="t('authenticationEvents.centerSessionId')">{{ selected.centerSessionId || '—' }}</el-descriptions-item>
        <el-descriptions-item :label="t('authenticationEvents.applicationSessionId')">{{ selected.applicationSessionId || '—' }}</el-descriptions-item>
      </el-descriptions>
    </el-dialog>
  </section>
</template>

<style scoped>
.authentication-events-view :deep(.art-table-card) { flex: 1; min-height: 0; }
.authentication-events-view :deep(.art-table-card .el-card__body) {
  display: flex; flex-direction: column; min-height: 0; height: 100%;
}
.authentication-events-actions { display: flex; justify-content: flex-end; margin-bottom: 8px; }
.authentication-events-pagination { display: flex; justify-content: flex-end; align-items: center; gap: 12px; margin-top: 12px; }
</style>
