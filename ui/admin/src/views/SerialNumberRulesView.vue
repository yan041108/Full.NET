<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElOption,
  ElSelect,
  ElTag
} from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails,
  type SerialNumberRuleResponse
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import {
  createSerialNumberRule,
  disableSerialNumberRule,
  enableSerialNumberRule,
  listSerialNumberRules,
  previewSerialNumber,
  updateSerialNumberRule
} from '../api/serial-number-rules';

const session = useSessionStore();
const { t } = useAdminI18n();
const rules = ref<SerialNumberRuleResponse[]>([]);
const selectedRuleId = ref<string>();
const ruleKey = ref('');
const displayName = ref('');
const description = ref('');
const scope = ref<0 | 1>(1);
const resetInterval = ref<0 | 1 | 2 | 3>(1);
const pattern = ref('INV-{utc:yyyy}{utc:MM}{utc:dd}-{tenant}-{sequence:5}');
const minimumValue = ref('1');
const maximumValue = ref('99999');
const displayOrder = ref('10');
const isEnabled = ref(true);
const previewTenant = ref('acme');
const previewSequence = ref('42');
const previewAtUtc = ref('2026-07-30T00:00:00Z');
const previewValue = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const canCreate = computed(() => session.can('serial_numbers.rules.create'));
const canUpdate = computed(() => session.can('serial_numbers.rules.update'));
const canEnable = computed(() => session.can('serial_numbers.rules.enable'));
const canDisable = computed(() => session.can('serial_numbers.rules.disable'));
const canPreview = computed(() => session.can('serial_numbers.rules.preview'));
const selectedRule = computed(() =>
  rules.value.find(item => item.id === selectedRuleId.value)
);
const showForm = computed(() =>
  selectedRule.value ? (canUpdate.value || canEnable.value || canDisable.value) : canCreate.value
);

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    rules.value = (await listSerialNumberRules()).items;
  } catch (error: unknown) {
    problem.value = toProblem(error, 'serialNumberRules.loadFailed');
  } finally {
    loading.value = false;
  }
}

function selectRule(rule: SerialNumberRuleResponse): void {
  selectedRuleId.value = rule.id;
  ruleKey.value = rule.ruleKey;
  displayName.value = rule.displayName;
  description.value = rule.description ?? '';
  scope.value = rule.scope;
  resetInterval.value = rule.resetInterval;
  pattern.value = rule.pattern;
  minimumValue.value = String(rule.minimumValue);
  maximumValue.value = String(rule.maximumValue);
  displayOrder.value = String(rule.displayOrder);
  isEnabled.value = rule.isEnabled;
}

function resetCreateForm(): void {
  selectedRuleId.value = undefined;
  ruleKey.value = '';
  displayName.value = '';
  description.value = '';
  scope.value = 1;
  resetInterval.value = 1;
  pattern.value = 'INV-{utc:yyyy}{utc:MM}{utc:dd}-{tenant}-{sequence:5}';
  minimumValue.value = '1';
  maximumValue.value = '99999';
  displayOrder.value = '10';
  isEnabled.value = true;
}

async function createRule(): Promise<void> {
  if (changing.value || !canCreate.value || !ruleKey.value.trim() || !displayName.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await createSerialNumberRule(buildCreateRequest());
    rules.value = [saved, ...rules.value.filter(item => item.id !== saved.id)];
    selectRule(saved);
    ElMessage.success(t('serialNumberRules.createSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function saveRule(): Promise<void> {
  const selected = selectedRule.value;
  if (!selected || changing.value || !canUpdate.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = await updateSerialNumberRule(selected.id, buildUpdateRequest(selected.version));
    const index = rules.value.findIndex(item => item.id === saved.id);
    if (index >= 0) {
      rules.value.splice(index, 1, saved);
    }
    selectRule(saved);
    ElMessage.success(t('serialNumberRules.updateSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function toggleEnabled(enable: boolean): Promise<void> {
  const selected = selectedRule.value;
  if (!selected || changing.value) {
    return;
  }
  if (enable && !canEnable.value) {
    return;
  }
  if (!enable && !canDisable.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    const saved = enable
      ? await enableSerialNumberRule(selected.id, { version: selected.version })
      : await disableSerialNumberRule(selected.id, { version: selected.version });
    const index = rules.value.findIndex(item => item.id === saved.id);
    if (index >= 0) {
      rules.value.splice(index, 1, saved);
    }
    selectRule(saved);
    ElMessage.success(t(enable ? 'serialNumberRules.enableSuccess' : 'serialNumberRules.disableSuccess'));
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

async function runPreview(): Promise<void> {
  if (!canPreview.value || changing.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  previewValue.value = '';
  try {
    const response = await previewSerialNumber({
      scope: scope.value,
      pattern: pattern.value,
      tenantIdentifier: scope.value === 1 ? previewTenant.value.trim() || null : null,
      sequenceValue: Number(previewSequence.value),
      atUtc: previewAtUtc.value
    });
    previewValue.value = response.value;
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    changing.value = false;
  }
}

function buildCreateRequest() {
  return {
    ruleKey: ruleKey.value.trim(),
    displayName: displayName.value.trim(),
    description: description.value.trim() || null,
    scope: scope.value,
    resetInterval: resetInterval.value,
    pattern: pattern.value.trim(),
    minimumValue: Number(minimumValue.value),
    maximumValue: Number(maximumValue.value),
    displayOrder: Number(displayOrder.value),
    isEnabled: isEnabled.value
  };
}

function buildUpdateRequest(version: number) {
  return {
    displayName: displayName.value.trim(),
    description: description.value.trim() || null,
    scope: scope.value,
    resetInterval: resetInterval.value,
    pattern: pattern.value.trim(),
    minimumValue: Number(minimumValue.value),
    maximumValue: Number(maximumValue.value),
    displayOrder: Number(displayOrder.value),
    isEnabled: isEnabled.value,
    version
  };
}

function toProblem(
  error: unknown,
  fallbackCode = 'serialNumberRules.operationFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: fallbackCode, title: t(fallbackCode) };
}
</script>

<template>
  <section class="serial-number-rules-view art-page-stack art-full-height" :aria-busy="loading">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('serialNumberRules.eyebrow') }}</p>
      <h1>{{ t('serialNumberRules.title') }}</h1>
      <p>{{ t('serialNumberRules.description') }}</p>
    </header>

    <ElCard v-if="showForm" class="art-card" :aria-busy="changing">
      <template #header>
        <h2>{{ selectedRule ? t('serialNumberRules.editTitle') : t('serialNumberRules.createTitle') }}</h2>
      </template>
      <div class="art-form-grid">
        <ElInput v-model="ruleKey" data-testid="serial-rule-key" :disabled="!!selectedRule" maxlength="128" :placeholder="t('serialNumberRules.fieldRuleKey')" />
        <ElInput v-model="displayName" data-testid="serial-rule-display-name" maxlength="128" :placeholder="t('serialNumberRules.fieldDisplayName')" />
        <ElInput v-model="description" type="textarea" :rows="2" maxlength="512" :placeholder="t('serialNumberRules.fieldDescription')" />
        <label>
          <span>{{ t('serialNumberRules.fieldScope') }}</span>
          <ElSelect v-model="scope" data-testid="serial-rule-scope" :disabled="changing">
            <ElOption :label="t('serialNumberRules.scopeHost')" :value="0" />
            <ElOption :label="t('serialNumberRules.scopeTenant')" :value="1" />
          </ElSelect>
        </label>
        <label>
          <span>{{ t('serialNumberRules.fieldResetInterval') }}</span>
          <ElSelect v-model="resetInterval" data-testid="serial-rule-reset-interval" :disabled="changing">
            <ElOption :label="t('serialNumberRules.resetNever')" :value="0" />
            <ElOption :label="t('serialNumberRules.resetDay')" :value="1" />
            <ElOption :label="t('serialNumberRules.resetMonth')" :value="2" />
            <ElOption :label="t('serialNumberRules.resetYear')" :value="3" />
          </ElSelect>
        </label>
        <ElInput v-model="pattern" data-testid="serial-rule-pattern" maxlength="256" :placeholder="t('serialNumberRules.fieldPattern')" />
        <ElInput v-model="minimumValue" data-testid="serial-rule-minimum" inputmode="numeric" />
        <ElInput v-model="maximumValue" data-testid="serial-rule-maximum" inputmode="numeric" />
        <ElInput v-model="displayOrder" data-testid="serial-rule-display-order" inputmode="numeric" />
        <div class="art-form-actions">
          <PermissionGate code="serial_numbers.rules.create">
            <ElButton v-if="!selectedRule" data-testid="serial-rule-create" type="primary" :disabled="changing" @click="createRule">
              {{ t('serialNumberRules.create') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="serial_numbers.rules.update">
            <ElButton v-if="selectedRule" data-testid="serial-rule-save" type="primary" :disabled="changing" @click="saveRule">
              {{ t('serialNumberRules.save') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="serial_numbers.rules.enable">
            <ElButton v-if="selectedRule && !selectedRule.isEnabled" data-testid="serial-rule-enable" :disabled="changing" @click="toggleEnabled(true)">
              {{ t('serialNumberRules.enable') }}
            </ElButton>
          </PermissionGate>
          <PermissionGate code="serial_numbers.rules.disable">
            <ElButton v-if="selectedRule && selectedRule.isEnabled" data-testid="serial-rule-disable" type="warning" plain :disabled="changing" @click="toggleEnabled(false)">
              {{ t('serialNumberRules.disable') }}
            </ElButton>
          </PermissionGate>
          <ElButton v-if="!selectedRule" plain :disabled="changing" @click="resetCreateForm">
            {{ t('serialNumberRules.reset') }}
          </ElButton>
        </div>
      </div>
    </ElCard>

    <ElCard v-if="canPreview" class="art-card" :aria-busy="changing">
      <template #header>
        <h2>{{ t('serialNumberRules.previewTitle') }}</h2>
      </template>
      <div class="art-form-grid">
        <ElInput v-if="scope === 1" v-model="previewTenant" data-testid="serial-rule-preview-tenant" maxlength="64" :placeholder="t('serialNumberRules.fieldPreviewTenant')" />
        <ElInput v-model="previewSequence" data-testid="serial-rule-preview-sequence" inputmode="numeric" :placeholder="t('serialNumberRules.fieldPreviewSequence')" />
        <ElInput v-model="previewAtUtc" data-testid="serial-rule-preview-at" :placeholder="t('serialNumberRules.fieldPreviewAtUtc')" />
        <PermissionGate code="serial_numbers.rules.preview">
          <ElButton data-testid="serial-rule-preview" :disabled="changing" @click="runPreview">
            {{ t('serialNumberRules.preview') }}
          </ElButton>
        </PermissionGate>
        <p v-if="previewValue" data-testid="serial-rule-preview-value">{{ previewValue }}</p>
      </div>
    </ElCard>

    <ElCard class="art-card">
      <template #header>
        <h2>{{ t('serialNumberRules.listTitle') }}</h2>
      </template>
      <p v-if="!rules.length" class="art-empty-state">{{ t('serialNumberRules.emptyList') }}</p>
      <ul v-else class="art-list">
        <li v-for="item in rules" :key="item.id">
          <button type="button" data-testid="serial-rule-load" :class="{ 'is-active': selectedRuleId === item.id }" @click="selectRule(item)">
            <strong>{{ item.displayName }}</strong>
            <span class="art-muted">{{ item.ruleKey }}</span>
            <ElTag :type="item.isEnabled ? 'success' : 'info'">
              {{ item.isEnabled ? t('serialNumberRules.statusEnabled') : t('serialNumberRules.statusDisabled') }}
            </ElTag>
          </button>
        </li>
      </ul>
    </ElCard>
  </section>
</template>