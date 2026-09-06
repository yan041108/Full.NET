<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { ElButton, ElCard, ElOption, ElSelect, ElSwitch, ElTag } from 'element-plus';
import { isFullNetProblemDetails, type FullNetProblemDetails } from '@fullnet/client-contracts';
import {
  listDataApprovalScenarios,
  updateDataApprovalScenarioBinding,
  type DataApprovalScenarioResponse
} from '../api/data-approval-scenarios';
import {
  listWorkflowDefinitions,
  listWorkflowDefinitionVersions,
  type WorkflowDefinitionResponse,
  type WorkflowDefinitionVersionResponse
} from '../api/workflow-runtime';
import PermissionGate from '../components/PermissionGate.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import { useSessionStore } from '../auth/session';

const { t } = useAdminI18n();
const session = useSessionStore();
const scenarios = ref<DataApprovalScenarioResponse[]>([]);
const definitions = ref<WorkflowDefinitionResponse[]>([]);
const versions = ref<WorkflowDefinitionVersionResponse[]>([]);
const selectedScenarioKey = ref<string>();
const selectedDefinitionId = ref<string>();
const selectedVersionId = ref<string>();
const isEnabled = ref(false);
const loading = ref(false);
const saving = ref(false);
const problem = ref<FullNetProblemDetails>();
const canManage = computed(() => session.can('data_approvals.scenarios.manage'));
const selectedScenario = computed(() =>
  scenarios.value.find(item => item.scenarioKey === selectedScenarioKey.value));

onMounted(async () => {
  await Promise.all([loadScenarios(), loadDefinitions()]);
});

watch(selectedScenarioKey, async key => {
  if (!key) return;
  const scenario = scenarios.value.find(item => item.scenarioKey === key);
  if (!scenario) return;
  isEnabled.value = scenario.isEnabled;
  selectedVersionId.value = scenario.workflowDefinitionVersionId ?? '';
  const definition = definitions.value.find(item => item.definitionKey === scenario.workflowDefinitionKey);
  selectedDefinitionId.value = definition?.id ?? '';
  if (definition?.id) {
    await loadVersions(definition.id);
  } else {
    versions.value = [];
  }
});

watch(selectedDefinitionId, async definitionId => {
  if (!definitionId) {
    versions.value = [];
    selectedVersionId.value = '';
    return;
  }
  await loadVersions(definitionId);
});

async function loadScenarios(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    scenarios.value = await listDataApprovalScenarios();
    if (!selectedScenarioKey.value && scenarios.value.length > 0) {
      selectedScenarioKey.value = scenarios.value[0].scenarioKey;
    }
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dataApprovalScenarios.loadFailed');
  } finally {
    loading.value = false;
  }
}

async function loadDefinitions(): Promise<void> {
  try {
    definitions.value = await listWorkflowDefinitions();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dataApprovalScenarios.loadFailed');
  }
}

async function loadVersions(definitionId: string): Promise<void> {
  try {
    versions.value = await listWorkflowDefinitionVersions(definitionId);
    if (!versions.value.some(item => item.id === selectedVersionId.value)) {
      selectedVersionId.value = versions.value[0]?.id ?? '';
    }
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dataApprovalScenarios.loadFailed');
  }
}

async function saveBinding(): Promise<void> {
  if (!selectedScenarioKey.value || !selectedScenario.value) return;
  saving.value = true;
  problem.value = undefined;
  try {
    const updated = await updateDataApprovalScenarioBinding(selectedScenarioKey.value, {
      isEnabled: isEnabled.value,
      workflowDefinitionVersionId: isEnabled.value ? selectedVersionId.value : null,
      version: selectedScenario.value.version ?? null
    });
    const index = scenarios.value.findIndex(item => item.scenarioKey === updated.scenarioKey);
    if (index >= 0) {
      scenarios.value[index] = updated;
    }
  } catch (error: unknown) {
    problem.value = toProblem(error, 'dataApprovalScenarios.saveFailed');
  } finally {
    saving.value = false;
  }
}

function scenarioLabel(scenarioKey: string): string {
  if (scenarioKey === 'serial_numbers.host_rule.update') {
    return t('dataApprovalScenarios.scenarioSerialRuleUpdate');
  }
  if (scenarioKey === 'serial_numbers.host_rule.disable') {
    return t('dataApprovalScenarios.scenarioSerialRuleDisable');
  }
  return scenarioKey;
}

function statusTagType(enabled: boolean): 'success' | 'info' {
  return enabled ? 'success' : 'info';
}

function toProblem(error: unknown, fallbackCode: string): FullNetProblemDetails {
  if (isFullNetProblemDetails(error)) return error;
  return { type: 'about:blank', status: 500, code: fallbackCode, title: fallbackCode };
}
</script>

<template>
  <section class="page-shell">
    <header class="page-header">
      <p class="eyebrow">{{ t('dataApprovalScenarios.eyebrow') }}</p>
      <h1>{{ t('dataApprovalScenarios.title') }}</h1>
      <p>{{ t('dataApprovalScenarios.description') }}</p>
    </header>

    <p v-if="problem" role="alert" class="problem-banner">{{ problem.code }}</p>

    <ElCard>
      <template #header>{{ t('dataApprovalScenarios.listTitle') }}</template>
      <div v-if="loading && !scenarios.length">{{ t('dataApprovalScenarios.loadFailed') }}</div>
      <div v-else-if="!scenarios.length">{{ t('dataApprovalScenarios.emptyList') }}</div>
      <ul v-else class="scenario-list">
        <li v-for="item in scenarios" :key="item.scenarioKey">
          <button
            type="button"
            data-testid="data-approval-scenario-select"
            @click="selectedScenarioKey = item.scenarioKey"
          >
            <strong>{{ scenarioLabel(item.scenarioKey) }}</strong>
            <ElTag :type="statusTagType(item.isEnabled)" data-testid="data-approval-scenario-status">
              {{ item.isEnabled ? t('dataApprovalScenarios.enabled') : t('dataApprovalScenarios.disabled') }}
            </ElTag>
          </button>
        </li>
      </ul>
    </ElCard>

    <ElCard v-if="selectedScenario">
      <template #header>{{ t('dataApprovalScenarios.bindingTitle') }}</template>
      <div class="form-grid">
        <label>{{ t('dataApprovalScenarios.fieldEnabled') }}
          <ElSwitch v-model="isEnabled" data-testid="data-approval-scenario-enabled" :disabled="!canManage" />
        </label>
        <label>{{ t('dataApprovalScenarios.fieldWorkflowDefinition') }}
          <ElSelect
            v-model="selectedDefinitionId"
            data-testid="data-approval-scenario-definition"
            :disabled="!canManage || !isEnabled"
            clearable
          >
            <ElOption
              v-for="definition in definitions"
              :key="definition.id"
              :value="definition.id"
              :label="definition.definitionKey"
            />
          </ElSelect>
        </label>
        <label>{{ t('dataApprovalScenarios.fieldWorkflowVersion') }}
          <ElSelect
            v-model="selectedVersionId"
            data-testid="data-approval-scenario-version"
            :disabled="!canManage || !isEnabled || !selectedDefinitionId"
          >
            <ElOption
              v-for="version in versions"
              :key="version.id"
              :value="version.id"
              :label="`v${version.versionNumber}`"
            />
          </ElSelect>
        </label>
      </div>
      <PermissionGate code="data_approvals.scenarios.manage">
        <ElButton
          type="primary"
          data-testid="data-approval-scenario-save"
          :loading="saving"
          @click="saveBinding"
        >
          {{ t('dataApprovalScenarios.save') }}
        </ElButton>
      </PermissionGate>
    </ElCard>
  </section>
</template>

<style scoped>
.scenario-list { list-style: none; padding: 0; margin: 0; }
.scenario-list button { display: flex; gap: 0.75rem; align-items: center; width: 100%; text-align: left; padding: 0.5rem 0; background: none; border: 0; cursor: pointer; }
.form-grid { display: grid; gap: 1rem; margin-bottom: 1rem; }
</style>
