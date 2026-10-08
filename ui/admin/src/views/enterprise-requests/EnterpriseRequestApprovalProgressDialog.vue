<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { ElButton, ElDialog } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails } from '@fullnet/client-contracts';
import { createEnterpriseRequestsApi, enterpriseRequestsHttp, enterpriseRequestPermissions,
  type EnterpriseRequestApprovalProgressResponse } from '../../api/enterprise-requests';
import { useSessionStore } from '../../auth/session';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { useAdminI18n } from '../../i18n/adminI18n';

const props = defineProps<{ requestId: string }>();
const emit = defineEmits<{ close: [] }>();
const session = useSessionStore();
const router = useRouter();
const { t, locale } = useAdminI18n();
const api = createEnterpriseRequestsApi(enterpriseRequestsHttp);
const progress = ref<EnterpriseRequestApprovalProgressResponse>();
const problem = ref<FullNetProblemDetails>();
const loading = ref(false);
const canRead = computed(() => session.can(enterpriseRequestPermissions.read));
const canOpenInbox = computed(() => canRead.value && session.can('notifications.inbox.read') && !!progress.value);
let currentRequest: ReturnType<typeof scope.begin>;
const scope = useAuthorizedViewScope(session, reset, load);

function reset(): void {
  progress.value = undefined; problem.value = undefined; loading.value = false;
  currentRequest = undefined;
}
watch(() => props.requestId, () => { scope.invalidate(); void load(); }, { flush: 'sync' });

async function load(): Promise<void> {
  if (!canRead.value || loading.value) return;
  const request = scope.begin(enterpriseRequestPermissions.read);
  if (!request) return;
  currentRequest = request;
  const id = props.requestId;
  loading.value = true; progress.value = undefined; problem.value = undefined;
  try {
    const value = await api.approvalProgress(id, request.signal);
    if (request.current() && id === props.requestId) progress.value = value;
  } catch (error: unknown) {
    if (!request.current() || id !== props.requestId) return;
    problem.value = isFullNetProblemDetails(error) ? error : {
      status: 500, code: 'client.enterprise_request_approval_progress_load_failed', title: t('enterpriseRequests.progressLoadFailed')
    };
  } finally {
    if (request.current() && id === props.requestId) loading.value = false;
    request.finish();
    if (currentRequest === request) currentRequest = undefined;
  }
}
function close(): void {
  // 关闭同步清空敏感快照并取消当前请求，避免弹窗动画期间接入迟到内容。
  scope.invalidate(); emit('close');
}
function openInbox(): void {
  if (!canOpenInbox.value) return;
  close();
  void router.push({ name: 'inbox-messages' });
}
const stateLabel = computed(() => progress.value ? t(`enterpriseRequests.progress.${progress.value.deliveryState}`) : '');
function time(value: string | null | undefined): string {
  return value ? new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'medium' }).format(new Date(value)) : '—';
}
</script>

<template>
  <el-dialog :model-value="true" :title="t('enterpriseRequests.approvalProgress')" width="min(640px, 94vw)"
    @update:model-value="open => { if (!open) close(); }">
    <div :aria-busy="loading" aria-live="polite">
      <p v-if="loading">{{ t('common.loading') }}</p>
      <p v-else-if="!canRead">{{ t('enterpriseRequests.progressAccessDenied') }}</p>
      <div v-else-if="problem" class="art-inline-alert" role="alert">
        <strong translate="no">{{ problem.code }}</strong><span>{{ problem.title }}</span>
      </div>
      <template v-else-if="progress">
        <p><strong>{{ stateLabel }}</strong></p>
        <p>{{ t('enterpriseRequests.progressHint') }}</p>
        <p v-if="progress.deliveryState === 'recovery_required'">{{ t('enterpriseRequests.progressRecoveryHint') }}</p>
        <dl class="approval-progress">
          <dt>{{ t('enterpriseRequests.requestStatus') }}</dt><dd translate="no">{{ progress.requestStatus }}</dd>
          <dt>{{ t('enterpriseRequests.requestVersion') }}</dt><dd>{{ progress.requestVersion }}</dd>
          <dt>{{ t('workflowInstances.instanceId') }}</dt><dd><code translate="no">{{ progress.workflowInstanceId ?? '—' }}</code></dd>
          <dt>{{ t('enterpriseRequests.submittedAt') }}</dt><dd>{{ time(progress.submittedAtUtc) }}</dd>
          <dt>{{ t('enterpriseRequests.startedAt') }}</dt><dd>{{ time(progress.startedAtUtc) }}</dd>
          <dt>{{ t('enterpriseRequests.completedAt') }}</dt><dd>{{ time(progress.completedAtUtc) }}</dd>
        </dl>
      </template>
    </div>
    <template #footer>
      <el-button v-if="canOpenInbox" @click="openInbox">{{ t('enterpriseRequests.openInbox') }}</el-button>
      <el-button v-if="canRead" :loading="loading" @click="load">{{ t('common.refresh') }}</el-button>
      <el-button @click="close">{{ t('common.cancel') }}</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.approval-progress { display: grid; grid-template-columns: minmax(7rem, auto) minmax(0, 1fr); gap: .75rem 1rem; }
.approval-progress dt { color: var(--el-text-color-secondary); }
.approval-progress dd { margin: 0; overflow-wrap: anywhere; }
</style>
