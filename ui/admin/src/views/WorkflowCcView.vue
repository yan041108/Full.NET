<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElMessage, ElTable, ElTableColumn, ElTag } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import { useRouter } from 'vue-router';
import { listMyWorkflowCc, markWorkflowCcRead, type WorkflowCcResponse } from '../api/workflow-cc';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  findWorkflowBusinessDetailRoute,
  formatWorkflowBusinessLabel
} from '../workflow/workflowBusinessDetail';

import { formatAdminDateTime } from '../workflow/workflowAdminFormat';
import { useArtCrudTableLayout } from '../framework/art-design/composables/useArtCrudTableLayout';

const { t, locale } = useAdminI18n();

function formatDateTime(value: string): string {
  return formatAdminDateTime(locale.value, value);
}
const router = useRouter();
const records = ref<WorkflowCcResponse[]>([]);
const loading = ref(false);
const { tableMainRef, tableHeight, updateTableHeight, watchLoading } = useArtCrudTableLayout({
  bottomOffset: 8
});
watchLoading(loading);
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
    void nextTick(updateTableHeight);
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

/** 通过可信白名单路由打开业务单据详情。 */
function openBusinessDetail(businessType: string, businessId: string): void {
  const route = findWorkflowBusinessDetailRoute(businessType);
  if (route === undefined) {
    return;
  }
  void router.push({ name: route.routeName, query: { [route.idQueryKey]: businessId } });
}
</script>

<template>
  <section class="workflow-cc art-page-stack art-full-height" :aria-busy="loading || actingId !== undefined">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('workflowCc.title') }}</h1>
    <p class="art-sr-heading">{{ t('workflowCc.caption') }}</p>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
    </div>

    <el-card class="workflow-cc__card art-table-card" shadow="never">
      <div ref="tableMainRef" class="art-crud-table-main">
      <el-table
        v-loading="loading"
        :data="records"
        :height="tableHeight"
        row-key="id"
        class="workflow-cc__table"
        empty-text=""
        :row-class-name="({ row }) => row.readAtUtc === null ? 'is-unread' : ''"
      >
        <el-table-column :label="t('workflowCc.business')" min-width="200">
          <template #default="{ row }">
            <div class="workflow-cc__business">
              <strong translate="no">{{ formatWorkflowBusinessLabel(row.businessTitle, row.businessType, row.businessId) }}</strong>
              <el-button
                v-if="findWorkflowBusinessDetailRoute(row.businessType)"
                link
                type="primary"
                data-testid="workflow-cc-view-document"
                @click="openBusinessDetail(row.businessType, row.businessId)"
              >
                {{ t('workflow.business.viewDocument') }}
              </el-button>
            </div>
          </template>
        </el-table-column>
        <el-table-column :label="t('workflowCc.node')" min-width="120" show-overflow-tooltip>
          <template #default="{ row }"><code translate="no">{{ row.nodeKey }}</code></template>
        </el-table-column>
        <el-table-column :label="t('workflowCc.createdAt')" width="168">
          <template #default="{ row }">
            <time translate="no" :datetime="row.createdAtUtc">{{ formatDateTime(row.createdAtUtc) }}</time>
          </template>
        </el-table-column>
        <el-table-column :label="t('workflowCc.status')" width="96" align="center">
          <template #default="{ row }">
            <el-tag size="small" :type="row.readAtUtc === null ? 'warning' : 'info'">
              {{ t(row.readAtUtc === null ? 'workflowCc.unread' : 'workflowCc.read') }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('workflowCc.actions')" width="120" fixed="right">
          <template #default="{ row }">
            <PermissionGate code="workflow.cc.mark_read">
              <el-button
                size="small"
                data-testid="workflow-cc-mark-read"
                :disabled="row.readAtUtc !== null"
                :loading="actingId === row.id"
                @click="markRead(row as WorkflowCcResponse)"
              >{{ t('workflowCc.markRead') }}</el-button>
            </PermissionGate>
          </template>
        </el-table-column>
        <template #empty>
          <p v-if="!loading" class="workflow-cc__empty">{{ t('workflowCc.empty') }}</p>
        </template>
      </el-table>
      </div>
    </el-card>
  </section>
</template>

<style scoped>
.workflow-cc { display: grid; gap: 1rem; min-height: 0; }
.workflow-cc__card {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}
.workflow-cc__card :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding-top: 0;
}
.workflow-cc__business { display: grid; gap: 4px; }
.workflow-cc :deep(tr.is-unread td:first-child) { box-shadow: inset 3px 0 0 var(--el-color-warning); }
.workflow-cc__empty { padding: 2rem 1rem; color: var(--el-text-color-secondary); text-align: center; }
</style>
