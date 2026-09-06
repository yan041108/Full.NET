<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElPagination,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, MyReleaseNote } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';
import PermissionGate from '../components/PermissionGate.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listMyReleaseNotes, markMyReleaseNoteRead } from '../api/my-release-notes';

defineOptions({ name: 'MyReleaseNotesView' });

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const pagedItems = ref<MyReleaseNote[]>([]);
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const selectedNote = ref<MyReleaseNote | null>(null);

const {
  tableMainRef,
  tableHeight,
  tableSize,
  tableZebra,
  tableBorder,
  tableHeaderBackground,
  tableHeaderCellStyle,
  watchLoading
} = useArtCrudTableLayout();

const canMarkRead = computed(() => session.can('platform.release_notes.mark_read'));

watch([page, pageSize], () => {
  void load();
});

watchLoading(loading);

onMounted(() => {
  void load();
});

function rowIndex(index: number): number {
  return (page.value - 1) * pageSize.value + index + 1;
}

function formatDateTime(value: string | null | undefined): string {
  if (!value) {
    return '—';
  }
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const response = await listMyReleaseNotes(page.value, pageSize.value);
    pagedItems.value = response.items;
    total.value = response.total;
    if (selectedNote.value) {
      selectedNote.value = response.items.find(item => item.id === selectedNote.value?.id) ?? null;
    }
  } catch (error: unknown) {
    problem.value = resolveProblem(error);
  } finally {
    loading.value = false;
  }
}

function selectNote(note: MyReleaseNote): void {
  selectedNote.value = note;
}

async function markRead(note: MyReleaseNote): Promise<void> {
  if (!canMarkRead.value || note.isRead || changing.value) {
    return;
  }
  changing.value = true;
  try {
    const updated = await markMyReleaseNoteRead(note.id);
    selectedNote.value = updated;
    await load();
  } catch (error: unknown) {
    problem.value = resolveProblem(error, 'myReleaseNotes.operationFailed');
  } finally {
    changing.value = false;
  }
}

function resolveProblem(
  error: unknown,
  fallbackKey: 'myReleaseNotes.loadFailed' | 'myReleaseNotes.operationFailed' = 'myReleaseNotes.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.my_release_note_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="my-release-notes-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('myReleaseNotes.title') }}</h1>
    <p class="art-page-description">{{ t('myReleaseNotes.description') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <div class="my-release-notes-layout">
      <el-card class="art-table-card" shadow="never">
        <div ref="tableMainRef" class="art-crud-table-main">
          <ArtTableHeader
            v-model:table-size="tableSize"
            v-model:zebra="tableZebra"
            v-model:border="tableBorder"
            v-model:header-background="tableHeaderBackground"
            :loading="loading"
            full-class="art-crud-table-main"
            layout="refresh,size"
            @refresh="load"
          />

          <div class="art-table" :class="{ 'is-empty': pagedItems.length === 0 }">
            <el-table
              v-loading="loading"
              :data="pagedItems"
              :height="tableHeight"
              :size="tableSize"
              :stripe="tableZebra"
              :border="tableBorder"
              :header-cell-style="tableHeaderCellStyle"
              class="art-crud-data-table"
              highlight-current-row
              @row-click="selectNote"
            >
              <el-table-column :label="t('users.columnIndex')" width="72" align="center">
                <template #default="{ $index }">{{ rowIndex($index) }}</template>
              </el-table-column>
              <el-table-column :label="t('myReleaseNotes.fieldVersion')" width="120">
                <template #default="{ row }">
                  <span translate="no">v{{ row.versionLabel }}</span>
                </template>
              </el-table-column>
              <el-table-column :label="t('myReleaseNotes.fieldTitle')" min-width="180">
                <template #default="{ row }">
                  <span translate="no">{{ row.title }}</span>
                </template>
              </el-table-column>
              <el-table-column :label="t('users.status')" width="100" align="center">
                <template #default="{ row }">
                  <el-tag :type="row.isRead ? 'info' : 'success'" size="small">
                    {{ row.isRead ? t('myReleaseNotes.read') : t('myReleaseNotes.unread') }}
                  </el-tag>
                </template>
              </el-table-column>
              <el-table-column :label="t('myReleaseNotes.publishedAt')" width="168">
                <template #default="{ row }">{{ formatDateTime(row.publishedAtUtc) }}</template>
              </el-table-column>
            </el-table>

            <p v-if="!loading && pagedItems.length === 0" class="art-table-empty">
              {{ t('myReleaseNotes.emptyList') }}
            </p>
          </div>

          <el-pagination
            v-model:current-page="page"
            v-model:page-size="pageSize"
            class="art-table-pagination"
            layout="total, sizes, prev, pager, next"
            :total="total"
            :page-sizes="[10, 20, 50]"
          />
        </div>
      </el-card>

      <el-card v-if="selectedNote" class="my-release-notes-detail" shadow="never">
        <template #header>
          <div class="my-release-notes-detail__header">
            <div>
              <strong translate="no">{{ selectedNote.title }}</strong>
              <span class="my-release-notes-detail__version" translate="no">v{{ selectedNote.versionLabel }}</span>
            </div>
            <PermissionGate code="platform.release_notes.mark_read">
              <el-button
                v-if="!selectedNote.isRead"
                type="primary"
                plain
                data-testid="my-release-notes-mark-read"
                :loading="changing"
                @click="markRead(selectedNote)"
              >
                {{ t('myReleaseNotes.markRead') }}
              </el-button>
            </PermissionGate>
          </div>
        </template>
        <p class="my-release-notes-detail__meta">
          {{ t('myReleaseNotes.publishedAt') }}: {{ formatDateTime(selectedNote.publishedAtUtc) }}
        </p>
        <pre class="my-release-notes-detail__content" translate="no">{{ selectedNote.content }}</pre>
      </el-card>
    </div>
  </section>
</template>

<style scoped>
.my-release-notes-layout {
  display: grid;
  gap: 16px;
  grid-template-columns: minmax(0, 1.2fr) minmax(280px, 0.8fr);
}

.my-release-notes-detail__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.my-release-notes-detail__version {
  margin-left: 8px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.my-release-notes-detail__meta {
  margin: 0 0 12px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.my-release-notes-detail__content {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
  line-height: 1.6;
}

@media (max-width: 960px) {
  .my-release-notes-layout {
    grid-template-columns: 1fr;
  }
}
</style>
