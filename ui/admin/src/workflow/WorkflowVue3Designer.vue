<script setup lang="ts">
import { computed, nextTick, provide, ref, watch } from 'vue';
import type { WorkflowDefinitionDraft, WorkflowFormField } from '@fullnet/client-contracts';
import type { WorkflowRecipientCandidateResponse } from '@fullnet/client-contracts';
import { ElButton, ElDrawer, ElInput, ElOption, ElSelect } from 'element-plus';
import { listWorkflowRecipientCandidates } from '../api/workflow-definitions';
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
  gatewayFields?: readonly WorkflowFormField[];
}>(), {
  enabledNodeTypes: () => ['start', 'human.approval', 'end'],
  gatewayFields: () => []
});
const emit = defineEmits<{
  'update:modelValue': [value: WorkflowVue3Node];
  'update:draft': [draft: WorkflowDefinitionDraft];
  'validation-error': [code: string];
}>();
const store = useStore();
const ccCandidates = ref<WorkflowRecipientCandidateResponse[]>([]);
const ccRecipientUserIds = ref<string[]>([]);
const ccCandidatesLoading = ref(false);
const gatewayCondition = ref<WorkflowVue3Node>();
const gatewayFieldKey = ref('');
const gatewayOperator = ref('equals');
const gatewayValue = ref<string | number | boolean>('');
const selectedGatewayField = computed(() =>
  props.gatewayFields.find(field => field.fieldKey === gatewayFieldKey.value));
const gatewayOperators = computed(() => {
  const common = [
    { value: 'equals', label: '等于' },
    { value: 'notEquals', label: '不等于' }
  ];
  const ordered = ['integer', 'money', 'decimal', 'date', 'time', 'datetime']
    .includes(selectedGatewayField.value?.fieldTypeKey ?? '')
    ? [
        { value: 'greaterThan', label: '大于' },
        { value: 'greaterThanOrEqual', label: '大于或等于' },
        { value: 'lessThan', label: '小于' },
        { value: 'lessThanOrEqual', label: '小于或等于' }
      ]
    : [];
  return [...common, ...ordered,
    { value: 'isEmpty', label: '为空' },
    { value: 'isNotEmpty', label: '不为空' }];
});
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

watch(() => store.copyerDrawer, visible => {
  if (!visible) return;
  const envelope = store.copyerConfig1 as {
    value?: WorkflowVue3Node;
    id?: number | string;
  };
  ccRecipientUserIds.value = Array.isArray(envelope.value?.recipientUserIds)
    ? envelope.value.recipientUserIds.filter((value): value is string => typeof value === 'string')
    : [];
  void loadCcCandidates();
});

watch(() => store.conditionDrawer, visible => {
  if (!visible) return;
  const envelope = store.conditionsConfig1 as { value?: WorkflowVue3Node };
  gatewayCondition.value = envelope.value;
  gatewayFieldKey.value = typeof envelope.value?.fieldKey === 'string'
    ? envelope.value.fieldKey
    : '';
  gatewayOperator.value = typeof envelope.value?.operator === 'string'
    ? envelope.value.operator
    : 'equals';
  const value = envelope.value?.value;
  gatewayValue.value = typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean'
    ? value
    : '';
});

/** 加载抄送候选人最小投影；服务端负责活动状态和权限边界。 */
async function loadCcCandidates(): Promise<void> {
  if (ccCandidatesLoading.value || ccCandidates.value.length > 0) return;
  ccCandidatesLoading.value = true;
  try {
    const result = await listWorkflowRecipientCandidates(1, 100);
    ccCandidates.value = result.items;
  } finally {
    ccCandidatesLoading.value = false;
  }
}

/** 保存闭合用户标识，同时维护 Workflow-Vue3 仅用于展示的用户列表。 */
function saveCcRecipients(): void {
  const ids = [...new Set(ccRecipientUserIds.value)];
  if (ids.length < 1 || ids.length > 20) {
    emit('validation-error', 'client.invalid_workflow_cc_recipients');
    return;
  }
  const envelope = store.copyerConfig1 as {
    value?: WorkflowVue3Node;
    id?: number | string;
  };
  const labels = new Map(ccCandidates.value.map(candidate => [candidate.id, candidate]));
  store.setCopyerConfig({
    ...envelope,
    flag: true,
    value: {
      ...envelope.value,
      recipientUserIds: ids,
      nodeUserList: ids.map(userId => ({
        id: userId,
        name: labels.get(userId)?.displayName ?? userId,
        type: 'user'
      }))
    }
  });
  store.setCopyer(false);
}

/** 关闭抄送配置抽屉并丢弃本次未保存选择。 */
function closeCcRecipients(): void {
  store.setCopyer(false);
}

/** 保存排他网关的单字段闭合条件，值类型严格跟随已发布表单字段。 */
function saveGatewayCondition(): void {
  const condition = gatewayCondition.value;
  const field = selectedGatewayField.value;
  if (condition?.isDefault === true) {
    store.setCondition(false);
    return;
  }
  if (condition === undefined || field === undefined || gatewayOperator.value === '') {
    emit('validation-error', 'client.invalid_workflow_gateway');
    return;
  }
  const isEmptyOperator = ['isEmpty', 'isNotEmpty'].includes(gatewayOperator.value);
  let value: string | number | boolean = gatewayValue.value;
  if (field.fieldTypeKey === 'integer' && !isEmptyOperator) {
    value = Number(gatewayValue.value);
    if (!Number.isSafeInteger(value)) {
      emit('validation-error', 'client.invalid_workflow_gateway');
      return;
    }
  } else if (field.fieldTypeKey === 'switch' && !isEmptyOperator) {
    value = gatewayValue.value === true || gatewayValue.value === 'true';
  } else if (!isEmptyOperator) {
    value = String(gatewayValue.value);
    if (value.length === 0) {
      emit('validation-error', 'client.invalid_workflow_gateway');
      return;
    }
  }
  const envelope = store.conditionsConfig1 as { value?: WorkflowVue3Node; id?: number | string };
  store.setConditionsConfig({
    ...envelope,
    flag: true,
    value: {
      ...condition,
      branchKey: typeof condition.branchKey === 'string' ? condition.branchKey : condition.id,
      fieldKey: field.fieldKey,
      operator: gatewayOperator.value,
      ...(isEmptyOperator ? { value: undefined } : { value })
    }
  });
  store.setCondition(false);
}

/** 关闭条件配置抽屉并丢弃未保存修改。 */
function closeGatewayCondition(): void {
  store.setCondition(false);
}

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
  <el-drawer
    :model-value="store.copyerDrawer"
    title="选择抄送人"
    size="min(520px, 94vw)"
    @close="closeCcRecipients"
  >
    <el-select
      v-model="ccRecipientUserIds"
      data-testid="workflow-cc-recipient-select"
      multiple
      filterable
      :multiple-limit="20"
      :loading="ccCandidatesLoading"
      placeholder="请选择 1–20 名抄送人"
      style="width: 100%"
    >
      <el-option
        v-for="candidate in ccCandidates"
        :key="candidate.id"
        :label="`${candidate.displayName} (${candidate.username})`"
        :value="candidate.id"
      />
    </el-select>
    <template #footer>
      <el-button @click="closeCcRecipients">取消</el-button>
      <el-button type="primary" :disabled="ccRecipientUserIds.length === 0" @click="saveCcRecipients">
        确定
      </el-button>
    </template>
  </el-drawer>
  <el-drawer
    :model-value="store.conditionDrawer"
    title="配置分支条件"
    size="min(520px, 94vw)"
    @close="closeGatewayCondition"
  >
    <p v-if="gatewayCondition?.isDefault === true">默认分支会在其他条件均不成立时执行，无需配置条件。</p>
    <div v-else class="workflow-gateway-form">
      <label>
        <span>表单字段</span>
        <el-select v-model="gatewayFieldKey" data-testid="workflow-gateway-field" placeholder="请选择已发布表单字段">
          <el-option v-for="field in gatewayFields" :key="field.fieldKey" :label="field.fieldKey" :value="field.fieldKey" />
        </el-select>
      </label>
      <label>
        <span>比较方式</span>
        <el-select v-model="gatewayOperator" data-testid="workflow-gateway-operator">
          <el-option v-for="item in gatewayOperators" :key="item.value" :label="item.label" :value="item.value" />
        </el-select>
      </label>
      <label v-if="!['isEmpty', 'isNotEmpty'].includes(gatewayOperator)">
        <span>比较值</span>
        <el-select v-if="selectedGatewayField?.fieldTypeKey === 'switch'" v-model="gatewayValue" data-testid="workflow-gateway-value">
          <el-option label="是" :value="true" />
          <el-option label="否" :value="false" />
        </el-select>
        <el-input
          v-else
          :model-value="typeof gatewayValue === 'boolean' ? String(gatewayValue) : gatewayValue"
          data-testid="workflow-gateway-value"
          @update:model-value="gatewayValue = $event"
        />
      </label>
    </div>
    <template #footer>
      <el-button @click="closeGatewayCondition">取消</el-button>
      <el-button type="primary" @click="saveGatewayCondition">确定</el-button>
    </template>
  </el-drawer>
</template>

<style scoped>
.workflow-vue3-adapter { position: relative; inset: auto; min-height: 620px; overflow: auto; background: var(--el-fill-color-light); }
.workflow-vue3-adapter__canvas { min-width: 960px; min-height: 620px; padding: 48px 24px 96px; transform-origin: 50% 0; }
.workflow-vue3-adapter__canvas.is-disabled { pointer-events: none; opacity: 0.72; }
.workflow-gateway-form { display: grid; gap: 18px; }
.workflow-gateway-form label { display: grid; gap: 8px; }
</style>
