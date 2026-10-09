<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { ElButton, ElDialog, ElInput, ElMessageBox } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails } from '@fullnet/client-contracts';
import { createEnterpriseRequestsApi, enterpriseRequestsHttp, enterpriseRequestPermissions,
  type EnterpriseRequestApprovalProgressResponse } from '../../api/enterprise-requests';
import { useSessionStore } from '../../auth/session';
import { useAuthorizedViewScope } from '../../composables/useAuthorizedViewScope';
import { useTaskStatusRefresh } from '../../composables/useTaskStatusRefresh';
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
const refreshing = ref(false);
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

const needsNotificationRefresh = computed(() => progress.value?.deliveryState === 'finalized'
  && (progress.value.finalNotification === null || (progress.value.finalNotification?.pendingDeliveryCount ?? 0) > 0));
// 审批与通知分别跟踪；人工恢复草稿优先，后台刷新不能覆盖正在编辑的输入。
useTaskStatusRefresh(() => canRead.value && !problem.value && !repairing.value
  && (progress.value?.deliveryState === 'queued' || progress.value?.deliveryState === 'started' || needsNotificationRefresh.value)
  && repairReason.value === '' && repairInstanceId.value === (progress.value?.workflowInstanceId ?? ''),
async current => { await fetchProgress(true, current); });

function reset(): void {
  progress.value = undefined; problem.value = undefined; loading.value = false;
  refreshing.value = false;
  currentRequest = undefined;
  repairInstanceId.value = ''; repairReason.value = ''; repairing.value = false;
}
watch(() => props.requestId, () => { scope.invalidate(); void load(); }, { flush: 'sync' });

async function load(): Promise<void> {
  await fetchProgress(false);
}
async function fetchProgress(background: boolean, current: () => boolean = () => true): Promise<void> {
  if (!canRead.value || loading.value || refreshing.value || repairing.value) return;
  const request = scope.begin(enterpriseRequestPermissions.read);
  if (!request) return;
  currentRequest = request;
  const id = props.requestId;
  if (background) refreshing.value = true;
  else { loading.value = true; progress.value = undefined; problem.value = undefined; }
  try {
    const value = await api.approvalProgress(id, request.signal);
    if (request.current() && id === props.requestId && current()) {
      progress.value = value; repairInstanceId.value = value.workflowInstanceId ?? ''; repairReason.value = '';
    }
  } catch (error: unknown) {
    if (!request.current() || id !== props.requestId || !current()) return;
    progress.value = undefined; repairInstanceId.value = ''; repairReason.value = '';
    problem.value = isFullNetProblemDetails(error) ? error : {
      status: 500, code: 'client.enterprise_request_approval_progress_load_failed', title: t('enterpriseRequests.progressLoadFailed')
    };
  } finally {
    request.finish();
    // 只释放本次查询的加载态，隐藏页面或草稿变化可以使刷新票据失效而不改变请求归属。
    if (currentRequest === request) {
      loading.value = false; refreshing.value = false; currentRequest = undefined;
    }
  }
}
async function repair(): Promise<void> {
  if (!canRepair.value || !validRepair.value || repairing.value || refreshing.value || !progress.value) return;
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
        <section v-if="progress.deliveryState === 'finalized'" class="notification-progress" aria-labelledby="final-notification-title">
          <h3 id="final-notification-title">{{ t('enterpriseRequests.notificationTitle') }}</h3>
          <p v-if="progress.finalNotification === undefined">{{ t('enterpriseRequests.notificationLegacy') }}</p>
          <p v-else-if="progress.finalNotification === null">{{ t('enterpriseRequests.notificationUnaccepted') }}</p>
          <template v-else>
            <p>{{ t('enterpriseRequests.notificationAccepted') }}</p>
            <dl class="approval-progress">
              <dt>{{ t('enterpriseRequests.notificationAcceptedAt') }}</dt><dd>{{ time(progress.finalNotification.acceptedAtUtc) }}</dd>
              <dt>{{ t('enterpriseRequests.notificationTotal') }}</dt><dd>{{ progress.finalNotification.totalDeliveryCount }}</dd>
              <dt>{{ t('enterpriseRequests.notificationPending') }}</dt><dd data-testid="notification-pending">{{ progress.finalNotification.pendingDeliveryCount }}</dd>
              <dt>{{ t('enterpriseRequests.notificationSent') }}</dt><dd data-testid="notification-sent">{{ progress.finalNotification.sentDeliveryCount }}</dd>
              <dt>{{ t('enterpriseRequests.notificationDelivered') }}</dt><dd data-testid="notification-delivered">{{ progress.finalNotification.deliveredDeliveryCount ?? 0 }}</dd>
              <dt>{{ t('enterpriseRequests.notificationRead') }}</dt><dd data-testid="notification-read">{{ progress.finalNotification.readDeliveryCount ?? 0 }}</dd>
              <dt>{{ t('enterpriseRequests.notificationSuppressed') }}</dt><dd data-testid="notification-suppressed">{{ progress.finalNotification.suppressedDeliveryCount ?? 0 }}</dd>
              <dt>{{ t('enterpriseRequests.notificationPersisted') }}</dt><dd data-testid="notification-persisted">{{ progress.finalNotification.persistedDeliveryCount ?? 0 }}</dd>
              <dt>{{ t('enterpriseRequests.notificationFailed') }}</dt><dd data-testid="notification-failed">{{ progress.finalNotification.failedDeliveryCount }}</dd>
              <dt>{{ t('enterpriseRequests.notificationDeadLettered') }}</dt><dd data-testid="notification-dead-lettered">{{ progress.finalNotification.deadLetteredDeliveryCount }}</dd>
              <dt>{{ t('enterpriseRequests.notificationUnknown') }}</dt><dd data-testid="notification-unknown">{{ progress.finalNotification.unknownDeliveryCount }}</dd>
              <dt>{{ t('enterpriseRequests.notificationOther') }}</dt><dd data-testid="notification-other">{{ progress.finalNotification.otherDeliveryCount }}</dd>
              <dt>{{ t('enterpriseRequests.notificationNextAttempt') }}</dt><dd>{{ time(progress.finalNotification.nextAttemptAtUtc) }}</dd>
            </dl>
            <p>{{ t('enterpriseRequests.notificationHint') }}</p>
          </template>
        </section>
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
      <el-button v-if="canRepair" :loading="repairing" :disabled="!validRepair || repairing || refreshing" @click="repair">{{ t('enterpriseRequests.repairApproval') }}</el-button>
      <el-button v-if="canRead" :loading="loading || refreshing" :disabled="repairing || refreshing" @click="load">{{ t('common.refresh') }}</el-button>
      <el-button @click="close">{{ t('common.cancel') }}</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.approval-progress { display: grid; grid-template-columns: minmax(7rem, auto) minmax(0, 1fr); gap: .75rem 1rem; }
.approval-progress dt { color: var(--el-text-color-regular); }
.approval-progress dd { margin: 0; overflow-wrap: anywhere; }
.approval-repair { display: grid; gap: .5rem; margin-top: 1rem; }
.notification-progress { margin-top: 1.25rem; }
.notification-progress h3 { font-size: 1rem; }
</style>
