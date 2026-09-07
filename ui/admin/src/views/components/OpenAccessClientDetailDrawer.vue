<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue';
import {
  ElAlert,
  ElButton,
  ElDescriptions,
  ElDescriptionsItem,
  ElDrawer,
  ElForm,
  ElFormItem,
  ElInput,
  ElPagination,
  ElTabPane,
  ElTabs,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, OpenAccessClient } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import PermissionGate from '../../components/PermissionGate.vue';
import { useAdminI18n } from '../../i18n/adminI18n';
import {
  debugOpenAccessClientSignature,
  getOpenAccessClientUsage,
  listOpenAccessClientAccessLogs,
  type OpenAccessClientAccessLogPage,
  type OpenAccessClientSignatureDebugResult,
  type OpenAccessClientUsage
} from '../../api/open-access-clients';

const props = defineProps<{
  modelValue: boolean;
  client: OpenAccessClient | null;
}>();

const emit = defineEmits<{
  'update:modelValue': [value: boolean];
}>();

const { locale, t } = useAdminI18n();
const activeTab = ref('logs');
const logsLoading = ref(false);
const usageLoading = ref(false);
const debugLoading = ref(false);
const logs = ref<OpenAccessClientAccessLogPage | null>(null);
const usage = ref<OpenAccessClientUsage | null>(null);
const debugResult = ref<OpenAccessClientSignatureDebugResult | null>(null);
const problem = ref<FullNetProblemDetails>();
const logsPage = ref(1);
const logsPageSize = ref(20);
const debugForm = reactive({
  secret: '',
  method: 'GET',
  path: '/api/v1/identity/users',
  query: 'page=1&pageSize=1',
  bodyBase64: '',
  timestamp: '',
  nonce: '',
  signature: '',
  signatureVersion: '1'
});

const open = computed({
  get: () => props.modelValue,
  set: (value: boolean) => emit('update:modelValue', value)
});

watch(
  () => [props.modelValue, props.client?.id, activeTab.value] as const,
  async ([visible, clientId, tab]) => {
    if (!visible || !clientId) {
      return;
    }
    if (tab === 'logs') {
      await loadLogs(clientId);
    } else if (tab === 'usage') {
      await loadUsage(clientId);
    }
  }
);

watch(
  () => props.modelValue,
  (visible) => {
    if (!visible) {
      activeTab.value = 'logs';
      logs.value = null;
      usage.value = null;
      debugResult.value = null;
      problem.value = undefined;
      logsPage.value = 1;
      debugForm.secret = '';
      debugForm.signature = '';
    }
  }
);

async function loadLogs(clientId: string): Promise<void> {
  logsLoading.value = true;
  problem.value = undefined;
  try {
    logs.value = await listOpenAccessClientAccessLogs(clientId, {
      page: logsPage.value,
      pageSize: logsPageSize.value
    });
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    logsLoading.value = false;
  }
}

async function loadUsage(clientId: string): Promise<void> {
  usageLoading.value = true;
  problem.value = undefined;
  try {
    usage.value = await getOpenAccessClientUsage(clientId);
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    usageLoading.value = false;
  }
}

async function submitDebug(): Promise<void> {
  if (!props.client) {
    return;
  }
  debugLoading.value = true;
  problem.value = undefined;
  debugResult.value = null;
  try {
    debugResult.value = await debugOpenAccessClientSignature(props.client.id, {
      secret: debugForm.secret.trim(),
      method: debugForm.method.trim(),
      path: debugForm.path.trim(),
      query: debugForm.query.trim() || null,
      bodyBase64: debugForm.bodyBase64.trim() || null,
      timestamp: debugForm.timestamp.trim(),
      nonce: debugForm.nonce.trim(),
      signature: debugForm.signature.trim(),
      signatureVersion: debugForm.signatureVersion.trim()
    });
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    debugLoading.value = false;
  }
}

function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'short',
    timeStyle: 'short'
  }).format(new Date(value));
}

function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.open_access_client_detail_failed',
        title: t('openAccessClients.detailLoadFailed')
      };
}

async function handleLogsPageChange(): Promise<void> {
  if (props.client) {
    await loadLogs(props.client.id);
  }
}
</script>

<template>
  <el-drawer
    v-model="open"
    :title="client ? t('openAccessClients.detailTitle', { name: client.name }) : t('openAccessClients.detailTitleFallback')"
    size="60%"
    destroy-on-close
    data-testid="open-access-client-detail-drawer"
  >
    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-tabs v-if="client" v-model="activeTab">
      <el-tab-pane :label="t('openAccessClients.tabLogs')" name="logs">
        <el-table v-loading="logsLoading" :data="logs?.items ?? []" row-key="id">
          <el-table-column :label="t('openAccessClients.logOccurredAt')" min-width="160">
            <template #default="{ row }">{{ formatDateTime(row.occurredAtUtc) }}</template>
          </el-table-column>
          <el-table-column :label="t('openAccessClients.logEventType')" prop="eventType" min-width="180" />
          <el-table-column :label="t('openAccessClients.logResultCode')" prop="resultCode" min-width="220" />
          <el-table-column :label="t('openAccessClients.logSucceeded')" width="100">
            <template #default="{ row }">
              <el-tag :type="row.succeeded ? 'success' : 'danger'">
                {{ row.succeeded ? t('openAccessClients.yes') : t('openAccessClients.no') }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column :label="t('openAccessClients.logIp')" prop="ipAddress" min-width="120" />
        </el-table>
        <el-pagination
          v-if="logs"
          v-model:current-page="logsPage"
          v-model:page-size="logsPageSize"
          :total="logs.total"
          layout="total, prev, pager, next"
          @current-change="handleLogsPageChange"
          @size-change="handleLogsPageChange"
        />
      </el-tab-pane>

      <el-tab-pane :label="t('openAccessClients.tabUsage')" name="usage">
        <el-descriptions v-loading="usageLoading" :column="1" border>
          <el-descriptions-item :label="t('openAccessClients.fieldDailyQuota')">
            {{ usage?.dailyRequestQuota ?? t('openAccessClients.unlimited') }}
          </el-descriptions-item>
          <el-descriptions-item :label="t('openAccessClients.usageSuccessToday')">
            {{ usage?.todaySuccessCount ?? 0 }}
          </el-descriptions-item>
          <el-descriptions-item :label="t('openAccessClients.usageFailureToday')">
            {{ usage?.todayFailureCount ?? 0 }}
          </el-descriptions-item>
          <el-descriptions-item :label="t('openAccessClients.usageWindow')">
            <span v-if="usage">
              {{ formatDateTime(usage.windowStartUtc) }} — {{ formatDateTime(usage.windowEndUtc) }}
            </span>
          </el-descriptions-item>
          <el-descriptions-item :label="t('openAccessClients.usageQuotaExceeded')">
            <el-tag :type="usage?.quotaExceeded ? 'danger' : 'success'">
              {{ usage?.quotaExceeded ? t('openAccessClients.yes') : t('openAccessClients.no') }}
            </el-tag>
          </el-descriptions-item>
        </el-descriptions>
      </el-tab-pane>

      <el-tab-pane :label="t('openAccessClients.tabDebug')" name="debug">
        <el-alert
          type="warning"
          :closable="false"
          :title="t('openAccessClients.debugWarning')"
          show-icon
          class="open-access-client-debug-alert"
        />
        <PermissionGate code="identity.open_access_clients.debug_signature">
          <el-form label-position="top" class="open-access-client-debug-form">
            <el-form-item :label="t('openAccessClients.debugSecret')">
              <el-input v-model="debugForm.secret" type="password" show-password autocomplete="off" />
            </el-form-item>
            <el-form-item :label="t('openAccessClients.debugMethod')">
              <el-input v-model="debugForm.method" />
            </el-form-item>
            <el-form-item :label="t('openAccessClients.debugPath')">
              <el-input v-model="debugForm.path" />
            </el-form-item>
            <el-form-item :label="t('openAccessClients.debugQuery')">
              <el-input v-model="debugForm.query" />
            </el-form-item>
            <el-form-item :label="t('openAccessClients.debugBodyBase64')">
              <el-input v-model="debugForm.bodyBase64" type="textarea" :rows="2" />
            </el-form-item>
            <el-form-item :label="t('openAccessClients.debugTimestamp')">
              <el-input v-model="debugForm.timestamp" />
            </el-form-item>
            <el-form-item :label="t('openAccessClients.debugNonce')">
              <el-input v-model="debugForm.nonce" />
            </el-form-item>
            <el-form-item :label="t('openAccessClients.debugSignature')">
              <el-input v-model="debugForm.signature" />
            </el-form-item>
            <el-button
              type="primary"
              :loading="debugLoading"
              data-testid="open-access-client-debug-submit"
              @click="submitDebug"
            >
              {{ t('openAccessClients.debugRun') }}
            </el-button>
          </el-form>
          <el-descriptions v-if="debugResult" :column="1" border class="open-access-client-debug-result">
            <el-descriptions-item :label="t('openAccessClients.debugMatch')">
              <el-tag :type="debugResult.signaturesMatch ? 'success' : 'danger'">
                {{ debugResult.signaturesMatch ? t('openAccessClients.yes') : t('openAccessClients.no') }}
              </el-tag>
            </el-descriptions-item>
            <el-descriptions-item :label="t('openAccessClients.debugCanonical')">
              <code translate="no">{{ debugResult.canonicalString }}</code>
            </el-descriptions-item>
            <el-descriptions-item :label="t('openAccessClients.debugExpected')">
              <code translate="no">{{ debugResult.expectedSignature }}</code>
            </el-descriptions-item>
            <el-descriptions-item :label="t('openAccessClients.debugProvided')">
              <code translate="no">{{ debugResult.providedSignature }}</code>
            </el-descriptions-item>
          </el-descriptions>
        </PermissionGate>
      </el-tab-pane>
    </el-tabs>
  </el-drawer>
</template>

<style scoped>
.open-access-client-debug-alert {
  margin-bottom: 16px;
}

.open-access-client-debug-form {
  margin-top: 12px;
}

.open-access-client-debug-result {
  margin-top: 16px;
}
</style>
