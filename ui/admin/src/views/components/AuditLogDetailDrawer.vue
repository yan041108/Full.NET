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
import type { OperationLogDetailsResponse } from '../../api/operation-logs';
import { useAdminI18n } from '../../i18n/adminI18n';
import { queryDomainChangeDiffs } from '../../api/auditing-analytics';
import { getAuditingOperationLogDetails } from '../../api/operation-logs';
import { usePermission } from '../../auth/permission';

export interface AuditLogDetailRecord {
  id: string;
  occurredAtUtc: string;
  traceId?: string | null;
  supportsDiff?: boolean;
  supportsRestrictedDetails?: boolean;
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
const { can } = usePermission();
const activeTab = ref('summary');
const diffLoading = ref(false);
const diffEntries = ref<AuditingDomainChangeDiffEntry[]>([]);
const diffProblem = ref<FullNetProblemDetails>();
const restrictedLoading = ref(false);
const restrictedDetails = ref<OperationLogDetailsResponse>();
const restrictedProblem = ref<FullNetProblemDetails>();
const restrictedRecordId = ref<string>();
const canReadRestricted = computed(() => props.record?.supportsRestrictedDetails === true
  && can('auditing.operations.read')
  && can('auditing.operations.details.read'));

const open = computed({
  get: () => props.modelValue,
  set: value => emit('update:modelValue', value)
});

const hasTraceId = computed(() => Boolean(props.record?.traceId?.trim()));

watch(
  () => [props.modelValue, props.record?.id, canReadRestricted.value, activeTab.value] as const,
  ([visible, id, allowed, tab], _, onCleanup) => {
    const authorizedId = visible && allowed ? id : undefined;
    if (restrictedRecordId.value !== authorizedId) {
      // 记录或权限边界变化时立即清除受限数据；同一记录切页签复用已校验响应。
      restrictedRecordId.value = authorizedId;
      restrictedDetails.value = undefined;
    }
    restrictedProblem.value = undefined;
    restrictedLoading.value = false;
    if (!authorizedId || (tab !== 'request' && tab !== 'response')
      || restrictedDetails.value !== undefined) return;
    const controller = new AbortController();
    onCleanup(() => controller.abort());
    void loadRestrictedDetails(authorizedId, controller);
  }
);

watch(
  () => [props.modelValue, props.record?.id, props.record?.traceId,
    props.record?.supportsDiff, activeTab.value] as const,
  ([visible, , traceId, supportsDiff, tab], _, onCleanup) => {
    diffEntries.value = [];
    diffProblem.value = undefined;
    diffLoading.value = false;
    if (!visible || supportsDiff === false || tab !== 'diff' || !traceId?.trim()) {
      return;
    }
    const controller = new AbortController();
    onCleanup(() => controller.abort());
    void loadDiff(traceId.trim(), controller);
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

async function loadDiff(traceId: string, controller: AbortController): Promise<void> {
  diffLoading.value = true;
  try {
    const result = await queryDomainChangeDiffs(traceId, controller.signal);
    if (!controller.signal.aborted) diffEntries.value = result.entries;
  } catch (error: unknown) {
    if (!controller.signal.aborted) {
      diffProblem.value = isFullNetProblemDetails(error)
        ? error
        : {
            status: 500,
            code: 'client.auditing_domain_change_diff_failed',
            title: t('auditAnalytics.diffLoadFailed')
          };
    }
  } finally {
    if (!controller.signal.aborted) diffLoading.value = false;
  }
}

async function loadRestrictedDetails(id: string, controller: AbortController): Promise<void> {
  restrictedLoading.value = true;
  try {
    const result = await getAuditingOperationLogDetails(id, controller.signal);
    if (!controller.signal.aborted) restrictedDetails.value = result;
  } catch (error: unknown) {
    if (!controller.signal.aborted) {
      restrictedProblem.value = isFullNetProblemDetails(error)
        ? error
        : { status: 500, code: 'client.operation_log_details_failed',
            title: t('auditAnalytics.restrictedLoadFailed') };
    }
  } finally {
    if (!controller.signal.aborted) restrictedLoading.value = false;
  }
}

function captureStateLabel(state: string | null | undefined): string {
  switch (state) {
    case 'captured': return t('auditAnalytics.captureCaptured');
    case 'not_enabled': return t('auditAnalytics.captureNotEnabled');
    case 'not_allowed': return t('auditAnalytics.captureNotAllowed');
    case 'redacted': return t('auditAnalytics.captureRedacted');
    case 'truncated': return t('auditAnalytics.captureTruncated');
    case 'failed': return t('auditAnalytics.captureFailed');
    case 'budget_exceeded': return t('auditAnalytics.captureBudgetExceeded');
    default: return t('auditAnalytics.captureNotApplicable');
  }
}

function restrictedSummary(tab: 'request' | 'response'): object | null | undefined {
  return tab === 'request'
    ? restrictedDetails.value?.context.requestSummary
    : restrictedDetails.value?.context.responseSummary;
}

function restrictedState(tab: 'request' | 'response'): string | null | undefined {
  return tab === 'request'
    ? restrictedDetails.value?.context.requestCaptureState
    : restrictedDetails.value?.context.responseCaptureState;
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
        <el-tab-pane :label="t(record.supportsRestrictedDetails ? 'auditAnalytics.tabMessage' : 'auditAnalytics.tabSummary')" name="summary">
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

        <el-tab-pane v-if="canReadRestricted" :label="t('auditAnalytics.tabRequest')" name="request">
          <div v-loading="restrictedLoading" class="audit-log-detail-drawer__restricted">
            <div v-if="restrictedProblem" class="art-inline-alert" role="alert">
              <strong translate="no">{{ restrictedProblem.code }}</strong>
              <span>{{ restrictedProblem.status === 404 ? t('auditAnalytics.restrictedUnavailable') : restrictedProblem.title }}</span>
            </div>
            <template v-else-if="restrictedDetails">
              <p>{{ captureStateLabel(restrictedState('request')) }}</p>
              <el-descriptions :column="1" border>
                <el-descriptions-item :label="t('auditAnalytics.clientIp')"><span translate="no">{{ restrictedDetails.context.clientIp ?? '—' }}</span></el-descriptions-item>
                <el-descriptions-item :label="t('auditAnalytics.clientPort')"><span translate="no">{{ restrictedDetails.context.clientPort ?? '—' }}</span></el-descriptions-item>
                <el-descriptions-item :label="t('auditAnalytics.serverIp')"><span translate="no">{{ restrictedDetails.context.serverIp ?? '—' }}</span></el-descriptions-item>
                <el-descriptions-item :label="t('auditAnalytics.serverPort')"><span translate="no">{{ restrictedDetails.context.serverPort ?? '—' }}</span></el-descriptions-item>
              </el-descriptions>
              <pre v-if="restrictedSummary('request')" translate="no">{{ JSON.stringify(restrictedSummary('request'), null, 2) }}</pre>
            </template>
          </div>
        </el-tab-pane>

        <el-tab-pane v-if="canReadRestricted" :label="t('auditAnalytics.tabResponse')" name="response">
          <div v-loading="restrictedLoading" class="audit-log-detail-drawer__restricted">
            <div v-if="restrictedProblem" class="art-inline-alert" role="alert">
              <strong translate="no">{{ restrictedProblem.code }}</strong>
              <span>{{ restrictedProblem.status === 404 ? t('auditAnalytics.restrictedUnavailable') : restrictedProblem.title }}</span>
            </div>
            <template v-else-if="restrictedDetails">
              <p>{{ captureStateLabel(restrictedState('response')) }}</p>
              <pre v-if="restrictedSummary('response')" translate="no">{{ JSON.stringify(restrictedSummary('response'), null, 2) }}</pre>
            </template>
          </div>
        </el-tab-pane>

        <el-tab-pane v-if="record.supportsDiff !== false" :label="t('auditAnalytics.tabDiff')" name="diff" :disabled="!hasTraceId">
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

.audit-log-detail-drawer__restricted pre {
  overflow-wrap: anywhere;
  white-space: pre-wrap;
}
</style>
