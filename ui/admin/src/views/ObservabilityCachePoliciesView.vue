<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
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

async function loadPolicies(): Promise<void> {
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
    policies.value = await listObservabilityCachePolicies();
    if (!selected.value) {
      selected.value = policies.value[0];
    }
    resetOperationForm();
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

function selectPolicy(policy: CachePolicySummary): void {
  selected.value = policy;
  resetOperationForm();
}

function resetOperationForm(): void {
  selectedOperationKey.value = selected.value?.invalidationOperations[0]?.operationKey;
  parameterValues.value = {};
  lastResult.value = [];
}

async function submitInvalidation(): Promise<void> {
  if (!selected.value || !selectedOperation.value || !canInvalidate.value) {
    return;
  }

  invalidating.value = true;
  problem.value = undefined;
  try {
    const result = await invalidateObservabilityCachePolicy(
      selected.value.entryName,
      {
        operationKey: selectedOperation.value.operationKey,
        parameters: parameterValues.value,
        scope
      }
    );
    lastResult.value = result.invalidatedTargets;
    ElMessage.success(t('observabilityCachePolicies.invalidateSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    invalidating.value = false;
  }
}

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
        <ElButton type="primary" :loading="loading" @click="loadPolicies">
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
          {{ t(`observabilityCachePolicies.accessKind.${selected.accessKind}`) }}
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
      </ElDescriptions>

      <div v-if="selected.canInvalidate && canInvalidate" class="observability-cache-policies__form">
        <ElForm label-position="top">
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
            <ElInput v-model="parameterValues[parameter.name]" />
          </ElFormItem>
          <ElFormItem :label="t('observabilityCachePolicies.scope')">
            <ElSelect v-model="scope">
              <ElOption
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
            {{ t('observabilityCachePolicies.invalidate') }}
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
