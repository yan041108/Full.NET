<script setup lang="ts">
import { ref, watch } from 'vue';
import type { WorkflowDefinitionDraft } from '@fullnet/client-contracts';
import NodeWrap from './vendor/workflow-vue3/src/components/nodeWrap.vue';
import { useStore } from './vendor/workflow-vue3/src/stores/index.js';
import type { WorkflowVue3Node } from './workflow-vue3-adapter';
import { fromWorkflowVue3Tree } from './workflow-vue3-adapter';
import './vendor/workflow-vue3/src/css/workflow.css';
import './vendor/workflow-vue3/src/css/override-element-ui.css';
import './vendor/workflow-vue3/src/css/dialog.css';

const props = defineProps<{
  modelValue: WorkflowVue3Node;
  disabled: boolean;
}>();
const emit = defineEmits<{
  'update:modelValue': [value: WorkflowVue3Node];
  'update:draft': [draft: WorkflowDefinitionDraft];
  'validation-error': [code: string];
}>();
const store = useStore();
const nodeConfig = ref<WorkflowVue3Node>(cloneWorkflowTree(props.modelValue));

watch(() => props.modelValue, value => {
  nodeConfig.value = cloneWorkflowTree(value);
}, { deep: true });
watch(nodeConfig, value => {
  store.setFlowNodeConfig(value);
  emit('update:modelValue', cloneWorkflowTree(value));
}, { deep: true, immediate: true });

/** Workflow-Vue3 编辑树是纯 JSON 契约，使用 JSON 克隆可安全跨越 Vue Proxy 边界。 */
function cloneWorkflowTree(value: WorkflowVue3Node): WorkflowVue3Node {
  return JSON.parse(JSON.stringify(value)) as WorkflowVue3Node;
}

function readDraft(): WorkflowDefinitionDraft {
  try {
    const draft = fromWorkflowVue3Tree(nodeConfig.value);
    emit('update:draft', draft);
    return draft;
  } catch (error: unknown) {
    emit('validation-error', error instanceof Error
      ? error.message
      : 'client.invalid_workflow_definition_draft');
    throw error;
  }
}

defineExpose({ readDraft });
</script>

<template>
  <section class="workflow-vue3-adapter dingflow-design" data-testid="workflow-vue3-designer">
    <div class="workflow-vue3-adapter__canvas" :class="{ 'is-disabled': disabled }">
      <node-wrap v-model:node-config="nodeConfig" />
      <div class="end-node">
        <div class="end-node-circle" />
        <div class="end-node-text">流程结束</div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.workflow-vue3-adapter { min-height: 620px; overflow: auto; background: var(--el-fill-color-light); }
.workflow-vue3-adapter__canvas { min-width: 960px; min-height: 620px; padding: 48px 24px 96px; transform-origin: 50% 0; }
.workflow-vue3-adapter__canvas.is-disabled { pointer-events: none; opacity: 0.72; }
</style>
