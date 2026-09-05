<template>
  <div class="add-node-btn-box">
    <div class="add-node-btn">
      <el-popover v-model:visible="visible" placement="right-start" width="auto">
        <div class="add-node-popover-body">
          <button v-if="enabledNodeTypes.has('human.approval')" type="button" class="add-node-popover-item approver" @click="addType(NodeType.Approver)">
            <span class="item-wrapper"><span class="iconfont">{{ getNodeTypeMeta(NodeType.Approver).icon }}</span></span>
            <span>审批人</span>
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
import { NodeType, getNodeTypeMeta } from '../utils/const.js'

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
  visible.value = false
  if (![NodeType.Approver, NodeType.Copyer, NodeType.Route, NodeType.ParallelRoute].includes(type)) return
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
