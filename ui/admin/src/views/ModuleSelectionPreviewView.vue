<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElCheckbox,
  ElCheckboxGroup,
  ElMessage,
  ElOption,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag
} from 'element-plus';
import type {
  FullNetProblemDetails,
  IdentityModuleSelectionAnalysis
} from '@fullnet/client-contracts';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  getModuleSelectionRuntime,
  validateModuleSelection
} from '../api/module-selection';

defineOptions({ name: 'ModuleSelectionPreviewView' });

const PRESETS = ['Full', 'Minimal', 'Platform', 'Content'] as const;

const { t } = useAdminI18n();
const runtime = ref<IdentityModuleSelectionAnalysis | null>(null);
const preview = ref<IdentityModuleSelectionAnalysis | null>(null);
const mode = ref<'preset' | 'explicit'>('preset');
const selectedPreset = ref<string>('Full');
const selectedModules = ref<string[]>([]);
const loading = ref(false);
const validating = ref(false);
const problem = ref<FullNetProblemDetails>();

const activeAnalysis = computed(() => preview.value ?? runtime.value);

const officialModules = computed(() =>
  runtime.value?.officialModuleKeys ?? activeAnalysis.value?.officialModuleKeys ?? []);

async function loadRuntime() {
  loading.value = true;
  problem.value = undefined;
  try {
    runtime.value = await getModuleSelectionRuntime();
    if (mode.value === 'explicit' && selectedModules.value.length === 0) {
      selectedModules.value = [...runtime.value.enabledModuleKeys];
    }
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    loading.value = false;
  }
}

async function runValidate() {
  validating.value = true;
  problem.value = undefined;
  try {
    preview.value = await validateModuleSelection(
      mode.value === 'preset'
        ? { preset: selectedPreset.value }
        : { enabled: selectedModules.value }
    );
    if (preview.value.isValid) {
      ElMessage.success(t('moduleSelection.validateSuccess'));
    } else {
      ElMessage.warning(t('moduleSelection.validateInvalid'));
    }
  } catch (error) {
    problem.value = error as FullNetProblemDetails;
  } finally {
    validating.value = false;
  }
}

function resetPreview() {
  preview.value = null;
}

onMounted(() => {
  void loadRuntime();
});
</script>

<template>
  <div class="module-selection-preview-view art-page-stack">
    <ArtTableHeader :title="t('moduleSelection.title')" />
    <p class="art-muted">{{ t('moduleSelection.description') }}</p>

    <ElAlert
      v-if="runtime"
      type="info"
      :closable="false"
      :title="t('moduleSelection.deploymentNoticeTitle')"
      :description="runtime.deploymentNotice"
      show-icon
      class="module-selection-notice"
    />

    <div class="module-selection-layout">
      <ElCard v-loading="loading">
        <h2>{{ t('moduleSelection.runtimeTitle') }}</h2>
        <template v-if="runtime">
          <p>
            <strong>{{ t('moduleSelection.sourceKind') }}:</strong>
            {{ runtime.sourceKind }}
            <template v-if="runtime.preset">
              ({{ runtime.preset }})
            </template>
          </p>
          <p>
            <ElTag :type="runtime.isValid ? 'success' : 'danger'">
              {{ runtime.isValid ? t('moduleSelection.valid') : t('moduleSelection.invalid') }}
            </ElTag>
          </p>
          <p class="art-muted">
            {{ t('moduleSelection.enabledCount', { count: runtime.enabledModuleKeys.length }) }}
          </p>
        </template>
        <ElButton :loading="loading" @click="loadRuntime">
          {{ t('moduleSelection.refreshRuntime') }}
        </ElButton>
      </ElCard>

      <ElCard>
        <h2>{{ t('moduleSelection.validateTitle') }}</h2>
        <div class="module-selection-mode">
          <ElSelect v-model="mode">
            <ElOption :label="t('moduleSelection.modePreset')" value="preset" />
            <ElOption :label="t('moduleSelection.modeExplicit')" value="explicit" />
          </ElSelect>
          <ElSelect
            v-if="mode === 'preset'"
            v-model="selectedPreset"
          >
            <ElOption
              v-for="preset in PRESETS"
              :key="preset"
              :label="preset"
              :value="preset"
            />
          </ElSelect>
        </div>
        <ElCheckboxGroup
          v-if="mode === 'explicit'"
          v-model="selectedModules"
          class="module-selection-checkboxes"
        >
          <ElCheckbox
            v-for="moduleKey in officialModules"
            :key="moduleKey"
            :label="moduleKey"
            :value="moduleKey"
          />
        </ElCheckboxGroup>
        <div class="module-selection-actions">
          <ElButton type="primary" :loading="validating" @click="runValidate">
            {{ t('moduleSelection.validateAction') }}
          </ElButton>
          <ElButton @click="resetPreview">
            {{ t('moduleSelection.resetPreview') }}
          </ElButton>
        </div>
      </ElCard>
    </div>

    <ElCard v-if="activeAnalysis" class="module-selection-results">
      <h2>{{ preview ? t('moduleSelection.previewTitle') : t('moduleSelection.runtimeModulesTitle') }}</h2>
      <ul v-if="activeAnalysis.issues.length" class="module-selection-issues">
        <li v-for="issue in activeAnalysis.issues" :key="`${issue.code}-${issue.moduleKey}-${issue.relatedModuleKey}`">
          <strong>{{ issue.code }}</strong>: {{ issue.message }}
        </li>
      </ul>
      <ElTable :data="activeAnalysis.modules" size="small" border>
        <ElTableColumn prop="moduleKey" :label="t('moduleSelection.moduleKey')" min-width="160" />
        <ElTableColumn :label="t('moduleSelection.enabled')" width="100">
          <template #default="{ row }">
            <ElTag :type="row.isEnabled ? 'success' : 'info'">
              {{ row.isEnabled ? t('moduleSelection.yes') : t('moduleSelection.no') }}
            </ElTag>
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('moduleSelection.dependencies')" min-width="180">
          <template #default="{ row }">
            {{ row.dependencies.join(', ') || '—' }}
          </template>
        </ElTableColumn>
        <ElTableColumn :label="t('moduleSelection.missingDependencies')" min-width="180">
          <template #default="{ row }">
            <span v-if="!row.missingDependencies.length">—</span>
            <ElTag
              v-for="dependency in row.missingDependencies"
              :key="dependency"
              type="danger"
              class="module-selection-missing-tag"
            >
              {{ dependency }}
            </ElTag>
          </template>
        </ElTableColumn>
      </ElTable>
    </ElCard>
  </div>
</template>

<style scoped>
.module-selection-notice {
  margin-bottom: 16px;
}

.module-selection-layout {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 16px;
  margin-bottom: 16px;
}

.module-selection-mode {
  display: grid;
  gap: 12px;
  margin-bottom: 16px;
}

.module-selection-checkboxes {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
  gap: 8px;
  margin-bottom: 16px;
}

.module-selection-actions {
  display: flex;
  gap: 12px;
}

.module-selection-issues {
  color: var(--el-color-danger);
  margin: 0 0 12px;
  padding-left: 20px;
}

.module-selection-missing-tag {
  margin-right: 6px;
}
</style>
