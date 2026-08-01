<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElButton, ElCard, ElMessage, ElTag } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getDiagnosticPolicy,
  restoreDiagnosticPolicy,
  type DiagnosticPolicy
} from '../api/diagnostic-policy';

const session = useSessionStore();
const { t } = useAdminI18n();
const policy = ref<DiagnosticPolicy>();
const loading = ref(false);
const canWrite = computed(() => session.can('settings.diagnostic-policy.write'));

onMounted(load);

async function load(): Promise<void> {
  loading.value = true;
  try {
    policy.value = await getDiagnosticPolicy();
  } catch {
    ElMessage.error(t('diagnosticPolicy.loadFailed'));
  } finally {
    loading.value = false;
  }
}

async function restore(): Promise<void> {
  if (!policy.value || !canWrite.value) return;
  loading.value = true;
  try {
    policy.value = await restoreDiagnosticPolicy(policy.value.configEntryVersion);
    ElMessage.success(t('diagnosticPolicy.restoreSuccess'));
  } catch {
    ElMessage.error(t('diagnosticPolicy.operationFailed'));
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <section class="diagnostic-policy-view">
    <header class="diagnostic-policy-view__heading">
      <p>{{ t('diagnosticPolicy.eyebrow') }}</p>
      <h1>{{ t('diagnosticPolicy.title') }}</h1>
      <span>{{ t('diagnosticPolicy.description') }}</span>
    </header>

    <ElCard>
      <template #header>
        <div class="diagnostic-policy-view__toolbar">
          <strong>{{ t('diagnosticPolicy.actionsTitle') }}</strong>
          <ElButton :disabled="!canWrite || loading" @click="restore">
            {{ t('diagnosticPolicy.restore') }}
          </ElButton>
        </div>
      </template>
      <p class="hint">{{ t('diagnosticPolicy.hint') }}</p>
      <p v-if="loading">{{ t('diagnosticPolicy.loading') }}</p>
      <template v-else-if="policy">
        <p>
          {{ t('diagnosticPolicy.pressureLabel') }}:
          <ElTag>{{ policy.pressureState }}</ElTag>
          <ElTag
            v-if="policy.isDefault"
            type="success"
            data-diagnostic-policy-state="default"
          >
            {{ t('diagnosticPolicy.defaultState') }}
          </ElTag>
        </p>
        <p>
          {{
            t('diagnosticPolicy.summary', {
              version: policy.version,
              configEntryVersion: policy.configEntryVersion,
              ruleCount: policy.activeRules.length
            })
          }}
        </p>
        <p v-if="policy.activeRules.length === 0" class="hint">
          {{ t('diagnosticPolicy.emptyRules') }}
        </p>
        <ul v-else>
          <li v-for="(rule, index) in policy.activeRules" :key="index">
            {{ rule.scopeKind }}={{ rule.scopeValue }}
            —
            {{ t('diagnosticPolicy.expiresAt', { expiresAtUtc: rule.expiresAtUtc }) }}
          </li>
        </ul>
      </template>
    </ElCard>
  </section>
</template>

<style scoped>
.diagnostic-policy-view {
  display: grid;
  gap: 16px;
}

.diagnostic-policy-view__heading h1 {
  margin: 4px 0;
}

.diagnostic-policy-view__toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
}

.hint {
  color: var(--el-text-color-secondary);
  font-size: 12px;
}
</style>