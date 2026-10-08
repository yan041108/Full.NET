<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { ElButton, ElDialog, ElInput, ElMessageBox } from 'element-plus';
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
const repairInstanceId = ref('');
const repairReason = ref('');
const repairing = ref(false);
const canRead = computed(() => session.can(enterpriseRequestPermissions.read));
const canRepair = computed(() => canRead.value && session.can(enterpriseRequestPermissions.repairApproval)
  && progress.value?.requestStatus === 'Submitted' && progress.value.requestVersion >= 2 && !loading.value);
const validRepair = computed(() => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(repairInstanceId.value.trim())
  && repairInstanceId.value.trim() !== '00000000-0000-0000-0000-000000000000'
  && repairReason.value.trim().length > 0 && repairReason.value.trim().length <= 500 && !/\p{Cc}/u.test(repairReason.value));
const canOpenInbox = computed(() => canRead.value && session.can('notifications.inbox.read') && !!progress.value);
let currentRequest: ReturnType<typeof scope.begin>;
const scope = useAuthorizedViewScope(session, reset, load);

function reset(): void {
  progress.value = undefined; problem.value = undefined; loading.value = false;
  currentRequest = undefined;
  repairInstanceId.value = ''; repairReason.value = ''; repairing.value = false;
}
watch(() => props.requestId, () => { scope.invalidate(); void load(); }, { flush: 'sync' });

async function load(): Promise<void> {
  if (!canRead.value || loading.value || repairing.value) return;
  const request = scope.begin(enterpriseRequestPermissions.read);
  if (!request) return;
  currentRequest = request;
  const id = props.requestId;
  loading.value = true; progress.value = undefined; problem.value = undefined;
  try {
    const value = await api.approvalProgress(id, request.signal);
    if (request.current() && id === props.requestId) {
      progress.value = value; repairInstanceId.value = value.workflowInstanceId ?? ''; repairReason.value = '';
    }
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
async function repair(): Promise<void> {
  if (!canRepair.value || !validRepair.value || repairing.value || !progress.value) return;
  const request = scope.begin(enterpriseRequestPermissions.repairApproval);
  if (!request) return;
  const id = props.requestId;
  const body = { workflowInstanceId: repairInstanceId.value.trim(), expectedVersion: progress.value.requestVersion,
    reason: repairReason.value.trim() };
  repairing.value = true; problem.value = undefined;
  let refresh = false;
  try {
    await ElMessageBox.confirm(t('enterpriseRequests.repairConfirm'), t('enterpriseRequests.repairApproval'), {
      confirmButtonText: t('enterpriseRequests.repairApproval'), cancelButtonText: t('common.cancel'), type: 'warning'
    });
    // 确认等待期间上下文可能变化，写入仍必须属于原账号、租户、权限与单据代次。
    if (!request.current() || !canRepair.value || id !== props.requestId) return;
    await api.repairApproval(id, body, request.signal);
    if (request.current() && canRead.value && id === props.requestId) refresh = true;
  } catch (error: unknown) {
    if (!request.current() || id !== props.requestId || error === 'cancel' || error === 'close') return;
    problem.value = isFullNetProblemDetails(error) ? error : {
      status: 500, code: 'client.enterprise_request_approval_repair_failed', title: t('enterpriseRequests.repairFailed')
    };
  } finally {
    if (request.current() && id === props.requestId) repairing.value = false;
    request.finish();
    if (refresh) { scope.invalidate(); void load(); }
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
        <div v-if="canRepair" class="approval-repair">
          <p>{{ t('enterpriseRequests.repairHint') }}</p>
          <label for="approval-repair-instance">{{ t('enterpriseRequests.repairInstance') }}</label>
          <el-input id="approval-repair-instance" v-model="repairInstanceId" :disabled="repairing || !!progress.workflowInstanceId"
            :placeholder="t('enterpriseRequests.repairInstance')" maxlength="36" />
          <label for="approval-repair-reason">{{ t('enterpriseRequests.repairReason') }}</label>
          <el-input id="approval-repair-reason" v-model="repairReason" type="textarea" :disabled="repairing"
            :placeholder="t('enterpriseRequests.repairReason')" maxlength="500" :rows="3" />
        </div>
      </template>
    </div>
    <template #footer>
      <el-button v-if="canOpenInbox" @click="openInbox">{{ t('enterpriseRequests.openInbox') }}</el-button>
      <el-button v-if="canRepair" :loading="repairing" :disabled="!validRepair || repairing" @click="repair">{{ t('enterpriseRequests.repairApproval') }}</el-button>
      <el-button v-if="canRead" :loading="loading" :disabled="repairing" @click="load">{{ t('common.refresh') }}</el-button>
      <el-button @click="close">{{ t('common.cancel') }}</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.approval-progress { display: grid; grid-template-columns: minmax(7rem, auto) minmax(0, 1fr); gap: .75rem 1rem; }
.approval-progress dt { color: var(--el-text-color-secondary); }
.approval-progress dd { margin: 0; overflow-wrap: anywhere; }
.approval-repair { display: grid; gap: .5rem; margin-top: 1rem; }
</style>
