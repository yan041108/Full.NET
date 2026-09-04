<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElMessage, ElTag } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import { listMyWorkflowCc, markWorkflowCcRead, type WorkflowCcResponse } from '../api/workflow-cc';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';

const { t } = useAdminI18n();
const records = ref<WorkflowCcResponse[]>([]);
const loading = ref(false);
const actingId = ref<string>();
const problem = ref<FullNetProblemDetails>();
let loadController: AbortController | undefined;

onMounted(load);
onBeforeUnmount(() => loadController?.abort());

/** 加载当前用户有权查看的最近抄送记录。 */
async function load(): Promise<void> {
  loadController?.abort();
  loadController = new AbortController();
  loading.value = true;
  problem.value = undefined;
  try {
    records.value = await listMyWorkflowCc(loadController.signal);
  } catch (error: unknown) {
    if (!loadController.signal.aborted) {
      problem.value = toProblem(error);
    }
  } finally {
    loading.value = false;
  }
}

/** 幂等标记一条本人抄送为已读，并立即更新本地只读投影。 */
async function markRead(record: WorkflowCcResponse): Promise<void> {
  if (record.readAtUtc !== null || actingId.value !== undefined) return;
  actingId.value = record.id;
  problem.value = undefined;
  try {
    const result = await markWorkflowCcRead(record.id);
    records.value = records.value.map(item => item.id === record.id
      ? { ...item, readAtUtc: result.readAtUtc }
      : item);
    ElMessage.success(t('workflowCc.markReadSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    actingId.value = undefined;
  }
}

/** 把未知客户端异常收敛为可展示的 ProblemDetails。 */
function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.workflow_cc_failed', title: t('workflowCc.operationFailed') };
}
</script>

<template>
  <section class="workflow-cc art-page-stack art-full-height" :aria-busy="loading || actingId !== undefined">
    <header>
      <h1 data-route-heading tabindex="-1">{{ t('workflowCc.title') }}</h1>
      <p>{{ t('workflowCc.caption') }}</p>
    </header>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-card shadow="never">
      <div v-if="records.length === 0 && !loading" class="workflow-cc__empty">
        {{ t('workflowCc.empty') }}
      </div>
      <div v-else class="workflow-cc__table-wrap">
        <table>
          <thead>
            <tr>
              <th>{{ t('workflowCc.business') }}</th>
              <th>{{ t('workflowCc.node') }}</th>
              <th>{{ t('workflowCc.createdAt') }}</th>
              <th>{{ t('workflowCc.status') }}</th>
              <th>{{ t('workflowCc.actions') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="record in records" :key="record.id" :class="{ 'is-unread': record.readAtUtc === null }">
              <td><strong translate="no">{{ record.businessType }}</strong><code translate="no">{{ record.businessId }}</code></td>
              <td><code translate="no">{{ record.nodeKey }}</code></td>
              <td><time :datetime="record.createdAtUtc">{{ record.createdAtUtc }}</time></td>
              <td><el-tag :type="record.readAtUtc === null ? 'warning' : 'info'">{{ t(record.readAtUtc === null ? 'workflowCc.unread' : 'workflowCc.read') }}</el-tag></td>
              <td>
                <PermissionGate code="workflow.cc.mark_read">
                  <el-button
                    data-testid="workflow-cc-mark-read"
                    :disabled="record.readAtUtc !== null"
                    :loading="actingId === record.id"
                    @click="markRead(record)"
                  >{{ t('workflowCc.markRead') }}</el-button>
                </PermissionGate>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </el-card>
  </section>
</template>

<style scoped>
.workflow-cc { display: grid; gap: 1rem; }
.workflow-cc header h1 { margin: 0; }
.workflow-cc header p { margin: .35rem 0 0; color: var(--el-text-color-secondary); }
.workflow-cc__table-wrap { overflow-x: auto; }
.workflow-cc table { width: 100%; border-collapse: collapse; }
.workflow-cc th, .workflow-cc td { padding: .85rem; border-bottom: 1px solid var(--el-border-color-lighter); text-align: left; }
.workflow-cc td:first-child { display: grid; gap: .25rem; }
.workflow-cc tr.is-unread td:first-child { border-left: 3px solid var(--el-color-warning); }
.workflow-cc__empty { padding: 3rem 1rem; color: var(--el-text-color-secondary); text-align: center; }
</style>
