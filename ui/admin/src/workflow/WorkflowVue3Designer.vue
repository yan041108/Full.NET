<script setup lang="ts">
import { computed, nextTick, provide, ref, watch } from 'vue';
import type { WorkflowDefinitionDraft } from '@fullnet/client-contracts';
import NodeWrap from './vendor/workflow-vue3/src/components/nodeWrap.vue';
import { useStore } from './vendor/workflow-vue3/src/stores/index.js';
import type { WorkflowVue3Node } from './workflow-vue3-adapter';
import { fromWorkflowVue3Tree } from './workflow-vue3-adapter';
import './vendor/workflow-vue3/src/css/workflow.css';
import './vendor/workflow-vue3/src/css/override-element-ui.css';
import './vendor/workflow-vue3/src/css/dialog.css';

const props = withDefaults(defineProps<{
  modelValue: WorkflowVue3Node;
  disabled: boolean;
  enabledNodeTypes?: readonly string[];
}>(), {
  enabledNodeTypes: () => ['start', 'human.approval', 'end']
});
const emit = defineEmits<{
  'update:modelValue': [value: WorkflowVue3Node];
  'update:draft': [draft: WorkflowDefinitionDraft];
  'validation-error': [code: string];
}>();
const store = useStore();
const transientDesignerKeys = new Set([
  'error',
  'errorTip',
  'settype',
  'examineMode',
  'nodeUserList',
  'placeHolder'
]);
// 复制设计器只能暴露服务端同时声明为可发布、可执行的节点类型。
provide(
  'fullnetWorkflowEnabledNodeTypes',
  computed(() => new Set(props.enabledNodeTypes))
);
const nodeConfig = ref<WorkflowVue3Node>(cloneWorkflowTree(props.modelValue));
let syncingExternalModel = true;
store.setFlowNodeConfig(nodeConfig.value);
void nextTick(() => {
  syncingExternalModel = false;
});

watch(() => props.modelValue, value => {
  syncingExternalModel = true;
  nodeConfig.value = cloneWorkflowTree(value);
  void nextTick(() => {
    syncingExternalModel = false;
  });
}, { deep: true });
watch(nodeConfig, value => {
  store.setFlowNodeConfig(value);

  // 复制设计器挂载时会补充校验状态等内部字段；外部模型同步期间不得把这些
  // 内部变更反向上抛，否则深度监听会在父子组件之间形成无穷回写闭环。
  if (syncingExternalModel
    || serializeComparableWorkflowTree(value) === serializeComparableWorkflowTree(props.modelValue)) return;
  emit('update:modelValue', cloneWorkflowTree(value));
}, { deep: true });

/** Workflow-Vue3 编辑树是纯 JSON 契约，使用 JSON 克隆可安全跨越 Vue Proxy 边界。 */
function cloneWorkflowTree(value: WorkflowVue3Node): WorkflowVue3Node {
  return JSON.parse(serializeWorkflowTree(value)) as WorkflowVue3Node;
}

/** 将流程树转换为稳定 JSON，用于克隆以及阻断等价模型的重复回写。 */
function serializeWorkflowTree(value: WorkflowVue3Node): string {
  return JSON.stringify(value);
}

/** 序列化可持久化流程语义，并忽略复制设计器挂载时自动补充的瞬时界面状态。 */
function serializeComparableWorkflowTree(value: WorkflowVue3Node): string {
  return JSON.stringify(value, (key, child) => transientDesignerKeys.has(key) ? undefined : child);
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
.workflow-vue3-adapter { position: relative; inset: auto; min-height: 620px; overflow: auto; background: var(--el-fill-color-light); }
.workflow-vue3-adapter__canvas { min-width: 960px; min-height: 620px; padding: 48px 24px 96px; transform-origin: 50% 0; }
.workflow-vue3-adapter__canvas.is-disabled { pointer-events: none; opacity: 0.72; }
</style>
