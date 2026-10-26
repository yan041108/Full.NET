<template>
  <div class="add-node-btn-box">
    <div class="add-node-btn">
      <el-popover v-model:visible="visible" placement="right-start" width="auto">
        <div class="add-node-popover-body">
          <button v-if="enabledNodeTypes.has('human.approval')" type="button" class="add-node-popover-item approver" @click="addType(NodeType.Approver)">
            <span class="item-wrapper"><span class="iconfont">{{ getNodeTypeMeta(NodeType.Approver).icon }}</span></span>
            <span>审批人</span>
          </button>
          <button v-if="enabledNodeTypes.has('human.group_approval')" type="button" class="add-node-popover-item approver" @click="addType(NodeType.GroupApprover)">
            <span class="item-wrapper"><span class="iconfont">{{ getNodeTypeMeta(NodeType.GroupApprover).icon }}</span></span>
            <span>{{ t('workflowDesigner.groups.node') }}</span>
          </button>
          <button v-if="enabledNodeTypes.has('human.task')" type="button" class="add-node-popover-item approver" @click="addType(NodeType.Handler)">
            <span class="item-wrapper"><span class="iconfont">{{ getNodeTypeMeta(NodeType.Handler).icon }}</span></span>
            <span>{{ t('workflowDesigner.tasks.node') }}</span>
          </button>
          <button v-if="enabledNodeTypes.has('human.claim')" type="button" class="add-node-popover-item approver" @click="addType(NodeType.Rob)">
            <span class="item-wrapper"><span class="iconfont">{{ getNodeTypeMeta(NodeType.Rob).icon }}</span></span>
            <span>{{ t('workflowDesigner.claims.node') }}</span>
          </button>
          <button v-if="enabledNodeTypes.has('human.vote')" type="button" class="add-node-popover-item approver" @click="addType(NodeType.Vote)">
            <span class="item-wrapper"><span class="iconfont">{{ getNodeTypeMeta(NodeType.Vote).icon }}</span></span>
            <span>{{ t('workflowDesigner.votes.node') }}</span>
          </button>
          <button v-if="enabledNodeTypes.has('notify.cc')" type="button" class="add-node-popover-item notifier" @click="addType(NodeType.Copyer)">
            <span class="item-wrapper"><span class="iconfont">{{ getNodeTypeMeta(NodeType.Copyer).icon }}</span></span>
            <span>抄送人</span>
          </button>
          <button v-if="enabledNodeTypes.has('gateway.exclusive')" type="button" class="add-node-popover-item condition" @click="addType(NodeType.Route)">
            <span class="item-wrapper"><span class="iconfont">{{ getNodeTypeMeta(NodeType.Route).icon }}</span></span>
            <span>条件分支</span>
          </button>
          <button v-if="enabledNodeTypes.has('gateway.parallel')" type="button" class="add-node-popover-item condition" @click="addType(NodeType.ParallelRoute)">
            <span class="item-wrapper"><span class="iconfont">{{ getNodeTypeMeta(NodeType.ParallelRoute).icon }}</span></span>
            <span>并行分支</span>
          </button>
          <button v-if="enabledNodeTypes.has('gateway.inclusive')" type="button" class="add-node-popover-item condition" @click="addType(NodeType.InclusiveRoute)">
            <span class="item-wrapper"><span class="iconfont">{{ getNodeTypeMeta(NodeType.InclusiveRoute).icon }}</span></span>
            <span>包容分支</span>
          </button>
          <button v-if="enabledNodeTypes.has('timer.delay')" type="button" class="add-node-popover-item condition" :aria-label="t('workflowDesigner.delay.title')" @click="addType(NodeType.Delay)">
            <span class="item-wrapper" aria-hidden="true"><span class="iconfont">{{ getNodeTypeMeta(NodeType.Delay).icon }}</span></span>
            <span>{{ t('workflowDesigner.delay.title') }}</span>
          </button>
          <button v-if="enabledNodeTypes.has('control.empty')" type="button" class="add-node-popover-item condition" :aria-label="t('workflowDesigner.conditions.emptyNode')" @click="addType(NodeType.Empty)">
            <span class="item-wrapper" aria-hidden="true"><span class="iconfont">{{ getNodeTypeMeta(NodeType.Empty).icon }}</span></span>
            <span>{{ t('workflowDesigner.conditions.emptyNode') }}</span>
          </button>
          <button v-if="enabledNodeTypes.has('data.modify')" type="button" class="add-node-popover-item condition" @click="addType(NodeType.ModifyData)"><span>{{ t('workflowDesigner.fieldActivity.modify') }}</span></button>
          <button v-if="enabledNodeTypes.has('data.clear')" type="button" class="add-node-popover-item condition" @click="addType(NodeType.DeleteData)"><span>{{ t('workflowDesigner.fieldActivity.clear') }}</span></button>
          <button v-if="enabledNodeTypes.has('integration.trigger')" type="button" class="add-node-popover-item condition" @click="addType(NodeType.Trigger)"><span>{{ t('workflowDesigner.connector.title') }}</span></button>
          <button v-if="enabledNodeTypes.has('business.command')" type="button" class="add-node-popover-item condition" @click="addType(NodeType.BusinessCommand)"><span>{{ t('workflowDesigner.businessCommand.title') }}</span></button>
          <button v-if="enabledNodeTypes.has('integration.async_trigger')" type="button" class="add-node-popover-item condition" @click="addType(NodeType.AsyncTrigger)"><span>{{ t('workflowDesigner.connector.asyncTitle') }}</span></button>
        </div>
        <template #reference>
          <button class="btn" type="button" aria-label="添加流程节点">
            <span aria-hidden="true">+</span>
          </button>
        </template>
      </el-popover>
    </div>
  </div>
</template>

<script setup>
import { computed, inject, ref } from 'vue'
import { ElPopover } from 'element-plus'
import { useAdminI18n } from '../../../../../i18n/adminI18n'
import { NodeType, getNodeTypeMeta } from '../utils/const.js'

const { t } = useAdminI18n()
const props = defineProps({
  childNodeP: {
    type: Object,
    default: () => null,
  },
})
const emit = defineEmits(['update:childNodeP'])
const visible = ref(false)
const enabledNodeTypes = inject(
  'fullnetWorkflowEnabledNodeTypes',
  computed(() => new Set(['human.approval']))
)
let fallbackSequence = 0

/** 生成可持久化的稳定节点键，避免随机短标识发生碰撞。 */
const createNodeId = () => {
  if (typeof globalThis.crypto?.randomUUID === 'function') return `node-${globalThis.crypto.randomUUID()}`
  fallbackSequence += 1
  return `node-${Date.now()}-${fallbackSequence}`
}

const addType = (type) => {
  if (type === NodeType.BusinessCommand) {
    emit('update:childNodeP', { id: createNodeId(), nodeName: t('workflowDesigner.businessCommand.title'), type, childNode: props.childNodeP }); visible.value = false;
    return;
  }
  visible.value = false
  if (![NodeType.Approver, NodeType.Handler, NodeType.Vote, NodeType.Rob, NodeType.GroupApprover, NodeType.Copyer, NodeType.Route, NodeType.ParallelRoute, NodeType.InclusiveRoute, NodeType.Empty, NodeType.Delay, NodeType.ModifyData, NodeType.DeleteData, NodeType.Trigger, NodeType.AsyncTrigger].includes(type)) return
  if ([NodeType.Trigger, NodeType.AsyncTrigger].includes(type)) {
    emit('update:childNodeP', { id: createNodeId(), nodeName: t(type === NodeType.AsyncTrigger ? 'workflowDesigner.connector.asyncTitle' : 'workflowDesigner.connector.title'), type, childNode: props.childNodeP })
    return
  }
  if ([NodeType.ModifyData, NodeType.DeleteData].includes(type)) {
    emit('update:childNodeP', { id: createNodeId(), nodeName: t(type === NodeType.ModifyData ? 'workflowDesigner.fieldActivity.modify' : 'workflowDesigner.fieldActivity.clear'), type, childNode: props.childNodeP })
    return
  }
  if (type === NodeType.ParallelRoute) {
    const firstBranchId = createNodeId()
    const secondBranchId = createNodeId()
    emit('update:childNodeP', {
      id: createNodeId(),
      nodeName: getNodeTypeMeta(type).label,
      type,
      error: false,
      conditionNodes: [
        {
          id: firstBranchId,
          nodeName: '并行条件1',
          type: NodeType.ConditionItem,
          priorityLevel: 1,
          branchKey: firstBranchId,
          childNode: {
            id: createNodeId(),
            nodeName: getNodeTypeMeta(NodeType.Approver).label,
            type: NodeType.Approver,
            settype: 1,
            examineMode: 1,
            nodeUserList: [],
            childNode: null,
          },
        },
        {
          id: secondBranchId,
          nodeName: '并行条件2',
          type: NodeType.ConditionItem,
          priorityLevel: 2,
          branchKey: secondBranchId,
          childNode: {
            id: createNodeId(),
            nodeName: getNodeTypeMeta(NodeType.Approver).label,
            type: NodeType.Approver,
            settype: 1,
            examineMode: 1,
            nodeUserList: [],
            childNode: null,
          },
        },
      ],
      childNode: props.childNodeP,
    })
    return
  }
  if (type === NodeType.InclusiveRoute) {
    const firstBranchId = createNodeId()
    const defaultBranchId = createNodeId()
    emit('update:childNodeP', {
      id: createNodeId(),
      nodeName: getNodeTypeMeta(type).label,
      type,
      error: false,
      conditionNodes: [
        {
          id: firstBranchId,
          nodeName: '包容条件1',
          type: NodeType.ConditionItem,
          priorityLevel: 1,
          branchKey: firstBranchId,
          fieldKey: '',
          operator: 'equals',
          childNode: {
            id: createNodeId(),
            nodeName: getNodeTypeMeta(NodeType.Approver).label,
            type: NodeType.Approver,
            settype: 1,
            examineMode: 1,
            nodeUserList: [],
            childNode: null,
          },
        },
        {
          id: defaultBranchId,
          nodeName: '其他条件',
          type: NodeType.ConditionItem,
          priorityLevel: 2,
          isDefault: true,
          childNode: {
            id: createNodeId(),
            nodeName: getNodeTypeMeta(NodeType.Approver).label,
            type: NodeType.Approver,
            settype: 1,
            examineMode: 1,
            nodeUserList: [],
            childNode: null,
          },
        },
      ],
      childNode: props.childNodeP,
    })
    return
  }
  if (type === NodeType.Route) {
    const firstBranchId = createNodeId()
    const defaultBranchId = createNodeId()
    emit('update:childNodeP', {
      id: createNodeId(),
      nodeName: getNodeTypeMeta(type).label,
      type,
      error: false,
      conditionNodes: [
        {
          id: firstBranchId,
          nodeName: '条件1',
          type: NodeType.ConditionItem,
          priorityLevel: 1,
          branchKey: firstBranchId,
          fieldKey: '',
          operator: 'equals',
          childNode: {
            id: createNodeId(),
            nodeName: getNodeTypeMeta(NodeType.Approver).label,
            type: NodeType.Approver,
            settype: 1,
            examineMode: 1,
            nodeUserList: [],
            childNode: null,
          },
        },
        {
          id: defaultBranchId,
          nodeName: '其他条件',
          type: NodeType.ConditionItem,
          priorityLevel: 2,
          isDefault: true,
          childNode: {
            id: createNodeId(),
            nodeName: getNodeTypeMeta(NodeType.Approver).label,
            type: NodeType.Approver,
            settype: 1,
            examineMode: 1,
            nodeUserList: [],
            childNode: null,
          },
        },
      ],
      childNode: props.childNodeP,
    })
    return
  }
  if (type === NodeType.Empty) {
    emit('update:childNodeP', { id: createNodeId(), nodeName: t('workflowDesigner.conditions.emptyNode'), type, childNode: props.childNodeP })
    return
  }
  if (type === NodeType.Delay) {
    emit('update:childNodeP', { id: createNodeId(), nodeName: t('workflowDesigner.delay.title'), type,
      delayPolicy: { modeKey: 'relative', timeZoneKey: 'UTC', duration: 1, unitKey: 'minute' }, childNode: props.childNodeP })
    return
  }
  if (type === NodeType.Handler) {
    emit('update:childNodeP', { id: createNodeId(), nodeName: t('workflowDesigner.tasks.node'), type,
      taskPolicy: { completionModeKey: 'all' }, childNode: props.childNodeP })
    return
  }
  if (type === NodeType.Rob) {
    emit('update:childNodeP', { id: createNodeId(), nodeName: t('workflowDesigner.claims.node'), type,
      claimPolicy: { allowRelease: true }, childNode: props.childNodeP })
    return
  }
  if (type === NodeType.Vote) {
    emit('update:childNodeP', { id: createNodeId(), nodeName: t('workflowDesigner.votes.node'), type,
      votePolicy: { ballotFormVersionId: '', participationPercentage: 100, approvalPercentage: 100, allowAbstain: true },
      childNode: props.childNodeP })
    return
  }
  if (type === NodeType.GroupApprover) {
    emit('update:childNodeP', { id: createNodeId(), nodeName: t('workflowDesigner.groups.node'), type,
      groups: [1, 2].map(number => ({ groupKey: `group_${number}`, groupName: t('workflowDesigner.groups.defaultName', { number }),
        assigneePolicy: { sources: [{ resolverKindKey: 'initiator' }] } })),
      groupApprovalPolicy: { modeKey: 'all' }, childNode: props.childNodeP })
    return
  }
  const isApprover = type === NodeType.Approver
  emit('update:childNodeP', {
    id: createNodeId(),
    nodeName: getNodeTypeMeta(type).label,
    type,
    error: false,
    settype: isApprover ? 1 : undefined,
    examineMode: isApprover ? 1 : undefined,
    nodeUserList: [],
    recipientUserIds: isApprover ? undefined : [],
    placeHolder: isApprover ? '当前登录人审批' : '流程抄送',
    childNode: props.childNodeP,
  })
}
</script>

<style scoped>
.add-node-btn-box { position: relative; display: inline-flex; width: 240px; flex: 1 0 240px; }
.add-node-btn-box::before { position: absolute; inset: 0; width: 2px; height: 100%; margin: auto; background: #cacaca; content: ''; }
.add-node-btn { z-index: 1; display: flex; width: 240px; justify-content: center; padding: 20px 0 32px; }
.btn { width: 32px; height: 32px; border: 0; border-radius: 50%; color: #fff; background: #3296fa; box-shadow: 0 2px 4px rgb(0 0 0 / 10%); cursor: pointer; }
.add-node-popover-body { display: flex; gap: 12px; }
.add-node-popover-item { display: grid; gap: 6px; border: 0; color: #191f25; background: transparent; text-align: center; cursor: pointer; }
.item-wrapper { display: grid; width: 58px; height: 58px; place-items: center; border: 1px solid #e2e2e2; border-radius: 50%; }
.approver .item-wrapper { color: #ff943e; }
.notifier .item-wrapper { color: #3296fa; }
.condition .item-wrapper { color: #15bc83; }
</style>
