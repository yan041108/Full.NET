<script setup lang="ts">
import { translateRuntimeMessage } from '../i18n/runtimeMessage';
import { computed, onMounted, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElDescriptions,
  ElDescriptionsItem,
  ElForm,
  ElFormItem,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type {
  CacheInvalidationOperationSummary,
  CachePolicySummary,
  FullNetProblemDetails
} from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  invalidateObservabilityCachePolicy,
  listObservabilityCachePolicies
} from '../api/observability-cache-policies';

defineOptions({ name: 'ObservabilityCachePoliciesView' });

const session = useSessionStore();
const { t } = useAdminI18n();
const policies = ref<CachePolicySummary[]>([]);
const selected = ref<CachePolicySummary>();
const selectedOperationKey = ref<string>();
const parameterValues = ref<Record<string, string>>({});
const scope = ref('all_layers_synchronous');
const loading = ref(false);
const invalidating = ref(false);
const problem = ref<FullNetProblemDetails>();
const lastResult = ref<string[]>([]);
let catalogRequestVersion = 0;
const canRead = computed(() => session.can('observability.cache_policies.read'));
const canInvalidate = computed(() => session.can('observability.cache_policies.invalidate'));
const selectedOperation = computed<CacheInvalidationOperationSummary | undefined>(() =>
  selected.value?.invalidationOperations.find(
    operation => operation.operationKey === selectedOperationKey.value
  ));

function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        type: 'about:blank',
        title: t('observabilityCachePolicies.loadFailed'),
        status: 500,
        code: 'client.unexpected_error'
      };
}

function formatDuration(seconds: number | null | undefined): string {
  if (seconds == null) {
    return t('observabilityCachePolicies.notApplicable');
  }
  if (seconds < 60) {
    return `${seconds}s`;
  }
  if (seconds < 3600) {
    return `${Math.round(seconds / 60)}m`;
  }
  return `${Math.round(seconds / 3600)}h`;
}

function formatPolicyDuration(seconds: number | string | null | undefined): string {
  if (seconds === undefined) return t('observabilityCachePolicies.unknown');
  if (seconds === null) return t('observabilityCachePolicies.notApplicable');
  const value = typeof seconds === 'string' && seconds.trim() === '' ? NaN : Number(seconds);
  if (!Number.isFinite(value) || value < 0) return t('observabilityCachePolicies.unknown');
  return `${value}s`;
}

function formatPolicyBoolean(value: boolean): string {
  return value ? t('observabilityCachePolicies.yes') : t('observabilityCachePolicies.no');
}

function formatPayloadBudget(bytes: number | null | undefined): string {
  if (bytes === undefined) return t('observabilityCachePolicies.unknown');
  if (bytes === null) return t('observabilityCachePolicies.notApplicable');
  return `${bytes} B`;
}

function formatRuntimeStatus(status: string | undefined): string {
  if (status === 'snapshot_loaded') return t('observabilityCachePolicies.snapshotLoaded');
  if (status === 'safe_default') return t('observabilityCachePolicies.safeDefault');
  if (status === 'observing') return t('observabilityCachePolicies.observing');
  if (status === 'not_applicable') return t('observabilityCachePolicies.notApplicable');
  return t('observabilityCachePolicies.unknown');
}

async function loadPolicies(): Promise<void> {
  if (loading.value) return;
  const version = ++catalogRequestVersion;
  if (!canRead.value) {
    problem.value = {
      type: 'about:blank',
      title: t('observabilityCachePolicies.forbidden'),
      status: 403,
      code: 'client.forbidden'
    };
    return;
  }

  loading.value = true;
  problem.value = undefined;
  try {
    const loaded = await listObservabilityCachePolicies();
    if (version !== catalogRequestVersion || !canRead.value) return;
    policies.value = loaded;
    // 目录刷新必须替换选中对象，避免详情和失效权限仍引用上一轮策略。
    selected.value = policies.value.find(policy => policy.entryName === selected.value?.entryName)
      ?? policies.value[0];
    resetOperationForm();
  } catch (error: unknown) {
    if (version === catalogRequestVersion && canRead.value) problem.value = toProblem(error);
  } finally {
    if (version === catalogRequestVersion) loading.value = false;
  }
}

function selectPolicy(policy: CachePolicySummary): void {
  if (loading.value || invalidating.value || !canRead.value) return;
  selected.value = policy;
  resetOperationForm();
}

function resetOperationForm(): void {
  scope.value = selected.value?.accessKind === 'local_snapshot' ? 'current_node_only' : 'all_layers_synchronous';
  selectedOperationKey.value = selected.value?.invalidationOperations[0]?.operationKey;
  parameterValues.value = {};
  lastResult.value = [];
}

async function submitInvalidation(): Promise<void> {
  if (loading.value || invalidating.value || !selected.value?.canInvalidate || !selectedOperation.value || !canRead.value || !canInvalidate.value) {
    return;
  }

  invalidating.value = true;
  problem.value = undefined;
  const entryName = selected.value.entryName;
  const isSnapshot = selected.value.accessKind === 'local_snapshot';
  const version = catalogRequestVersion;
  try {
    const result = await invalidateObservabilityCachePolicy(
      entryName,
      {
        operationKey: selectedOperation.value.operationKey,
        parameters: parameterValues.value,
        scope: scope.value
      }
    );
    // 权限变化或目录替换后的迟到结果不能显示到另一条目上。
    if (version === catalogRequestVersion && canRead.value && canInvalidate.value && selected.value?.entryName === entryName) {
      lastResult.value = result.invalidatedTargets;
      ElMessage.success(t(isSnapshot ? 'observabilityCachePolicies.snapshotRefreshSuccess' : 'observabilityCachePolicies.invalidateSuccess'));
    }
  } catch (error: unknown) {
    if (version === catalogRequestVersion && canRead.value) problem.value = toProblem(error);
  } finally {
    invalidating.value = false;
  }
}

watch(canRead, allowed => {
  if (allowed) {
    void loadPolicies();
    return;
  }
  // 撤销读取权限后清除已有目录，同时让在途响应失效。
  ++catalogRequestVersion;
  loading.value = false;
  policies.value = [];
  selected.value = undefined;
  resetOperationForm();
});

onMounted(() => {
  void loadPolicies();
});
</script>

<template>
  <section class="observability-cache-policies art-page-stack" :aria-busy="loading || invalidating">
    <header>
      <p class="art-eyebrow">{{ t('observabilityCachePolicies.eyebrow') }}</p>
      <h1>{{ t('observabilityCachePolicies.title') }}</h1>
      <p>{{ t('observabilityCachePolicies.description') }}</p>
    </header>

    <p v-if="problem" class="art-problem">{{ problem.title }}</p>

    <ElCard class="art-card">
      <div class="observability-cache-policies__toolbar">
        <ElButton v-if="canRead" type="primary" :loading="loading" :disabled="invalidating" @click="loadPolicies">
          {{ t('observabilityCachePolicies.refresh') }}
        </ElButton>
      </div>

      <ElTable
        :data="policies"
        highlight-current-row
        row-key="entryName"
        @row-click="selectPolicy"
      >
        <ElTableColumn prop="entryName" :label="t('observabilityCachePolicies.entryName')" min-width="200" />
        <ElTableColumn prop="ownerModule" :label="t('observabilityCachePolicies.ownerModule')" width="120" />
        <ElTableColumn prop="consistencyClass" :label="t('observabilityCachePolicies.consistencyClass')" width="100" />
        <ElTableColumn :label="t('observabilityCachePolicies.canInvalidate')" width="120">
          <template #default="{ row }">
            <ElTag :type="row.canInvalidate ? 'success' : 'info'">
              {{ row.canInvalidate ? t('observabilityCachePolicies.yes') : t('observabilityCachePolicies.no') }}
            </ElTag>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>

    <ElCard v-if="selected" class="art-card">
      <template #header>
        <h2>{{ selected.entryName }}</h2>
      </template>

      <ElDescriptions :column="2" border>
        <ElDescriptionsItem :label="t('observabilityCachePolicies.accessKind')">
          {{ translateRuntimeMessage(t, `observabilityCachePolicies.accessKind.${selected.accessKind}`) }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityCachePolicies.l1Duration')">
          {{ formatDuration(selected.l1DurationSeconds) }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityCachePolicies.l2Duration')">
          {{ formatDuration(selected.l2DurationSeconds) }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityCachePolicies.directInvalidation')">
          {{ selected.requiresDirectInvalidation ? t('observabilityCachePolicies.yes') : t('observabilityCachePolicies.no') }}
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityCachePolicies.jitter')">
          <span data-policy-field="jitter">{{ formatPolicyDuration(selected.jitterSeconds) }}</span>
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityCachePolicies.negativeDuration')">
          <span data-policy-field="negative-duration">{{ formatPolicyDuration(selected.negativeDurationSeconds) }}</span>
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityCachePolicies.failSafe')">
          <span data-policy-field="fail-safe">{{ formatPolicyBoolean(selected.failSafeEnabled) }}</span>
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityCachePolicies.failSafeRetention')">
          <span data-policy-field="fail-safe-retention">{{ formatPolicyDuration(selected.failSafeMaxDurationSeconds) }}</span>
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityCachePolicies.versionRecheck')">
          <span data-policy-field="version-recheck">{{ formatPolicyBoolean(selected.requiresVersionRecheck) }}</span>
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityCachePolicies.maxBytes')">
          <span data-policy-field="max-bytes">{{ formatPayloadBudget(selected.maxSerializedBytes) }}</span>
        </ElDescriptionsItem>
        <ElDescriptionsItem :label="t('observabilityCachePolicies.runtimeStatus')">
          <span data-policy-field="runtime-status">{{ formatRuntimeStatus(selected.runtimeStatus) }}</span>
        </ElDescriptionsItem>
        <ElDescriptionsItem v-if="selected.accessKind === 'local_snapshot'" :label="t('observabilityCachePolicies.snapshotAge')">
          <span data-policy-field="snapshot-age">{{ formatPolicyDuration(selected.snapshotAgeSeconds) }}</span>
        </ElDescriptionsItem>
        <ElDescriptionsItem v-if="selected.accessKind === 'local_snapshot'" :label="t('observabilityCachePolicies.snapshotMaxAge')">
          {{ formatPolicyDuration(selected.snapshotMaxAgeSeconds) }}
        </ElDescriptionsItem>
      </ElDescriptions>
      <p class="art-muted">{{ selected.accessKind === 'local_snapshot' ? t('observabilityCachePolicies.snapshotHint') : t('observabilityCachePolicies.runtimeStatusHint') }}</p>

      <div v-if="canRead && selected.canInvalidate && canInvalidate" class="observability-cache-policies__form">
        <ElForm label-position="top" :disabled="loading || invalidating">
          <ElFormItem :label="t('observabilityCachePolicies.operation')">
            <ElSelect v-model="selectedOperationKey" @change="parameterValues = {}">
              <ElOption
                v-for="operation in selected.invalidationOperations"
                :key="operation.operationKey"
                :label="operation.displayName"
                :value="operation.operationKey"
              />
            </ElSelect>
          </ElFormItem>
          <ElFormItem
            v-for="parameter in selectedOperation?.parameters ?? []"
            :key="parameter.name"
            :label="parameter.name"
            :required="parameter.required"
          >
            <ElInput v-model="parameterValues[parameter.name]" :maxlength="256" />
          </ElFormItem>
          <ElFormItem :label="t('observabilityCachePolicies.scope')">
            <ElSelect v-model="scope">
              <ElOption
                v-if="selected.accessKind !== 'local_snapshot'"
                value="all_layers_synchronous"
                :label="t('observabilityCachePolicies.scope.all_layers_synchronous')"
              />
              <ElOption
                value="current_node_only"
                :label="t('observabilityCachePolicies.scope.current_node_only')"
              />
            </ElSelect>
          </ElFormItem>
          <ElButton
            type="danger"
            :loading="invalidating"
            @click="submitInvalidation"
          >
            {{ t(selected.accessKind === 'local_snapshot' ? 'observabilityCachePolicies.refreshSnapshot' : 'observabilityCachePolicies.invalidate') }}
          </ElButton>
        </ElForm>
      </div>

      <ul v-if="lastResult.length > 0" class="observability-cache-policies__result">
        <li v-for="target in lastResult" :key="target">{{ target }}</li>
      </ul>
    </ElCard>
  </section>
</template>

<style scoped>
.observability-cache-policies__toolbar {
  display: flex;
  justify-content: flex-end;
  margin-bottom: 1rem;
}

.observability-cache-policies__form {
  margin-top: 1.25rem;
}

.observability-cache-policies__result {
  margin: 1rem 0 0;
  padding-left: 1.25rem;
}
</style>
