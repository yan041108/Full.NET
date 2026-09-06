<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import {
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElTabPane,
  ElTabs,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type {
  AuditingDomainChangeDiffEntry,
  FullNetProblemDetails
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useAdminI18n } from '../../i18n/adminI18n';
import { queryDomainChangeDiffs } from '../../api/auditing-analytics';

export interface AuditLogDetailRecord {
  id: string;
  occurredAtUtc: string;
  traceId?: string | null;
  title: string;
  subtitle?: string | null;
  fields: Array<{ label: string; value: string | number | boolean | null | undefined }>;
}

const props = defineProps<{
  modelValue: boolean;
  record: AuditLogDetailRecord | null;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
}>();

const { locale, t } = useAdminI18n();
const activeTab = ref('summary');
const diffLoading = ref(false);
const diffEntries = ref<AuditingDomainChangeDiffEntry[]>([]);
const diffProblem = ref<FullNetProblemDetails>();

const open = computed({
  get: () => props.modelValue,
  set: value => emit('update:modelValue', value)
});

const hasTraceId = computed(() => Boolean(props.record?.traceId?.trim()));

watch(
  () => [props.modelValue, props.record?.traceId, activeTab.value] as const,
  async ([visible, traceId, tab]) => {
    if (!visible || tab !== 'diff' || !traceId?.trim()) {
      return;
    }
    await loadDiff(traceId.trim());
  }
);

watch(
  () => props.modelValue,
  visible => {
    if (!visible) {
      activeTab.value = 'summary';
      diffEntries.value = [];
      diffProblem.value = undefined;
    }
  }
);

async function loadDiff(traceId: string): Promise<void> {
  diffLoading.value = true;
  diffProblem.value = undefined;
  try {
    const result = await queryDomainChangeDiffs(traceId);
    diffEntries.value = result.entries;
  } catch (error: unknown) {
    diffProblem.value = isFullNetProblemDetails(error)
      ? error
      : {
          status: 500,
          code: 'client.auditing_domain_change_diff_failed',
          title: t('auditAnalytics.diffLoadFailed')
        };
  } finally {
    diffLoading.value = false;
  }
}

function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

function availabilityLabel(availability: AuditingDomainChangeDiffEntry['availability']): string {
  switch (availability) {
    case 'available':
      return t('auditAnalytics.diffAvailable');
    case 'unparseable':
      return t('auditAnalytics.diffUnparseable');
    default:
      return t('auditAnalytics.diffNoRecorded');
  }
}

function availabilityTagType(
  availability: AuditingDomainChangeDiffEntry['availability']
): 'success' | 'warning' | 'info' {
  switch (availability) {
    case 'available':
      return 'success';
    case 'unparseable':
      return 'warning';
    default:
      return 'info';
  }
}

function formatFieldValue(value: string | null | undefined): string {
  if (value === null || value === undefined || value === '') {
    return t('auditAnalytics.diffValueMissing');
  }
  return value;
}
</script>

<template>
  <el-drawer
    v-model="open"
    :title="record?.title ?? t('auditAnalytics.detailTitle')"
    size="56%"
    append-to-body
  >
    <template v-if="record">
      <p v-if="record.subtitle" class="audit-log-detail-drawer__subtitle" translate="no">
        {{ record.subtitle }}
      </p>

      <el-tabs v-model="activeTab">
        <el-tab-pane :label="t('auditAnalytics.tabSummary')" name="summary">
          <el-descriptions :column="1" border>
            <el-descriptions-item
              v-for="field in record.fields"
              :key="field.label"
              :label="field.label"
            >
              <span translate="no">{{ field.value ?? '—' }}</span>
            </el-descriptions-item>
          </el-descriptions>
        </el-tab-pane>

        <el-tab-pane :label="t('auditAnalytics.tabDiff')" name="diff" :disabled="!hasTraceId">
          <p v-if="!hasTraceId" class="audit-log-detail-drawer__hint">
            {{ t('auditAnalytics.diffTraceMissing') }}
          </p>

          <template v-else>
            <div v-if="diffProblem" class="art-inline-alert" role="alert">
              <strong translate="no">{{ diffProblem.code }}</strong>
              <span>{{ diffProblem.title }}</span>
            </div>

            <div v-loading="diffLoading">
              <p v-if="!diffLoading && diffEntries.length === 0" class="audit-log-detail-drawer__hint">
                {{ t('auditAnalytics.diffEmpty') }}
              </p>

              <section
                v-for="entry in diffEntries"
                :key="entry.auditId"
                class="audit-log-detail-drawer__entry"
              >
                <div class="audit-log-detail-drawer__entry-header">
                  <div>
                    <strong translate="no">{{ entry.moduleKey }} / {{ entry.actionKey }}</strong>
                    <p translate="no">{{ formatDateTime(entry.occurredAtUtc) }}</p>
                  </div>
                  <el-tag :type="availabilityTagType(entry.availability)" effect="plain">
                    {{ availabilityLabel(entry.availability) }}
                  </el-tag>
                </div>

                <p
                  v-if="entry.availability !== 'available'"
                  class="audit-log-detail-drawer__hint"
                >
                  {{ availabilityLabel(entry.availability) }}
                </p>

                <el-table
                  v-else
                  :data="entry.fields"
                  stripe
                  border
                  style="width: 100%"
                >
                  <el-table-column :label="t('auditAnalytics.diffField')" prop="fieldKey" min-width="140" />
                  <el-table-column :label="t('auditAnalytics.diffBefore')" min-width="160">
                    <template #default="{ row }">
                      <span translate="no">{{ formatFieldValue(row.beforeValue) }}</span>
                    </template>
                  </el-table-column>
                  <el-table-column :label="t('auditAnalytics.diffAfter')" min-width="160">
                    <template #default="{ row }">
                      <span translate="no">{{ formatFieldValue(row.afterValue) }}</span>
                    </template>
                  </el-table-column>
                </el-table>
              </section>
            </div>
          </template>
        </el-tab-pane>
      </el-tabs>
    </template>
  </el-drawer>
</template>

<style scoped>
.audit-log-detail-drawer__subtitle {
  margin: 0 0 12px;
  color: var(--art-text-secondary, #667085);
}

.audit-log-detail-drawer__hint {
  margin: 0;
  color: var(--art-text-secondary, #667085);
}

.audit-log-detail-drawer__entry {
  margin-bottom: 20px;
}

.audit-log-detail-drawer__entry-header {
  display: flex;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}
</style>
