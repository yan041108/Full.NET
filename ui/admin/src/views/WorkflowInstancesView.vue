<script setup lang="ts">
import { computed, onBeforeUnmount, ref } from 'vue';
import { ElButton, ElCard, ElInput } from 'element-plus';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import {
  getWorkflowInstance,
  listWorkflowInstanceExecutionLogs,
  type WorkflowExecutionLogResponse,
  type WorkflowInstanceResponse
} from '../api/workflow-instances';
import { useAdminI18n } from '../i18n/adminI18n';

const { t } = useAdminI18n();
const instanceId = ref('');
const loading = ref(false);
const instance = ref<WorkflowInstanceResponse>();
const executionLogs = ref<WorkflowExecutionLogResponse[]>([]);
const problem = ref<FullNetProblemDetails>();
let loadController: AbortController | undefined;

const canSearch = computed(() => instanceId.value.trim().length > 0 && !loading.value);

onBeforeUnmount(() => loadController?.abort());

async function load(): Promise<void> {
  const requestedId = instanceId.value.trim();
  if (requestedId.length === 0 || loading.value) {
    return;
  }

  loadController?.abort();
  const controller = new AbortController();
  loadController = controller;
  loading.value = true;
  problem.value = undefined;
  instance.value = undefined;
  executionLogs.value = [];

  try {
    const [loadedInstance, loadedLogs] = await Promise.all([
      getWorkflowInstance(requestedId, controller.signal),
      listWorkflowInstanceExecutionLogs(requestedId, controller.signal)
    ]);
    if (!controller.signal.aborted) {
      instance.value = loadedInstance;
      executionLogs.value = loadedLogs;
    }
  } catch (error: unknown) {
    if (!controller.signal.aborted) {
      problem.value = toProblem(error);
    }
  } finally {
    if (loadController === controller) {
      loading.value = false;
    }
  }
}

function toProblem(error: unknown): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : {
        status: 500,
        code: 'client.workflow_instance_failed',
        title: t('workflowInstances.loadFailed')
      };
}
</script>

<template>
  <section class="workflow-instances art-page-stack art-full-height" :aria-busy="loading">
    <header class="workflow-instances__header">
      <div>
        <span class="workflow-instances__eyebrow">{{ t('workflowInstances.eyebrow') }}</span>
        <h1 data-route-heading tabindex="-1">{{ t('workflowInstances.title') }}</h1>
        <p>{{ t('workflowInstances.caption') }}</p>
      </div>
    </header>

    <el-card shadow="never" class="workflow-instances__search-card">
      <form class="workflow-instances__search" @submit.prevent="load">
        <label for="workflow-instance-id">{{ t('workflowInstances.instanceId') }}</label>
        <div class="workflow-instances__search-row">
          <el-input
            id="workflow-instance-id"
            v-model="instanceId"
            data-testid="workflow-instance-id"
            clearable
            autocomplete="off"
            :placeholder="t('workflowInstances.instanceIdPlaceholder')"
          />
          <el-button
            type="primary"
            native-type="submit"
            data-testid="workflow-instance-search"
            :loading="loading"
            :disabled="!canSearch"
            @click="load"
          >
            {{ t('workflowInstances.search') }}
          </el-button>
        </div>
        <small>{{ t('workflowInstances.searchHint') }}</small>
      </form>
    </el-card>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <template v-if="instance">
      <section
        class="workflow-instances__summary"
        data-testid="workflow-instance-summary"
        :aria-label="t('workflowInstances.summary')"
      >
        <article class="workflow-instances__identity">
          <span>{{ t('workflowInstances.business') }}</span>
          <strong>{{ instance.businessType }} · {{ instance.businessId }}</strong>
          <code translate="no">{{ instance.id }}</code>
        </article>
        <article>
          <span>{{ t('workflowInstances.status') }}</span>
          <strong class="workflow-instances__status" translate="no">{{ instance.statusKey }}</strong>
        </article>
        <article>
          <span>{{ t('workflowInstances.revision') }}</span>
          <strong translate="no">#{{ instance.revision }}</strong>
        </article>
        <article>
          <span>{{ t('workflowInstances.startedAt') }}</span>
          <time :datetime="instance.startedAtUtc">{{ instance.startedAtUtc }}</time>
        </article>
        <article>
          <span>{{ t('workflowInstances.activeTodo') }}</span>
          <code translate="no">{{ instance.activeTodoId ?? t('workflowInstances.none') }}</code>
        </article>
      </section>

      <el-card shadow="never" class="workflow-instances__timeline-card">
        <template #header>
          <div class="workflow-instances__timeline-heading">
            <div>
              <h2>{{ t('workflowInstances.timeline') }}</h2>
              <p>{{ t('workflowInstances.timelineCaption') }}</p>
            </div>
            <span translate="no">{{ executionLogs.length }}</span>
          </div>
        </template>

        <div v-if="executionLogs.length === 0" class="workflow-instances__empty">
          {{ t('workflowInstances.noLogs') }}
        </div>
        <ol v-else class="workflow-instances__timeline">
          <li
            v-for="log in executionLogs"
            :key="log.id"
            data-testid="workflow-execution-log"
          >
            <span class="workflow-instances__marker" aria-hidden="true"></span>
            <div class="workflow-instances__event">
              <div class="workflow-instances__event-heading">
                <strong translate="no">{{ log.transitionKey }}</strong>
                <time :datetime="log.createdAtUtc">{{ log.createdAtUtc }}</time>
              </div>
              <div class="workflow-instances__transition">
                <code translate="no">{{ log.fromStatusKey ?? '∅' }}</code>
                <span aria-hidden="true">→</span>
                <code translate="no">{{ log.toStatusKey }}</code>
              </div>
              <small v-if="log.stepId">
                {{ t('workflowInstances.step') }}
                <code translate="no">{{ log.stepId }}</code>
              </small>
            </div>
          </li>
        </ol>
      </el-card>
    </template>

    <div v-else-if="!problem && !loading" class="workflow-instances__landing">
      <span aria-hidden="true">01 — N</span>
      <p>{{ t('workflowInstances.empty') }}</p>
    </div>
  </section>
</template>

<style scoped>
.workflow-instances {
  display: grid;
  gap: 1rem;
  max-width: 1120px;
  margin: 0 auto;
}

.workflow-instances__header h1 {
  margin: 0.2rem 0 0;
  color: var(--el-text-color-primary);
  font-size: clamp(1.5rem, 2.5vw, 2.15rem);
  letter-spacing: -0.035em;
}

.workflow-instances__header p,
.workflow-instances__timeline-heading p {
  margin: 0.45rem 0 0;
  color: var(--el-text-color-secondary);
}

.workflow-instances__eyebrow {
  color: var(--el-color-primary);
  font-size: 0.72rem;
  font-weight: 800;
  letter-spacing: 0.16em;
  text-transform: uppercase;
}

.workflow-instances__search {
  display: grid;
  gap: 0.55rem;
}

.workflow-instances__search label {
  color: var(--el-text-color-primary);
  font-weight: 720;
}

.workflow-instances__search small {
  color: var(--el-text-color-secondary);
}

.workflow-instances__search-row {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  gap: 0.75rem;
}

.workflow-instances__summary {
  display: grid;
  grid-template-columns: minmax(260px, 1.55fr) repeat(4, minmax(130px, 1fr));
  overflow: hidden;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: var(--el-border-radius-base);
  background: var(--el-bg-color);
}

.workflow-instances__summary article {
  display: grid;
  align-content: start;
  gap: 0.4rem;
  min-height: 108px;
  padding: 1rem;
  border-left: 1px solid var(--el-border-color-lighter);
}

.workflow-instances__summary article:first-child {
  border-left: 0;
}

.workflow-instances__summary span {
  color: var(--el-text-color-secondary);
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.06em;
  text-transform: uppercase;
}

.workflow-instances__summary strong,
.workflow-instances__summary time {
  color: var(--el-text-color-primary);
}

.workflow-instances__identity {
  background: linear-gradient(145deg, var(--el-color-primary-light-9), transparent 72%);
}

.workflow-instances__identity code,
.workflow-instances__summary article > code {
  overflow-wrap: anywhere;
  color: var(--el-text-color-secondary);
  font-size: 0.72rem;
}

.workflow-instances__status {
  width: fit-content;
  padding: 0.25rem 0.55rem;
  border-radius: 999px;
  color: var(--el-color-primary-dark-2) !important;
  background: var(--el-color-primary-light-9);
}

.workflow-instances__timeline-heading,
.workflow-instances__event-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
}

.workflow-instances__timeline-heading h2 {
  margin: 0;
  font-size: 1.05rem;
}

.workflow-instances__timeline-heading > span {
  display: grid;
  min-width: 2.2rem;
  height: 2.2rem;
  place-items: center;
  border: 1px solid var(--el-border-color);
  border-radius: 50%;
  color: var(--el-text-color-secondary);
  font-weight: 750;
}

.workflow-instances__timeline {
  display: grid;
  gap: 0;
  margin: 0;
  padding: 0;
  list-style: none;
}

.workflow-instances__timeline li {
  position: relative;
  display: grid;
  grid-template-columns: 20px minmax(0, 1fr);
  gap: 0.9rem;
  padding-bottom: 1.25rem;
}

.workflow-instances__timeline li:not(:last-child)::before {
  position: absolute;
  top: 12px;
  bottom: -2px;
  left: 5px;
  width: 1px;
  background: var(--el-border-color);
  content: '';
}

.workflow-instances__marker {
  z-index: 1;
  width: 11px;
  height: 11px;
  margin-top: 0.25rem;
  border: 2px solid var(--el-bg-color);
  border-radius: 50%;
  background: var(--el-color-primary);
  box-shadow: 0 0 0 1px var(--el-color-primary);
}

.workflow-instances__event {
  display: grid;
  gap: 0.55rem;
  padding: 0 0 1rem;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.workflow-instances__event-heading time,
.workflow-instances__event small {
  color: var(--el-text-color-secondary);
  font-size: 0.75rem;
}

.workflow-instances__transition {
  display: flex;
  align-items: center;
  gap: 0.55rem;
}

.workflow-instances__transition code {
  padding: 0.2rem 0.45rem;
  border-radius: 4px;
  background: var(--el-fill-color-light);
}

.workflow-instances__landing,
.workflow-instances__empty {
  display: grid;
  min-height: 150px;
  place-items: center;
  color: var(--el-text-color-secondary);
  text-align: center;
}

.workflow-instances__landing span {
  color: var(--el-border-color-darker);
  font-size: clamp(2.5rem, 8vw, 5.5rem);
  font-weight: 800;
  letter-spacing: -0.08em;
}

.workflow-instances__landing p {
  margin: -3rem 1rem 0;
}

@media (max-width: 900px) {
  .workflow-instances__summary {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .workflow-instances__summary article,
  .workflow-instances__summary article:first-child {
    border-top: 1px solid var(--el-border-color-lighter);
    border-left: 1px solid var(--el-border-color-lighter);
  }

  .workflow-instances__identity {
    grid-column: 1 / -1;
  }
}

@media (max-width: 560px) {
  .workflow-instances__search-row,
  .workflow-instances__summary {
    grid-template-columns: 1fr;
  }

  .workflow-instances__summary article,
  .workflow-instances__identity {
    grid-column: auto;
  }

  .workflow-instances__event-heading {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
