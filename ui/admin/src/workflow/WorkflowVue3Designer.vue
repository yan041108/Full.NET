<script setup lang="ts">
import { computed, nextTick, provide, ref, watch } from 'vue';
import type { WorkflowDefinitionDraft, WorkflowFormField } from '@fullnet/client-contracts';
import type { WorkflowRecipientCandidateResponse } from '@fullnet/client-contracts';
import { ElButton, ElDrawer, ElInput, ElInputNumber, ElOption, ElSelect, ElSwitch } from 'element-plus';
import { listWorkflowOrganizationUnitCandidates, listWorkflowPositionCandidates, listWorkflowRecipientCandidates, listWorkflowRoleCandidates, previewWorkflowAssignees } from '../api/workflow-definitions';
import { useAdminI18n } from '../i18n/adminI18n';
import { readWorkflowOperationPermissions, workflowHumanActions, workflowTaskActions, workflowVoteActions, workflowClaimActions } from './workflow-human-actions';
import { readVotePolicy } from './workflow-vote-policy';
import { workflowAssigneeReferenceFields, workflowPreviousActorNodes } from './workflow-assignee-references';
import WorkflowVotePolicyEditor from './WorkflowVotePolicyEditor.vue';
import WorkflowAssigneeHandlingEditor from './WorkflowAssigneeHandlingEditor.vue';
import WorkflowFlowAdministratorEditor from './WorkflowFlowAdministratorEditor.vue';
import WorkflowCcRecipientPolicyEditor from './WorkflowCcRecipientPolicyEditor.vue';
import { readAssigneeHandling, type WorkflowAssigneeHandling } from './workflow-vue3-adapter';
import WorkflowConditionEditor from './WorkflowConditionEditor.vue';
import WorkflowDelayPolicyEditor from './WorkflowDelayPolicyEditor.vue';
import WorkflowFieldMutationEditor from './WorkflowFieldMutationEditor.vue';
import WorkflowConnectorEditor from './WorkflowConnectorEditor.vue';
import WorkflowBusinessCommandEditor from './WorkflowBusinessCommandEditor.vue';
import WorkflowSubprocessEditor from './WorkflowSubprocessEditor.vue';
import { readWorkflowSubprocess, type WorkflowSubprocessConfiguration } from './workflow-subprocess';
import { readWorkflowBusinessCommand, type WorkflowBusinessCommandConfiguration } from './workflow-business-command';
import { readWorkflowConnector, type WorkflowConnectorConfiguration } from './workflow-connector';
import { readWorkflowFieldMutation, type WorkflowFieldMutationConfiguration } from './workflow-field-mutation';
import { readWorkflowSlaPolicy } from './workflow-sla-policy';
import { readWorkflowResultCallback } from './workflow-result-callback';
import WorkflowActivityListenerEditor from './WorkflowActivityListenerEditor.vue';
import { readWorkflowActivityListeners, type WorkflowActivityListener } from './workflow-activity-listeners';
import { readWorkflowDelayPolicy, type WorkflowDelayPolicy } from './workflow-delay-policy';
import { readWorkflowCondition, type WorkflowConditionConfig } from './workflow-conditions';
import NodeWrap from './vendor/workflow-vue3/src/components/nodeWrap.vue';
import { useStore } from './vendor/workflow-vue3/src/stores/index.js';
import type { WorkflowVue3Node } from './workflow-vue3-adapter';
import { readWorkflowAwaitDependencies, fromWorkflowVue3Tree, readApprovalPolicy, readAssigneePolicy, readGroupApprovalConfiguration, readTaskPolicy, readClaimPolicy, readWithdrawalPolicy, readStarterSelectionPolicy, readFlowAdministratorUserIds, readCcRecipientPolicy } from './workflow-vue3-adapter';
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
const { t } = useAdminI18n();
const withdrawalDrawer = ref(false);
const administratorDrawer = ref(false);
const administratorUserIds = ref<string[]>([]);
const withdrawalMode = ref<'disabled' | 'before_any_human_action' | 'at_active_nodes'>('disabled');
const withdrawalNodeKeys = ref<string[]>([]);
const withdrawalNodes = computed(() => {
  try { return fromWorkflowVue3Tree(nodeConfig.value).nodes.filter(node => ['human.approval', 'human.group_approval', 'human.task', 'human.vote', 'human.claim'].includes(node.nodeTypeKey))
    .map(node => ({ key: node.nodeKey, label: isRecord(node.config) && typeof node.config.nodeName === 'string' ? node.config.nodeName : node.nodeKey })); }
  catch { return []; }
});
const ccCandidates = ref<WorkflowRecipientCandidateResponse[]>([]);
const ccRecipientUserIds = ref<string[]>([]);
const ccCandidatesLoading = ref(false);
const ccRecipientMode = ref<'users' | 'policy'>('users');
const ccRecipientPolicy = ref<{ sources: Record<string, unknown>[] }>({ sources: [{ resolverKindKey: 'initiator' }] });
const ccPreviousNodes = computed(() => {
  const envelope = store.copyerConfig1 as { value?: WorkflowVue3Node }; const target = envelope.value?.id;
  if (!target) return [];
  // 未保存节点可能没有收件人，仅为计算拓扑投影临时策略，不修改待保存的选人配置。
  const root = cloneWorkflowTree(nodeConfig.value);
  function visit(node: WorkflowVue3Node): void {
    if (node.id === target) { const value = node as Record<string, unknown>; delete value.recipientUserIds; value.recipientPolicy = { sources: [{ resolverKindKey: 'initiator' }] }; }
    if (node.childNode) visit(node.childNode); for (const branch of node.conditionNodes ?? []) visit(branch);
  }
  visit(root); try { return workflowPreviousActorNodes(fromWorkflowVue3Tree(root), target); } catch { return []; }
});
const slaEnabled = ref(false);
const resultCallbackField = ref('');
const activityListeners = ref<WorkflowActivityListener[]>([]);
const resultCallbackCandidates = computed(() => {
  const envelope = store.approverConfig1 as { value?: WorkflowVue3Node };
  const grants = isRecord(envelope.value?.fieldPolicies) ? envelope.value.fieldPolicies : {};
  return props.gatewayFields.filter(field => field.fieldTypeKey === 'switch').map(field => ({ ...field,
    writable: grants[field.fieldKey] === undefined || grants[field.fieldKey] === 'editable' || grants[field.fieldKey] === 'required' }));
});
const slaDueMinutes = ref(1440);
const slaPauseMode = ref('elapsed');
const slaAction = ref('suspend');
const timeoutEnabled = ref(false);
const timeoutDueMinutes = ref(1440);
const timeoutReminderIntervalMinutes = ref(60);
const timeoutMaxReminderCount = ref(3);
const timeoutEscalationEnabled = ref(false);
const timeoutEscalationMinutes = ref(2880);
const timeoutEscalationRecipientUserId = ref('');
const approvalSignRequired = ref(false);
const approvalCommentRequired = ref(false);
const approvalAllowedActions = ref<string[]>([...workflowHumanActions]);
const approvalModeKey = ref<'single' | 'all' | 'any' | 'nOfM' | 'percent' | 'sequential'>('single');
const approvalApproverUserIds = ref<string[]>([]);
const editingGroups = ref(false);
const editingTask = ref(false);
const editingVote = ref(false);
const editingClaim = ref(false);
const claimAllowRelease = ref(true);
const votePolicy = ref<Record<string, unknown>>({ ballotFormVersionId: '', participationPercentage: 100, approvalPercentage: 100, allowAbstain: true });
const taskCompletionMode = ref<'all' | 'any' | 'sequential'>('all');
const availableHumanActions = computed(() => editingClaim.value ? workflowClaimActions : editingTask.value ? workflowTaskActions : editingVote.value ? workflowVoteActions : workflowHumanActions);
const approvalGroups = ref<Record<string, unknown>[]>([]);
const approvalGroupIndex = ref(0);
const groupGateMode = ref<'all' | 'any' | 'nOfM' | 'percent'>('all');
const groupGateRequired = ref(2);
const groupGatePercentage = ref(100);

const approvalRequiredApprovals = ref(2);
const approvalRequiredPercentage = ref(100);
const assigneeSourceKind = ref<'default' | 'initiator' | 'specified_users' | 'role_members' | 'flow_administrators' | 'organization_unit_leader' | 'initiator_primary_unit_leader' | 'initiator_direct_manager' | 'initiator_ancestor_unit_leader' | 'position_members' | 'organization_unit_members' | 'initiator_leader_chain' | 'form_users' | 'form_units' | 'previous_node_actors' | 'starter_selected_users'>('default');
const starterSelectionKey = ref('');
const starterSelectionMode = ref<'single' | 'multiple'>('single');
const starterSelectionMaximum = ref(20);
const starterSelectionRange = ref<'all' | 'specified_users' | 'role_members'>('all');
const assigneeFieldKey = ref('');
const assigneePreviousNodeKey = ref('');
const assigneeReferenceFields = computed(() => workflowAssigneeReferenceFields(props.gatewayFields));
const assigneePreviousNodes = computed(() => {
  const envelope = store.approverConfig1 as { value?: WorkflowVue3Node };
  try { return workflowPreviousActorNodes(fromWorkflowVue3Tree(nodeConfig.value), envelope.value?.id ?? ''); } catch { return []; }
});
const dynamicAssignees = computed(() => ['form_users', 'form_units', 'previous_node_actors', 'starter_selected_users', 'flow_administrators'].includes(assigneeSourceKind.value) ||
  assigneeSources.value.some(source => ['form_users', 'form_units', 'previous_node_actors', 'starter_selected_users', 'flow_administrators'].includes(String(source.resolverKindKey))));
const assigneeSources = ref<Record<string, unknown>[]>([]);
const assigneeHandling = ref<WorkflowAssigneeHandling>();
const assigneeSourceIndex = ref(0);
const assigneeUserIds = ref<string[]>([]);
const assigneeRoleIds = ref<string[]>([]);
const assigneePositionIds = ref<string[]>([]);
const assigneeMaximumLevels = ref(3);
const assigneeUnitId = ref('');
const assigneeAncestorLevel = ref(1);
const assigneePreviewUsers = ref<readonly WorkflowRecipientCandidateResponse[]>([]);
const assigneePreviewLoading = ref(false);
const assigneePreviewError = ref('');
const roleCandidates = ref<Array<{ id: string; code: string; name: string }>>([]);
const organizationUnitCandidates = ref<Array<{ id: string; code: string; name: string }>>([]);
const roleCandidatesLoading = ref(false);
const organizationUnitCandidatesLoading = ref(false);
const positionCandidates = ref<Array<{ id: string; code: string; name: string }>>([]);
const positionCandidatesLoading = ref(false);
const positionCandidatesError = ref(false);
const gatewayCondition = ref<WorkflowVue3Node>();
const gatewayAdvanced = ref(false);
const gatewayExpression = ref<WorkflowConditionConfig>({ fieldKey: '', operator: 'isNotEmpty' });
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
provide('fullnetWorkflowApprovalPresentation', approvalPresentation);
provide('fullnetWorkflowCcPresentation', ccPresentation);
provide('fullnetWorkflowDelayPresentation', delayPresentation);
provide('fullnetWorkflowFieldPresentation', fieldPresentation);
provide('fullnetWorkflowConnectorPresentation', connectorPresentation);
provide('fullnetWorkflowSubprocessPresentation', subprocessPresentation);
const nodeConfig = ref<WorkflowVue3Node>(cloneWorkflowTree(props.modelValue));
const dependencyNodeKey = ref('');
const dependencyKeys = ref<string[]>([]);
const dependencyError = ref(false);
const dependencyNodes = computed(() => {
  const result: WorkflowVue3Node[] = [];
  const visit = (node: WorkflowVue3Node): void => {
    if (node.type === 11 && node.id !== dependencyNodeKey.value) result.push(node);
    if (node.childNode) visit(node.childNode);
    for (const branch of node.conditionNodes ?? []) if (branch.childNode) visit(branch.childNode);
  };
  visit(nodeConfig.value); return result;
});
provide('fullnetWorkflowDependencyLabel', computed(() => t('workflowDesigner.dependencies.title')));
provide('fullnetWorkflowEditDependencies', (node: WorkflowVue3Node): void => {
  if (props.disabled || !node.id || !props.enabledNodeTypes.includes('integration.async_trigger')) return;
  dependencyNodeKey.value = node.id;
  try { dependencyKeys.value = readWorkflowAwaitDependencies(node.awaitActivityNodeKeys) ?? []; dependencyError.value = false; }
  catch { dependencyKeys.value = []; dependencyError.value = true; }
});
function saveDependencies(): void {
  if (props.disabled || !dependencyNodeKey.value) return;
  const selected = readWorkflowAwaitDependencies(dependencyKeys.value);
  if (selected?.some(key => !dependencyNodes.value.some(node => node.id === key))) { dependencyError.value = true; return; }
  // 保存时重新定位当前树，只变更依赖元数据，避免覆盖抽屉期间的其他配置。
  const visit = (node: WorkflowVue3Node): boolean => {
    if (node.id === dependencyNodeKey.value) {
      const target = node as Record<string, unknown>;
      if (selected) target.awaitActivityNodeKeys = selected; else delete target.awaitActivityNodeKeys;
      return true;
    }
    if (node.childNode && visit(node.childNode)) return true;
    return (node.conditionNodes ?? []).some(branch => branch.childNode ? visit(branch.childNode) : false);
  };
  if (!visit(nodeConfig.value)) { dependencyError.value = true; return; }
  dependencyNodeKey.value = '';
}
watch(() => props.disabled, disabled => { if (disabled) dependencyNodeKey.value = ''; });

let syncingExternalModel = true;
let lastEmittedModel: WorkflowVue3Node | undefined;
let lastSemanticJson = serializeComparableWorkflowTree(nodeConfig.value);
store.setFlowNodeConfig(nodeConfig.value);
void nextTick(() => {
  syncingExternalModel = false;
});

watch(() => props.modelValue, value => {
  // v-model 原样回送刚发出的对象，不再克隆整树并重建所有递归节点的依赖。
  if (value === lastEmittedModel) return;
  dependencyNodeKey.value = '';
  syncingExternalModel = true;
  nodeConfig.value = cloneWorkflowTree(value);
  lastSemanticJson = serializeComparableWorkflowTree(nodeConfig.value);
  store.setFlowNodeConfig(nodeConfig.value);
  void nextTick(() => {
    syncingExternalModel = false;
  });
});
watch(nodeConfig, value => {
  store.setFlowNodeConfig(value);

  // 复制设计器挂载时会补充校验状态等内部字段；外部模型同步期间不得把这些
  // 内部变更反向上抛，否则深度监听会在父子组件之间形成无穷回写闭环。
  if (syncingExternalModel) return;
  const semanticJson = serializeComparableWorkflowTree(value);
  if (semanticJson === lastSemanticJson) return;
  lastSemanticJson = semanticJson;
  lastEmittedModel = cloneWorkflowTree(value);
  emit('update:modelValue', lastEmittedModel);
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
  ccRecipientMode.value = envelope.value?.recipientPolicy === undefined ? 'users' : 'policy';
  try { ccRecipientPolicy.value = envelope.value?.recipientPolicy === undefined ? { sources: [{ resolverKindKey: 'initiator' }] } : readCcRecipientPolicy(envelope.value.recipientPolicy); }
  catch { emit('validation-error', 'client.invalid_workflow_cc_recipients'); store.setCopyer(false); return; }
  void loadCcCandidates();
});

watch(() => store.approverDrawer, visible => {
  if (!visible) return;
  const envelope = store.approverConfig1 as { value?: WorkflowVue3Node };
  editingTask.value = Number(envelope.value?.type) === 18;
  editingVote.value = Number(envelope.value?.type) === 19;
  editingClaim.value = Number(envelope.value?.type) === 17;
  claimAllowRelease.value = editingClaim.value ? readClaimPolicy(envelope.value?.claimPolicy).allowRelease : true;
  if (editingVote.value) votePolicy.value = isRecord(envelope.value?.votePolicy) ? JSON.parse(JSON.stringify(envelope.value.votePolicy)) as Record<string, unknown> :
    { ballotFormVersionId: '', participationPercentage: 100, approvalPercentage: 100, allowAbstain: true };
  taskCompletionMode.value = editingTask.value ? readTaskPolicy(envelope.value?.taskPolicy).completionModeKey : 'all';
  const sla = isRecord(envelope.value?.slaPolicy) ? envelope.value.slaPolicy : undefined;
  resultCallbackField.value = typeof envelope.value?.resultCallbackFieldKey === 'string' ? envelope.value.resultCallbackFieldKey : '';
  activityListeners.value = readWorkflowActivityListeners(envelope.value?.listeners ?? []);
  slaEnabled.value = sla !== undefined; slaDueMinutes.value = readInteger(sla?.dueAfterMinutes, 1440);
  slaPauseMode.value = typeof sla?.pauseModeKey === 'string' ? sla.pauseModeKey : 'elapsed';
  slaAction.value = typeof sla?.actionKey === 'string' ? sla.actionKey : 'suspend';
  const configuredPolicy = envelope.value?.timeoutPolicy;
  const policy: Record<string, unknown> | undefined = isRecord(configuredPolicy)
    ? configuredPolicy : undefined;
  timeoutEnabled.value = policy !== undefined;
  timeoutDueMinutes.value = readInteger(policy?.dueAfterMinutes, 1440);
  timeoutReminderIntervalMinutes.value = readInteger(policy?.reminderIntervalMinutes, 60);
  timeoutMaxReminderCount.value = readInteger(policy?.maxReminderCount, 3);
  timeoutEscalationEnabled.value = policy?.escalationAfterMinutes !== undefined;
  timeoutEscalationMinutes.value = readInteger(policy?.escalationAfterMinutes, 2880);
  timeoutEscalationRecipientUserId.value = typeof policy?.escalationRecipientUserId === 'string'
    ? policy.escalationRecipientUserId
    : '';
  approvalSignRequired.value = envelope.value?.signRequired === true;
  approvalCommentRequired.value = envelope.value?.commentRequired === true;
  const operationPermissions = envelope.value?.operationPermissions === undefined ? undefined
    : readWorkflowOperationPermissions(envelope.value.operationPermissions, editingClaim.value ? 'human.claim' : editingTask.value ? 'human.task' : editingVote.value ? 'human.vote' : 'human.approval');
  approvalAllowedActions.value = availableHumanActions.value.filter(key => operationPermissions === undefined || operationPermissions[key] === true);
  editingGroups.value = Number(envelope.value?.type) === 21;
  approvalGroups.value = editingGroups.value && Array.isArray(envelope.value?.groups)
    ? JSON.parse(JSON.stringify(envelope.value.groups)) as Record<string, unknown>[] : [];
  approvalGroupIndex.value = 0;
  const gate = isRecord(envelope.value?.groupApprovalPolicy) ? envelope.value.groupApprovalPolicy : undefined;
  groupGateMode.value = ['all', 'any', 'nOfM', 'percent'].includes(String(gate?.modeKey)) ? gate!.modeKey as typeof groupGateMode.value : 'all';
  groupGateRequired.value = readInteger(gate?.requiredApprovals, 2);
  groupGatePercentage.value = readInteger(gate?.requiredPercentage, 100);
  loadMemberConfiguration(editingGroups.value ? approvalGroups.value[0] : envelope.value);
  void loadCcCandidates();
  void loadRoleCandidates();
  void loadOrganizationUnitCandidates();
  void loadPositionCandidates();
});

/** 切换组时仅替换组内选人编辑状态，节点动作及超时配置仍属于整个活动。 */
function loadMemberConfiguration(config: Record<string, unknown> | undefined): void {
  const configuredApproval = isRecord(config?.approvalPolicy)
    ? config?.approvalPolicy as Record<string, unknown>
    : undefined;
  const configuredMode = configuredApproval?.modeKey;
  approvalModeKey.value = configuredMode === 'all' || configuredMode === 'any' || configuredMode === 'nOfM' || configuredMode === 'percent' || configuredMode === 'sequential'
    ? configuredMode
    : 'single';
  approvalApproverUserIds.value = Array.isArray(configuredApproval?.approverUserIds)
    ? configuredApproval.approverUserIds.filter(
      (value): value is string => typeof value === 'string')
    : [];
  approvalRequiredApprovals.value = readInteger(configuredApproval?.requiredApprovals, 2);
  approvalRequiredPercentage.value = readInteger(configuredApproval?.requiredPercentage, 100);
  const configuredAssignee = isRecord(config?.assigneePolicy)
    ? config?.assigneePolicy as Record<string, unknown>
    : undefined;
  assigneeSources.value = Array.isArray(configuredAssignee?.sources)
    ? JSON.parse(JSON.stringify(configuredAssignee.sources)) as Record<string, unknown>[] : [];
  assigneeHandling.value = configuredAssignee?.handling === undefined ? undefined : readAssigneeHandling(configuredAssignee.handling);
  assigneeSourceIndex.value = 0;
  loadAssigneeSource();
  assigneePreviewUsers.value = [];
  assigneePreviewError.value = '';
}

/** 切换前保留当前组的完整闭合配置，避免跨组编辑丢失。 */
function selectApprovalGroup(index: number): void {
  if (index === approvalGroupIndex.value || !saveCurrentGroup()) return;
  approvalGroupIndex.value = index;
  loadMemberConfiguration(approvalGroups.value[index]);
}

function saveCurrentGroup(): boolean {
  try { if (assigneeHandling.value !== undefined) readAssigneeHandling(assigneeHandling.value); }
  catch { emit('validation-error', 'client.invalid_workflow_assignee_policy'); return false; }
  const member: Record<string, unknown> = {};
  if (!writeMemberConfiguration(member)) return false;
  const current = approvalGroups.value[approvalGroupIndex.value];
  if (!current) return false;
  approvalGroups.value[approvalGroupIndex.value] = { groupKey: current.groupKey, groupName: current.groupName,
    ...(member.assigneePolicy === undefined
      ? member.approvalPolicy === undefined ? { assigneePolicy: { sources: [{ resolverKindKey: 'initiator' }] } } : {}
      : { assigneePolicy: member.assigneePolicy }),
    ...(member.approvalPolicy === undefined ? {} : { approvalPolicy: member.approvalPolicy }) };
  return true;
}

function addApprovalGroup(): void {
  if (approvalGroups.value.length >= 8 || !saveCurrentGroup()) return;
  let number = approvalGroups.value.length + 1;
  while (approvalGroups.value.some(group => group.groupKey === `group_${number}`)) number++;
  approvalGroups.value.push({ groupKey: `group_${number}`, groupName: t('workflowDesigner.groups.defaultName', { number }),
    assigneePolicy: { sources: [{ resolverKindKey: 'initiator' }] } });
  approvalGroupIndex.value = approvalGroups.value.length - 1;
  loadMemberConfiguration(approvalGroups.value.at(-1));
}

function removeApprovalGroup(): void {
  if (approvalGroups.value.length <= 2) return;
  approvalGroups.value.splice(approvalGroupIndex.value, 1);
  approvalGroupIndex.value = Math.min(approvalGroupIndex.value, approvalGroups.value.length - 1);
  loadMemberConfiguration(approvalGroups.value[approvalGroupIndex.value]);
}

watch(() => store.conditionDrawer, visible => {
  if (!visible) return;
  const envelope = store.conditionsConfig1 as { value?: WorkflowVue3Node; priorityLevel?: number };
  const selected = envelope.value?.conditionNodes?.find(item => item.priorityLevel === envelope.priorityLevel) ?? envelope.value;
  gatewayCondition.value = selected;
  gatewayAdvanced.value = selected?.condition !== undefined;
  gatewayExpression.value = (selected?.condition as WorkflowConditionConfig | undefined)
    ?? { fieldKey: typeof selected?.fieldKey === 'string' ? selected.fieldKey : props.gatewayFields[0]?.fieldKey ?? '',
      operator: typeof selected?.operator === 'string' ? selected.operator : 'isNotEmpty',
      ...(selected?.value === undefined ? {} : { value: selected.value }) };
  gatewayFieldKey.value = typeof selected?.fieldKey === 'string'
    ? selected.fieldKey
    : '';
  gatewayOperator.value = typeof selected?.operator === 'string'
    ? selected.operator
    : 'equals';
  const value = selected?.value;
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

/** 加载办理人角色候选。 */
async function loadRoleCandidates(): Promise<void> {
  if (roleCandidatesLoading.value || roleCandidates.value.length > 0) return;
  roleCandidatesLoading.value = true;
  try {
    const result = await listWorkflowRoleCandidates(1, 100);
    roleCandidates.value = result.items;
  } finally {
    roleCandidatesLoading.value = false;
  }
}

/** 加载机构单元候选。 */
async function loadOrganizationUnitCandidates(): Promise<void> {
  if (organizationUnitCandidatesLoading.value || organizationUnitCandidates.value.length > 0) return;
  organizationUnitCandidatesLoading.value = true;
  try {
    const result = await listWorkflowOrganizationUnitCandidates(1, 100);
    organizationUnitCandidates.value = result.items;
  } finally {
    organizationUnitCandidatesLoading.value = false;
  }
}

/** 岗位加载失败可重试，不把失败当作当前租户没有岗位。 */
async function loadPositionCandidates(): Promise<void> {
  if (positionCandidatesLoading.value || positionCandidates.value.length > 0) return;
  positionCandidatesLoading.value = true; positionCandidatesError.value = false;
  try { positionCandidates.value = (await listWorkflowPositionCandidates(1, 100)).items; }
  catch { positionCandidatesError.value = true; }
  finally { positionCandidatesLoading.value = false; }
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
  const value = { ...envelope.value }; delete value.recipientPolicy;
  store.setCopyerConfig({
    ...envelope,
    flag: true,
    value: {
      ...value,
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

function saveCcPolicy(policy: { sources: Record<string, unknown>[] }): void {
  if (props.disabled) return;
  const envelope = store.copyerConfig1 as { value?: WorkflowVue3Node; id?: number | string };
  const value = { ...envelope.value }; delete value.recipientUserIds;
  store.setCopyerConfig({ ...envelope, flag: true, value: { ...value, recipientPolicy: readCcRecipientPolicy(policy), nodeUserList: [] } }); store.setCopyer(false);
}
function ccPresentation(node: WorkflowVue3Node): { text: string; error: boolean; tip: string } | undefined {
  if (node.recipientPolicy === undefined) return undefined;
  try { const policy = readCcRecipientPolicy(node.recipientPolicy); return { text: t('workflowDesigner.cc.summary', { count: policy.sources.length }), error: false, tip: '' }; }
  catch { return { text: t('workflowDesigner.cc.invalid'), error: true, tip: t('workflowDesigner.cc.invalid') }; }
}

function delayPresentation(node: WorkflowVue3Node): { text: string; error: boolean; tip: string } {
  try {
    const policy = readWorkflowDelayPolicy(node.delayPolicy);
    return { text: policy.modeKey === 'relative' || policy.modeKey === 'working' ? `${policy.modeKey === 'working' ? `${t('workflowDesigner.delay.working')} · ` : ''}${policy.duration} ${t(`workflowDesigner.delay.${policy.unitKey}`)}` : policy.at ?? policy.fieldKey,
      error: false, tip: '' };
  } catch { return { text: t('workflowDesigner.delay.invalid'), error: true, tip: t('workflowDesigner.delay.invalid') }; }
}
function saveDelay(policy: WorkflowDelayPolicy): void {
  if (props.disabled) return;
  const envelope = store.delayConfig1 as { value?: WorkflowVue3Node; id?: number | string };
  store.setDelayConfig({ ...envelope, flag: true, value: { ...envelope.value, delayPolicy: readWorkflowDelayPolicy(policy) } });
  store.setDelay(false);
}

function fieldPresentation(node: WorkflowVue3Node): { text: string; error: boolean; tip: string } {
  try {
    const value = readWorkflowFieldMutation(node.type === 16 ? 'data.clear' : 'data.modify', node);
    return { text: (value.mutationPolicy.fieldKeys ?? value.mutationPolicy.assignments!.map(row => row.fieldKey)).join('、'), error: false, tip: '' };
  } catch { const text = t('workflowDesigner.fieldActivity.invalid'); return { text, error: true, tip: text }; }
}
function saveField(value: WorkflowFieldMutationConfiguration, clear: boolean): void {
  if (props.disabled) return;
  const envelope = (clear ? store.deleteDataConfig1 : store.modifyDataConfig1) as { value?: WorkflowVue3Node; id?: number | string };
  const next = { ...envelope, flag: true, value: { ...envelope.value, ...readWorkflowFieldMutation(clear ? 'data.clear' : 'data.modify', value) } };
  if (clear) { store.setDeleteDataConfig(next); store.setDeleteData(false); }
  else { store.setModifyDataConfig(next); store.setModifyData(false); }
}

function connectorPresentation(node: WorkflowVue3Node): { text: string; error: boolean; tip: string } {
  if (node.type === 23) {
    try { const policy = readWorkflowBusinessCommand(node).businessCommandPolicy; return { text: `${policy.actionKey}@${policy.actionVersion}`, error: false, tip: '' }; }
    catch { const text = t('workflowDesigner.businessCommand.invalid'); return { text, error: true, tip: text }; }
  }
  try { const value = readWorkflowConnector(node).connectorPolicy; return { text: `${value.connectorKey}@${value.connectorVersion}`, error: false, tip: '' }; }
  catch { const text = t('workflowDesigner.connector.invalid'); return { text, error: true, tip: text }; }
}
function subprocessPresentation(node: WorkflowVue3Node): { text: string; error: boolean; tip: string } {
  try { const config = readWorkflowSubprocess(node); return { text: config.subprocessPolicy.definitionVersionId, error: false, tip: '' }; }
  catch { const text = t('workflowDesigner.subprocess.invalid'); return { text, error: true, tip: text }; }
}
function saveSubprocess(value: WorkflowSubprocessConfiguration): void {
  if (props.disabled) return;
  const envelope = store.subProcessConfig1 as { value?: WorkflowVue3Node; id?: number | string }; const node: Record<string, unknown> = { ...envelope.value };
  delete node.boundSubprocess;
  delete node.multiInstancePolicy;
  store.setSubProcessConfig({ ...envelope, flag: true, value: { ...node, ...readWorkflowSubprocess(value) } }); store.setSubProcess(false);
}
function saveConnector(value: WorkflowConnectorConfiguration): void {
  if (props.disabled) return;
  const envelope = store.triggerConfig1 as { value?: WorkflowVue3Node; id?: number | string };
  const node = { ...envelope.value }; delete node.boundConnectorHash;
  store.setTriggerConfig({ ...envelope, flag: true, value: { ...node, ...readWorkflowConnector(value) } }); store.setTrigger(false);
}
const editingBusinessCommand = computed(() => (store.triggerConfig1 as { value?: WorkflowVue3Node }).value?.type === 23);
function saveBusinessCommand(value: WorkflowBusinessCommandConfiguration): void {
  if (props.disabled) return;
  const envelope = store.triggerConfig1 as { value?: WorkflowVue3Node; id?: number | string };
  const node = { ...envelope.value }; delete node.boundBusinessActionHash;
  store.setTriggerConfig({ ...envelope, flag: true, value: { ...node, ...readWorkflowBusinessCommand(value) } }); store.setTrigger(false);
}

/** 关闭抄送配置抽屉并丢弃本次未保存选择。 */
function closeCcRecipients(): void {
  store.setCopyer(false);
}

/** 切换来源时恢复该条独立配置，不以第一条覆盖其他来源。 */
function loadAssigneeSource(): void {
  const source = assigneeSources.value[assigneeSourceIndex.value];
  assigneeSourceKind.value = (source?.resolverKindKey ?? 'default') as typeof assigneeSourceKind.value;
  assigneeUserIds.value = Array.isArray(source?.userIds) ? [...source.userIds] as string[] : [];
  assigneeRoleIds.value = Array.isArray(source?.roleIds) ? [...source.roleIds] as string[] : [];
  assigneePositionIds.value = Array.isArray(source?.positionIds) ? [...source.positionIds] as string[] : [];
  assigneeMaximumLevels.value = readInteger(source?.maximumLevels, 3);
  assigneeUnitId.value = typeof source?.unitId === 'string' ? source.unitId : '';
  assigneeFieldKey.value = typeof source?.fieldKey === 'string' ? source.fieldKey : '';
  assigneePreviousNodeKey.value = typeof source?.nodeKey === 'string' ? source.nodeKey : '';
  if (isRecord(source?.selectionPolicy)) {
    const policy = readStarterSelectionPolicy(source.selectionPolicy);
    starterSelectionKey.value = policy.selectionKey; starterSelectionMode.value = policy.modeKey;
    starterSelectionMaximum.value = policy.maximumUsers; starterSelectionRange.value = policy.range.kindKey;
    assigneeUserIds.value = [...policy.range.userIds ?? []]; assigneeRoleIds.value = [...policy.range.roleIds ?? []];
  } else {
    starterSelectionKey.value = `selection-${crypto.randomUUID()}`; starterSelectionMode.value = 'single'; starterSelectionRange.value = 'all'; starterSelectionMaximum.value = 20;
  }
  assigneeAncestorLevel.value = readInteger(source?.ancestorLevel, 1);
  assigneePreviewUsers.value = [];
}

/** 构造正在编辑的单条来源；默认成员由服务端结合审批模式裁定。 */
function buildAssigneeSource(): Record<string, unknown> | undefined {
  if (assigneeSourceKind.value === 'default') return undefined;
  if (assigneeSourceKind.value === 'starter_selected_users') {
    try { return { resolverKindKey: 'starter_selected_users', selectionPolicy: readStarterSelectionPolicy({
      selectionKey: starterSelectionKey.value, modeKey: starterSelectionMode.value,
      maximumUsers: starterSelectionMode.value === 'single' ? 1 : starterSelectionMaximum.value,
      range: { kindKey: starterSelectionRange.value,
        ...(starterSelectionRange.value === 'specified_users' ? { userIds: assigneeUserIds.value } : {}),
        ...(starterSelectionRange.value === 'role_members' ? { roleIds: assigneeRoleIds.value } : {}) }
    }) }; } catch { return undefined; }
  }
  if (assigneeSourceKind.value === 'form_users' || assigneeSourceKind.value === 'form_units')
    return assigneeReferenceFields.value.some(field => field.fieldKey === assigneeFieldKey.value)
      ? { resolverKindKey: assigneeSourceKind.value, fieldKey: assigneeFieldKey.value } : undefined;
  if (assigneeSourceKind.value === 'previous_node_actors')
    return assigneePreviousNodes.value.some(node => node.key === assigneePreviousNodeKey.value)
      ? { resolverKindKey: assigneeSourceKind.value, nodeKey: assigneePreviousNodeKey.value } : undefined;
  if (assigneeSourceKind.value === 'initiator') {
    return { resolverKindKey: 'initiator' };
  }
  if (assigneeSourceKind.value === 'specified_users') {
    const userIds = [...new Set(assigneeUserIds.value)];
    if (userIds.length < 1 || userIds.length > 20) {
      return undefined;
    }
    return { resolverKindKey: 'specified_users', userIds };
  }
  if (assigneeSourceKind.value === 'role_members') {
    const roleIds = [...new Set(assigneeRoleIds.value)];
    if (roleIds.length < 1 || roleIds.length > 5) {
      return undefined;
    }
    return { resolverKindKey: 'role_members', roleIds };
  }
  if (assigneeSourceKind.value === 'organization_unit_leader' || assigneeSourceKind.value === 'organization_unit_members') {
    if (!assigneeUnitId.value) {
      return undefined;
    }
    return { resolverKindKey: assigneeSourceKind.value, unitId: assigneeUnitId.value };
  }
  if (assigneeSourceKind.value === 'position_members') {
    const positionIds = [...new Set(assigneePositionIds.value)];
    return positionIds.length >= 1 && positionIds.length <= 5 ? { resolverKindKey: 'position_members', positionIds } : undefined;
  }
  if (assigneeSourceKind.value === 'initiator_leader_chain') {
    const maximumLevels = assigneeMaximumLevels.value;
    return Number.isInteger(maximumLevels) && maximumLevels >= 1 && maximumLevels <= 20
      ? { resolverKindKey: 'initiator_leader_chain', maximumLevels } : undefined;
  }
  if (assigneeSourceKind.value === 'initiator_primary_unit_leader') {
    return { resolverKindKey: 'initiator_primary_unit_leader' };
  }
  if (assigneeSourceKind.value === 'initiator_direct_manager') return { resolverKindKey: 'initiator_direct_manager' };
  if (assigneeSourceKind.value === 'flow_administrators') return { resolverKindKey: 'flow_administrators' };
  if (assigneeSourceKind.value !== 'initiator_ancestor_unit_leader') return undefined;
  const ancestorLevel = assigneeAncestorLevel.value;
  if (!Number.isInteger(ancestorLevel) || ancestorLevel < 1 || ancestorLevel > 20) {
    return undefined;
  }
  return { resolverKindKey: 'initiator_ancestor_unit_leader', ancestorLevel };
}

/** 合并每条来源的配置，缺省仅在整个策略省略时成立。 */
function buildAssigneePolicy(): { sources: Record<string, unknown>[]; handling?: WorkflowAssigneeHandling } | undefined {
  const source = buildAssigneeSource();
  if (source === undefined) {
    if (assigneeSourceKind.value !== 'default' || assigneeHandling.value === undefined) return undefined;
    const normal = !editingTask.value && !editingVote.value && !editingClaim.value && approvalModeKey.value !== 'single'
      ? { resolverKindKey: 'specified_users', userIds: [...approvalApproverUserIds.value] } : { resolverKindKey: 'initiator' };
    return { sources: [normal], handling: assigneeHandling.value };
  }
  const sources = [...assigneeSources.value];
  sources[assigneeSourceIndex.value] = source;
  return { sources, ...(assigneeHandling.value === undefined ? {} : { handling: assigneeHandling.value }) };
}

/** 先保存当前来源，再进入另一条，防止切换时丢失未保存选择。 */
function selectAssigneeSource(index: number): boolean {
  const policy = buildAssigneePolicy();
  if (!policy && (assigneeSourceKind.value !== 'default' || assigneeSources.value.length > 1)) {
    emit('validation-error', 'client.invalid_workflow_assignee_policy');
    return false;
  }
  assigneeSources.value = policy?.sources ?? [];
  assigneeSourceIndex.value = index;
  loadAssigneeSource();
  return true;
}

/** 来源总数最多八条，与服务端闭合配置保持一致。 */
function addAssigneeSource(): void {
  if (assigneeSources.value.length >= 8 || !selectAssigneeSource(assigneeSourceIndex.value)) return;
  assigneeSources.value.push({ resolverKindKey: 'initiator' });
  assigneeSourceIndex.value = assigneeSources.value.length - 1;
  loadAssigneeSource();
}

/** 显式移除当前来源，其余来源及顺序保持不变。 */
function removeAssigneeSource(): void {
  assigneeSources.value.splice(assigneeSourceIndex.value, 1);
  assigneeSourceIndex.value = Math.max(0, assigneeSourceIndex.value - 1);
  loadAssigneeSource();
}

/** 预览当前办理人策略对发起人的解析结果。 */
async function previewAssigneeScope(): Promise<void> {
  if (dynamicAssignees.value || assigneeHandling.value !== undefined) return;
  assigneePreviewLoading.value = true;
  assigneePreviewError.value = '';
  assigneePreviewUsers.value = [];
  try {
    const selectedPolicy = buildAssigneePolicy();
    if (!selectedPolicy && assigneeSourceKind.value !== 'default') throw new Error('invalid source');
    const assigneePolicy = selectedPolicy ?? (approvalModeKey.value === 'single'
      ? { sources: [{ resolverKindKey: 'initiator' }] }
      : { sources: [{ resolverKindKey: 'specified_users', userIds: approvalApproverUserIds.value }] });
    assigneePreviewUsers.value = await previewWorkflowAssignees(assigneePolicy, undefined, undefined,
      selectedPolicy && approvalModeKey.value !== 'single' ? approvalApproverUserIds.value : []);
    if (assigneePreviewUsers.value.length === 0) {
      assigneePreviewError.value = t('workflowDesigner.assignee.previewEmpty');
    }
  } catch {
    assigneePreviewError.value = t('workflowDesigner.assignee.previewFailed');
  } finally {
    assigneePreviewLoading.value = false;
  }
}

/** 保存审批参与人、收敛方式及超时策略；发布后由服务端固化为步骤快照。 */
function saveApprovalConfiguration(): void {
  if (props.disabled) return;
  try { if (assigneeHandling.value !== undefined) readAssigneeHandling(assigneeHandling.value); }
  catch { emit('validation-error', 'client.invalid_workflow_assignee_policy'); return; }
  const envelope = store.approverConfig1 as { value?: WorkflowVue3Node; id?: number | string };
  const value = { ...envelope.value };
  // 服务端定义不携带原版瞬时展示数组；授权仍由下面的闭合策略决定。
  if (!Array.isArray(value.nodeUserList)) value.nodeUserList = [];
  if (approvalSignRequired.value) value.signRequired = true; else delete value.signRequired;
  if (approvalCommentRequired.value) value.commentRequired = true; else delete value.commentRequired;
  if (approvalAllowedActions.value.length === availableHumanActions.value.length) delete value.operationPermissions;
  else value.operationPermissions = Object.fromEntries(availableHumanActions.value.map(key => [key, approvalAllowedActions.value.includes(key)]));
  if (editingClaim.value) {
    const assignees = buildAssigneePolicy();
    if (assignees === undefined && assigneeSourceKind.value !== 'default') { emit('validation-error', 'client.invalid_workflow_assignee_policy'); return; }
    if (assignees === undefined) delete value.assigneePolicy; else value.assigneePolicy = assignees;
    value.claimPolicy = readClaimPolicy({ allowRelease: claimAllowRelease.value });
    delete value.approvalPolicy; delete value.taskPolicy; delete value.votePolicy;
  } else if (editingVote.value) {
    const assignees = buildAssigneePolicy();
    if (assignees === undefined && assigneeSourceKind.value !== 'default') { emit('validation-error', 'client.invalid_workflow_assignee_policy'); return; }
    if (assignees === undefined) delete value.assigneePolicy; else value.assigneePolicy = assignees;
    try { value.votePolicy = readVotePolicy(votePolicy.value); }
    catch { emit('validation-error', 'client.invalid_workflow_vote_policy'); return; }
    delete value.approvalPolicy; delete value.taskPolicy;
  } else if (editingTask.value) {
    const assignees = buildAssigneePolicy();
    if (assignees === undefined && assigneeSourceKind.value !== 'default') { emit('validation-error', 'client.invalid_workflow_assignee_policy'); return; }
    if (assignees === undefined) delete value.assigneePolicy; else value.assigneePolicy = assignees;
    value.taskPolicy = readTaskPolicy({ completionModeKey: taskCompletionMode.value });
    delete value.approvalPolicy;
  } else if (editingGroups.value) {
    if (!saveCurrentGroup()) return;
    try {
      Object.assign(value, readGroupApprovalConfiguration({ groups: approvalGroups.value,
        groupApprovalPolicy: { modeKey: groupGateMode.value,
          ...(groupGateMode.value === 'nOfM' ? { requiredApprovals: groupGateRequired.value } : {}),
          ...(groupGateMode.value === 'percent' ? { requiredPercentage: groupGatePercentage.value } : {}) } }));
    } catch { emit('validation-error', 'client.invalid_workflow_approval_policy'); return; }
    delete value.assigneePolicy; delete value.approvalPolicy;
  } else if (!writeMemberConfiguration(value)) return;
  try {
    const listeners = readWorkflowActivityListeners(activityListeners.value);
    if (listeners.length) value.listeners = listeners; else delete value.listeners;
  } catch { emit('validation-error', 'client.invalid_workflow_activity_listeners'); return; }
  if (!resultCallbackField.value) delete value.resultCallbackFieldKey;
  else {
    const grants = isRecord(value.fieldPolicies) ? { ...value.fieldPolicies } : {};
    const field = props.gatewayFields.find(item => item.fieldKey === resultCallbackField.value);
    if (!Object.hasOwn(grants, resultCallbackField.value)) grants[resultCallbackField.value] = field?.required ? 'required' : 'editable';
    try {
      const key = readWorkflowResultCallback({ resultCallbackFieldKey: resultCallbackField.value, fieldPolicies: grants },
        editingClaim.value ? 'human.claim' : editingTask.value ? 'human.task' : editingVote.value ? 'human.vote' : editingGroups.value ? 'human.group_approval' : 'human.approval', props.gatewayFields);
      value.resultCallbackFieldKey = key; value.fieldPolicies = grants;
    } catch { emit('validation-error', 'client.invalid_workflow_result_callback'); return; }
  }
  if (!slaEnabled.value) delete value.slaPolicy;
  else {
    try { value.slaPolicy = readWorkflowSlaPolicy({ dueAfterMinutes: slaDueMinutes.value, pauseModeKey: slaPauseMode.value, actionKey: slaAction.value },
      editingClaim.value ? 'human.claim' : editingTask.value ? 'human.task' : editingVote.value ? 'human.vote' : editingGroups.value ? 'human.group_approval' : 'human.approval'); }
    catch { emit('validation-error', 'client.invalid_workflow_sla_policy'); return; }
  }
  if (!timeoutEnabled.value) {
    delete value.timeoutPolicy;
  } else {
    const escalationValid = !timeoutEscalationEnabled.value ||
      (timeoutEscalationMinutes.value >= timeoutDueMinutes.value &&
        timeoutEscalationRecipientUserId.value.length > 0);
    if (timeoutDueMinutes.value < 1 || timeoutReminderIntervalMinutes.value < 1 ||
      timeoutMaxReminderCount.value < 0 ||
      (timeoutMaxReminderCount.value === 0 && !timeoutEscalationEnabled.value) ||
      !escalationValid) {
      emit('validation-error', 'client.invalid_workflow_timeout_policy');
      return;
    }
    value.timeoutPolicy = {
      dueAfterMinutes: timeoutDueMinutes.value,
      reminderIntervalMinutes: timeoutReminderIntervalMinutes.value,
      maxReminderCount: timeoutMaxReminderCount.value,
      ...(timeoutEscalationEnabled.value ? {
        escalationAfterMinutes: timeoutEscalationMinutes.value,
        escalationRecipientUserId: timeoutEscalationRecipientUserId.value
      } : {})
    };
  }
  store.setApproverConfig({ ...envelope, flag: true, value });
  store.setApprover(false);
}

/** 普通审批与组内审批共用同一份来源、人数和比例规则。 */
function writeMemberConfiguration(value: Record<string, unknown>): boolean {
  const assigneePolicy = buildAssigneePolicy();
  if (assigneePolicy === undefined && (assigneeSourceKind.value !== 'default' || assigneeSources.value.length > 1)) {
    emit('validation-error', 'client.invalid_workflow_assignee_policy');
    return false;
  }
  if (assigneePolicy === undefined) {
    delete value.assigneePolicy;
  } else {
    value.assigneePolicy = assigneePolicy;
  }
  if (approvalModeKey.value === 'single') {
    delete value.approvalPolicy;
    if (assigneeSourceKind.value === 'initiator' || assigneeSourceKind.value === 'default') value.settype = 5;
  } else {
    const approvers = [...new Set(approvalApproverUserIds.value)];
    const required = approvalRequiredApprovals.value;
    const percentage = approvalRequiredPercentage.value;
    if (approvers.length > 20 || approvers.length < 2 && assigneePolicy === undefined ||
      (approvalModeKey.value === 'nOfM' &&
        (!Number.isInteger(required) || required <= 1 || required >= (assigneePolicy === undefined ? approvers.length : 20))) ||
      (approvalModeKey.value === 'percent' &&
        (!Number.isInteger(percentage) || percentage < 1 || percentage > 100))) {
      emit('validation-error', 'client.invalid_workflow_approval_policy');
      return false;
    }
    value.approvalPolicy = {
      modeKey: approvalModeKey.value,
      approverUserIds: approvers,
      ...(approvalModeKey.value === 'nOfM' ? { requiredApprovals: required } : {}),
      ...(approvalModeKey.value === 'percent' ? { requiredPercentage: percentage } : {})
    };
    value.nodeUserList = approvers.map(id => ({ id, type: 'user',
      name: ccCandidates.value.find(candidate => candidate.id === id)?.displayName ?? id }));
    value.settype = 1;
    value.examineMode = approvalModeKey.value === 'any' ? 3 : approvalModeKey.value === 'all' ? 2 : 1;
  }
  return true;
}

/** 关闭审批配置并丢弃未保存输入。 */
function closeApprovalConfiguration(): void {
  store.setApprover(false);
}

/** 从不可信设计器配置读取整数，非法值回落到安全默认值。 */
function readInteger(value: unknown, fallback: number): number {
  return Number.isInteger(value) ? Number(value) : fallback;
}

/** 识别普通对象，避免把数组或空值当成策略配置。 */
function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** 画布摘要只解释同一份闭合配置，来源人数在真实激活时决定。 */
function approvalPresentation(node: WorkflowVue3Node): { text: string; error: boolean; tip: string } {
  try {
    if (Number(node.type) === 21) {
      const config = readGroupApprovalConfiguration(node);
      return { text: t('workflowDesigner.groups.summary', { count: config.groups.length }) + ' · ' + t(`workflowDesigner.approval.${config.groupApprovalPolicy.modeKey}`), error: false, tip: '' };
    }
    const policy = node.approvalPolicy === undefined ? undefined : readApprovalPolicy(node.approvalPolicy, node.assigneePolicy);
    const sources = node.assigneePolicy === undefined ? [] : readAssigneePolicy(node.assigneePolicy).sources as Record<string, unknown>[];
    const labels: Record<string, string> = {
      initiator: t('workflowDesigner.assignee.initiator'), specified_users: t('workflowDesigner.assignee.specifiedUsers'),
      role_members: t('workflowDesigner.assignee.roleMembers'), organization_unit_leader: t('workflowDesigner.assignee.unitLeader'),
      initiator_primary_unit_leader: t('workflowDesigner.assignee.primaryUnitLeader'),
      initiator_direct_manager: t('workflowDesigner.assignee.directManager'),
      flow_administrators: t('workflowDesigner.assignee.flowAdministrators'),
      initiator_ancestor_unit_leader: t('workflowDesigner.assignee.initiatorAncestorUnitLeader'),
      position_members: t('workflowDesigner.assignee.positionMembers'), organization_unit_members: t('workflowDesigner.assignee.unitMembers'),
      initiator_leader_chain: t('workflowDesigner.assignee.leaderChain')
    };
    const parts = sources.map(source => labels[String(source.resolverKindKey)]);
    const users = policy?.approverUserIds as string[] | undefined;
    if (users?.length) parts.push(t('workflowDesigner.assignee.specifiedCount', { count: users.length }));
    if (parts.length === 0) parts.push(labels.initiator!);
    if (Number(node.type) === 18) {
      const task = readTaskPolicy(node.taskPolicy);
      parts.push(t(`workflowDesigner.tasks.${task.completionModeKey}`));
    }
    if (Number(node.type) === 17) {
      const claim = readClaimPolicy(node.claimPolicy);
      parts.push(t(claim.allowRelease ? 'workflowDesigner.claims.releaseAllowed' : 'workflowDesigner.claims.releaseDisabled'));
    }
    if (Number(node.type) === 19) {
      const vote = readVotePolicy(node.votePolicy);
      parts.push(t('workflowDesigner.votes.thresholds', { participation: vote.participationPercentage, approval: vote.approvalPercentage }));
    }
    if (policy) {
      const modes: Record<string, string> = { all: t('workflowDesigner.approval.all'), any: t('workflowDesigner.approval.any'),
        nOfM: t('workflowDesigner.approval.nOfM'), percent: t('workflowDesigner.approval.percent'), sequential: t('workflowDesigner.approval.sequential') };
      parts.push(modes[String(policy.modeKey)]!);
      if (policy.modeKey === 'percent') parts.push(new Intl.NumberFormat(undefined, { style: 'percent' }).format(Number(policy.requiredPercentage)/100));
    }
    return { text: parts.join(' · '), error: false, tip: '' };
  } catch {
    const tip = t('workflowDesigner.assignee.configurationInvalid');
    return { text: tip, error: true, tip };
  }
}

/** 保存排他网关的单字段闭合条件，值类型严格跟随已发布表单字段。 */
function saveGatewayCondition(): void {
  const condition = gatewayCondition.value;
  const field = selectedGatewayField.value;
  if (condition?.isDefault === true) {
    store.setCondition(false);
    return;
  }
  if (condition !== undefined && gatewayAdvanced.value) {
    try {
      const expression = readWorkflowCondition(gatewayExpression.value);
      const { fieldKey: _field, operator: _operator, value: _value, ...rest } = condition;
      commitGatewayCondition({ ...rest, condition: expression });
      store.setCondition(false);
    } catch { emit('validation-error', 'client.invalid_workflow_gateway'); }
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
  commitGatewayCondition({
      ...condition,
      condition: undefined,
      branchKey: typeof condition.branchKey === 'string' ? condition.branchKey : condition.id,
      fieldKey: field.fieldKey,
      operator: gatewayOperator.value,
      ...(isEmptyOperator ? { value: undefined } : { value })
  });
  store.setCondition(false);
}

/** 原组件信封保存整个网关；只替换选定条件，兄弟分支及公共后继保持原拓扑。 */
function commitGatewayCondition(condition: WorkflowVue3Node): void {
  const envelope = store.conditionsConfig1 as { value?: WorkflowVue3Node; priorityLevel?: number; id?: number | string };
  const gateway = envelope.value;
  const value = gateway?.conditionNodes === undefined ? condition : {
    ...gateway, conditionNodes: gateway.conditionNodes.map(item => item.priorityLevel === envelope.priorityLevel ? condition : item)
  };
  store.setConditionsConfig({ ...envelope, flag: true, value });
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

function openWithdrawalPolicy(): void {
  const policy = nodeConfig.value.withdrawalPolicy === undefined ? { modeKey: 'disabled' as const } : readWithdrawalPolicy(nodeConfig.value.withdrawalPolicy);
  withdrawalMode.value = policy.modeKey; withdrawalNodeKeys.value = policy.modeKey === 'at_active_nodes' ? [...policy.nodeKeys] : [];
  withdrawalDrawer.value = true;
}
function openAdministrators(): void {
  try { administratorUserIds.value = readFlowAdministratorUserIds(nodeConfig.value.administratorUserIds ?? []); administratorDrawer.value = true; }
  catch { emit('validation-error', 'client.invalid_workflow_administrators'); }
}
function saveAdministrators(ids: string[]): void {
  if (props.disabled) return;
  const next = { ...nodeConfig.value }; const selected = readFlowAdministratorUserIds(ids);
  if (selected.length) next.administratorUserIds = selected; else delete next.administratorUserIds;
  nodeConfig.value = next; administratorDrawer.value = false;
}
function saveWithdrawalPolicy(): void {
  try {
    const policy = readWithdrawalPolicy({ modeKey: withdrawalMode.value, ...(withdrawalMode.value === 'at_active_nodes' ? { nodeKeys: withdrawalNodeKeys.value } : {}) });
    if (policy.modeKey === 'at_active_nodes' && policy.nodeKeys.some(key => !withdrawalNodes.value.some(node => node.key === key))) throw new Error('client.invalid_workflow_withdrawal_policy');
    const next = { ...nodeConfig.value };
    if (policy.modeKey === 'disabled') delete next.withdrawalPolicy; else next.withdrawalPolicy = policy;
    nodeConfig.value = next; withdrawalDrawer.value = false;
  } catch { emit('validation-error', 'client.invalid_workflow_withdrawal_policy'); }
}

function setWorkflowKind(value: unknown): void {
  if (!props.disabled && (value === 'approval' || value === 'automation'))
    nodeConfig.value = { ...nodeConfig.value, workflowKindKey: value };
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
    <label>
      {{ t('workflowDesigner.kind.label') }}
      <el-select :model-value="nodeConfig.workflowKindKey ?? 'approval'" :disabled="disabled"
        :aria-label="t('workflowDesigner.kind.label')" style="width: 180px" @update:model-value="setWorkflowKind">
        <el-option value="approval" :label="t('workflowDesigner.kind.approval')" />
        <el-option value="automation" :label="t('workflowDesigner.kind.automation')" />
      </el-select>
    </label>
    <el-button :disabled="disabled" data-testid="workflow-withdrawal-open" @click="openWithdrawalPolicy">{{ t('workflowDesigner.withdrawal.title') }}</el-button>
    <el-button :disabled="disabled" data-testid="workflow-administrators-open" @click="openAdministrators">{{ t('workflowDesigner.administrators.title') }}</el-button>
    <div class="workflow-vue3-adapter__canvas" :class="{ 'is-disabled': disabled }">
      <node-wrap v-model:node-config="nodeConfig" />
      <div class="end-node">
        <div class="end-node-circle" />
        <div class="end-node-text">流程结束</div>
      </div>
    </div>
  </section>
  <ElDrawer :model-value="Boolean(dependencyNodeKey)" :title="t('workflowDesigner.dependencies.title')" size="min(560px, 94vw)" @update:model-value="dependencyNodeKey = ''">
    <p>{{ t('workflowDesigner.dependencies.help') }}</p>
    <ElSelect v-if="dependencyNodeKey" data-testid="workflow-dependency-selection" v-model="dependencyKeys" multiple :multiple-limit="16" :disabled="disabled" :aria-label="t('workflowDesigner.dependencies.title')" style="width: 100%">
      <ElOption v-for="node in dependencyNodes" :key="node.id" :value="node.id!" :label="`${node.nodeName ?? node.id} (${node.id})`" />
      <ElOption v-for="key in dependencyKeys.filter(key => !dependencyNodes.some(node => node.id === key))" :key="key" :value="key" :label="key" disabled />
    </ElSelect>
    <p v-if="dependencyError" role="alert">{{ t('workflowDesigner.dependencies.invalid') }}</p>
    <ElButton :disabled="disabled" data-testid="workflow-save-dependencies" @click="saveDependencies">{{ t('workflowDesigner.timeout.confirm') }}</ElButton>
  </ElDrawer>
  <ElDrawer :model-value="store.triggerDrawer" :title="t(editingBusinessCommand ? 'workflowDesigner.businessCommand.title' : 'workflowDesigner.connector.title')" size="min(760px, 94vw)" @update:model-value="store.setTrigger($event)">
    <WorkflowBusinessCommandEditor v-if="store.triggerDrawer && editingBusinessCommand" :model-value="(store.triggerConfig1 as { value?: WorkflowVue3Node }).value" :disabled="disabled" @save="saveBusinessCommand" @cancel="store.setTrigger(false)" />
    <WorkflowConnectorEditor v-else-if="store.triggerDrawer" :model-value="(store.triggerConfig1 as { value?: WorkflowVue3Node }).value" :fields="gatewayFields" :disabled="disabled" @save="saveConnector" @cancel="store.setTrigger(false)" />
  </ElDrawer>
  <ElDrawer :model-value="store.subProcessDrawer" :title="t('workflowDesigner.subprocess.title')" size="min(760px, 94vw)" @update:model-value="store.setSubProcess($event)">
    <WorkflowSubprocessEditor v-if="store.subProcessDrawer" :model-value="(store.subProcessConfig1 as { value?: WorkflowVue3Node }).value" :fields="gatewayFields" :disabled="disabled" @save="saveSubprocess" @cancel="store.setSubProcess(false)" />
  </ElDrawer>
  <ElDrawer :model-value="store.delayDrawer" :title="t('workflowDesigner.delay.title')" size="min(560px, 94vw)" @update:model-value="store.setDelay($event)">
    <WorkflowDelayPolicyEditor v-if="store.delayDrawer" :model-value="(store.delayConfig1 as { value?: WorkflowVue3Node }).value?.delayPolicy"
      :fields="gatewayFields" :disabled="disabled" @save="saveDelay" @cancel="store.setDelay(false)" />
  </ElDrawer>
  <ElDrawer :model-value="store.modifyDataDrawer" :title="t('workflowDesigner.fieldActivity.modify')" size="min(760px, 94vw)" @update:model-value="store.setModifyData($event)">
    <WorkflowFieldMutationEditor v-if="store.modifyDataDrawer" :model-value="(store.modifyDataConfig1 as { value?: WorkflowVue3Node }).value" kind="data.modify" :fields="gatewayFields" :disabled="disabled" @save="saveField($event, false)" @cancel="store.setModifyData(false)" />
  </ElDrawer>
  <ElDrawer :model-value="store.deleteDataDrawer" :title="t('workflowDesigner.fieldActivity.clear')" size="min(760px, 94vw)" @update:model-value="store.setDeleteData($event)">
    <WorkflowFieldMutationEditor v-if="store.deleteDataDrawer" :model-value="(store.deleteDataConfig1 as { value?: WorkflowVue3Node }).value" kind="data.clear" :fields="gatewayFields" :disabled="disabled" @save="saveField($event, true)" @cancel="store.setDeleteData(false)" />
  </ElDrawer>
  <el-drawer v-model="administratorDrawer" :title="t('workflowDesigner.administrators.title')" size="min(560px, 94vw)">
    <WorkflowFlowAdministratorEditor v-if="administratorDrawer" :user-ids="administratorUserIds" :disabled="disabled"
      @save="saveAdministrators" @cancel="administratorDrawer = false" />
  </el-drawer>
  <el-drawer v-model="withdrawalDrawer" :title="t('workflowDesigner.withdrawal.title')" size="min(560px, 94vw)">
    <div class="workflow-timeout-form">
      <p>{{ t('workflowDesigner.withdrawal.help') }}</p>
      <el-select v-model="withdrawalMode" data-testid="workflow-withdrawal-mode">
        <el-option v-for="mode in (['disabled', 'before_any_human_action', 'at_active_nodes'] as const)" :key="mode" :value="mode" :label="t(`workflowDesigner.withdrawal.${mode}`)" />
      </el-select>
      <el-select v-if="withdrawalMode === 'at_active_nodes'" v-model="withdrawalNodeKeys" multiple :multiple-limit="16" data-testid="workflow-withdrawal-nodes">
        <el-option v-for="node in withdrawalNodes" :key="node.key" :value="node.key" :label="node.label" />
      </el-select>
    </div>
    <template #footer><el-button type="primary" data-testid="workflow-withdrawal-save" @click="saveWithdrawalPolicy">{{ t('workflowDesigner.withdrawal.save') }}</el-button></template>
  </el-drawer>
  <el-drawer
    :model-value="store.copyerDrawer"
    title="选择抄送人"
    size="min(520px, 94vw)"
    @close="closeCcRecipients"
  >
    <el-select v-model="ccRecipientMode" :disabled="disabled" :aria-label="t('workflowDesigner.cc.mode')" data-testid="workflow-cc-mode">
      <el-option value="users" :label="t('workflowDesigner.cc.users')" /><el-option value="policy" :label="t('workflowDesigner.cc.policy')" />
    </el-select>
    <WorkflowCcRecipientPolicyEditor v-if="store.copyerDrawer && ccRecipientMode === 'policy'" :model-value="ccRecipientPolicy" :disabled="disabled"
      :fields="assigneeReferenceFields" :previous-nodes="ccPreviousNodes" @save="saveCcPolicy" @cancel="closeCcRecipients" />
    <el-select v-if="ccRecipientMode === 'users'"
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
      <el-button v-if="ccRecipientMode === 'users'" type="primary" :disabled="ccRecipientUserIds.length === 0" @click="saveCcRecipients">
        确定
      </el-button>
    </template>
  </el-drawer>
  <el-drawer
    :model-value="store.approverDrawer"
    :title="t('workflowDesigner.approval.title')"
    size="min(560px, 94vw)"
    @close="closeApprovalConfiguration"
  >
    <div class="workflow-timeout-form">
      <section v-if="editingGroups" class="workflow-group-editor">
        <label><span>{{ t('workflowDesigner.groups.current') }}</span>
          <el-select :model-value="approvalGroupIndex" data-testid="workflow-group-select" @update:model-value="selectApprovalGroup">
            <el-option v-for="(group, index) in approvalGroups" :key="String(group.groupKey)" :value="index" :label="String(group.groupName ?? group.groupKey)" />
          </el-select>
        </label>
        <label><span>{{ t('workflowDesigner.groups.key') }}</span><el-input v-if="approvalGroups[approvalGroupIndex]" :model-value="String(approvalGroups[approvalGroupIndex]!.groupKey ?? '')" @update:model-value="approvalGroups[approvalGroupIndex]!.groupKey = $event" data-testid="workflow-group-key" /></label>
        <label><span>{{ t('workflowDesigner.groups.name') }}</span><el-input v-if="approvalGroups[approvalGroupIndex]" :model-value="String(approvalGroups[approvalGroupIndex]!.groupName ?? '')" @update:model-value="approvalGroups[approvalGroupIndex]!.groupName = $event" data-testid="workflow-group-name" /></label>
        <el-button :disabled="approvalGroups.length >= 8" data-testid="workflow-group-add" @click="addApprovalGroup">{{ t('workflowDesigner.groups.add') }}</el-button>
        <el-button :disabled="approvalGroups.length <= 2" @click="removeApprovalGroup">{{ t('workflowDesigner.groups.remove') }}</el-button>
        <label><span>{{ t('workflowDesigner.groups.gate') }}</span>
          <el-select v-model="groupGateMode" data-testid="workflow-group-gate">
            <el-option v-for="mode in (['all', 'any', 'nOfM', 'percent'] as const)" :key="mode" :value="mode" :label="t(`workflowDesigner.approval.${mode}`)" />
          </el-select>
        </label>
        <label v-if="groupGateMode === 'nOfM'"><span>{{ t('workflowDesigner.groups.required') }}</span><el-input-number v-model="groupGateRequired" :min="2" :max="Math.max(2, approvalGroups.length - 1)" /></label>
        <label v-if="groupGateMode === 'percent'"><span>{{ t('workflowDesigner.approval.percentage') }}</span><el-input-number v-model="groupGatePercentage" :min="1" :max="100" /></label>
        <p>{{ t('workflowDesigner.groups.help') }}</p>
      </section>
      <div class="workflow-assignee-sources" :aria-label="t('workflowDesigner.assignee.sources')">
        <el-button v-for="(_, index) in assigneeSources" :key="index"
          :type="index === assigneeSourceIndex ? 'primary' : 'default'"
          :data-testid="`workflow-assignee-source-${index}`" :aria-pressed="index === assigneeSourceIndex"
          @click="selectAssigneeSource(index)">{{ t('workflowDesigner.assignee.sourceNumber', { number: index + 1 }) }}</el-button>
        <el-button data-testid="workflow-assignee-add-source" :disabled="assigneeSources.length >= 8" @click="addAssigneeSource">{{ t('workflowDesigner.assignee.addSource') }}</el-button>
        <el-button v-if="assigneeSources.length > 0" data-testid="workflow-assignee-remove-source" @click="removeAssigneeSource">{{ t('workflowDesigner.assignee.removeSource') }}</el-button>
      </div>
      <label>
        <span>办理人来源</span>
        <el-select v-model="assigneeSourceKind" data-testid="workflow-assignee-source-kind">
          <el-option :label="t('workflowDesigner.assignee.defaultMembers')" value="default" :disabled="assigneeSources.length > 1" />
          <el-option label="流程发起人" value="initiator" />
          <el-option label="指定用户" value="specified_users" />
          <el-option :label="t('workflowDesigner.assignee.flowAdministrators')" value="flow_administrators" />
          <el-option label="角色成员" value="role_members" />
          <el-option label="机构负责人" value="organization_unit_leader" />
          <el-option label="发起人主部门负责人" value="initiator_primary_unit_leader" />
          <el-option :label="t('workflowDesigner.assignee.directManager')" value="initiator_direct_manager" />
          <el-option :label="t('workflowDesigner.assignee.initiatorAncestorUnitLeader')" value="initiator_ancestor_unit_leader" />
          <el-option :label="t('workflowDesigner.assignee.positionMembers')" value="position_members" />
          <el-option :label="t('workflowDesigner.assignee.unitMembers')" value="organization_unit_members" />
          <el-option :label="t('workflowDesigner.assignee.leaderChain')" value="initiator_leader_chain" />
          <el-option :label="t('workflowDesigner.assignee.formUsers')" value="form_users" />
          <el-option :label="t('workflowDesigner.assignee.formUnits')" value="form_units" />
          <el-option :label="t('workflowDesigner.assignee.previousActors')" value="previous_node_actors" />
          <el-option :label="t('workflowDesigner.assignee.starterSelected')" value="starter_selected_users" />
        </el-select>
      </label>
      <p v-if="assigneeSourceKind === 'default'">{{ t('workflowDesigner.assignee.defaultMembersHelp') }}</p>
      <template v-if="assigneeSourceKind === 'starter_selected_users'">
        <label><span>{{ t('workflowDesigner.assignee.selectionKey') }}</span><el-input v-model="starterSelectionKey" maxlength="128" data-testid="workflow-starter-selection-key" /></label>
        <label><span>{{ t('workflowDesigner.assignee.selectionMode') }}</span><el-select v-model="starterSelectionMode" data-testid="workflow-starter-selection-mode">
          <el-option :label="t('workflowDesigner.assignee.selectionSingle')" value="single" /><el-option :label="t('workflowDesigner.assignee.selectionMultiple')" value="multiple" />
        </el-select></label>
        <label v-if="starterSelectionMode === 'multiple'"><span>{{ t('workflowDesigner.assignee.selectionMaximum') }}</span><el-input-number v-model="starterSelectionMaximum" :min="1" :max="20" data-testid="workflow-starter-selection-maximum" /></label>
        <label><span>{{ t('workflowDesigner.assignee.selectionRange') }}</span><el-select v-model="starterSelectionRange" data-testid="workflow-starter-selection-range">
          <el-option :label="t('workflowDesigner.assignee.selectionAll')" value="all" /><el-option :label="t('workflowDesigner.assignee.selectionUsers')" value="specified_users" /><el-option :label="t('workflowDesigner.assignee.selectionRoles')" value="role_members" />
        </el-select></label>
      </template>
      <label v-if="assigneeSourceKind === 'form_users' || assigneeSourceKind === 'form_units'">
        <span>{{ t('workflowDesigner.assignee.referenceField') }}</span>
        <el-select v-model="assigneeFieldKey" data-testid="workflow-assignee-field">
          <el-option v-for="field in assigneeReferenceFields" :key="field.fieldKey" :label="field.fieldKey" :value="field.fieldKey" />
        </el-select>
      </label>
      <label v-if="assigneeSourceKind === 'previous_node_actors'">
        <span>{{ t('workflowDesigner.assignee.previousNode') }}</span>
        <el-select v-model="assigneePreviousNodeKey" data-testid="workflow-assignee-previous-node">
          <el-option v-for="node in assigneePreviousNodes" :key="node.key" :label="node.label" :value="node.key" />
        </el-select>
      </label>
      <label v-if="assigneeSourceKind === 'initiator_ancestor_unit_leader'">
        <span>{{ t('workflowDesigner.assignee.ancestorLevel') }}</span>
        <el-input-number
          v-model="assigneeAncestorLevel"
          data-testid="workflow-assignee-ancestor-level"
          :min="1"
          :max="20"
        />
      </label>
      <label v-if="assigneeSourceKind === 'specified_users' || assigneeSourceKind === 'starter_selected_users' && starterSelectionRange === 'specified_users'">
        <span>指定用户</span>
        <el-select v-model="assigneeUserIds" multiple filterable :multiple-limit="20" :loading="ccCandidatesLoading" data-testid="workflow-assignee-users">
          <el-option v-for="candidate in ccCandidates" :key="candidate.id" :label="`${candidate.displayName} (${candidate.username})`" :value="candidate.id" />
        </el-select>
      </label>
      <label v-if="assigneeSourceKind === 'role_members' || assigneeSourceKind === 'starter_selected_users' && starterSelectionRange === 'role_members'">
        <span>角色</span>
        <el-select v-model="assigneeRoleIds" multiple filterable :multiple-limit="5" :loading="roleCandidatesLoading" data-testid="workflow-assignee-roles">
          <el-option v-for="candidate in roleCandidates" :key="candidate.id" :label="`${candidate.name} (${candidate.code})`" :value="candidate.id" />
        </el-select>
      </label>
      <label v-if="assigneeSourceKind === 'organization_unit_leader' || assigneeSourceKind === 'organization_unit_members'">
        <span>机构单元</span>
        <el-select v-model="assigneeUnitId" filterable :loading="organizationUnitCandidatesLoading" data-testid="workflow-assignee-unit">
          <el-option v-for="candidate in organizationUnitCandidates" :key="candidate.id" :label="`${candidate.name} (${candidate.code})`" :value="candidate.id" />
        </el-select>
      </label>
      <label v-if="assigneeSourceKind === 'position_members'">
        <span>{{ t('workflowDesigner.assignee.positions') }}</span>
        <el-select v-model="assigneePositionIds" multiple filterable :multiple-limit="5" :loading="positionCandidatesLoading" data-testid="workflow-assignee-positions">
          <el-option v-for="candidate in positionCandidates" :key="candidate.id" :label="`${candidate.name} (${candidate.code})`" :value="candidate.id" />
        </el-select>
        <span v-if="positionCandidatesError" role="alert">{{ t('workflowDesigner.assignee.candidatesUnavailable') }} <el-button @click="loadPositionCandidates">{{ t('workflowDesigner.assignee.retryCandidates') }}</el-button></span>
      </label>
      <label v-if="assigneeSourceKind === 'initiator_leader_chain'">
        <span>{{ t('workflowDesigner.assignee.maximumLevels') }}</span>
        <el-input-number v-model="assigneeMaximumLevels" :min="1" :max="20" data-testid="workflow-assignee-maximum-levels" />
      </label>
      <WorkflowAssigneeHandlingEditor v-model="assigneeHandling" :users="ccCandidates" :roles="roleCandidates"
        :units="organizationUnitCandidates" :positions="positionCandidates" :vote="editingVote" />
      <div class="workflow-assignee-preview">
        <div class="workflow-assignee-preview__header">
          <span>{{ t('workflowDesigner.assignee.previewTitle') }}</span>
          <el-button
            data-testid="workflow-assignee-preview"
            :loading="assigneePreviewLoading"
            :disabled="dynamicAssignees || assigneeHandling !== undefined"
            @click="previewAssigneeScope"
          >{{ t('workflowDesigner.assignee.previewAction') }}</el-button>
        </div>
        <p v-if="assigneePreviewError" class="workflow-assignee-preview__error">{{ assigneePreviewError }}</p>
        <p v-if="dynamicAssignees">{{ t('workflowDesigner.assignee.dynamicHint') }}</p>
        <p v-else-if="assigneeHandling !== undefined">{{ t('workflowDesigner.handling.previewHint') }}</p>
        <ul v-else-if="assigneePreviewUsers.length > 0" class="workflow-assignee-preview__users">
          <li v-for="user in assigneePreviewUsers" :key="user.id">
            {{ user.displayName }} ({{ user.username }})
          </li>
        </ul>
      </div>
      <label v-if="editingClaim"><span>{{ t('workflowDesigner.claims.allowRelease') }}</span>
        <ElSwitch v-model="claimAllowRelease" data-testid="workflow-claim-release" />
      </label>
      <WorkflowVotePolicyEditor v-else-if="editingVote" v-model="votePolicy" :result-fields="gatewayFields" />
      <label v-else-if="editingTask"><span>{{ t('workflowDesigner.tasks.mode') }}</span>
        <el-select v-model="taskCompletionMode" data-testid="workflow-task-mode">
          <el-option v-for="mode in (['all', 'any', 'sequential'] as const)" :key="mode" :value="mode" :label="t(`workflowDesigner.tasks.${mode}`)" />
        </el-select>
      </label>
      <template v-else>
      <label>
        <span>{{ t('workflowDesigner.approval.mode') }}</span>
        <el-select v-model="approvalModeKey" data-testid="workflow-approval-mode">
          <el-option :label="t('workflowDesigner.approval.single')" value="single" />
          <el-option :label="t('workflowDesigner.approval.all')" value="all" />
          <el-option :label="t('workflowDesigner.approval.sequential')" value="sequential" />
          <el-option :label="t('workflowDesigner.approval.any')" value="any" />
          <el-option :label="t('workflowDesigner.approval.nOfM')" value="nOfM" />
          <el-option :label="t('workflowDesigner.approval.percent')" value="percent" />
        </el-select>
      </label>
      <p v-if="approvalModeKey === 'sequential'">{{ t('workflowDesigner.approval.sequentialHelp') }}</p>
      <label v-if="approvalModeKey !== 'single'">
        <span>{{ t('workflowDesigner.approval.approvers') }}</span>
        <el-select
          v-model="approvalApproverUserIds"
          multiple
          filterable
          :multiple-limit="20"
          :loading="ccCandidatesLoading"
          data-testid="workflow-approval-approvers"
        >
          <el-option v-for="candidate in ccCandidates" :key="candidate.id" :label="`${candidate.displayName} (${candidate.username})`" :value="candidate.id" />
        </el-select>
      </label>
      <label v-if="approvalModeKey === 'nOfM'">
        <span>{{ t('workflowDesigner.approval.required') }}</span>
        <el-input-number
          v-model="approvalRequiredApprovals"
          :min="2"
          :max="assigneeSourceKind === 'default' ? Math.max(2, approvalApproverUserIds.length - 1) : 19"
          data-testid="workflow-approval-required"
        />
      </label>
      <label v-if="approvalModeKey === 'percent'">
        <span>{{ t('workflowDesigner.approval.percentage') }}</span>
        <el-input-number v-model="approvalRequiredPercentage" :min="1" :max="100" :step="1"
          data-testid="workflow-approval-percentage" />
      </label>
      </template>
      <WorkflowActivityListenerEditor v-model="activityListeners" :disabled="disabled" />
      <label><span>{{ t('workflowDesigner.result.label') }}</span><el-select v-model="resultCallbackField" data-testid="workflow-result-callback-field" :disabled="disabled">
        <el-option value="" :label="t('workflowDesigner.result.none')" />
        <el-option v-for="field in resultCallbackCandidates" :key="field.fieldKey" :value="field.fieldKey" :label="field.fieldKey" :disabled="!field.writable" />
      </el-select></label>
      <p>{{ t('workflowDesigner.result.help') }}</p><p v-if="!resultCallbackCandidates.length">{{ t('workflowDesigner.result.noFields') }}</p>
      <label><span>{{ t('workflowDesigner.sla.enabled') }}</span><el-switch v-model="slaEnabled" data-testid="workflow-sla-enabled" /></label>
      <template v-if="slaEnabled">
        <p>{{ t('workflowDesigner.sla.help') }}</p>
        <label><span>{{ t('workflowDesigner.sla.dueMinutes') }}</span><el-input-number v-model="slaDueMinutes" :min="1" :max="525600" :precision="0" data-testid="workflow-sla-due" /></label>
        <label><span>{{ t('workflowDesigner.sla.pauseMode') }}</span><el-select v-model="slaPauseMode" data-testid="workflow-sla-pause">
          <el-option value="elapsed" :label="t('workflowDesigner.sla.elapsed')" /><el-option value="freeze" :label="t('workflowDesigner.sla.freeze')" />
        </el-select></label>
        <label><span>{{ t('workflowDesigner.sla.action') }}</span><el-select v-model="slaAction" data-testid="workflow-sla-action">
          <el-option value="suspend" :label="t('workflowDesigner.sla.suspend')" /><el-option value="auto_reject" :label="t('workflowDesigner.sla.autoReject')" />
          <el-option v-if="!editingTask && !editingVote && !editingClaim" value="auto_pass" :label="t('workflowDesigner.sla.autoPass')" />
        </el-select></label>
      </template>
      <label><span>{{ t('workflowDesigner.timeout.enabled') }}</span><el-switch v-model="timeoutEnabled" data-testid="workflow-timeout-enabled" /></label>
      <template v-if="timeoutEnabled">
        <label><span>{{ t('workflowDesigner.timeout.dueMinutes') }}</span><el-input-number v-model="timeoutDueMinutes" :min="1" :max="525600" data-testid="workflow-timeout-due" /></label>
        <label><span>{{ t('workflowDesigner.timeout.reminderIntervalMinutes') }}</span><el-input-number v-model="timeoutReminderIntervalMinutes" :min="1" :max="43200" /></label>
        <label><span>{{ t('workflowDesigner.timeout.maxReminderCount') }}</span><el-input-number v-model="timeoutMaxReminderCount" :min="0" :max="100" data-testid="workflow-timeout-reminder-count" /></label>
        <label><span>{{ t('workflowDesigner.timeout.escalationEnabled') }}</span><el-switch v-model="timeoutEscalationEnabled" data-testid="workflow-timeout-escalation-enabled" /></label>
        <template v-if="timeoutEscalationEnabled">
          <label><span>{{ t('workflowDesigner.timeout.escalationMinutes') }}</span><el-input-number v-model="timeoutEscalationMinutes" :min="timeoutDueMinutes" :max="525600" /></label>
          <label>
            <span>{{ t('workflowDesigner.timeout.escalationRecipient') }}</span>
            <el-select v-model="timeoutEscalationRecipientUserId" filterable data-testid="workflow-timeout-escalation-recipient">
              <el-option v-for="candidate in ccCandidates" :key="candidate.id" :label="`${candidate.displayName} (${candidate.username})`" :value="candidate.id" />
            </el-select>
          </label>
        </template>
      </template>
    </div>
    <div class="workflow-gateway-form">
      <label><span>{{ t('workflowDesigner.actions.allowed') }}</span>
        <el-select v-model="approvalAllowedActions" multiple :aria-label="t('workflowDesigner.actions.allowed')">
          <el-option v-for="action in availableHumanActions" :key="action" :value="action" :label="t(`workflowDesigner.actions.${action}`)" />
        </el-select>
      </label>
      <el-switch v-model="approvalCommentRequired" :active-text="t('workflowDesigner.actions.commentRequired')" :aria-label="t('workflowDesigner.actions.commentRequired')" />
      <el-switch v-model="approvalSignRequired" :active-text="t('workflowDesigner.actions.signRequired')" :aria-label="t('workflowDesigner.actions.signRequired')" />
    </div>
    <template #footer>
      <el-button @click="closeApprovalConfiguration">{{ t('workflowDesigner.timeout.cancel') }}</el-button>
      <el-button type="primary" :disabled="disabled" data-testid="workflow-timeout-save" @click="saveApprovalConfiguration">{{ t('workflowDesigner.timeout.confirm') }}</el-button>
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
      <el-switch v-model="gatewayAdvanced" :active-text="t('workflowDesigner.conditions.advanced')" :aria-label="t('workflowDesigner.conditions.advanced')" />
      <WorkflowConditionEditor v-if="gatewayAdvanced" v-model="gatewayExpression" :fields="gatewayFields" />
      <template v-else>
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
      </template>
    </div>
    <template #footer>
      <el-button @click="closeGatewayCondition">取消</el-button>
      <el-button type="primary" data-testid="workflow-gateway-save" @click="saveGatewayCondition">确定</el-button>
    </template>
  </el-drawer>
</template>

<style scoped>
.workflow-vue3-adapter { position: relative; inset: auto; min-height: 620px; overflow: auto; background: var(--el-fill-color-light); }
.workflow-vue3-adapter__canvas { min-width: 960px; min-height: 620px; padding: 48px 24px 96px; transform-origin: 50% 0; }
.workflow-vue3-adapter__canvas.is-disabled { pointer-events: none; opacity: 0.72; }
.workflow-gateway-form { display: grid; gap: 18px; }
.workflow-gateway-form label { display: grid; gap: 8px; }
.workflow-timeout-form { display: grid; gap: 18px; }
.workflow-timeout-form label { display: grid; gap: 8px; }
.workflow-group-editor { display: grid; gap: 12px; padding-bottom: 18px; border-bottom: 1px solid var(--el-border-color-light); }
.workflow-assignee-sources { display: flex; flex-wrap: wrap; gap: 8px; }
.workflow-assignee-sources :deep(.el-button + .el-button) { margin-left: 0; }
.workflow-assignee-preview { display: grid; gap: 0.75rem; padding: 0.75rem; border: 1px solid var(--el-border-color-lighter); border-radius: 8px; }
.workflow-assignee-preview__header { display: flex; align-items: center; justify-content: space-between; gap: 0.75rem; }
.workflow-assignee-preview__error { margin: 0; color: var(--el-color-danger); }
.workflow-assignee-preview__users { margin: 0; padding-left: 1.1rem; }
</style>
