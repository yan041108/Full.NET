<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { ElButton, ElDialog } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails } from '@fullnet/client-contracts';
import { createEnterpriseRequestsApi, enterpriseRequestsHttp, enterpriseRequestPermissions,
  type EnterpriseRequestResponse } from '../../api/enterprise-requests';
import { useSessionStore } from '../../auth/session';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../../i18n/adminI18n';
import { requestStatusLabel, requestTime } from './enterprise-request-presentation';

const props = defineProps<{ requestId: string }>();
const emit = defineEmits<{ close: [] }>();
const session = useSessionStore();
const { t, locale } = useAdminI18n();
const api = createEnterpriseRequestsApi(enterpriseRequestsHttp);
const record = ref<EnterpriseRequestResponse>();
const problem = ref<FullNetProblemDetails>();
const loading = ref(false);
const canRead = computed(() => session.can(enterpriseRequestPermissions.read));
const scope = useAuthorizedViewScope(session, reset, load);

function reset(): void {
  record.value = undefined; problem.value = undefined; loading.value = false;
}
watch(() => props.requestId, () => { scope.invalidate(); void load(); }, { flush: 'sync' });
async function load(): Promise<void> {
  if (!canRead.value || loading.value) return;
  const request = scope.begin(enterpriseRequestPermissions.read);
  if (!request) return;
  const id = props.requestId;
  loading.value = true; record.value = undefined; problem.value = undefined;
  try {
    const value = await api.get(id, request.signal);
    if (request.current() && id === props.requestId) record.value = value;
  } catch (error: unknown) {
    if (!request.current() || id !== props.requestId) return;
    problem.value = isFullNetProblemDetails(error) ? error : {
      status: 500, code: 'client.enterprise_request_detail_load_failed', title: t('enterpriseRequests.detailLoadFailed')
    };
  } finally {
    if (request.current() && id === props.requestId) loading.value = false;
    request.finish();
  }
}
function close(): void {
  // 关闭立即失效并清空详情，动画期间也不得接入迟到资料。
  scope.invalidate(); emit('close');
}
</script>

<template>
  <el-dialog :model-value="true" :title="t('enterpriseRequests.detailTitle')" width="min(720px, 94vw)"
    @update:model-value="open => { if (!open) close(); }">
    <div :aria-busy="loading" aria-live="polite">
      <p v-if="loading">{{ t('common.loading') }}</p>
      <p v-else-if="!canRead">{{ t('enterpriseRequests.progressAccessDenied') }}</p>
      <div v-else-if="problem" class="art-inline-alert" role="alert">
        <strong translate="no">{{ problem.code }}</strong><span>{{ problem.title }}</span>
      </div>
      <dl v-else-if="record" class="request-detail">
        <dt>{{ t('enterpriseRequests.requestNumber') }}</dt><dd translate="no">{{ record.requestNumber }}</dd>
        <dt>{{ t('enterpriseRequests.title') }}</dt><dd>{{ record.title }}</dd>
        <dt>{{ t('enterpriseRequests.requestStatus') }}</dt><dd>{{ requestStatusLabel(record.status, t) }}</dd>
        <!-- 精确十进制字符串保持原值，禁止先转换 Number 丢失金额精度。 -->
        <dt>{{ t('enterpriseRequests.totalAmount') }}</dt><dd>{{ typeof record.totalAmount === 'number'
          ? new Intl.NumberFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(record.totalAmount) : record.totalAmount }}</dd>
        <dt>{{ t('enterpriseRequests.requestVersion') }}</dt><dd>{{ record.version }}</dd>
        <dt>{{ t('enterpriseRequests.id') }}</dt><dd translate="no">{{ record.id }}</dd>
        <dt>{{ t('enterpriseRequests.organizationUnitId') }}</dt><dd translate="no">{{ record.organizationUnitId }}</dd>
        <dt>{{ t('enterpriseRequests.applicantUserId') }}</dt><dd translate="no">{{ record.applicantUserId }}</dd>
        <dt>{{ t('enterpriseRequests.createdById') }}</dt><dd translate="no">{{ record.createdById }}</dd>
        <dt>{{ t('enterpriseRequests.createdAtUtc') }}</dt><dd>{{ requestTime(record.createdAtUtc, locale) }}</dd>
        <dt>{{ t('enterpriseRequests.updatedById') }}</dt><dd translate="no">{{ record.updatedById ?? '—' }}</dd>
        <dt>{{ t('enterpriseRequests.updatedAtUtc') }}</dt><dd>{{ requestTime(record.updatedAtUtc, locale) }}</dd>
      </dl>
    </div>
    <template #footer>
      <el-button v-if="canRead" :loading="loading" @click="load">{{ t('common.refresh') }}</el-button>
      <el-button @click="close">{{ t('common.cancel') }}</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.request-detail { display: grid; grid-template-columns: minmax(7rem, auto) minmax(0, 1fr); gap: .75rem 1rem; }
.request-detail dt { font-weight: 600; }
.request-detail dd { margin: 0; overflow-wrap: anywhere; }
@media (max-width: 480px) {
  .request-detail { grid-template-columns: minmax(0, 1fr); gap: .25rem; }
  .request-detail dd { margin-bottom: .75rem; }
}
</style>
